namespace TheRainDriedUp;

/// <summary>
/// A character the mod adds: a race with an imported body (docs/character-pipeline.md) and a recruitable character of that race.
/// </summary>
/// <param name="Name">The character's and the race's name in game.</param>
/// <param name="Stem">The body's file stem in <c>TheRainDriedUp/items/races</c>.</param>
/// <param name="RaceId">The RACE record's id.</param>
/// <param name="CharacterId">The CHARACTER record's id.</param>
/// <param name="Note">What the recruit window says about this version.</param>
public sealed record ModCharacter(string Name, string Stem, string RaceId, string CharacterId, string Note)
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
    /// Raiden retopologized to a Kenshi-style budget.
    /// </summary>
    public static readonly ModCharacter Raiden = new("Raiden", "theraindriedup_raiden", "10-" + FileName, "11-" + FileName,
        "Retopologized: 6,997 triangles, one 2048 atlas");

    /// <summary>
    /// Raiden from the original model, every triangle kept, for comparison.
    /// </summary>
    public static readonly ModCharacter RaidenFull = new("Raiden (full poly)", "theraindriedup_raidenfull", "12-" + FileName, "13-" + FileName,
        "Original model: 52,265 triangles, one 2048 atlas");

    /// <summary>
    /// Every character, in the recruit window's order.
    /// </summary>
    public static readonly IReadOnlyList<ModCharacter> All = [Raiden, RaidenFull];
}
