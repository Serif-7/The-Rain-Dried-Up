using System.Numerics;
using Meitou.Data.Ogre;

namespace StructureImport;

/// <summary>
/// Puts an item model on the hand bones of one of the game's human skeletons (docs/structures.md "Worn on the hands"): a mesh skinned to
/// <c>Bip01 R Hand</c> / <c>Bip01 L Hand</c> and their forearms, in the skeleton's bind pose, as the engine draws a worn unarmed weapon's
/// <c>mesh</c> (engine docs/characters.md "Worn unarmed weapons").
/// </summary>
public static class WornItem
{
    /// <summary>
    /// The game's skeleton files a man's and a woman's worn mesh are skinned to.
    /// </summary>
    public const string MaleSkeleton = "male_skeleton.skeleton", FemaleSkeleton = "female_skeleton.skeleton";

    /// <summary>
    /// How far the palm's centre sits past the wrist (the hand bone's origin) along the hand bone's x, in game units. <b>Observed</b>: the
    /// vertices of <c>human_male.mesh</c> weighted to <c>Bip01 R Hand</c> reach 1.8 units past the wrist.
    /// </summary>
    const float PalmUnits = 0.7f;

    /// <summary>
    /// Half the stretch about the wrist over which a vertex's weight passes from the forearm to the hand, in game units.
    /// </summary>
    const float BlendUnits = 0.3f;

    /// <summary>
    /// The game folder for the mod folder <paramref name="mod"/> (in <c>&lt;Kenshi&gt;/mods</c>): <c>KENSHI_PATH</c> when set, else the mods
    /// folder's parent; null when that has no <c>data</c> folder (a stand-in mods folder, as in CI).
    /// </summary>
    public static string? FindGame(string mod)
    {
        string? set = Environment.GetEnvironmentVariable("KENSHI_PATH");
        string? game = string.IsNullOrEmpty(set) ? Path.GetDirectoryName(Path.GetDirectoryName(mod)) : set;
        return game is not null && Directory.Exists(Path.Combine(game, "data")) ? game : null;
    }

