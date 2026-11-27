using System.Text.Json;
using System.Text.Json.Serialization;

namespace StructureImport;

/// <summary>
/// How one model becomes an item's look: the optional <c>&lt;model&gt;.json</c> beside a <c>.glb</c> in <c>ItemModels</c> (docs/structures.md
/// "Item models"). Every field has a default, so a model needs no file at all.
/// </summary>
public sealed class ItemSpec
{
    /// <summary>
    /// The material record's name; default: the file name, dashes and underscores as spaces.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// The longest side in metres (the model is scaled evenly to it); default: the model's own size, glTF being in metres.
    /// </summary>
    public float? Size { get; set; }

    /// <summary>
    /// Degrees the model is turned about the vertical axis before anything else.
    /// </summary>
    public float Yaw { get; set; }

    /// <summary>
    /// The largest texture side in pixels; bigger textures are halved until they fit.
    /// </summary>
    public int TextureSize { get; set; } = 1024;

    /// <summary>
    /// Worn on the body's hand bones (docs/structures.md "Worn on the hands"): <c>hands</c>, <c>right hand</c> or <c>left hand</c>; null for an
    /// item that is only held or lies on the ground.
    /// </summary>
    public string? Worn { get; set; }

    /// <summary>
    /// For a worn item: where the palm sits along the model's length, 0 at its fist end (-X) and 1 at its arm opening (+X).
    /// </summary>
    public float Grip { get; set; } = 0.5f;

    /// <summary>
    /// For a worn item: where the palm sits up the model, 0 at its base and 1 at its top; for a gauntlet with a handle, the hollow the fist
    /// closes in.
    /// </summary>
    public float GripHeight { get; set; } = 0.5f;

    /// <summary>
    /// For a worn item: the bone it moves with. <c>hand</c> bends with the wrist (knuckles, claws over the fingers); <c>forearm</c> stays on the
    /// arm's line when the wrist bends (a blade along the arm, a gauntlet held straight by its sleeve).
    /// </summary>
    public string Follows { get; set; } = "hand";

    /// <summary>
    /// For a worn item: degrees it is tilted about the palm's centre, its fist end towards the back of the hand (negative: towards the palm), so
    /// the sleeve lies along the forearm when the grip is not on the model's long axis.
    /// </summary>
    public float Tilt { get; set; }

    /// <summary>
    /// The worn settings together.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public WornFit Fit => new(Grip, GripHeight, Tilt, Follows == "forearm");

    /// <summary>
    /// The values <see cref="Worn"/> takes.
    /// </summary>
    public static readonly string[] WornValues = ["hands", "right hand", "left hand"];

    /// <summary>
    /// The values <see cref="Follows"/> takes.
    /// </summary>
    public static readonly string[] FollowsValues = ["hand", "forearm"];

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
    public static ItemSpec ForModel(string model)
    {
        string path = Path.ChangeExtension(model, ".json");
        var spec = File.Exists(path)
            ? JsonSerializer.Deserialize<ItemSpec>(File.ReadAllText(path), s_json) ?? new ItemSpec()
            : new ItemSpec();
        spec.Validate(path);
        return spec;
    }

    /// <summary>
    /// The name of the model file <paramref name="model"/>'s material record.
    /// </summary>
    public string DisplayName(string model) =>
        string.IsNullOrWhiteSpace(Name) ? Path.GetFileNameWithoutExtension(model).Replace('-', ' ').Replace('_', ' ') : Name;

    /// <summary>
    /// Throws an <see cref="InvalidDataException"/> naming <paramref name="path"/> for a value the import can't use.
    /// </summary>
    void Validate(string path)
    {
        string? problem = Size is { } s && (!float.IsFinite(s) || s <= 0) ? "size must be a number above 0"
            : !float.IsFinite(Yaw) ? "yaw must be a number"
            : Worn is not null && !WornValues.Contains(Worn) ? $"worn must be one of {string.Join(", ", WornValues)}"
            : !(Grip is >= 0 and <= 1) ? "grip must be a number from 0 to 1"
            : !(GripHeight is >= 0 and <= 1) ? "gripHeight must be a number from 0 to 1"
            : !(Tilt is >= -90 and <= 90) ? "tilt must be a number of degrees from -90 to 90"
            : !FollowsValues.Contains(Follows) ? $"follows must be one of {string.Join(", ", FollowsValues)}"
            : TextureSize is < 4 or > StructureSpec.MaxTextureSize || (TextureSize & (TextureSize - 1)) != 0
                ? $"textureSize must be a power of two from 4 to {StructureSpec.MaxTextureSize}"
            : null;
        if (problem is not null) throw new InvalidDataException($"{path}: {problem}");
    }
}
