using System.Numerics;

namespace RaceImport;

/// <summary>
/// The model's own ears kept on the human head (docs/race-import.md "Ears"). On each side, the highest vertex the head graft drops is an ear's
/// tip, and the dropped vertices on that side within <see cref="Height"/> below it and <see cref="Width"/> across are the ear. Each ear moves
/// from the model's head to the human's as one piece: its base's middle to the same place in the human head's box as it had in the model
/// head's (the rest of the dropped vertices), grown by <see cref="Scale"/>, then on to <see cref="Sink"/> inside the skull from the head
/// vertex nearest it, so its cut base never shows. The ears ride the head bone alone.
/// </summary>
public static class HeadEars
{
    /// <summary>
    /// How far down from its tip an ear reaches, as a share of the human head's height (rim to crown).
    /// </summary>
    const float Height = 0.35f;

    /// <summary>
    /// How far across from its tip an ear reaches, as a share of <see cref="Height"/>.
    /// </summary>
    const float Width = 0.5f;

    /// <summary>
    /// The lowest share of an ear's height that counts as its base.
    /// </summary>
    const float Base = 0.2f;

    /// <summary>
    /// How much each ear grows about its base: the extracted ear is only its part above the model's hair, and the human head is barer.
    /// </summary>
    const float Scale = 1.4f;

    /// <summary>
    /// How far inside the human skull an ear's base sits, in game units.
    /// </summary>
    const float Sink = 0.1f;

    /// <summary>
    /// The dropped vertices that are an ear's (on either side of the head's middle, x = 0), each moved onto the human head.
    /// </summary>
    public static HashSet<int> Place(Vector3[] positions, int[] indices, bool[] dropped, HumanHead head)
    {
        float reach = Height * (head.Positions.Max(p => p.Y) - head.Positions.Min(p => p.Y));
        var weld = Surface.Weld(positions);
        var neighbours = Surface.Neighbours(weld, indices);
        int[][] ears = [Ear(positions, weld, neighbours, dropped, -1, reach), Ear(positions, weld, neighbours, dropped, 1, reach)];
        var all = ears.SelectMany(e => e).ToHashSet();
        var skull = Box(Enumerable.Range(0, positions.Length).Where(v => dropped[v] && !all.Contains(v)).Select(v => positions[v]));
        var human = Box(head.Positions);
        foreach (var ear in ears.Where(e => e.Length > 0)) Move(positions, ear, skull, human, head);

        return all;
    }

    /// <summary>
    /// A box: its middle and its size.
    /// </summary>
    static (Vector3 Middle, Vector3 Size) Box(IEnumerable<Vector3> points)
    {
        var list = points.ToList();
        if (list.Count == 0) return (Vector3.Zero, Vector3.One);

        var min = list.Aggregate(Vector3.Min);
        var max = list.Aggregate(Vector3.Max);
        return ((min + max) / 2, Vector3.Max(max - min, new Vector3(1e-3f)));
    }

    /// <summary>
    /// Moves one ear from the model head's box onto the human head (see the class).
    /// </summary>
    static void Move(Vector3[] positions, int[] ear, (Vector3 Middle, Vector3 Size) skull, (Vector3 Middle, Vector3 Size) human, HumanHead head)
    {
        var middle = BaseMiddle(positions, ear);
        var mapped = human.Middle + (middle - skull.Middle) * (human.Size / skull.Size);
        var skin = head.Positions.MinBy(p => Vector3.DistanceSquared(p, mapped));
        var placed = skin + Vector3.Normalize(human.Middle - skin) * Sink;
        foreach (int v in ear) positions[v] = placed + (positions[v] - middle) * Scale;
    }

    /// <summary>
    /// The middle of an ear's base: its vertices in the lowest <see cref="Base"/> of its height.
    /// </summary>
    static Vector3 BaseMiddle(Vector3[] positions, int[] ear)
    {
        float low = ear.Min(v => positions[v].Y);
        float high = ear.Max(v => positions[v].Y);
        var bottom = ear.Where(v => positions[v].Y <= low + Base * (high - low)).ToArray();
        return bottom.Aggregate(Vector3.Zero, (sum, v) => sum + positions[v]) / bottom.Length;
    }

    /// <summary>
    /// One side's ear: the dropped vertices within <paramref name="reach"/> below the side's highest dropped vertex and <see cref="Width"/> of it
    /// across, joined to that tip through others of them (so tufts of hair beside it stay out).
    /// </summary>
    static int[] Ear(Vector3[] positions, int[] weld, HashSet<int>[] neighbours, bool[] dropped, int side, float reach)
    {
        var candidates = Enumerable.Range(0, positions.Length).Where(v => dropped[v] && positions[v].X * side > 0).ToArray();
        if (candidates.Length == 0) return [];

        int tip = candidates.MaxBy(v => positions[v].Y);
        var top = positions[tip];
        var inside = candidates.Where(v => positions[v].Y >= top.Y - reach && Across(positions[v], top) <= Width * reach).ToArray();
        var joined = Joined(weld[tip], neighbours, inside.Select(v => weld[v]).ToHashSet());
        return [.. inside.Where(v => joined.Contains(weld[v]))];
    }

    /// <summary>
    /// The welded vertices of <paramref name="allowed"/> reached from <paramref name="start"/> along edges through others of them.
    /// </summary>
    static HashSet<int> Joined(int start, HashSet<int>[] neighbours, HashSet<int> allowed)
    {
        var seen = new HashSet<int> { start };
        var queue = new Queue<int>(seen);
        while (queue.TryDequeue(out int v))
            foreach (int n in neighbours[v])
                if (allowed.Contains(n) && seen.Add(n)) queue.Enqueue(n);

        return seen;
    }

    /// <summary>
    /// The horizontal distance between two points.
    /// </summary>
    static float Across(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));
}
