using System.Numerics;

namespace RaceImport;

/// <summary>
/// Fits a Biped (<c>Bip001 ...</c>) body onto the game's skeleton (docs/race-import.md "How the fit works"): the model is scaled by head height,
/// each limb bone is turned to point where the game's does and stretched to its length, the torso is fitted as one rigid piece, and the feet
/// are squashed so the soles land on the ground. Each vertex follows its own joints, a joint the game lacks following its nearest ancestor.
/// </summary>
public sealed class BodyFit
{
    #region Rig

    /// <summary>
    /// The model's joint names start with this; the rest of the name is the game's bone after <see cref="KenshiPrefix"/>.
    /// </summary>
    const string SourcePrefix = "Bip001 ";

    /// <summary>
    /// The game's Biped bone names start with this.
    /// </summary>
    const string KenshiPrefix = "Bip01 ";

    /// <summary>
    /// Each limb or spine bone's direction: towards this child, named the same in both rigs unless a pair (model child, game child) is given.
    /// </summary>
    static readonly Dictionary<string, (string Source, string Kenshi)> Directions = new()
    {
        ["Spine"] = ("Spine1", "Spine1"),
        ["Spine1"] = ("Spine2", "Spine2"),
        ["Spine2"] = ("Neck", "Neck"),
        ["Neck"] = ("Head", "Head"),
        ["Head"] = ("mixamorig:HeadTop_End", "HeadNub"),
        ["L Clavicle"] = ("L UpperArm", "L UpperArm"),
        ["L UpperArm"] = ("L Forearm", "L Forearm"),
        ["L Forearm"] = ("L Hand", "L Hand"),
        ["R Clavicle"] = ("R UpperArm", "R UpperArm"),
        ["R UpperArm"] = ("R Forearm", "R Forearm"),
        ["R Forearm"] = ("R Hand", "R Hand"),
        ["L Thigh"] = ("L Calf", "L Calf"),
        ["L Calf"] = ("L Foot", "L Foot"),
        ["L Foot"] = ("L Toe0", "L Toe0"),
        ["R Thigh"] = ("R Calf", "R Calf"),
        ["R Calf"] = ("R Foot", "R Foot"),
        ["R Foot"] = ("R Toe0", "R Toe0"),
    };

    /// <summary>
    /// Bones without a usable child in both rigs take their parent's rotation (null: none), unstretched.
    /// </summary>
    static readonly Dictionary<string, string?> Inherited = new()
    {
        ["L Hand"] = "L Forearm",
        ["R Hand"] = "R Forearm",
        ["L Toe0"] = "L Foot",
        ["R Toe0"] = "R Foot",
        ["Pelvis"] = null,
    };

    /// <summary>
    /// Bones fitted as one rigid torso, so the chest keeps its shape (per-bone fits shear it where spine and clavicle weights blend).
    /// </summary>
    static readonly string[] Torso = ["Pelvis", "Spine", "Spine1", "Spine2", "Neck", "L Clavicle", "R Clavicle"];

    /// <summary>
    /// The joints the torso is fitted to: where the head, arms and legs attach. Pelvis and spine joints sit differently in each rig (the
    /// game's pelvis is level with its hips, a Biped export's above them), and fitting to them dropped the chest about a unit.
    /// </summary>
    static readonly string[] TorsoAnchors = ["Neck", "L UpperArm", "R UpperArm", "L Thigh", "R Thigh"];

    /// <summary>
    /// How far the head moves from where the torso carries it towards the game's head joint (0 none, 1 all the way). The game's neck rises
    /// about a unit above its shoulders and the fitted models' barely does, so moving it all the way stretched their necks to about three
    /// times their length; half way keeps the neck near the game's length, the head mesh sitting below its bone.
    /// </summary>
    const float HeadReach = 0.5f;

    /// <summary>
    /// The least and most a bone is stretched.
    /// </summary>
    const float MinStretch = 0.6f, MaxStretch = 1.6f;

    /// <summary>
    /// A hand turned less than this about the forearm (radians) is left as the model has it; only a hand turned the wrong way (palms forward or
    /// back where the game's face the thighs) is twisted.
    /// </summary>
    const float MinHandTwist = 0.5f;

    /// <summary>
    /// A hand whose breadth is less than this many times its thickness across the forearm has no clear palm, and is left as it is.
    /// </summary>
    const float MinHandFlatness = 1.1f;

    /// <summary>
    /// The share of a hand's twist the forearm takes, so the turn spreads over wrist and elbow instead of wringing one of them.
    /// </summary>
    const float ForearmTwistShare = 0.5f;

