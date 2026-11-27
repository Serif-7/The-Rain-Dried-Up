using System.Numerics;

namespace RaceImport;

/// <summary>
/// A mesh's surface with its uv seams welded: vertices at one position count as one, so smoothing never tears a seam.
/// </summary>
public static class Surface
{
    /// <summary>
    /// For each vertex, the index of its position among the distinct positions (to 1e-4 units).
    /// </summary>
    public static int[] Weld(Vector3[] positions)
    {
        var ids = new Dictionary<(int, int, int), int>();
        return [.. positions.Select(Key).Select(k => ids.TryGetValue(k, out int id) ? id : ids[k] = ids.Count)];
    }

    /// <summary>
    /// Each welded vertex's welded neighbours along the triangles' edges.
    /// </summary>
    public static HashSet<int>[] Neighbours(int[] weld, int[] indices)
    {
        int count = weld.Length == 0 ? 0 : weld.Max() + 1;
        var neighbours = Enumerable.Range(0, count).Select(_ => new HashSet<int>()).ToArray();
        for (int t = 0; t < indices.Length; t += 3)
            for (int k = 0; k < 3; k++)
            {
                int a = weld[indices[t + k]], b = weld[indices[t + (k + 1) % 3]];
                if (a == b) continue;

                neighbours[a].Add(b);
                neighbours[b].Add(a);
            }

        return neighbours;
    }

    /// <summary>
    /// A position rounded to 1e-4 units.
    /// </summary>
    static (int, int, int) Key(Vector3 p) => ((int)MathF.Round(p.X * 1e4f), (int)MathF.Round(p.Y * 1e4f), (int)MathF.Round(p.Z * 1e4f));
}
