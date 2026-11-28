using System.Numerics;
using StructureImport;

namespace RaceImport;

/// <summary>
/// One body import: load the model, fit it onto the game's skeleton, write the mesh and textures (docs/race-import.md).
/// </summary>
public static class BodyImporter
{
    /// <summary>
    /// Side of the written textures in pixels.
    /// </summary>
    public const int TextureSide = 2048;

    /// <summary>
    /// Imports <paramref name="model"/> as <c>&lt;stem&gt;.mesh</c>, <c>&lt;stem&gt;_d.dds</c>, (with a normal map) <c>&lt;stem&gt;_n.dds</c> and the
    /// part map <c>&lt;stem&gt;_parts.png</c> in <paramref name="outFolder"/>, as <paramref name="options"/> say; returns a line for the log.
    /// </summary>
    public static string Import(string model, string kenshiFolder, string outFolder, string stem, ImportOptions options)
    {
        var rig = options.Rig;
        var body = SourceBody.Load(model);
        var classes = options.NoHair || options.SkinTone
            ? TextureClasses.Classify(body.BaseColourImage ?? throw new InvalidDataException("the model has no base colour texture"), body.Positions, body.Uvs, body.Indices)
            : null;
        var (membrane, sleeved) = options.Wings ? WingRig.Apply(body) : (new bool[body.Positions.Length], new bool[body.Positions.Length]);
        var kenshi = KenshiSkeleton.Load(kenshiFolder, rig);
        var human = HumanBody.Load(kenshiFolder, rig);
        var bustHuman = options.Bust is { } bust && bust != rig ? HumanBody.Load(kenshiFolder, bust) : human;
        var fit = new BodyFit(body, kenshi, options.Neck, options.Palms);
        var (fitted, normals, weights) = fit.Repose();
        if (options.Wings) new WingSpread(fit, kenshi).Apply(body, membrane, fitted, normals, weights);
        var (lifted, lift) = BustLift.Apply(fitted, weights, kenshi, bustHuman);
        var wingWeights = Kept(weights, membrane);
        var (positions, conformed, copied, tail) = options.Rigid
            ? (lifted, 0, 0, new bool[lifted.Length])
            : Conformed(lifted, body.Indices, weights, membrane, kenshi, human, options.Slim);
        Restore(weights, wingWeights);
        var unheaded = new SkinnedMesh(positions, normals, body.Uvs, [], body.Indices, weights);
        var hair = options.NoHair ? classes : null;
        var (mesh, source) = Headed(unheaded, options, kenshiFolder, human, kenshi, hair);
        int patched = 0;
        if (hair is not null) (mesh, source, patched) = HairHolePatch.Apply(mesh, source, human, kenshi, SkinUv(body, hair));
        var parts = options.Attach.Select(Attachment.Load).ToArray();
        (mesh, source) = Attachments.Apply(mesh, source, parts, kenshi);
        mesh = mesh with { Tangents = Tangents(mesh.Positions, mesh.Normals, mesh.Uvs, mesh.Indices) };
        sleeved = [.. source.Select(v => v >= 0 && sleeved[v])];

        Directory.CreateDirectory(outFolder);
        SkinnedMeshWriter.Write(Path.Combine(outFolder, stem + ".mesh"), mesh, stem, rig.Skeleton);
        var painted = options.SkinTone && classes is not null ? SkinTone.Repainted(body, classes, TextureSide) : body;
        bool normalMap = BodyTextures.Write(painted, parts, outFolder, stem, TextureSide);
        int toned = options.SkinTone && classes is not null ? WriteMask(outFolder, stem, mesh, source, body, classes) : 0;
        bool[] attached = [.. source.Select(v => v == -1)];
        int hideable = WritePartMap(kenshiFolder, rig, Path.Combine(outFolder, stem + "_parts.png"), mesh, human, sleeved, parts.Length > 0 ? attached : null);

        var min = mesh.Positions.Aggregate(Vector3.Min);
        var max = mesh.Positions.Aggregate(Vector3.Max);
        return $"Race body {stem} ({rig.Skeleton}, neck x{options.Neck:0.00}): {mesh.Positions.Length} vertices, {mesh.Indices.Length / 3} triangles, scale {fit.Scale:0.00}, " +
            $"bounds {min:0.0} to {max:0.0}, bust lifted {lift:0.00}, {conformed} vertices pulled onto the skin, human weights on {copied} and tail on the pelvis for {tail.Count(t => t)} vertices, normal map {(normalMap ? "yes" : "no")}, {hideable} vertices in hideable parts, {parts.Length} parts attached, " +
            $"{membrane.Count(m => m)} vertices of wing membrane, {patched} triangles patched under the hair, {mesh.Poses.Length} face poses, {toned} skin-toned triangles, hands twisted {Twists(fit)}, {fit.WristJoints} wrist joints on the hands";
    }

