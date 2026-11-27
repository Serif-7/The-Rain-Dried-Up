using System.Numerics;

namespace RaceImport;

/// <summary>
/// Gives a fitted body the human body's weights around the torso (docs/race-import.md "Human weights"), so the editor's body sliders, which
/// scale the game's bones, shape it as they shape a human. The models' rigs leave the chest on <c>Spine1</c> and the clavicles and the neck on
/// the head (their <c>Spine2</c> and <c>Neck</c> joints carry no vertices), so Stomach and Waist dragged the chest and Chest did nothing; and
/// Meshy binds a tail to a leg, foot or toe. Steps, on the surface with its uv seams welded: tails go to the pelvis (<see cref="MoveTails"/>,
/// before slimming); each torso, neck, shoulder and hip vertex near the human takes the human's weights there; then those weights are smoothed
/// (<see cref="Apply"/>).
/// </summary>
public static class HumanWeights
{
    #region Settings

    /// <summary>
    /// A vertex this close to the human (game units, 0.1 m) takes the human's weights fully; at <see cref="Far"/> and beyond, none.
    /// </summary>
    const float Near = 0.5f, Far = 1.2f;

    /// <summary>
    /// How many nearest human vertices a vertex's human weights blend (by inverse distance).
    /// </summary>
    const int Donors = 6;

    /// <summary>
    /// A leg-carried vertex further than this from every human vertex and behind <see cref="Behind"/> (the depth, + forward) is a tail's.
    /// </summary>
    const float TailGap = 0.8f, Behind = -0.6f;

    /// <summary>
    /// A leg-carried vertex beside a tail vertex, carried by the same bone, behind <see cref="Behind"/> and at least this far from the human, is
    /// the tail's too (its root, closer to the body).
    /// </summary>
    const float TailRootGap = 0.3f;

    /// <summary>
    /// Rings of neighbours around the changed vertices that are smoothed too, and the rounds of smoothing (each halfway to the neighbours' mean).
    /// </summary>
    const int Rings = 2, Rounds = 4;

    /// <summary>
    /// Bones whose vertices take the human's weights (with the head's below the game's head joint: the neck).
    /// </summary>
    static readonly string[] Takers =
    [
        "Bip01 Pelvis", "Bip01 Spine", "Bip01 Spine1", "Bip01 Spine2", "Bip01 Neck", "Bip01 L Clavicle", "Bip01 R Clavicle", "L Boob", "R Boob",
        "Bip01 L UpperArm", "Bip01 R UpperArm", "Bip01 L Thigh", "Bip01 R Thigh",
    ];

    /// <summary>
    /// Bones whose human vertices give weights: the takers' and the head, forearms and calves next to them (not the hands and feet).
    /// </summary>
    static readonly string[] Givers = [.. Takers, "Bip01 Head", "Bip01 L Forearm", "Bip01 R Forearm", "Bip01 L Calf", "Bip01 R Calf"];

    /// <summary>
    /// The torso's bones: a torso vertex takes weights from these and the limbs' upper bones, a limb vertex from <see cref="Shoulder"/> or
    /// <see cref="Hip"/> and its own limb.
    /// </summary>
    static readonly string[] Torso = ["Bip01 Pelvis", "Bip01 Spine", "Bip01 Spine1", "Bip01 Spine2", "Bip01 Neck", "Bip01 Head", "L Boob", "R Boob"];

    /// <summary>
    /// The torso bones an arm vertex may take weights from (the armpit's), so an arm hanging by the hips never takes the pelvis's.
    /// </summary>
    static readonly string[] Shoulder = ["Bip01 Spine1", "Bip01 Spine2", "Bip01 Neck"];

    /// <summary>
    /// The torso bones a leg vertex may take weights from.
    /// </summary>
    static readonly string[] Hip = ["Bip01 Pelvis", "Bip01 Spine"];

