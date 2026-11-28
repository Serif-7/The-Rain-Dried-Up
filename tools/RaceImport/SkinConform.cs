using System.Numerics;

namespace RaceImport;

/// <summary>
/// The parts of a body <see cref="SkinConform"/> pulls onto the human skin.
/// </summary>
[Flags]
public enum SkinPull
{
    /// <summary>
    /// The pelvis, spines, bust, legs and feet.
    /// </summary>
    Torso = 1,

    /// <summary>
    /// The clavicles, upper arms and forearms, each onto its own arm's skin.
    /// </summary>
    Arms = 2,

    /// <summary>
    /// The front of the crotch (a fur tuft), onto the human's pelvis skin.
    /// </summary>
    Groin = 4,

    /// <summary>
    /// Everything a full import pulls.
    /// </summary>
    All = Torso | Arms | Groin,
}

/// <summary>
/// Pulls the torso, hips, legs, feet, arms and the front of the crotch onto the human body's skin, so the game's trousers, skirts, coats, sleeves
/// and boots (modelled around the human) cover them (docs/race-import.md "Skin conform"). Runs after slimming, which only pulls to the human's
/// widest extent per slice: a vertex outside the human surface by more than a cloth margin moves onto it, unless it is far out (a tail, a claw),
/// carried by the hands or head, or near a tail.
/// </summary>
public sealed class SkinConform
{
    /// <summary>
    /// The bones whose vertices are pulled (a vertex is pulled by its weight on them).
    /// </summary>
    static readonly string[] Bones =
    [
        "Bip01 Pelvis", "Bip01 Spine", "Bip01 Spine1", "Bip01 Spine2", "L Boob", "R Boob",
        "Bip01 L Thigh", "Bip01 L Calf", "Bip01 L Foot", "Bip01 L Toe0", "Bip01 R Thigh", "Bip01 R Calf", "Bip01 R Foot", "Bip01 R Toe0",
    ];

    /// <summary>
    /// The arm bones of each side, whose vertices are pulled onto that arm's skin (<see cref="SkinPull.Arms"/>); the hands stay, being covered
    /// only by gloves.
    /// </summary>
    static readonly string[][] ArmBones =
    [
        ["Bip01 L Clavicle", "Bip01 L UpperArm", "Bip01 L Forearm"],
        ["Bip01 R Clavicle", "Bip01 R UpperArm", "Bip01 R Forearm"],
    ];

    /// <summary>
    /// The bones of each side's human arm skin: the arm bones and the hand.
    /// </summary>
    static readonly string[][] ArmSkinBones = [[.. ArmBones[0], "Bip01 L Hand"], [.. ArmBones[1], "Bip01 R Hand"]];

    /// <summary>
    /// The bones of the human pelvis skin a groin vertex is pulled onto (<see cref="SkinPull.Groin"/>).
    /// </summary>
    static readonly string[] PelvisSkinBones = ["Bip01 Pelvis", "Bip01 Spine"];

    /// <summary>
    /// Room left for tight cloth beyond the human skin (game units, 0.1 m).
    /// </summary>
    const float Margin = 0.05f;

    /// <summary>
    /// A vertex outside the skin by up to <see cref="Full"/> is pulled fully; the pull fades out to <see cref="Reach"/>, beyond which the vertex is
    /// a shape no clothing covers (a tail, a claw) and stays.
    /// </summary>
    const float Full = 1.0f, Reach = 1.6f;

    /// <summary>
    /// Half the width of the gap between the legs: a vertex below the pelvis joint and this close to the middle line is never pulled onto the
    /// nearest skin, which is an inner thigh, since that folds what hangs between the legs (**Observed**, male Crevan: feathers turned inside out).
    /// In front of the pelvis joint it is pulled onto the pelvis skin only (<see cref="SkinPull.Groin"/>); behind it, it stays.
    /// </summary>
    const float Crotch = 0.6f;

    /// <summary>
    /// The human vertices searched for the nearest skin.
    /// </summary>
    const int Candidates = 6;

    /// <summary>
    /// Passes of pull and smoothing, and the smoothing rounds per pass: smoothing spreads a pass's pull, the next pass takes up what it undid.
    /// </summary>
    const int Passes = 3, SmoothingRounds = 3;

    /// <summary>
    /// The human body's vertices.
    /// </summary>
    readonly Vector3[] _skin;

    /// <summary>
    /// The human body's triangles, three indices each, wound so their normals point out.
    /// </summary>
    readonly int[] _triangles;

    /// <summary>
    /// Each human vertex's triangles (index of the first corner).
    /// </summary>
    readonly List<int>[] _around;

