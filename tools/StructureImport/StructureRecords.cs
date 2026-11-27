using Meitou.Data.Fcs;

namespace StructureImport;

/// <summary>
/// One imported part: its mesh and textures by bare file name (the game looks resources up by bare name, so the folder never matters) and its
/// collision file, "" for none; <paramref name="AlphaThreshold"/> (0..255) when the material is cut out.
/// </summary>
public sealed record PartFiles(string Mesh, string Collision, string Diffuse, string Normal, string Metal, int? AlphaThreshold, string MaterialName);

/// <summary>
/// The records of one structure (docs/structures.md "Records"): a BUILDING, one BUILDING_PART per material with its MATERIAL_SPEC, and the
/// research that unlocks it. Every field the game's own decorations carry is written (field set of Camping Tent 1,
/// <c>18026-small_changes_otto.mod</c>), so nothing is left to a default.
/// </summary>
public static class StructureRecords
{
    /// <summary>
    /// The string id of the research every new game starts with finished (<c>_Default Start</c>).
    /// </summary>
    public const string DefaultStart = "5359-gamedata.base";

    /// <summary>
    /// Flags of a record the mod adds: save counter 1.
    /// </summary>
    const uint NewFlags = 0x10;

    /// <summary>
    /// Flags of a game record the mod changes: MODIFIED, save counter 1.
    /// </summary>
    const uint ChangedFlags = 0x11;

    /// <summary>
    /// The structure's BUILDING, parts and materials, with string ids from <paramref name="ids"/>.
    /// </summary>
    public static List<FcsRecord> Build(StructureSpec spec, string name, float heightUnits, IReadOnlyList<PartFiles> parts, RecordIds ids)
    {
        var records = new List<FcsRecord>();
        var partLinks = new List<FcsReference>();
        for (int i = 0; i < parts.Count; i++)
        {
            var material = MaterialSpec(ids.Material(i), $"{name} {parts[i].MaterialName}".Trim(), parts[i]);
            var part = Part(ids.Part(i), $"{name} part {i + 1}", parts[i], material.StringId, spec);
            records.AddRange([material, part]);
            partLinks.Add(new FcsReference(part.StringId, 0, 100, 0));
        }

        records.Insert(0, Building(ids.Building, name, heightUnits, spec, partLinks));
        return records;
    }

    /// <summary>
    /// The change to a game RESEARCH (or one of the mod's) that adds <paramref name="building"/> to its <c>enable buildings</c>.
    /// </summary>
    public static FcsRecord Unlock(string research, string building)
    {
        var record = new FcsRecord { StringId = research, Flags = ChangedFlags, RecordType = FcsRecordType.Research };
        record.References["enable buildings"] = [new FcsReference(building, 0, 0, 0)];
        return record;
    }

    /// <summary>
    /// The BUILDING: no function, the given parts, built from Building Materials.
    /// </summary>
    static FcsRecord Building(string id, string name, float heightUnits, StructureSpec spec, List<FcsReference> parts)
    {
        var r = New(id, FcsRecordType.Building, name);
        foreach (var (key, value) in BuildingBools(spec)) r.Bools[key] = value;
        foreach (var (key, value) in BuildingFloats(spec, heightUnits)) r.Floats[key] = value;
        foreach (var (key, value) in BuildingInts(spec)) r.Ints[key] = value;
        r.Strings["Description"] = spec.Description;
        r.Strings["building category"] = spec.Category;
        r.Strings["building group"] = spec.Group;
        r.Strings["inventory content name"] = "";
        r.Filenames["destroyed boundary"] = "";
        r.Filenames["distant mesh"] = "";
        r.Filenames["icon"] = "";
        r.References["parts"] = parts;
        return r;
    }

    /// <summary>
    /// The BUILDING's flags.
    /// </summary>
    static (string, bool)[] BuildingBools(StructureSpec spec) =>
    [
        ("allow animals", false), ("always locks", false), ("cavernous", false), ("ceiling placement", false),
        ("creates player town", spec.CreatesTown), ("destroyed navmesh", false), ("enforce ceiling", false), ("has inventory", false),
        ("has lock", false), ("interior terrain", false), ("is exterior furniture", spec.Placement is "outdoor" or "both"), ("is feature", false),
        ("is foliage", false), ("is gateway", false), ("is interior furniture", spec.Placement is "indoor" or "both"), ("is node", false),
        ("is shelter", false), ("is sign", false), ("is wall gate", false), ("links together", false), ("match slope", false),
    ];

    /// <summary>
    /// The BUILDING's numbers; <c>building height</c> is the model's, which the construction scaffold climbs to.
    /// </summary>
    static (string, float)[] BuildingFloats(StructureSpec spec, float heightUnits) =>
    [
        ("build speed mult", spec.BuildSpeed), ("build threshold", 0), ("building height", heightUnits), ("door comes out", 0),
        ("door move dist", 30), ("door move speed", 1), ("hardness", 30), ("interior integrity", 100), ("link length", 0), ("max slope", 25),
        ("output rate", 1), ("scale", 1), ("stackable bonus mult", 1), ("vertical position tolerance", 20),
    ];