    /// <summary>
    /// How far along the forearm (0 at the elbow, 1 at the wrist) a helper joint hung off the forearm must sit to follow the hand instead: Meshy
    /// hangs the thumb off the forearm at the wrist, which would leave it behind when the hand turns.
    /// </summary>
    const float WristJointReach = 0.75f;

    #endregion

    #region Fit

    /// <summary>
    /// The model being fitted.
    /// </summary>
    readonly SourceBody _body;

    /// <summary>
    /// The game's skeleton.
    /// </summary>
    readonly KenshiSkeleton _kenshi;

    /// <summary>
    /// Model joint rest positions by short name (<c>L Forearm</c>; non-Biped joints by their full name).
    /// </summary>
    readonly Dictionary<string, Vector3> _source;

    /// <summary>
    /// The neck's length as a share of the fitted one (1 keeps it): the head moves that share of the way from the neck joint.
    /// </summary>
    readonly float _neck;

    /// <summary>
    /// Whether the hands are turned palm to thigh (<see cref="TwistHands"/>); claws and pincers keep the model's turn.
    /// </summary>
    readonly bool _palms;

    /// <summary>
    /// The model's scale to the game's units: the game's head height over the model's (head joint above its lowest vertex).
    /// </summary>
    public float Scale { get; }

    /// <summary>
    /// The fit of each game bone the model has, by short name.
    /// </summary>
    public IReadOnlyDictionary<string, BoneFit> Fits { get; }

    /// <summary>
    /// The turn given to each twisted hand (<c>L</c>, <c>R</c>) about its forearm, radians (<see cref="TwistHands"/>).
    /// </summary>
    public Dictionary<string, float> HandTwists { get; } = [];

    /// <summary>
    /// The model's helper joints hung off a forearm at the wrist (thumbs) that carry vertices and follow the hand (<see cref="WristJointReach"/>).
    /// </summary>
    public int WristJoints { get; private set; }

    /// <summary>
    /// Works out the fits of <paramref name="body"/> onto <paramref name="kenshi"/>, the neck <paramref name="neck"/> times its fitted length,
    /// the hands turned palm to thigh with <paramref name="palms"/>.
    /// Throws when the model lacks a Biped joint the fit needs.
    /// </summary>
    public BodyFit(SourceBody body, KenshiSkeleton kenshi, float neck = 1, bool palms = false)
    {
        _body = body;
        _kenshi = kenshi;
        _neck = neck;
        _palms = palms;
        _source = body.JointNames.Select((n, i) => (Name: Short(n) ?? n, i)).GroupBy(p => p.Name).ToDictionary(g => g.Key, g => body.JointPositions[g.First().i]);
        Scale = HeadHeight(kenshi) / (Source("Head").Y - body.Positions.Min(p => p.Y));
        Fits = BuildFits();
    }

    /// <summary>
    /// The limb fits, then the torso's and the feet's, which replace those of their bones.
    /// </summary>
    Dictionary<string, BoneFit> BuildFits()
    {
        var fits = LimbFits();
        var torso = AddTorso(fits);
        LowerHead(fits, torso);
        AddFeet(fits);
        if (_palms) TwistHands(fits);
        return fits;
    }

    /// <summary>
    /// Each directed or inherited bone the model and the game both have: turned onto the game's direction and stretched to its length.
    /// </summary>
    Dictionary<string, BoneFit> LimbFits()
    {
        var turns = new Dictionary<string, (Matrix4x4 Rotation, Vector3? Along, float Ratio)>();
        foreach (var (bone, (sourceChild, kenshiChild)) in Directions)
        {
            var source = Source(sourceChild) - Source(bone);
            var kenshi = Kenshi(kenshiChild) - Kenshi(bone);
            var rotation = Matrix4x4.CreateFromQuaternion(Between(Vector3.Normalize(source), Vector3.Normalize(kenshi)));
            turns[bone] = (rotation, Vector3.Normalize(source), kenshi.Length() / (Scale * source.Length()));
        }

        foreach (var (bone, parent) in Inherited) turns[bone] = (parent is null ? Matrix4x4.Identity : turns[parent].Rotation, null, 1);

        var fits = new Dictionary<string, BoneFit>();
        foreach (var (bone, (rotation, along, ratio)) in turns)
        {
            if (!_source.ContainsKey(bone) || !_kenshi.Has(KenshiPrefix + bone)) continue;
            fits[bone] = new BoneFit(Source(bone), Kenshi(bone), rotation, along, Math.Clamp(ratio, MinStretch, MaxStretch));
        }

        return fits;
    }

