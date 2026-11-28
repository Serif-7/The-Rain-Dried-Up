using System.Text;
using Meitou.Data.Fcs;

namespace StructureImport;

/// <summary>
/// Imports every model of a mod's <c>Structures</c> folder, and of the <c>ItemModels</c> folder beside it, into the mod's deployed folder: the files
/// under <c>structures/</c> and the records in <c>&lt;Mod&gt;.mod</c>.
/// </summary>
public static class Importer
{
    /// <summary>
    /// The folder of the deployed mod the files go to; the game finds them by bare name wherever they are.
    /// </summary>
    public const string AssetFolder = "structures";

    /// <summary>
    /// The folder beside <c>Structures</c> whose models become items' looks (docs/structures.md "Item models").
    /// </summary>
    public const string ItemFolder = "ItemModels";

    /// <summary>
    /// Above this many triangles a model is reported as heavy (the game's largest buildings have about this many).
    /// </summary>
    const int HeavyTriangles = 150_000;

    /// <summary>
    /// Imports the <c>.glb</c> / <c>.gltf</c> files of <paramref name="models"/> (a mod's <c>Structures</c> folder, which may be missing) and of the
    /// <see cref="ItemFolder"/> beside it into the mod folder <paramref name="mod"/> (its name is the mod's), writing each folder's
    /// <see cref="StructureIds.FileName"/>. Returns one line per model for the log.
    /// </summary>
    public static List<string> ImportFolder(string models, string mod)
    {
        mod = Path.TrimEndingDirectorySeparator(Path.GetFullPath(mod));
        string modName = Path.GetFileName(mod);
        string assets = Path.Combine(mod, AssetFolder);
        if (Directory.Exists(assets)) Directory.Delete(assets, recursive: true);
        Directory.CreateDirectory(assets);

        var records = new List<FcsRecord>();
        var log = new List<string>();
        ImportStructures(models, assets, modName, records, log);
        ImportItems(ItemFolderBeside(models), assets, modName, WornItem.FindGame(mod), records, log);
        ModFileMerger.Merge(Path.Combine(mod, modName + ".mod"), records);
        return log;
    }