    /// <summary>
    /// The BUILDING's integers and enums.
    /// </summary>
    static (string, int)[] BuildingInts(StructureSpec spec) =>
    [
        ("build materials", spec.Materials), ("desirability", 0), ("door axis", 0), ("door navmesh axis", 0), ("door type", 0),
        ("exterior material", 2), ("interior ambience", 0), ("interior material", 3), ("itemtype limit", 4), ("link type", 0),
        ("lock level bonus", 0), ("max operators", -1), ("node type", 1), ("path mode", 0), ("power capacity", 0), ("power output", 0),
        ("select sound", 0), ("storage size height", 18), ("storage size width", 18),
    ];

    /// <summary>
    /// A BUILDING_PART: the mesh at the building's origin, its collision, solid, and its MATERIAL_SPEC.
    /// </summary>
    static FcsRecord Part(string id, string name, PartFiles files, string material, StructureSpec spec)
    {
        var r = New(id, FcsRecordType.BuildingPart, name);
        foreach (var (key, value) in PartBools(spec)) r.Bools[key] = value;
        foreach (var key in PartZeroFloats) r.Floats[key] = 0;
        r.Floats["density"] = 1;
        r.Floats["wind speed rotation danger"] = 200;
        r.Floats["wind speed rotation max"] = 100;
        foreach (var key in PartZeroInts) r.Ints[key] = 0;
        r.Ints["material match chance"] = 100;
        r.Ints["rotation axis"] = 1;
        r.Filenames["destroyed collision"] = "";
        r.Filenames["destroyed mesh"] = "";
        r.Filenames["phs or mesh"] = files.Mesh;
        r.Filenames["xml collision"] = files.Collision;
        r.References["material"] = [new FcsReference(material, 0, 0, 0)];
        return r;
    }

    /// <summary>
    /// The part's flags: solid unless the model has no collision.
    /// </summary>
    static (string, bool)[] PartBools(StructureSpec spec) =>
    [
        ("above ground", true), ("affects footprint", false), ("is door", false), ("is for position marker", false), ("is stairs", false),
        ("is unwalkable roof", false), ("passable", spec.Collision == "none"),
    ];

    /// <summary>
    /// The part's numbers that are 0.
    /// </summary>
    static readonly string[] PartZeroFloats =
    [
        "footprint vertical", "offset X", "offset Y", "offset Z", "rotation speed max", "rotation speed min", "wind speed rotation min",
    ];

    /// <summary>
    /// The part's integers that are 0.
    /// </summary>
    static readonly string[] PartZeroInts = ["building floor", "ground type", "material match base chance", "rotation function"];

    /// <summary>
    /// A MATERIAL_SPEC: the three textures; a cut-out material is <c>material type</c> 1 (ALPHA: the normal map's alpha below <c>alpha
    /// threshold</c> / 255 is cut), else 0.
    /// </summary>
    static FcsRecord MaterialSpec(string id, string name, PartFiles files)
    {
        var r = New(id, FcsRecordType.MaterialSpec, name);
        r.Filenames["metalness map"] = files.Metal;
        r.Filenames["metalness map 2"] = "";
        r.Filenames["normal map"] = files.Normal;
        r.Filenames["normal map 2"] = "";
        r.Filenames["texture map"] = files.Diffuse;
        r.Filenames["texture map 2"] = "";
        r.Floats["scaffolding tex scale"] = 4;
        r.Floats["specular mult"] = 1;
        r.Floats["tile X"] = 1;
        r.Floats["tile Y"] = 1;
        r.Ints["material type"] = files.AlphaThreshold is null ? 0 : 1;
        if (files.AlphaThreshold is { } threshold) r.Ints["alpha threshold"] = threshold;
        return r;
    }

    /// <summary>
    /// An item model's MATERIAL_SPECS_CLOTHING, the material an ITEM's <c>material</c> list names for its mesh on the ground, in hand and in its
    /// icon (engine docs/formats/runtime-materials.md "Callers"): diffuse and normal map; a cut-out material is <c>material type</c> 1 (ALPHA: the
    /// normal map's alpha), else 0 (DEFAULT). Metalness has no field here.
    /// </summary>
    public static FcsRecord ItemMaterial(string id, string name, PartFiles files)
    {
        var r = New(id, FcsRecordType.MaterialSpecsClothing, name);
        r.Filenames["normal map"] = files.Normal;
        r.Filenames["texture map"] = files.Diffuse;
        r.Floats["specular mult"] = 1;
        r.Ints["material type"] = files.AlphaThreshold is null ? 0 : 1;
        return r;
    }

    /// <summary>
    /// A new record of the mod.
    /// </summary>
    static FcsRecord New(string id, FcsRecordType type, string name) => new() { StringId = id, Flags = NewFlags, Name = name, RecordType = type };
}