    /// <summary>
    /// The first file named <paramref name="name"/> under the game's <c>data</c> folder (letter case ignored), null for none.
    /// </summary>
    public static string? FindFile(string game, string name) =>
        Directory.EnumerateFiles(Path.Combine(game, "data"), "*.skeleton", SearchOption.AllDirectories)
            .FirstOrDefault(f => Path.GetFileName(f).Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The placed item (<see cref="MeshMath.Place"/>: game units, on its base, centred) on the hands <paramref name="worn"/> names
    /// (<see cref="ItemSpec.WornValues"/>), placed and skinned as <paramref name="fit"/> says, on the skeleton file <paramref name="skeletonPath"/>.
    /// </summary>
    public static (SurfaceMesh Mesh, MeshSkin Skin) Wear(SurfaceMesh item, string worn, WornFit fit, string skeletonPath)
    {
        var skeleton = OgreSkeletonReader.Read(File.ReadAllBytes(skeletonPath));
        var world = BindPose(skeleton);
        var itemToHand = ItemToHand(item, fit);

        var output = new SurfaceMesh { Look = item.Look };
        var weights = new List<(ushort, float)[]>();
        foreach (string side in Sides(worn)) AddSide(item, itemToHand, Bone(skeleton, $"Bip01 {side} Hand"), fit.Forearm, world, output,
                weights);
        return (output, new MeshSkin(Path.GetFileName(skeletonPath), [.. weights]));
    }

    /// <summary>
    /// The skeleton sides (<c>R</c>, <c>L</c>) a <see cref="ItemSpec.Worn"/> value covers.
    /// </summary>
    static string[] Sides(string worn) => worn switch
    {
        "hands" => ["R", "L"],
        "right hand" => ["R"],
        _ => ["L"],
    };

    /// <summary>
    /// Adds one copy of the item on <paramref name="hand"/>: each vertex in the bind pose, weighted to the hand past the wrist and to the forearm
    /// before it, blended over <see cref="BlendUnits"/> each way (all to the forearm when <paramref name="forearm"/>). Both sides use the same
    /// placement in the hand bone's frame, so the item's top stays on the back of each hand.
    /// </summary>
    static void AddSide(SurfaceMesh item, Matrix4x4 itemToHand, OgreBone hand, bool forearm, Dictionary<ushort, Matrix4x4> world,
            SurfaceMesh output, List<(ushort, float)[]> weights)
    {
        ushort forearmBone = hand.Parent ?? throw new InvalidDataException($"{hand.Name} has no parent bone");
        var handToBind = world[hand.Handle];
        var itemToBind = itemToHand * handToBind;
        int first = output.Vertices.Count;

        foreach (var v in item.Vertices)
        {
            var local = Vector3.Transform(v.Position, itemToHand);
            float handWeight = forearm ? 0 : HandWeight(local.X);
            var normal = Vector3.Normalize(Vector3.TransformNormal(v.Normal, itemToBind));
            output.Vertices.Add(new MeshVertex(Vector3.Transform(local, handToBind), normal, v.Uv));
            weights.Add([(hand.Handle, handWeight), (forearmBone, 1 - handWeight)]);
        }

        output.Indices.AddRange(item.Indices.Select(i => i + first));
    }

    /// <summary>
    /// The hand bone's share of a vertex <paramref name="pastWrist"/> units past the wrist along the hand bone's x: 0 before
    /// <see cref="BlendUnits"/> short of it, 1 beyond <see cref="BlendUnits"/> past it.
    /// </summary>
    static float HandWeight(float pastWrist) => Math.Clamp((pastWrist + BlendUnits) / (2 * BlendUnits), 0, 1);

    /// <summary>
    /// The placed item's space to the hand bone's: its grip point (<see cref="WornFit.Grip"/> along X from its fist end,
    /// <see cref="WornFit.GripHeight"/> up Y from its base, centred on Z) to the palm's centre, its fist end (-X) along the hand bone's +x (towards
    /// the fingers), its top (+Y) along the bone's +z (the back of the hand: the bind pose's palms face the thighs) and its +Z along the bone's +y;
    /// then turned <see cref="WornFit.Tilt"/> about the palm's centre, its fist end towards the back of the hand.
    /// </summary>
    static Matrix4x4 ItemToHand(SurfaceMesh item, WornFit fit)
    {
        var (min, max) = MeshMath.Bounds([item]);
        var size = max - min;
        var gripPoint = new Vector3(min.X + fit.Grip * size.X, min.Y + fit.GripHeight * size.Y, (min.Z + max.Z) / 2);

        // Rows are where the item's x, y and z axes go (System.Numerics row vectors); a rotation, so the winding is kept.
        var turn = new Matrix4x4(
            -1, 0, 0, 0,
            0, 0, 1, 0,
            0, 1, 0, 0,
            0, 0, 0, 1);

        // About the bone's y (across the hand): its +x (towards the fingers) towards its +z (the back of the hand).
        var tilt = Matrix4x4.CreateRotationY(-fit.Tilt * MathF.PI / 180);
        return Matrix4x4.CreateTranslation(-gripPoint) * turn * tilt * Matrix4x4.CreateTranslation(PalmUnits, 0, 0);
    }

    /// <summary>
    /// The bone named <paramref name="name"/>. Throws when the skeleton has none.
    /// </summary>
    static OgreBone Bone(OgreSkeleton skeleton, string name) =>
        skeleton.Bones.FirstOrDefault(b => b.Name == name) ?? throw new InvalidDataException($"The skeleton has no bone {name}");

    /// <summary>
    /// Each bone's bind pose in the skeleton's space: its scale, orientation and position, then its parent's.
    /// </summary>
    static Dictionary<ushort, Matrix4x4> BindPose(OgreSkeleton skeleton)
    {
        var world = new Dictionary<ushort, Matrix4x4>();
        foreach (var bone in skeleton.Bones) Of(bone);
        return world;

        Matrix4x4 Of(OgreBone bone)
        {
            if (world.TryGetValue(bone.Handle, out var known)) return known;

            var turn = Matrix4x4.CreateFromQuaternion(bone.Orientation);
            var local = Matrix4x4.CreateScale(bone.Scale) * turn * Matrix4x4.CreateTranslation(bone.Position);
            var parent = bone.Parent is { } p ? skeleton.FindBone(p) : null;
            var placed = parent is null ? local : local * Of(parent);
            world[bone.Handle] = placed;
            return placed;
        }
    }
}

/// <summary>
/// How a worn item sits on the hand (docs/structures.md "Worn on the hands").
/// </summary>
/// <param name="Grip">Where the palm's centre sits along the model's length, 0 at its fist end (-X) and 1 at its arm opening (+X).</param>
/// <param name="GripHeight">Where the palm's centre sits up the model, 0 at its base and 1 at its top.</param>
/// <param name="Tilt">Degrees the model is turned about the palm's centre, its fist end towards the back of the hand.</param>
/// <param name="Forearm">Whether the whole model is skinned to the forearm, so a bent wrist leaves it on the arm's line.</param>
public sealed record WornFit(float Grip, float GripHeight, float Tilt, bool Forearm);
