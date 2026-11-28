using System.Text.Json;

namespace StructureImport;

/// <summary>
/// The string ids of one model's records: <c>&lt;n&gt;-&lt;Mod&gt;.mod</c>, the game's form, with n from the model's block of 100 counted from
/// <paramref name="First"/>: a structure's from <see cref="Base"/> (the building, then parts from +1, materials from +50), an item model's from
/// <see cref="ItemBase"/> (its material).
/// </summary>
public sealed record RecordIds(int Block, string ModFile, int First = RecordIds.Base)
{
    /// <summary>
    /// The first number used: far above the ids a mod's own data generator counts up from 1.
    /// </summary>
    public const int Base = 900000;

    /// <summary>
    /// The first number of the item models' blocks (Structures and ItemModels keep their own <c>ids.json</c>).
    /// </summary>
    public const int ItemBase = 950000;

    /// <summary>
    /// The most parts (materials) one structure may have.
    /// </summary>
    public const int MaxParts = 49;

    /// <summary>
    /// The BUILDING's id.
    /// </summary>
    public string Building => Id(0);

    /// <summary>
    /// Part <paramref name="i"/>'s id.
    /// </summary>
    public string Part(int i) => Id(1 + i);

    /// <summary>
    /// Material <paramref name="i"/>'s id.
    /// </summary>
    public string Material(int i) => Id(50 + i);

    /// <summary>
    /// An item model's MATERIAL_SPECS_CLOTHING id.
    /// </summary>
    public string ItemMaterial => Id(0);

    /// <summary>
    /// Whether <paramref name="stringId"/> is one the import makes in <paramref name="modFile"/> (any structure's or item model's).
    /// </summary>
    public static bool IsImported(string stringId, string modFile)
    {
        int dash = stringId.IndexOf('-');
        return dash > 0 && stringId[(dash + 1)..] == modFile && int.TryParse(stringId[..dash], out int n) && n >= Base && n < Base + 100000;
    }

    /// <summary>
    /// The id with number <paramref name="offset"/> in the block.
    /// </summary>
    string Id(int offset) => $"{First + Block * 100 + offset}-{ModFile}";
}

/// <summary>
/// The block each structure's records use, kept in <c>Structures/ids.json</c> beside the models so ids never change between imports: a save
/// names a placed building by its record's string id. Commit the file with the models.
/// </summary>
public sealed class StructureIds
{
    /// <summary>
    /// The file's name.
    /// </summary>
    public const string FileName = "ids.json";

    /// <summary>
    /// The JSON layout: indented, camelCase, LF line breaks on every OS (System.Text.Json otherwise breaks lines with the OS's newline, CRLF on
    /// Windows, so ids.json flipped between imports on Windows and Linux).
    /// </summary>
    static readonly JsonSerializerOptions s_json = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// The block of each model, by file name without extension.
    /// </summary>
    public SortedDictionary<string, int> Blocks { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The ids in <paramref name="folder"/>, empty when it has no file yet.
    /// </summary>
    public static StructureIds Load(string folder)
    {
        string path = Path.Combine(folder, FileName);
        if (!File.Exists(path)) return new StructureIds();

        var ids = JsonSerializer.Deserialize<StructureIds>(File.ReadAllText(path), s_json) ?? new StructureIds();
        ids.Blocks = new SortedDictionary<string, int>(ids.Blocks, StringComparer.OrdinalIgnoreCase);
        return ids;
    }

    /// <summary>
    /// The block of <paramref name="model"/>, a new one (one above the highest) when it has none.
    /// </summary>
    public int BlockOf(string model)
    {
        if (Blocks.TryGetValue(model, out int block)) return block;

        block = Blocks.Count == 0 ? 0 : Blocks.Values.Max() + 1;
        Blocks[model] = block;
        return block;
    }

    /// <summary>
    /// Writes the ids to <paramref name="folder"/> when they differ from the file there.
    /// </summary>
    public void Save(string folder)
    {
        if (!Directory.Exists(folder)) return;

        string path = Path.Combine(folder, FileName);
        string text = JsonSerializer.Serialize(this, s_json) + "\n";
        if (!File.Exists(path) || File.ReadAllText(path) != text) File.WriteAllText(path, text);
    }
}