    /// <summary>
    /// The limbs, each its bones from the torso out: a limb vertex takes weights only from its own limb (and <see cref="Shoulder"/> or
    /// <see cref="Hip"/>), so an arm beside a hip or a thigh beside the other never takes the other's.
    /// </summary>
    static readonly (string[] Bones, string[] Torso)[] Limbs =
    [
        (["Bip01 L Clavicle", "Bip01 L UpperArm", "Bip01 L Forearm"], Shoulder),
        (["Bip01 R Clavicle", "Bip01 R UpperArm", "Bip01 R Forearm"], Shoulder),
        (["Bip01 L Thigh", "Bip01 L Calf"], Hip),
        (["Bip01 R Thigh", "Bip01 R Calf"], Hip),
    ];

    /// <summary>
    /// The foot joints (<see cref="FootReach"/>).
    /// </summary>
    static readonly string[] Feet = ["Bip01 L Foot", "Bip01 R Foot", "Bip01 L Toe0", "Bip01 R Toe0"];

    /// <summary>
    /// How near a foot joint (units) a vertex is part of the foot rather than a tail.
    /// </summary>
    const float FootReach = 1.5f;

    /// <summary>
    /// Bones a tail may be bound to.
    /// </summary>
    static readonly string[] Legs =
    [
        "Bip01 L Thigh", "Bip01 R Thigh", "Bip01 L Calf", "Bip01 R Calf", "Bip01 L Foot", "Bip01 R Foot", "Bip01 L Toe0", "Bip01 R Toe0",
    ];

    #endregion

    #region Transfer

    /// <summary>
    /// Finds the tail's vertices and puts them on the pelvis (before slimming, which then leaves them and their surroundings alone); returns
    /// them.
    /// </summary>
    public static bool[] MoveTails(Vector3[] positions, int[] indices, Dictionary<ushort, float>[] weights, KenshiSkeleton kenshi, HumanBody human)
    {
        var weld = Surface.Weld(positions);
        var points = Welded(positions, weld);
        var tail = Tail(points, Welded(weights, weld), Gaps(points, human), Surface.Neighbours(weld, indices), kenshi);
        ushort pelvis = kenshi.Handle("Bip01 Pelvis");

        var result = new bool[positions.Length];
        for (int v = 0; v < positions.Length; v++)
        {
            if (!tail.Contains(weld[v])) continue;

            result[v] = true;
            weights[v] = new Dictionary<ushort, float> { [pelvis] = 1 };
        }

        return result;
    }

    /// <summary>
    /// Gives the torso the human's weights and smooths them (with the <paramref name="tail"/>'s edge); returns how many welded vertices took
    /// human weights.
    /// </summary>
    public static int Apply(Vector3[] positions, int[] indices, Dictionary<ushort, float>[] weights, bool[] tail, KenshiSkeleton kenshi,
        HumanBody human)
    {
        var weld = Surface.Weld(positions);
        var neighbours = Surface.Neighbours(weld, indices);
        var points = Welded(positions, weld);
        var own = Welded(weights, weld);
        var tails = Enumerable.Range(0, positions.Length).Where(v => tail[v]).Select(v => weld[v]).ToHashSet();

        var changed = Copy(points, own, Gaps(points, human), tails, kenshi, human);
        var smoothed = Smooth(own, neighbours, Grow([.. changed, .. tails], neighbours));
        for (int v = 0; v < weights.Length; v++) weights[v] = new Dictionary<ushort, float>(smoothed[weld[v]]);

        return changed.Count;
    }

    /// <summary>
    /// Each point's distance to the nearest human vertex.
    /// </summary>
    static float[] Gaps(Vector3[] points, HumanBody human)
    {
        int[] all = [.. Enumerable.Range(0, human.Positions.Length)];
        return [.. points.Select(p => Nearest(p, human.Positions, all, 1)[0].Distance)];
    }

