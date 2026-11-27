using System.Numerics;
using Meitou.Data.Ogre;

namespace RaceImport;

/// <summary>
/// The bind pose of one of the game's human skeletons (<see cref="HumanRig.Skeleton"/>, 3ds Max Biped bone names <c>Bip01 ...</c>): each bone's
/// handle and rest position, in file units (0.1 m, Y up), by name.
/// </summary>
public sealed class KenshiSkeleton
{
    /// <summary>
    /// Bone name to handle and rest position.
    /// </summary>
    readonly Dictionary<string, (ushort Handle, Vector3 Position)> _bones;

    /// <summary>
    /// Bone handle to name.
    /// </summary>
    readonly Dictionary<ushort, string> _names;

    /// <summary>
    /// A skeleton of the given bones.
    /// </summary>
    KenshiSkeleton(Dictionary<string, (ushort Handle, Vector3 Position)> bones)
    {
        _bones = bones;
        _names = bones.ToDictionary(p => p.Value.Handle, p => p.Key);
    }

    /// <summary>
    /// The name of the bone with that handle, null for none.
    /// </summary>
    public string? Name(ushort handle) => _names.GetValueOrDefault(handle);

    /// <summary>
    /// Whether the skeleton has a bone of that name.
    /// </summary>
    public bool Has(string name) => _bones.ContainsKey(name);

    /// <summary>
    /// The bone's handle. Throws when there is no such bone.
    /// </summary>
    public ushort Handle(string name) => _bones[name].Handle;

    /// <summary>
    /// The bone's rest position. Throws when there is no such bone.
    /// </summary>
    public Vector3 Position(string name) => _bones[name].Position;

    /// <summary>
    /// The skeleton of <paramref name="rig"/> in the game in <paramref name="kenshiFolder"/> (<see cref="GameFile"/>).
    /// </summary>
    public static KenshiSkeleton Load(string kenshiFolder, HumanRig rig) =>
        Read(OgreSkeletonReader.Read(File.ReadAllBytes(GameFile(kenshiFolder, rig.Skeleton))));

    /// <summary>
    /// The first file of that name under the game's <c>data</c> folder (letter case ignored). Throws when there is none.
    /// </summary>
    public static string GameFile(string kenshiFolder, string fileName)
    {
        string data = Path.Combine(kenshiFolder, "data");
        var options = new EnumerationOptions { RecurseSubdirectories = true, MatchCasing = MatchCasing.CaseInsensitive };
        string path = Directory.Exists(data) ? Directory.EnumerateFiles(data, fileName, options).FirstOrDefault() ?? "" : "";
        if (path.Length == 0) throw new FileNotFoundException($"no {fileName} under {data}");

        return path;
    }

    /// <summary>
    /// The rest positions of a read skeleton: each bone's local transform (scale, rotation, translation) under its parent's.
    /// </summary>
    public static KenshiSkeleton Read(OgreSkeleton skeleton)
    {
        var byHandle = skeleton.Bones.ToDictionary(b => b.Handle);
        var worlds = new Dictionary<ushort, Matrix4x4>();
        var bones = new Dictionary<string, (ushort, Vector3)>();
        foreach (var bone in skeleton.Bones) bones[bone.Name] = (bone.Handle, World(bone, byHandle, worlds).Translation);

        return new KenshiSkeleton(bones);
    }

    /// <summary>
    /// A bone's rest transform in the skeleton's space, memoised in <paramref name="worlds"/>.
    /// </summary>
    static Matrix4x4 World(OgreBone bone, Dictionary<ushort, OgreBone> byHandle, Dictionary<ushort, Matrix4x4> worlds)
    {
        if (worlds.TryGetValue(bone.Handle, out var known)) return known;

        var local = Matrix4x4.CreateScale(bone.Scale)
            * Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(bone.Orientation))
            * Matrix4x4.CreateTranslation(bone.Position);
        var world = bone.Parent is { } parent ? local * World(byHandle[parent], byHandle, worlds) : local;
        worlds[bone.Handle] = world;

        return world;
    }
}