    /// <summary>
    /// One rigid fit (rotation and translation, scale fixed) for the torso bones, onto the game's torso, shoulder and hip joints.
    /// </summary>
    (Matrix4x4 Rotation, Vector3 Translation) AddTorso(Dictionary<string, BoneFit> fits)
    {
        var anchors = TorsoAnchors.Where(b => _source.ContainsKey(b) && _kenshi.Has(KenshiPrefix + b)).ToArray();
        var (rotation, translation) = RigidFit.Fit([.. anchors.Select(b => Scale * Source(b))], [.. anchors.Select(Kenshi)]);
        foreach (var bone in Torso.Where(b => _source.ContainsKey(b) && _kenshi.Has(KenshiPrefix + b)))
        {
            var target = Vector3.TransformNormal(Scale * Source(bone), rotation) + translation;
            fits[bone] = new BoneFit(Source(bone), target, rotation, null, 1);
        }

        return (rotation, translation);
    }

    /// <summary>
    /// The head placed <see cref="HeadReach"/> of the way from where the rigid torso carries it to the game's head joint, then drawn towards the
    /// carried neck joint so the neck is <see cref="_neck"/> times as long.
    /// </summary>
    void LowerHead(Dictionary<string, BoneFit> fits, (Matrix4x4 Rotation, Vector3 Translation) torso)
    {
        if (!fits.TryGetValue("Head", out var head)) return;

        var carried = Vector3.TransformNormal(Scale * Source("Head"), torso.Rotation) + torso.Translation;
        var neck = Vector3.TransformNormal(Scale * Source("Neck"), torso.Rotation) + torso.Translation;
        var placed = Vector3.Lerp(carried, head.Target, HeadReach);
        fits["Head"] = head with { Target = neck + (placed - neck) * _neck };
    }

    /// <summary>
    /// The feet kept upright and squashed so the sole lands on the ground (paws and crab feet are taller than the game's foot); the toes move
    /// with the foot.
    /// </summary>
    void AddFeet(Dictionary<string, BoneFit> fits)
    {
        foreach (var side in new[] { "L", "R" })
        {
            string foot = side + " Foot";
            float squash = Kenshi(foot).Y / (Scale * Source(foot).Y);
            var fit = new BoneFit(Source(foot), Kenshi(foot), Matrix4x4.CreateScale(1, squash, 1), null, 1);
            fits[foot] = fit;
            fits[side + " Toe0"] = fit;
        }
    }

    /// <summary>
    /// Each hand turned about its forearm so its palm faces the thigh as the game's do (seen from the front, the game's hand shows its thumb
    /// edge, its breadth running front to back; **Verified** on <c>human_male.mesh</c> and <c>human_female.mesh</c>): the fit turns a bone
    /// onto the game's direction but keeps its twist, so a model whose palms face forward or down in its rest pose kept them so.
    /// </summary>
    void TwistHands(Dictionary<string, BoneFit> fits)
    {
        foreach (var side in new[] { "L", "R" })
        {
            string hand = side + " Hand", forearm = side + " Forearm";
            if (!fits.ContainsKey(hand) || !fits.ContainsKey(forearm)) continue;

            var axis = Vector3.Normalize(Kenshi(hand) - Kenshi(forearm));
            float angle = HandTwist(fits, hand, axis);
            if (MathF.Abs(angle) < MinHandTwist) continue;

            fits[hand] = Twisted(fits[hand], axis, angle);
            fits[forearm] = Twisted(fits[forearm], axis, angle * ForearmTwistShare);
            HandTwists[side] = angle;
        }
    }

    /// <summary>
    /// The turn about <paramref name="axis"/> (radians, within a quarter turn either way) that takes the fitted hand's breadth to the game's,
    /// front to back; 0 when the hand has no clear palm.
    /// </summary>
    float HandTwist(Dictionary<string, BoneFit> fits, string hand, Vector3 axis)
    {
        var points = HandVertices(fits, hand).Select(v => fits[hand].Apply(_body.Positions[v], Scale)).ToArray();
        if (points.Length < 8) return 0;

        var (breadth, flatness) = Breadth(points, axis);
        if (flatness < MinHandFlatness) return 0;

        var wanted = Vector3.Normalize(Flatten(Vector3.UnitZ, axis));
        float angle = MathF.Atan2(Vector3.Dot(Vector3.Cross(breadth, wanted), axis), Vector3.Dot(breadth, wanted));
        if (angle > MathF.PI / 2) angle -= MathF.PI;
        if (angle < -MathF.PI / 2) angle += MathF.PI;
        return angle;
    }