    /// <summary>
    /// The welded vertices of a tail: leg-carried, behind the body, far from the human and not at a foot (<see cref="FootReach"/>: a heel's spur
    /// or talon is not a tail); then, spreading from them, their root's.
    /// </summary>
    static HashSet<int> Tail(Vector3[] points, Dictionary<ushort, float>[] own, float[] gaps, HashSet<int>[] neighbours, KenshiSkeleton kenshi)
    {
        var legs = Legs.Select(kenshi.Handle).ToHashSet();
        Vector3[] feet = [.. Feet.Select(kenshi.Position)];
        var tail = new HashSet<int>();
        for (int v = 0; v < points.Length; v++)
            if (legs.Contains(Heaviest(own[v])) && points[v].Z < Behind && gaps[v] > TailGap && !AtFoot(points[v], feet)) tail.Add(v);

        var queue = new Queue<int>(tail);
        while (queue.TryDequeue(out int v))
            foreach (int n in neighbours[v])
            {
                if (tail.Contains(n) || Heaviest(own[n]) != Heaviest(own[v]) || points[n].Z >= Behind || gaps[n] < TailRootGap) continue;
                if (AtFoot(points[n], feet)) continue;

                tail.Add(n);
                queue.Enqueue(n);
            }

        return tail;
    }

    /// <summary>
    /// Whether a point lies within <see cref="FootReach"/> of a foot joint.
    /// </summary>
    static bool AtFoot(Vector3 point, Vector3[] feet) => feet.Any(f => Vector3.Distance(point, f) < FootReach);

    /// <summary>
    /// Each taker vertex near the human (not a tail's) blended towards the human's weights there, fully within <see cref="Near"/>, from the
    /// donors its own part may take (<see cref="DonorSets"/>); returns the vertices that changed.
    /// </summary>
    static List<int> Copy(Vector3[] points, Dictionary<ushort, float>[] own, float[] gaps, HashSet<int> tail, KenshiSkeleton kenshi,
        HumanBody human)
    {
        var takers = Takers.Where(kenshi.Has).Select(kenshi.Handle).ToHashSet();
        var donors = DonorSets(kenshi, human);
        ushort head = kenshi.Handle("Bip01 Head");
        float neckTop = kenshi.Position("Bip01 Head").Y;

        var changed = new List<int>();
        for (int v = 0; v < points.Length; v++)
        {
            ushort bone = Heaviest(own[v]);
            bool taker = takers.Contains(bone) || bone == head && points[v].Y < neckTop;
            float strength = Math.Clamp((Far - gaps[v]) / (Far - Near), 0, 1);
            if (!taker || tail.Contains(v) || strength <= 0) continue;

            own[v] = Blend(own[v], HumanAt(points[v], donors.GetValueOrDefault(bone, donors[head]), human), strength);
            changed.Add(v);
        }

        return changed;
    }

    /// <summary>
    /// Per bone, the human vertices a vertex it carries takes weights from: for a limb's bone, its limb's and the torso bones next to it; for a
    /// torso bone (the head's too), the torso's and every limb's upper bones (not forearms or calves).
    /// </summary>
    static Dictionary<ushort, int[]> DonorSets(KenshiSkeleton kenshi, HumanBody human)
    {
        var givers = Givers.Where(kenshi.Has).Select(kenshi.Handle).ToHashSet();
        var upper = Limbs.SelectMany(l => l.Bones.Take(l.Bones.Length - 1));
        var torso = Handles(kenshi, [.. Torso, .. upper]);
        var result = Torso.Where(kenshi.Has).ToDictionary(kenshi.Handle, _ => DonorsOf(human, torso, givers));
        foreach (var (bones, near) in Limbs)
        {
            var allowed = Handles(kenshi, [.. bones, .. near]);
            foreach (var bone in Handles(kenshi, bones)) result[bone] = DonorsOf(human, allowed, givers);
        }

        return result;
    }

    /// <summary>
    /// The human vertices carried by a bone that is both <paramref name="allowed"/> and a giver.
    /// </summary>
    static int[] DonorsOf(HumanBody human, HashSet<ushort> allowed, HashSet<ushort> givers) =>
        [.. Enumerable.Range(0, human.Positions.Length).Where(v => allowed.Contains(human.Bones[v]) && givers.Contains(human.Bones[v]))];

    /// <summary>
    /// The handles of the named bones the rig has.
    /// </summary>
    static HashSet<ushort> Handles(KenshiSkeleton kenshi, IEnumerable<string> names) => names.Where(kenshi.Has).Select(kenshi.Handle).ToHashSet();