    /// <summary>
    /// The lifted body slimmed and pulled onto the human skin with the human's torso weights (docs/race-import.md "Slimming", "Skin conform",
    /// "Human weights"): the positions, how many vertices the conform moved, how many took human weights, and the tail's vertices.
    /// </summary>
    static (Vector3[] Positions, int Conformed, int Copied, bool[] Tail) Conformed(Vector3[] lifted, int[] indices,
        Dictionary<ushort, float>[] weights, bool[] membrane, KenshiSkeleton kenshi, HumanBody human, float slim)
    {
        var tail = HumanWeights.MoveTails(lifted, indices, weights, kenshi, human);
        bool[] spared = [.. tail.Zip(membrane, (t, m) => t || m)];
        var slimmed = new BodyConform(kenshi, human).Apply(lifted, weights, indices, slim, spared);
        var (positions, conformed) = new SkinConform(kenshi, human).Apply(slimmed, weights, indices, slim, spared);
        int copied = HumanWeights.Apply(positions, indices, weights, tail, kenshi, human);
        return (positions, conformed, copied, tail);
    }

    /// <summary>
    /// The fitted mesh with the human head grafted on when the options say headless (<see cref="HeadGraft"/>), and each vertex's source vertex
    /// (-1 for the head's).
    /// </summary>
    static (SkinnedMesh Mesh, int[] Source) Headed(SkinnedMesh mesh, ImportOptions options, string kenshiFolder, HumanBody human,
        KenshiSkeleton kenshi, Texel[]? hair)
    {
        if (!options.Headless) return (mesh, [.. Enumerable.Range(0, mesh.Positions.Length)]);

        ushort? ears = options.Ears ? kenshi.Handle("Bip01 Head") : null;
        return HeadGraft.Apply(mesh, human, HumanHead.Load(kenshiFolder, options.Rig), ears, hair);
    }

    /// <summary>
    /// A uv inside the model's skin (the centre of its <see cref="Texel.Skin"/> triangle widest in the texture), for skin the import adds.
    /// </summary>
    static Vector2 SkinUv(SourceBody body, Texel[] classes)
    {
        var uvs = body.Uvs;
        var indices = body.Indices;
        int widest = Enumerable.Range(0, classes.Length)
            .Where(t => classes[t] == Texel.Skin)
            .MaxBy(t => UvArea(uvs[indices[3 * t]], uvs[indices[3 * t + 1]], uvs[indices[3 * t + 2]]));
        return (uvs[indices[3 * widest]] + uvs[indices[3 * widest + 1]] + uvs[indices[3 * widest + 2]]) / 3;
    }

    /// <summary>
    /// A triangle's area in uv space.
    /// </summary>
    static float UvArea(Vector2 a, Vector2 b, Vector2 c) => MathF.Abs((b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y)) / 2;

    /// <summary>
    /// Writes the skin-tone body mask of the final mesh (<see cref="SkinTone.WriteMask"/>) over the model's skin triangles it kept; returns how
    /// many triangles it covers.
    /// </summary>
    static int WriteMask(string outFolder, string stem, SkinnedMesh mesh, int[] source, SourceBody body, Texel[] classes) =>
        SkinTone.WriteMask(outFolder, stem, mesh, source, body.Indices, classes);