    /// <summary>
    /// The parts pulled.
    /// </summary>
    readonly SkinPull _pulls;

    /// <summary>
    /// The torso bones' handles (<see cref="Bones"/>).
    /// </summary>
    readonly HashSet<ushort> _pulled;

    /// <summary>
    /// Each side's arm bones' handles (<see cref="ArmBones"/>).
    /// </summary>
    readonly HashSet<ushort>[] _arms;

    /// <summary>
    /// The skins searched: every human vertex, each side's arm vertices and the pelvis vertices (<see cref="Skin"/>).
    /// </summary>
    readonly int[][] _skins;

    /// <summary>
    /// The pelvis joint (<see cref="Crotch"/>).
    /// </summary>
    readonly Vector3 _pelvis;

    /// <summary>
    /// Prepares the pull of <paramref name="pulls"/> onto <paramref name="human"/> for the bones of <paramref name="kenshi"/>.
    /// </summary>
    public SkinConform(KenshiSkeleton kenshi, HumanBody human, SkinPull pulls = SkinPull.All)
    {
        _skin = human.Positions;
        _triangles = Outward(human.Positions, human.Indices);
        _around = [.. _skin.Select(_ => new List<int>())];
        for (int t = 0; t < _triangles.Length; t += 3)
            for (int k = 0; k < 3; k++) _around[_triangles[t + k]].Add(t);

        _pulls = pulls;
        _pulled = Handles(kenshi, Bones);
        _arms = [.. ArmBones.Select(side => Handles(kenshi, side))];
        _skins =
        [
            [.. Enumerable.Range(0, _skin.Length)],
            SkinOf(human, Handles(kenshi, ArmSkinBones[0])),
            SkinOf(human, Handles(kenshi, ArmSkinBones[1])),
            SkinOf(human, Handles(kenshi, PelvisSkinBones)),
        ];
        _pelvis = kenshi.Position("Bip01 Pelvis");
    }

    /// <summary>
    /// The handles of those of <paramref name="bones"/> the skeleton has.
    /// </summary>
    static HashSet<ushort> Handles(KenshiSkeleton kenshi, IEnumerable<string> bones) => bones.Where(kenshi.Has).Select(kenshi.Handle).ToHashSet();

    /// <summary>
    /// The human vertices whose heaviest bone is one of <paramref name="bones"/>.
    /// </summary>
    static int[] SkinOf(HumanBody human, HashSet<ushort> bones) => [.. Enumerable.Range(0, human.Positions.Length).Where(v => bones.Contains(human.Bones[v]))];

    /// <summary>
    /// The skins of <see cref="_skins"/>: all, the left arm, the right arm, the pelvis.
    /// </summary>
    enum Skin
    {
        All,
        LeftArm,
        RightArm,
        Pelvis,
    }

    /// <summary>
    /// The positions pulled onto the skin over <see cref="Passes"/> passes, scaled by <paramref name="strength"/>; vertices in
    /// <paramref name="kept"/> (a tail's) and those near them stay. Returns how many vertices moved by more than a millimetre.
    /// </summary>
    public (Vector3[] Positions, int Moved) Apply(Vector3[] positions, Dictionary<ushort, float>[] weights, int[] indices, float strength, bool[] kept)
    {
        var targets = positions.Select((p, v) => Target(p, weights[v])).ToArray();
        var current = positions;
        for (int pass = 0; pass < Passes; pass++)
        {
            var offsets = Pulls(current, targets);
            BodyConform.Spare(offsets, current, kept);
            offsets = BodyConform.Smoothed(offsets, current, indices, SmoothingRounds);
            BodyConform.Spare(offsets, current, kept);
            current = [.. current.Select((p, v) => p + strength * offsets[v])];
        }

        int moved = positions.Where((p, v) => Vector3.Distance(p, current[v]) > 0.01f).Count();
        return (current, moved);
    }

    /// <summary>
    /// The skin a vertex at <paramref name="position"/> with <paramref name="weights"/> is pulled onto and its share of the pull (0: none): a
    /// groin vertex the pelvis skin by its torso weight; one carried more by an arm than the torso its arm's skin by its arm weight; any other
    /// the whole skin by its torso weight.
    /// </summary>
    (Skin Skin, float Share) Target(Vector3 position, Dictionary<ushort, float> weights)
    {
        float torso = weights.Where(p => _pulled.Contains(p.Key)).Sum(p => p.Value);
        if (position.Y < _pelvis.Y && MathF.Abs(position.X) < Crotch)
            return _pulls.HasFlag(SkinPull.Groin) && position.Z > _pelvis.Z ? (Skin.Pelvis, torso) : (Skin.All, 0);

        float left = weights.Where(p => _arms[0].Contains(p.Key)).Sum(p => p.Value);
        float right = weights.Where(p => _arms[1].Contains(p.Key)).Sum(p => p.Value);
        float arm = MathF.Max(left, right);
        if (_pulls.HasFlag(SkinPull.Arms) && arm > torso) return (left >= right ? Skin.LeftArm : Skin.RightArm, arm);

        return (Skin.All, _pulls.HasFlag(SkinPull.Torso) ? torso : 0);
    }