    /// <summary>
    /// The human's weights at <paramref name="point"/>: its <see cref="Donors"/> nearest donor vertices' weights, by inverse distance.
    /// </summary>
    static Dictionary<ushort, float> HumanAt(Vector3 point, int[] donors, HumanBody human)
    {
        var nearest = Nearest(point, human.Positions, donors, Donors);
        var shares = nearest.Select(n => 1 / (n.Distance + 1e-3f)).ToArray();
        float total = shares.Sum();
        var result = new Dictionary<ushort, float>();
        for (int i = 0; i < nearest.Length; i++)
            foreach (var (bone, weight) in human.Weights[nearest[i].Index])
                result[bone] = result.GetValueOrDefault(bone) + weight * shares[i] / total;

        return result;
    }

    #endregion

    #region Smoothing

    /// <summary>
    /// <paramref name="seed"/> and <see cref="Rings"/> rings of neighbours around it.
    /// </summary>
    static HashSet<int> Grow(IEnumerable<int> seed, HashSet<int>[] neighbours)
    {
        var region = seed.ToHashSet();
        for (int ring = 0; ring < Rings; ring++) region.UnionWith(region.SelectMany(v => neighbours[v]).ToArray());

        return region;
    }

    /// <summary>
    /// The weights with each vertex in <paramref name="region"/> moved halfway to its neighbours' mean, <see cref="Rounds"/> times.
    /// </summary>
    static Dictionary<ushort, float>[] Smooth(Dictionary<ushort, float>[] weights, HashSet<int>[] neighbours, HashSet<int> region)
    {
        var current = weights;
        for (int round = 0; round < Rounds; round++)
        {
            var next = (Dictionary<ushort, float>[])current.Clone();
            foreach (int v in region)
            {
                if (neighbours[v].Count == 0) continue;

                var mean = Mean(neighbours[v].Select(n => current[n]));
                next[v] = Blend(current[v], mean, 0.5f);
            }

            current = next;
        }

        return current;
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Per welded vertex, its first member's value.
    /// </summary>
    static T[] Welded<T>(T[] values, int[] weld)
    {
        var result = new T[weld.Max() + 1];
        for (int v = values.Length - 1; v >= 0; v--) result[weld[v]] = values[v];

        return result;
    }

    /// <summary>
    /// The bone with the most weight.
    /// </summary>
    static ushort Heaviest(Dictionary<ushort, float> weights) => weights.MaxBy(p => p.Value).Key;

    /// <summary>
    /// <paramref name="a"/> moved towards <paramref name="b"/> by <paramref name="t"/>, normalised to sum 1.
    /// </summary>
    static Dictionary<ushort, float> Blend(Dictionary<ushort, float> a, Dictionary<ushort, float> b, float t)
    {
        var result = new Dictionary<ushort, float>();
        foreach (var (bone, weight) in a) result[bone] = weight * (1 - t);
        foreach (var (bone, weight) in b) result[bone] = result.GetValueOrDefault(bone) + weight * t;

        float total = result.Values.Sum();
        return result.ToDictionary(p => p.Key, p => p.Value / total);
    }

    /// <summary>
    /// The mean of the weights.
    /// </summary>
    static Dictionary<ushort, float> Mean(IEnumerable<Dictionary<ushort, float>> all)
    {
        var result = new Dictionary<ushort, float>();
        int count = 0;
        foreach (var weights in all)
        {
            foreach (var (bone, weight) in weights) result[bone] = result.GetValueOrDefault(bone) + weight;
            count++;
        }

        return result.ToDictionary(p => p.Key, p => p.Value / count);
    }

    /// <summary>
    /// The <paramref name="count"/> candidates nearest <paramref name="point"/>, nearest first.
    /// </summary>
    static (int Index, float Distance)[] Nearest(Vector3 point, Vector3[] positions, int[] candidates, int count)
    {
        var best = new List<(int Index, float Squared)>(count + 1);
        foreach (int c in candidates)
        {
            float d = Vector3.DistanceSquared(point, positions[c]);
            if (best.Count == count && d >= best[^1].Squared) continue;

            int at = best.FindIndex(b => b.Squared > d);
            best.Insert(at < 0 ? best.Count : at, (c, d));
            if (best.Count > count) best.RemoveAt(count);
        }

        return [.. best.Select(b => (b.Index, MathF.Sqrt(b.Squared)))];
    }

    #endregion
}