    /// <summary>
    /// The part map of the fitted mesh from the rig's human one, the <paramref name="attached"/> parts' vertices in none (and the map as wide as
    /// the atlas); returns how many vertices got a part.
    /// </summary>
    static int WritePartMap(string kenshiFolder, HumanRig rig, string path, SkinnedMesh mesh, HumanBody human, bool[] sleeved, bool[]? attached)
    {
        var humanMap = Rgba.Decode(File.ReadAllBytes(KenshiSkeleton.GameFile(kenshiFolder, rig.Mask)));
        var heaviest = mesh.Weights.Select(w => w.MaxBy(p => p.Value).Key).ToArray();
        return PartMap.Write(path, mesh.Positions, mesh.Uvs, heaviest, human, humanMap, sleeved, attached, attached is not null);
    }

    /// <summary>
    /// Copies of the weights of the vertices <paramref name="kept"/> marks (the wing membrane's, which the torso's weight copy must not touch).
    /// </summary>
    static Dictionary<int, Dictionary<ushort, float>> Kept(Dictionary<ushort, float>[] weights, bool[] kept) =>
        Enumerable.Range(0, weights.Length)
            .Where(v => kept[v])
            .ToDictionary(v => v, v => new Dictionary<ushort, float>(weights[v]));

    /// <summary>
    /// Puts back the weights <see cref="Kept"/> saved.
    /// </summary>
    static void Restore(Dictionary<ushort, float>[] weights, Dictionary<int, Dictionary<ushort, float>> kept)
    {
        foreach (var (v, w) in kept) weights[v] = w;
    }

    /// <summary>
    /// Per-vertex tangents from the uv layout, made perpendicular to the normal, w the bitangent's handedness.
    /// </summary>
    public static Vector4[] Tangents(Vector3[] positions, Vector3[] normals, Vector2[] uvs, int[] indices)
    {
        var tangents = new Vector3[positions.Length];
        var bitangents = new Vector3[positions.Length];
        for (int t = 0; t < indices.Length; t += 3) AddTriangle(positions, uvs, indices.AsSpan(t, 3), tangents, bitangents);

        var result = new Vector4[positions.Length];
        for (int v = 0; v < positions.Length; v++)
        {
            var n = normals[v];
            var tangent = tangents[v] - n * Vector3.Dot(tangents[v], n);
            tangent = tangent.LengthSquared() > 0 ? Vector3.Normalize(tangent) : tangent;
            float handedness = Vector3.Dot(Vector3.Cross(n, tangent), bitangents[v]) < 0 ? -1 : 1;
            result[v] = new Vector4(tangent, handedness);
        }

        return result;
    }

    /// <summary>
    /// Adds one triangle's uv-space tangent and bitangent to its three vertices.
    /// </summary>
    static void AddTriangle(Vector3[] positions, Vector2[] uvs, ReadOnlySpan<int> tri, Vector3[] tangents, Vector3[] bitangents)
    {
        var e1 = positions[tri[1]] - positions[tri[0]];
        var e2 = positions[tri[2]] - positions[tri[0]];
        var d1 = uvs[tri[1]] - uvs[tri[0]];
        var d2 = uvs[tri[2]] - uvs[tri[0]];
        float det = d1.X * d2.Y - d2.X * d1.Y;
        float f = MathF.Abs(det) > 1e-12f ? 1 / det : 0;

        var tangent = f * (e1 * d2.Y - e2 * d1.Y);
        var bitangent = f * (e2 * d1.X - e1 * d2.X);
        foreach (int v in tri)
        {
            tangents[v] += tangent;
            bitangents[v] += bitangent;
        }
    }

    /// <summary>
    /// The hands the fit twisted, in degrees (<c>L 92, R -88</c>), or <c>none</c>.
    /// </summary>
    static string Twists(BodyFit fit) =>
        fit.HandTwists.Count == 0 ? "none" : string.Join(", ", fit.HandTwists.Select(p => $"{p.Key} {p.Value * 180 / MathF.PI:0}"));
}