    /// <summary>
    /// Each vertex's pull: towards the nearest point of its target skin by its distance outside it less the margin, faded by distance and scaled
    /// by its share.
    /// </summary>
    Vector3[] Pulls(Vector3[] positions, (Skin Skin, float Share)[] targets)
    {
        var offsets = new Vector3[positions.Length];
        Parallel.For(0, positions.Length, v =>
        {
            var (skin, share) = targets[v];
            if (share <= 0) return;

            var (closest, outside) = Nearest(positions[v], _skins[(int)skin]);
            float distance = Vector3.Distance(positions[v], closest);
            if (!outside || distance <= Margin || distance >= Reach) return;

            float fade = Math.Clamp((Reach - distance) / (Reach - Full), 0, 1);
            offsets[v] = (closest - positions[v]) * ((distance - Margin) / distance) * fade * share;
        });

        return offsets;
    }

    /// <summary>
    /// The nearest point of the skin to <paramref name="point"/> (on the triangles round its nearest human vertices of <paramref name="skin"/>)
    /// and whether the point lies outside the skin there.
    /// </summary>
    (Vector3 Closest, bool Outside) Nearest(Vector3 point, int[] skin)
    {
        var best = (Point: point, Distance: float.MaxValue, Outside: false);
        foreach (int vertex in NearestVertices(point, skin))
            foreach (int t in _around[vertex])
            {
                var (a, b, c) = (_skin[_triangles[t]], _skin[_triangles[t + 1]], _skin[_triangles[t + 2]]);
                var q = ClosestOnTriangle(point, a, b, c);
                float d = Vector3.DistanceSquared(point, q);
                if (d >= best.Distance) continue;

                best = (q, d, Vector3.Dot(point - q, Vector3.Cross(b - a, c - a)) > 0);
            }

        return (best.Point, best.Outside);
    }

    /// <summary>
    /// The <see cref="Candidates"/> human vertices of <paramref name="skin"/> nearest <paramref name="point"/>.
    /// </summary>
    int[] NearestVertices(Vector3 point, int[] skin)
    {
        var best = new List<(int Index, float Squared)>(Candidates + 1);
        foreach (int v in skin)
        {
            float d = Vector3.DistanceSquared(point, _skin[v]);
            if (best.Count == Candidates && d >= best[^1].Squared) continue;

            int at = best.FindIndex(b => b.Squared > d);
            best.Insert(at < 0 ? best.Count : at, (v, d));
            if (best.Count > Candidates) best.RemoveAt(Candidates);
        }

        return [.. best.Select(b => b.Index)];
    }

    /// <summary>
    /// The triangles wound so their normals point out of the body: reversed when the mesh's signed volume is negative.
    /// </summary>
    static int[] Outward(Vector3[] positions, int[] indices)
    {
        float volume = 0;
        for (int t = 0; t < indices.Length; t += 3)
            volume += Vector3.Dot(positions[indices[t]], Vector3.Cross(positions[indices[t + 1]], positions[indices[t + 2]]));

        if (volume >= 0) return indices;

        var flipped = (int[])indices.Clone();
        for (int t = 0; t < flipped.Length; t += 3) (flipped[t + 1], flipped[t + 2]) = (flipped[t + 2], flipped[t + 1]);
        return flipped;
    }

    /// <summary>
    /// The point of triangle <paramref name="a"/>, <paramref name="b"/>, <paramref name="c"/> nearest <paramref name="p"/> (Ericson, Real-Time
    /// Collision Detection, 5.1.5).
    /// </summary>
    internal static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a, ac = c - a, ap = p - a;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0) return a;

        Vector3 bp = p - b;
        float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3) return b;

        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0) return a + d1 / (d1 - d3) * ab;

        Vector3 cp = p - c;
        float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6) return c;

        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0) return a + d2 / (d2 - d6) * ac;

        float va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0) return b + (d4 - d3) / (d4 - d3 + (d5 - d6)) * (c - b);

        float denominator = 1 / (va + vb + vc);
        return a + ab * (vb * denominator) + ac * (vc * denominator);
    }
}