    /// <summary>
    /// The item models' folder beside a <c>Structures</c> folder.
    /// </summary>
    public static string ItemFolderBeside(string models) =>
        Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(models)))!, ItemFolder);

    /// <summary>
    /// Imports the structures of a folder (none when it is missing), adding their records and log lines.
    /// </summary>
    static void ImportStructures(string models, string assets, string modName, List<FcsRecord> records, List<string> log)
    {
        if (!Directory.Exists(models)) return;

        var ids = StructureIds.Load(models);
        foreach (string model in ModelFiles(models))
        {
            var block = new RecordIds(ids.BlockOf(Path.GetFileNameWithoutExtension(model)), modName + ".mod");
            var (structureRecords, line) = Named(model, () => ImportModel(model, assets, FilePrefix(modName, model), block));
            records.AddRange(structureRecords);
            log.Add("Structure " + line);
        }

        ids.Save(models);
    }

    /// <summary>
    /// Imports the item models of a folder (none when it is missing), adding their records and log lines; worn ones are skinned to the
    /// skeletons of the game in <paramref name="game"/> (null: not found).
    /// </summary>
    static void ImportItems(string folder, string assets, string modName, string? game, List<FcsRecord> records, List<string> log)
    {
        if (!Directory.Exists(folder)) return;

        var ids = StructureIds.Load(folder);
        foreach (string model in ModelFiles(folder))
        {
            var block = new RecordIds(ids.BlockOf(Path.GetFileNameWithoutExtension(model)), modName + ".mod", RecordIds.ItemBase);
            var (record, line) = Named(model, () => ImportItem(model, assets, FilePrefix(modName, model), block, game));
            records.Add(record);
            log.Add("Item " + line);
        }

        ids.Save(folder);
    }

    /// <summary>
    /// The model files of a folder, in name order.
    /// </summary>
    public static IEnumerable<string> ModelFiles(string folder) =>
        Directory.EnumerateFiles(folder)
            .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".glb" or ".gltf")
            .Order(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Runs one model's import, a failure's message naming the model (a glTF reader's own messages don't).
    /// </summary>
    static T Named<T>(string model, Func<T> import)
    {
        try
        {
            return import();
        }
        catch (Exception e) when (!e.Message.Contains(model, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{model}: {e.Message}", e);
        }
    }

    /// <summary>
    /// Imports one model: its files into <paramref name="assets"/> (named from <paramref name="prefix"/>), its records with ids from
    /// <paramref name="ids"/>, and a log line.
    /// </summary>
    static (List<FcsRecord> Records, string Line) ImportModel(string model, string assets, string prefix, RecordIds ids)
    {
        var spec = StructureSpec.ForModel(model);
        var meshes = GlbScene.Load(model);
        if (meshes.Count > RecordIds.MaxParts)
            throw new InvalidDataException($"{model}: {meshes.Count} materials, at most {RecordIds.MaxParts}; merge some in Blender");

        MeshMath.Place(meshes, MeshMath.Scale(meshes, spec.Height), spec.Yaw, spec.Sink * MeshMath.UnitsPerMetre);
        var (_, max) = MeshMath.Bounds(meshes);
        var parts = meshes.Select((mesh, i) => WritePart(mesh, i, meshes, spec, assets, prefix)).ToList();

        string name = spec.DisplayName(model);
        var records = StructureRecords.Build(spec, name, max.Y, parts, ids);
        if (spec.Research != "none") records.Add(StructureRecords.Unlock(Research(spec.Research), ids.Building));

        return (records, Summary(model, name, meshes, max.Y, spec));
    }

    /// <summary>
    /// Imports one item model: its mesh (on its base, centred, as the game's item meshes are) and textures into <paramref name="assets"/>, its
    /// MATERIAL_SPECS_CLOTHING with the id from <paramref name="ids"/>, the worn meshes of a worn item, and a log line naming what the mod's ITEM
    /// must point at.
    /// </summary>
    static (FcsRecord Record, string Line) ImportItem(string model, string assets, string prefix, RecordIds ids, string? game)
    {
        var spec = ItemSpec.ForModel(model);
        var meshes = GlbScene.Load(model);
        if (meshes.Count != 1) throw new InvalidDataException($"{model}: {meshes.Count} materials; an item model needs exactly one");

        MeshMath.Place(meshes, MeshMath.ScaleLongest(meshes, spec.Size), spec.Yaw, 0);
        var mesh = meshes[0];
        OgreMeshWriter.Write(mesh, MeshMath.Tangents(mesh), Path.Combine(assets, prefix + ".mesh"));
        var (diffuse, normal, metal) = TextureBaker.Write(TextureBaker.Bake(mesh.Look, spec.TextureSize), assets, prefix);
        int? threshold = mesh.Look.AlphaCutoff is { } cut ? (int)MathF.Round(cut * 255) : null;
        var files = new PartFiles(prefix + ".mesh", "", diffuse, normal, metal, threshold, mesh.Look.Name);

        var record = StructureRecords.ItemMaterial(ids.ItemMaterial, spec.DisplayName(model), files);
        var (min, max) = MeshMath.Bounds(meshes);
        float longest = MathF.Max(max.X - min.X, MathF.Max(max.Y - min.Y, max.Z - min.Z)) / MeshMath.UnitsPerMetre;
        string line = $"{Path.GetFileName(model)} -> mesh {files.Mesh}, material {record.StringId}, {mesh.Indices.Count / 3} triangles, "
            + $"{longest:0.###} m long";
        if (spec.Worn is { } worn) line += WriteWorn(mesh, spec, worn, assets, prefix, game);
        return (record, line);
    }

    /// <summary>
    /// Writes a worn item's meshes on the man's and the woman's skeleton (<c>&lt;prefix&gt;_worn.mesh</c>, <c>&lt;prefix&gt;_worn_female.mesh</c>);
    /// returns the log line's ending, which says why when they could not be written.
    /// </summary>
    static string WriteWorn(SurfaceMesh mesh, ItemSpec spec, string worn, string assets, string prefix, string? game)
    {
        if (game is null) return $"; worn on {worn}: not written, the game's skeletons were not found (set KENSHI_PATH)";

        var names = new List<string>();
        foreach (var (skeletonName, suffix) in new[] { (WornItem.MaleSkeleton, "_worn"), (WornItem.FemaleSkeleton, "_worn_female") })
        {
            string skeleton = WornItem.FindFile(game, skeletonName)
                ?? throw new FileNotFoundException($"No {skeletonName} under {Path.Combine(game, "data")}");
            var (wornMesh, skin) = WornItem.Wear(mesh, worn, spec.Fit, skeleton);
            string name = prefix + suffix + ".mesh";
            OgreMeshWriter.Write(wornMesh, MeshMath.Tangents(wornMesh), Path.Combine(assets, name), skin);
            names.Add(name);
        }

        return $"; worn on {worn}: mesh {names[0]}, mesh female {names[1]}";
    }

    /// <summary>
    /// Writes part <paramref name="index"/>'s mesh and textures; the first part also carries the whole model's collision.
    /// </summary>
    static PartFiles WritePart(SurfaceMesh mesh, int index, List<SurfaceMesh> all, StructureSpec spec, string assets, string prefix)
    {
        string stem = all.Count == 1 ? prefix : $"{prefix}_{index + 1}";
        OgreMeshWriter.Write(mesh, MeshMath.Tangents(mesh), Path.Combine(assets, stem + ".mesh"));
        var textures = TextureBaker.Bake(mesh.Look, spec.TextureSize);
        var (diffuse, normal, metal) = TextureBaker.Write(textures, assets, stem);
        string collision = index == 0 ? WriteCollision(all, spec, Path.Combine(assets, prefix + "_col.xml")) : "";
        int? threshold = mesh.Look.AlphaCutoff is { } cut ? (int)MathF.Round(cut * 255) : null;
        return new PartFiles(stem + ".mesh", collision, diffuse, normal, metal, threshold, mesh.Look.Name);
    }

    /// <summary>
    /// Writes the model's collision as <paramref name="spec"/> asks; returns its file name, "" for none.
    /// </summary>
    static string WriteCollision(List<SurfaceMesh> meshes, StructureSpec spec, string path)
    {
        switch (spec.Collision)
        {
            case "none":
                return "";
            case "box":
                var (min, max) = MeshMath.Bounds(meshes);
                CollisionWriter.WriteBox(min, max, path);
                break;
            default:
                CollisionWriter.WriteMesh(Simplifier.Reduce(Simplifier.Weld(meshes), spec.CollisionTriangles), path);
                break;
        }

        return Path.GetFileName(path);
    }

    /// <summary>
    /// The research string id a setting names.
    /// </summary>
    static string Research(string setting) => setting == "start" ? StructureRecords.DefaultStart : setting;

    /// <summary>
    /// The start of every file name of a model: mod and model names, lower case, letters and digits only (bare names are shared by every mod).
    /// </summary>
    static string FilePrefix(string modName, string model)
    {
        static string Clean(string s) => new([.. s.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_')]);
        return $"{Clean(modName)}_{Clean(Path.GetFileNameWithoutExtension(model))}";
    }

    /// <summary>
    /// The log line of an imported model.
    /// </summary>
    static string Summary(string model, string name, List<SurfaceMesh> meshes, float height, StructureSpec spec)
    {
        int triangles = meshes.Sum(m => m.Indices.Count / 3);
        var line = new StringBuilder($"{Path.GetFileName(model)} -> \"{name}\": {meshes.Count} part(s), {triangles} triangles, ");
        line.Append($"{height / MeshMath.UnitsPerMetre:0.##} m tall, collision {spec.Collision}, unlocked by {spec.Research}");
        if (triangles > HeavyTriangles) line.Append($" (heavy: consider reducing it below {HeavyTriangles} triangles in Meshy or Blender)");
        return line.ToString();
    }
}
