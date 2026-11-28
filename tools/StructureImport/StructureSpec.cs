using System.Text.Json;
using System.Text.Json.Serialization;

namespace StructureImport;

/// <summary>
/// How one model becomes a building: the optional <c>&lt;model&gt;.json</c> beside the <c>.glb</c> (docs/structures.md "Settings"). Every field has
/// a default, so a model needs no file at all.
/// </summary>
public sealed class StructureSpec
{
    /// <summary>
    /// The name in the build menu; default: the file name, dashes and underscores as spaces.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// The build menu's text about it.
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// The finished height in metres (the model is scaled evenly to it); default: the model's own size, glTF being in metres.
    /// </summary>
    public float? Height { get; set; }

    /// <summary>
    /// Metres the model is sunk into the ground, so it doesn't float on a slope.
    /// </summary>
    public float Sink { get; set; }

    /// <summary>
    /// Degrees the model is turned about the vertical axis before anything else.
    /// </summary>
    public float Yaw { get; set; }

    /// <summary>
    /// The build menu's category (<c>building category</c>): one of the game's (BUILDINGS, CAMPING, EXTERIOR, FARMING, ...) or a new name.
    /// </summary>
    public string Category { get; set; } = "EXTERIOR";

    /// <summary>
    /// The build menu's group (<c>building group</c>): buildings sharing one are listed as variants of one entry.
    /// </summary>
    public string Group { get; set; } = "";

    /// <summary>
    /// Building Materials it costs (<c>build materials</c>).
    /// </summary>
    public int Materials { get; set; } = 5;

    /// <summary>
    /// How fast it is built (<c>build speed mult</c>; the game's tents use 5, its houses 1 to 2).
    /// </summary>
    public float BuildSpeed { get; set; } = 2;

    /// <summary>
    /// What unlocks it: <c>start</c> (known from the start of a game, added to the research <c>_Default Start</c>), <c>none</c> (nothing; another
    /// record of the mod unlocks it), or the string id of a RESEARCH whose <c>enable buildings</c> gets it.
    /// </summary>
    public string Research { get; set; } = "start";

    /// <summary>
    /// Where it may stand: <c>outdoor</c>, <c>indoor</c> or <c>both</c> (<c>is exterior furniture</c> / <c>is interior furniture</c>).
    /// </summary>
    public string Placement { get; set; } = "outdoor";

    /// <summary>
    /// Whether placing it outside a town starts an outpost (<c>creates player town</c>).
    /// </summary>
    public bool CreatesTown { get; set; } = true;

    /// <summary>
    /// The collision: <c>mesh</c> (a simplified copy of the model), <c>box</c> (its bounding box) or <c>none</c> (walk through it).
    /// </summary>
    public string Collision { get; set; } = "mesh";

    /// <summary>
    /// The most triangles the <c>mesh</c> collision keeps.
    /// </summary>
    public int CollisionTriangles { get; set; } = 2000;

    /// <summary>
    /// The largest texture side in pixels; bigger textures are halved until they fit.
    /// </summary>
    public int TextureSize { get; set; } = 2048;

    /// <summary>
    /// The largest <see cref="TextureSize"/> (the game's own textures go to 4096).
    /// </summary>
    public const int MaxTextureSize = 8192;

    /// <summary>
    /// The largest <see cref="CollisionTriangles"/>: collision is for walking and placing, not looks.
    /// </summary>
    public const int MaxCollisionTriangles = 50_000;

    /// <summary>
    /// The JSON options: camelCase names, comments and trailing commas allowed, unknown names refused (a typo would else go unnoticed).
    /// </summary>
    static readonly JsonSerializerOptions s_json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>
    /// The settings file beside <paramref name="model"/> (same name, <c>.json</c>), or the defaults when there is none. Throws when the file is
    /// malformed or a value is out of range.
    /// </summary>
    public static StructureSpec ForModel(string model)
    {
        string path = Path.ChangeExtension(model, ".json");
        var spec = File.Exists(path)
            ? JsonSerializer.Deserialize<StructureSpec>(File.ReadAllText(path), s_json) ?? new StructureSpec()
            : new StructureSpec();
        spec.Validate(path);
        return spec;
    }

    /// <summary>
    /// The name shown in the game for the model file <paramref name="model"/>.
    /// </summary>
    public string DisplayName(string model) =>
        string.IsNullOrWhiteSpace(Name) ? Path.GetFileNameWithoutExtension(model).Replace('-', ' ').Replace('_', ' ') : Name;

    /// <summary>
    /// Throws an <see cref="InvalidDataException"/> naming <paramref name="path"/> for a value the import can't use.
    /// </summary>
    void Validate(string path)
    {
        string? problem = Height is { } h && (!float.IsFinite(h) || h <= 0) ? "height must be a number above 0"
            : !float.IsFinite(Sink) || !float.IsFinite(Yaw) ? "sink and yaw must be numbers"
            : Materials < 0 ? "materials must be 0 or more"
            : BuildSpeed <= 0 ? "buildSpeed must be above 0"
            : Placement is not ("outdoor" or "indoor" or "both") ? "placement must be outdoor, indoor or both"
            : Collision is not ("mesh" or "box" or "none") ? "collision must be mesh, box or none"
            : CollisionTriangles is < 12 or > MaxCollisionTriangles ? $"collisionTriangles must be 12 to {MaxCollisionTriangles}"
            : TextureSize is < 4 or > MaxTextureSize || (TextureSize & (TextureSize - 1)) != 0
                ? $"textureSize must be a power of two from 4 to {MaxTextureSize}"
            : string.IsNullOrWhiteSpace(Research) ? "research must be start, none or a RESEARCH string id"
            : null;
        if (problem is not null) throw new InvalidDataException($"{path}: {problem}");
    }
}
