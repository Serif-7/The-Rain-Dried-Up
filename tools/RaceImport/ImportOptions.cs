namespace RaceImport;

/// <summary>
/// How a body is imported (docs/race-import.md "Run it"): the rig it is fitted to, the slimming strength (0 none, 1 full), whose human's bust
/// height the bust rises to (null: the rig's own), the neck's length as a share of the fitted one, whether the arms carry wings
/// (<see cref="WingRig"/>) and whether the model's head is replaced by the human one (<see cref="HeadGraft"/>) and whether the model's ears stay on it (<see cref="HeadEars"/>)
/// and whether the hands are turned palm to thigh (<see cref="BodyFit"/>, palms) and whether the model keeps its own weights and shape
/// (<c>rigid</c>: no slimming, skin conform, tail move or human weights; for mechanical bodies whose parts must not stretch), whether the model's
/// own hair goes with its head (<c>nohair</c>, <see cref="HeadGraft"/>), whether the skin takes the editor's skin tone (<see cref="SkinTone"/>)
/// and the static part models merged into it (<see cref="Attachments"/>).
/// </summary>
public sealed record ImportOptions(HumanRig Rig, float Slim = 1, HumanRig? Bust = null, float Neck = 1, bool Wings = false, bool Headless = false,
    bool Ears = false, bool Palms = false, bool Rigid = false, bool NoHair = false, bool SkinTone = false)
{
    /// <summary>
    /// The part models (.glb paths) attached, in order; each has its spec in <c>parts/&lt;stem&gt;.json</c>. Empty is the one shared empty array,
    /// so options without parts compare equal.
    /// </summary>
    public IReadOnlyList<string> Attach { get; init; } = Array.Empty<string>();

    /// <summary>
    /// The options from the command line's optional arguments, in any order: <c>male</c> / <c>female</c>, a slimming strength,
    /// <c>bust=male|female</c>, <c>neck=&lt;share&gt;</c>, <c>wings</c>, <c>headless</c>, <c>ears</c> (headless with the model's ears) and
    /// <c>palms</c>, <c>rigid</c>, <c>nohair</c> (headless without the model's hair), <c>skintone</c> and <c>attach=&lt;part.glb&gt;</c>
    /// (repeatable); null for anything else.
    /// </summary>
    public static ImportOptions? Parse(IEnumerable<string> arguments)
    {
        var options = new ImportOptions(HumanRig.Male);
        foreach (string argument in arguments)
        {
            var next = Apply(options, argument);
            if (next is null) return null;

            options = next;
        }

        return options;
    }

    /// <summary>
    /// <paramref name="options"/> with one argument applied; null when it is not one.
    /// </summary>
    static ImportOptions? Apply(ImportOptions options, string argument)
    {
        if (HumanRig.Named(argument) is { } rig) return options with { Rig = rig };
        if (argument == "wings") return options with { Wings = true };
        if (argument == "headless") return options with { Headless = true };
        if (argument == "palms") return options with { Palms = true };
        if (argument == "rigid") return options with { Rigid = true };
        if (argument == "nohair") return options with { Headless = true, NoHair = true };
        if (argument == "skintone") return options with { SkinTone = true };
        if (argument == "ears") return options with { Headless = true, Ears = true };
        if (argument.StartsWith("attach=", StringComparison.Ordinal) && argument.Length > 7)
            return options with { Attach = [.. options.Attach, argument[7..]] };
        if (argument.StartsWith("bust=", StringComparison.Ordinal))
            return HumanRig.Named(argument[5..]) is { } bust ? options with { Bust = bust } : null;
        if (argument.StartsWith("neck=", StringComparison.Ordinal))
            return Number(argument[5..]) is { } neck ? options with { Neck = Math.Clamp(neck, 0.2f, 1.5f) } : null;

        return Number(argument) is { } slim ? options with { Slim = Math.Clamp(slim, 0, 1) } : null;
    }

    /// <summary>
    /// The invariant-culture number, or null.
    /// </summary>
    static float? Number(string text) =>
        float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value)
            ? value
            : null;
}
