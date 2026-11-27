namespace TheRainDriedUp;

/// <summary>
/// A character the mod adds: a race with an imported body (docs/character-pipeline.md) and a recruitable character of that race.
/// </summary>
/// <param name="Name">The character's and the race's name in game.</param>
/// <param name="Stem">The body's file stem in <c>TheRainDriedUp/items/races</c>.</param>
/// <param name="RaceId">The RACE record's id.</param>
/// <param name="CharacterId">The CHARACTER record's id.</param>
/// <param name="Note">What the recruit window says about this version.</param>
/// <param name="NormalMap">Whether the import wrote a <c>_n.dds</c> (only for a model with a normal map).</param>
public sealed record ModCharacter(string Name, string Stem, string RaceId, string CharacterId, string Note, bool NormalMap = true)
{
    /// <summary>
    /// A body file's path as records name it: under the deployed mod folder.
    /// </summary>
    public string File(string suffix) => $@".\mods\{Characters.Folder}\items\races\{Stem}{suffix}";
}

/// <summary>
/// The mod's characters, and the ids of its records (<c>&lt;n&gt;-TheRainDriedUp.mod</c>).
/// </summary>
public static class Characters
{
    /// <summary>
    /// The mod's folder in Kenshi's mods folder, and its .mod file.
    /// </summary>
    public const string Folder = "TheRainDriedUp", FileName = Folder + ".mod";

    /// <summary>
    /// The RACE_GROUP the character editor and the game list the Raidens under; each Raiden is one of its subraces.
    /// </summary>
    public const string RaidenGroup = "30-" + FileName, RaidenGroupName = "Raiden";

    /// <summary>
    /// Raiden retopologized from the original model to a Kenshi-style budget.
    /// </summary>
    public static readonly ModCharacter Raiden = new("Raiden (retopo)", "theraindriedup_raiden", "10-" + FileName, "11-" + FileName,
        "The original model retopologized: 6,971 triangles");

    /// <summary>
    /// Raiden from the original model, every triangle kept, for comparison.
    /// </summary>
    public static readonly ModCharacter RaidenFull = new("Raiden (full poly)", "theraindriedup_raidenfull", "12-" + FileName, "13-" + FileName,
        "The original model, every triangle kept: 52,265 triangles");

    /// <summary>
    /// Raiden 1 to 5: Meshy models rigged with the 3ds Max Biped skeleton, imported as they are.
    /// </summary>
    public static readonly ModCharacter Raiden1 = new("Raiden 1", "theraindriedup_raiden1", "14-" + FileName, "15-" + FileName,
        "Dark armour with blade fins: 14,962 triangles", NormalMap: false),
        Raiden2 = new("Raiden 2", "theraindriedup_raiden2", "16-" + FileName, "17-" + FileName,
        "Dark armour, slim: 5,294 triangles"),
        Raiden3 = new("Raiden 3", "theraindriedup_raiden3", "18-" + FileName, "19-" + FileName,
        "Scavenger plates and rags: 7,569 triangles"),
        Raiden4 = new("Raiden 4", "theraindriedup_raiden4", "20-" + FileName, "21-" + FileName,
        "Visor and wrapped vest: 4,989 triangles"),
        Raiden5 = new("Raiden 5", "theraindriedup_raiden5", "22-" + FileName, "23-" + FileName,
        "Pale wasteland armour: 5,312 triangles");

    /// <summary>
    /// Every character, in the recruit window's order; all are subraces of <see cref="RaidenGroup"/>.
    /// </summary>
    public static readonly IReadOnlyList<ModCharacter> All = [Raiden, RaidenFull, Raiden1, Raiden2, Raiden3, Raiden4, Raiden5];
}