    /// <summary>
    /// The vertices mostly carried by <paramref name="hand"/> or the joints that follow it (fingers, claws).
    /// </summary>
    IEnumerable<int> HandVertices(Dictionary<string, BoneFit> fits, string hand)
    {
        var joints = Enumerable.Range(0, _body.JointNames.Length).Where(j => FollowedBone(fits, j) == hand).ToHashSet();
        return Enumerable.Range(0, _body.Positions.Length).Where(v => _body.Influences[v].Where(p => joints.Contains(p.Joint)).Sum(p => p.Weight) > 0.5f);
    }

    /// <summary>
    /// The fitted bone a model joint follows: its own, or its nearest fitted ancestor's; null for none.
    /// </summary>
    string? FollowedBone(IReadOnlyDictionary<string, BoneFit> fits, int joint) =>
        NearestFitted(fits, joint) is { } followed && AtWrist(fits, followed, joint) ? followed.Replace("Forearm", "Hand") : NearestFitted(fits, joint);

    /// <summary>
    /// A model joint's own fitted bone, or its nearest fitted ancestor's; null for none.
    /// </summary>
    string? NearestFitted(IReadOnlyDictionary<string, BoneFit> fits, int joint)
    {
        while (joint >= 0 && !(Short(_body.JointNames[joint]) is { } bone && fits.ContainsKey(bone))) joint = _body.JointParents[joint];
        return joint < 0 ? null : Short(_body.JointNames[joint]);
    }

    /// <summary>
    /// Whether a helper joint following <paramref name="followed"/> is a forearm's joint at the wrist (<see cref="WristJointReach"/>), which
    /// follows the hand: with <c>palms</c> only, as a claw's parts are left as they were.
    /// </summary>
    bool AtWrist(IReadOnlyDictionary<string, BoneFit> fits, string followed, int joint)
    {
        if (!_palms || !followed.EndsWith(" Forearm") || Short(_body.JointNames[joint]) == followed) return false;

        string hand = followed.Replace("Forearm", "Hand");
        if (!fits.ContainsKey(hand) || !_source.ContainsKey(hand)) return false;

        var elbow = Source(followed);
        var wrist = Source(hand) - elbow;
        float along = Vector3.Dot(_body.JointPositions[joint] - elbow, wrist) / wrist.LengthSquared();
        return along >= WristJointReach;
    }

    /// <summary>
    /// The direction across <paramref name="axis"/> along which <paramref name="points"/> spread most, and how many times their spread across it
    /// that spread is.
    /// </summary>
    static (Vector3 Breadth, float Flatness) Breadth(Vector3[] points, Vector3 axis)
    {
        var u = Perpendicular(axis);
        var w = Vector3.Cross(axis, u);
        var middle = points.Aggregate(Vector3.Zero, (a, p) => a + p) / points.Length;
        float uu = 0, uw = 0, ww = 0;
        foreach (var p in points)
        {
            float a = Vector3.Dot(p - middle, u), b = Vector3.Dot(p - middle, w);
            uu += a * a;
            uw += a * b;
            ww += b * b;
        }

        float half = (uu + ww) / 2, root = MathF.Sqrt(((uu - ww) / 2) * ((uu - ww) / 2) + uw * uw);
        float theta = MathF.Atan2(2 * uw, uu - ww) / 2;
        float flatness = MathF.Sqrt((half + root) / MathF.Max(half - root, 1e-9f));
        return (Vector3.Normalize(MathF.Cos(theta) * u + MathF.Sin(theta) * w), flatness);
    }

    /// <summary>
    /// A fit turned afterwards by <paramref name="angle"/> about <paramref name="axis"/> through its target joint.
    /// </summary>
    static BoneFit Twisted(BoneFit fit, Vector3 axis, float angle) => fit with { Linear = fit.Linear * Matrix4x4.CreateFromAxisAngle(axis, angle) };

    /// <summary>
    /// A direction with its part along <paramref name="axis"/> removed.
    /// </summary>
    static Vector3 Flatten(Vector3 direction, Vector3 axis) => direction - axis * Vector3.Dot(direction, axis);

    #endregion

    #region Repose

