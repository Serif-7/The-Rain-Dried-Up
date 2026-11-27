using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Modding;

namespace TheRainDriedUp;

/// <summary>
/// The characters' records and their race group, built in the data phase from the loaded game's own (docs/character-pipeline.md "Make it a race"): each RACE a copy
/// of the Greenlander with the imported body, no head or hair (the body has its own), one gender; each CHARACTER a Drifter of that race with
/// no kit, money or dialogue. Ported from KenshiRaces' RaceRecords (GPL-3.0).
/// </summary>
public static class CharacterRecords
{
    /// <summary>
    /// The race the races copy.
    /// </summary>
    public const string Greenlander = "17-gamedata.quack", GreenlanderName = "Greenlander";

    /// <summary>
    /// The character the characters copy.
    /// </summary>
    public const string Drifter = "2205-gamedata.base", DrifterName = "Drifter";

    /// <summary>
    /// A CHARACTER's item lists, emptied so a spawn carries nothing.
    /// </summary>
    static readonly string[] KitLists = ["backpack", "clothing", "inventory", "shopping", "weapons"];

    /// <summary>
    /// Adds every character's race and character, and the Raiden race group that lists the races as its subraces.
    /// </summary>
    public static void Add(ModDataContext c)
    {
        foreach (var character in Characters.All)
        {
            FillRace(c.Add(character.RaceId, FcsRecordType.Race, character.Name), c.Data, character);
            FillCharacter(c.Add(character.CharacterId, FcsRecordType.Character, character.Name), c.Data, character);
        }

        FillGroup(c.Add(Characters.RaidenGroup, FcsRecordType.RaceGroup, Characters.RaidenGroupName));
    }

    /// <summary>
    /// The race group: every character's race as one of its subraces.
    /// </summary>
    public static void FillGroup(FcsRecord group)
    {
        group.References["races"] = [.. Characters.All.Select(c => new FcsReference(c.RaceId, 0, 0, 0))];
    }

    /// <summary>
    /// A race: the Greenlander's fields with the character's body for both genders, its own head kept by listing no human one.
    /// </summary>
    public static void FillRace(FcsRecord race, GameDatabase game, ModCharacter character)
    {
        CopyFrom(Model(game, FcsRecordType.Race, Greenlander, GreenlanderName), race);
        race.Bools["single gender"] = true;
        race.Bools["no shirts"] = true;
        race.Bools["playable"] = false;
        SetBody(race, "male", character);
        SetBody(race, "female", character);

        // A race listing no head draws none: the body carries its own head and hair.
        race.References["heads male"] = [];
        race.References["heads female"] = [];
        race.References["hairs"] = [];
        race.Ints["morph num"] = 0;
    }

    /// <summary>
    /// A character: the Drifter's fields, of the character's race, with nothing to carry and nothing to say.
    /// </summary>
    public static void FillCharacter(FcsRecord record, GameDatabase game, ModCharacter character)
    {
        CopyFrom(Model(game, FcsRecordType.Character, Drifter, DrifterName), record);
        record.Ints["female chance"] = 0;
        record.Ints["money min"] = 0;
        record.Ints["money max"] = 0;
        record.Ints["bounty chance"] = 0;
        foreach (string list in KitLists) record.References[list] = [];

        record.References["race"] = [new FcsReference(character.RaceId, 100, 0, 0)];
        record.References["dialogue"] = [];
    }

    /// <summary>
    /// One gender's body files: mesh, colour, normals and part map; no skinny or strong normals and no skin-tone mask.
    /// </summary>
    static void SetBody(FcsRecord race, string gender, ModCharacter character)
    {
        race.Filenames[$"{gender} mesh"] = character.File(".mesh");
        race.Filenames[$"body texture {gender}"] = character.File("_d.dds");
        race.Filenames[$"nm {gender}"] = character.NormalMap ? character.File("_n.dds") : "";
        race.Filenames[$"nm {gender} skinny"] = "";
        race.Filenames[$"nm {gender} strong"] = "";
        race.Filenames[$"body mask {gender}"] = "";
        race.Filenames[$"part map {gender}"] = character.File("_parts.png");
    }

    /// <summary>
    /// The game record to copy: by id, else the first of its type with that name.
    /// </summary>
    static GameRecord Model(GameDatabase game, FcsRecordType type, string id, string name) =>
        game.Find(id)
            ?? game.OfType(type).Where(r => r.Name == name).OrderBy(r => r.StringId, StringComparer.Ordinal).FirstOrDefault()
            ?? throw new InvalidOperationException($"The game data has no {type} '{name}' ({id}) to copy.");

    /// <summary>
    /// Copies every field and reference list of a game record into a new one.
    /// </summary>
    static void CopyFrom(GameRecord from, FcsRecord to)
    {
        foreach (var (key, value) in from.Bools) to.Bools[key] = value;
        foreach (var (key, value) in from.Floats) to.Floats[key] = value;
        foreach (var (key, value) in from.Ints) to.Ints[key] = value;
        foreach (var (key, value) in from.Vector3S) to.Vector3S[key] = value;
        foreach (var (key, value) in from.Vector4S) to.Vector4S[key] = value;
        foreach (var (key, value) in from.Strings) to.Strings[key] = value;
        foreach (var (key, value) in from.Filenames) to.Filenames[key] = value;
        foreach (string list in from.ReferenceLists) to.References[list] = [.. from.GetReferences(list).Select(Link)];
    }

    /// <summary>
    /// A game reference as a record's reference.
    /// </summary>
    static FcsReference Link(GameReference link) => new(link.TargetStringId, link.Values.Value0, link.Values.Value1, link.Values.Value2);
}