    /// <summary>
    /// The model's vertices and normals moved onto the game's skeleton, and each vertex's weights by game bone handle.
    /// </summary>
    public (Vector3[] Positions, Vector3[] Normals, Dictionary<ushort, float>[] Weights) Repose()
    {
        var follow = FollowedFits();
        int count = _body.Positions.Length;
        var positions = new Vector3[count];
        var normals = new Vector3[count];
        var weights = new Dictionary<ushort, float>[count];
        for (int v = 0; v < count; v++) (positions[v], normals[v], weights[v]) = ReposeVertex(v, follow);

        return (positions, normals, weights);
    }

    /// <summary>
    /// One vertex: the weighted sum of its joints' fits, and its weights gathered by the game bones those joints follow.
    /// </summary>
    (Vector3, Vector3, Dictionary<ushort, float>) ReposeVertex(int v, (string Bone, BoneFit Fit)[] follow)
    {
        var position = Vector3.Zero;
        var normal = Vector3.Zero;
        var weights = new Dictionary<ushort, float>();
        foreach (var (joint, weight) in _body.Influences[v])
        {
            var (bone, fit) = follow[joint];
            position += weight * fit.Apply(_body.Positions[v], Scale);
            normal += weight * fit.Apply(_body.Normals[v], Scale, isNormal: true);
            ushort handle = _kenshi.Handle(KenshiPrefix + bone);
            weights[handle] = weights.GetValueOrDefault(handle) + weight;
        }

        return (position, normal == Vector3.Zero ? Vector3.UnitY : Vector3.Normalize(normal), weights);
    }

    /// <summary>
    /// For each model joint, the game bone it follows and that bone's fit: itself, or its nearest ancestor that has a fit (fingers, tails and
    /// helper joints follow their limb). Throws when a joint has none.
    /// </summary>
    (string Bone, BoneFit Fit)[] FollowedFits()
    {
        var follow = new (string, BoneFit)[_body.JointNames.Length];
        WristJoints = 0;
        for (int i = 0; i < follow.Length; i++)
        {
            string? followed = FollowedBone(Fits, i);
            if (followed is null && Carries(i)) throw new InvalidDataException($"joint {_body.JointNames[i]} has no fitted ancestor");

            string name = followed ?? "Pelvis";
            follow[i] = (name, Fits[name]);
            if (followed != NearestFitted(Fits, i) && Carries(i)) WristJoints++;
        }

        return follow;
    }

    /// <summary>
    /// Whether any vertex is weighted to joint <paramref name="joint"/> (a joint none carries, like a stray root bone, may lack a fitted ancestor).
    /// </summary>
    bool Carries(int joint) => _body.Influences.Any(influences => influences.Any(p => p.Joint == joint));

    #endregion

    #region Helpers

    /// <summary>
    /// A model joint's rest position by short name. Throws when the model lacks it.
    /// </summary>
    Vector3 Source(string name) =>
        _source.TryGetValue(name, out var p) ? p : throw new InvalidDataException($"the model has no joint {SourcePrefix}{name} (or {name})");

    /// <summary>
    /// A game bone's rest position by short name.
    /// </summary>
    Vector3 Kenshi(string name) => _kenshi.Position(KenshiPrefix + name);

    /// <summary>
    /// The game's head height: its head bone's height above the ground.
    /// </summary>
    static float HeadHeight(KenshiSkeleton kenshi) => kenshi.Position(KenshiPrefix + "Head").Y;

    /// <summary>
    /// A model joint's name without <see cref="SourcePrefix"/>, null for a joint that is not a Biped one.
    /// </summary>
    static string? Short(string name) => name.StartsWith(SourcePrefix, StringComparison.Ordinal) ? name[SourcePrefix.Length..] : null;

    /// <summary>
    /// The shortest rotation taking unit direction <paramref name="a"/> to unit direction <paramref name="b"/>.
    /// </summary>
    static Quaternion Between(Vector3 a, Vector3 b)
    {
        float d = Math.Clamp(Vector3.Dot(a, b), -1, 1);
        if (d > 1 - 1e-7f) return Quaternion.Identity;
        if (d < -1 + 1e-7f) return Quaternion.CreateFromAxisAngle(Perpendicular(a), MathF.PI);

        var axis = Vector3.Cross(a, b);
        return Quaternion.Normalize(new Quaternion(axis, 1 + d));
    }

    /// <summary>
    /// A unit direction perpendicular to <paramref name="a"/>.
    /// </summary>
    static Vector3 Perpendicular(Vector3 a)
    {
        var axis = Vector3.Cross(a, Vector3.UnitX);
        if (axis.Length() < 1e-6f) axis = Vector3.Cross(a, Vector3.UnitY);
        return Vector3.Normalize(axis);
    }

    #endregion
}
