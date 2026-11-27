using System.Numerics;

namespace RaceImport;

/// <summary>
/// Closes the holes that dropping a model's hair opens (docs/race-import.md "Hair holes"). Meshy fuses long hair to the body as one shell, with
/// no skin under it, so <c>nohair</c> leaves the upper back open. The human's skin triangles the fitted body leaves bare there are copied in,
/// grown <see cref="Rings"/> rings under the body's edge and sunk <see cref="Sink"/> under the human's skin, so the body's own skin is drawn
/// over them where they overlap and no seam opens. They keep the human's weights and take one skin texel of the body's texture.
/// </summary>
public static class HairHolePatch
{
    /// <summary>
    /// A human triangle is bare when no body triangle lies this close to its centre or one of its corners, in game units: about a fitted body's triangle, far more
    /// than the gap the skin conform leaves.
    /// </summary>
    const float Bare = 0.25f;

    /// <summary>
    /// How many rings of the human's triangles the bare ones grow by, so that the patch reaches under the body's open edge; only into
    /// triangles the body covers from outside (<see cref="Covered"/>).
    /// </summary>
    const int Rings = 3;

    /// <summary>
    /// How far under the human's skin the patch sits, in game units, so the body's skin is drawn over it where they overlap.
    /// </summary>
    const float Sink = 0.04f;

    /// <summary>
    /// A bare region counts only when one of its triangles lies within this of one of the body's open edges, in game units: so bare skin
    /// under a fuller body (a bust lifted off the human's) is not copied, only skin under a hole.
    /// </summary>
    const float NearEdge = 0.6f;

    /// <summary>
    /// The source value of the patch's vertices in <see cref="Apply"/>'s result (the head's and the parts' are -1).
    /// </summary>
    public const int Patched = -2;

    /// <summary>
    /// The human bones whose skin may be copied: the torso, the neck (to the head's rim), the shoulders and the upper arms, where long
    /// hair hangs.
    /// </summary>
    static readonly string[] Bones =
        ["Bip01 Spine", "Bip01 Spine1", "Bip01 Spine2", "Bip01 Neck", "Bip01 Head", "Bip01 L Clavicle", "Bip01 R Clavicle", "Bip01 L UpperArm", "Bip01 R UpperArm"];

    /// <summary>
    /// <paramref name="mesh"/> with the bare human skin under its holes appended (their vertices' source <see cref="Patched"/>, uv
    /// <paramref name="skinUv"/>), and how many triangles that adds.
    /// </summary>
    public static (SkinnedMesh Mesh, int[] Source, int Triangles) Apply(SkinnedMesh mesh, int[] source, HumanBody human, KenshiSkeleton kenshi,
        Vector2 skinUv)
    {
        var patch = PatchTriangles(mesh, human, kenshi);
        if (patch.Count == 0) return (mesh, source, 0);

        var (joined, added) = Joined(mesh, human, patch, skinUv);
        return (joined, [.. source, .. Enumerable.Repeat(Patched, added)], patch.Count);
    }

    /// <summary>
    /// The human's triangles (by index) to copy: the bare ones on <see cref="Bones"/> joined to one near an open edge of the body, grown by
    /// <see cref="Rings"/>.
    /// </summary>
    public static HashSet<int> PatchTriangles(SkinnedMesh mesh, HumanBody human, KenshiSkeleton kenshi)
    {
        var allowed = Bones.Where(kenshi.Has).Select(kenshi.Handle).ToHashSet();
        var grid = new TriangleGrid(mesh.Positions, mesh.Indices, 2 * Bare);
        var edges = OpenEdgePoints(mesh);
        int triangles = human.Indices.Length / 3;
        var bare = Enumerable.Range(0, triangles)
            .Where(t => Corners(human, t).All(v => allowed.Contains(human.Bones[v])))
            .Where(t => Points(human, t).Any(p => !grid.Within(p, Bare)))
            .ToHashSet();
        var neighbours = Neighbours(human);
        var underHoles = Connected(bare.Where(t => edges.Any(e => Vector3.Distance(e, Centre(human, t)) < NearEdge + Bare)), bare, neighbours);

        return Grown(underHoles, neighbours, t => Corners(human, t).All(v => allowed.Contains(human.Bones[v])) && Covered(human, t, grid));
    }

    /// <summary>
    /// The positions of the body's open edges (edges of one triangle, along welded vertices) away from the head's neck rim: the head's
    /// vertices (uv below 0) are left out, so its rim and the eyes don't count.
    /// </summary>
    static List<Vector3> OpenEdgePoints(SkinnedMesh mesh)
    {
        var weld = Surface.Weld(mesh.Positions);
        var count = new Dictionary<(int, int), int>();
        for (int t = 0; t < mesh.Indices.Length; t += 3)
            for (int k = 0; k < 3; k++)
            {
                int a = weld[mesh.Indices[t + k]], b = weld[mesh.Indices[t + (k + 1) % 3]];
                if (a == b) continue;

                var key = (Math.Min(a, b), Math.Max(a, b));
                count[key] = count.GetValueOrDefault(key) + 1;
            }

        var body = Enumerable.Range(0, mesh.Positions.Length).Where(v => mesh.Uvs[v].Y >= 0).Select(v => weld[v]).ToHashSet();
        return [.. count.Where(p => p.Value == 1 && body.Contains(p.Key.Item1) && body.Contains(p.Key.Item2))
            .Select(p => (mesh.Positions[p.Key.Item1] + mesh.Positions[p.Key.Item2]) / 2)];
    }

    /// <summary>
    /// Whether the body covers a human triangle from outside: its nearest point to the triangle's centre lies on or outside the human's skin
    /// there, so the patch, sunk under the skin, stays hidden (where the body sits inside the skin the patch would show over it).
    /// </summary>
    static bool Covered(HumanBody human, int t, TriangleGrid grid)
    {
        var c = Corners(human, t);
        var normal = Vector3.Cross(human.Positions[c[1]] - human.Positions[c[0]], human.Positions[c[2]] - human.Positions[c[0]]);
        var centre = Centre(human, t);
        return grid.Nearest(centre, 2 * Bare) is { } q && normal != Vector3.Zero && Vector3.Dot(q - centre, Vector3.Normalize(normal)) > -Sink / 2;
    }

    /// <summary>
    /// The human's triangles sharing a corner (along welded vertices) with each.
    /// </summary>
    static List<int>[] Neighbours(HumanBody human)
    {
        var weld = Surface.Weld(human.Positions);
        int triangles = human.Indices.Length / 3;
        var byCorner = new Dictionary<int, List<int>>();
        for (int t = 0; t < triangles; t++)
            foreach (int v in Corners(human, t))
                (byCorner.TryGetValue(weld[v], out var list) ? list : byCorner[weld[v]] = []).Add(t);

        return [.. Enumerable.Range(0, triangles).Select(t => Corners(human, t).SelectMany(v => byCorner[weld[v]]).Distinct().ToList())];
    }

    /// <summary>
    /// The triangles of <paramref name="region"/> reached from <paramref name="seeds"/> through others of it.
    /// </summary>
    static HashSet<int> Connected(IEnumerable<int> seeds, HashSet<int> region, List<int>[] neighbours)
    {
        var reached = seeds.ToHashSet();
        var queue = new Queue<int>(reached);
        while (queue.TryDequeue(out int t))
            foreach (int n in neighbours[t])
                if (region.Contains(n) && reached.Add(n)) queue.Enqueue(n);

        return reached;
    }

    /// <summary>
    /// <paramref name="seed"/> grown by <see cref="Rings"/> rings of neighbouring triangles that <paramref name="allowed"/> admits.
    /// </summary>
    static HashSet<int> Grown(HashSet<int> seed, List<int>[] neighbours, Func<int, bool> allowed)
    {
        var grown = new HashSet<int>(seed);
        for (int ring = 0; ring < Rings; ring++) grown.UnionWith([.. grown.SelectMany(t => neighbours[t]).Where(allowed)]);

        return grown;
    }

    /// <summary>
    /// The mesh with the <paramref name="patch"/> triangles of the human appended, sunk along the human's vertex normals, and how many vertices
    /// that adds.
    /// </summary>
    static (SkinnedMesh Mesh, int Added) Joined(SkinnedMesh mesh, HumanBody human, HashSet<int> patch, Vector2 skinUv)
    {
        var normals = VertexNormals(human);
        var index = new Dictionary<int, int>();
        var positions = mesh.Positions.ToList();
        var meshNormals = mesh.Normals.ToList();
        var uvs = mesh.Uvs.ToList();
        var weights = mesh.Weights.ToList();
        var indices = mesh.Indices.ToList();
        foreach (int t in patch.Order())
            foreach (int v in Corners(human, t))
            {
                if (!index.TryGetValue(v, out int at))
                {
                    at = index[v] = positions.Count;
                    positions.Add(human.Positions[v] - Sink * normals[v]);
                    meshNormals.Add(normals[v]);
                    uvs.Add(skinUv);
                    weights.Add(new Dictionary<ushort, float>(human.Weights[v]));
                }

                indices.Add(at);
            }

        var joined = mesh with { Positions = [.. positions], Normals = [.. meshNormals], Uvs = [.. uvs], Weights = [.. weights], Indices = [.. indices] };
        return (joined, index.Count);
    }

    /// <summary>
    /// The human's outward vertex normals, area-weighted over welded vertices so seams don't split them.
    /// </summary>
    static Vector3[] VertexNormals(HumanBody human)
    {
        var weld = Surface.Weld(human.Positions);
        var sums = new Vector3[human.Positions.Length];
        for (int t = 0; t < human.Indices.Length / 3; t++)
        {
            var c = Corners(human, t);
            var normal = Vector3.Cross(human.Positions[c[1]] - human.Positions[c[0]], human.Positions[c[2]] - human.Positions[c[0]]);
            foreach (int v in c) sums[weld[v]] += normal;
        }

        return [.. Enumerable.Range(0, sums.Length).Select(v => sums[weld[v]] == Vector3.Zero ? Vector3.UnitY : Vector3.Normalize(sums[weld[v]]))];
    }

    /// <summary>
    /// A human triangle's three vertices.
    /// </summary>
    static int[] Corners(HumanBody human, int t) => [human.Indices[3 * t], human.Indices[3 * t + 1], human.Indices[3 * t + 2]];

    /// <summary>
    /// A human triangle's corners and centre.
    /// </summary>
    static Vector3[] Points(HumanBody human, int t) => [.. Corners(human, t).Select(v => human.Positions[v]), Centre(human, t)];

    /// <summary>
    /// A human triangle's centre.
    /// </summary>
    static Vector3 Centre(HumanBody human, int t) =>
        (human.Positions[human.Indices[3 * t]] + human.Positions[human.Indices[3 * t + 1]] + human.Positions[human.Indices[3 * t + 2]]) / 3;
}

/// <summary>
/// A mesh's triangles bucketed in a uniform grid, for "is any triangle within this distance" queries.
/// </summary>
sealed class TriangleGrid
{
    /// <summary>
    /// Side of a cell in game units.
    /// </summary>
    readonly float _cell;

    /// <summary>
    /// The mesh's positions.
    /// </summary>
    readonly Vector3[] _positions;

    /// <summary>
    /// The mesh's triangle list.
    /// </summary>
    readonly int[] _indices;

    /// <summary>
    /// The triangles (by first index) whose bounds touch each cell.
    /// </summary>
    readonly Dictionary<(int, int, int), List<int>> _cells = [];

    /// <summary>
    /// Buckets the triangles in cells of side <paramref name="cell"/>.
    /// </summary>
    public TriangleGrid(Vector3[] positions, int[] indices, float cell)
    {
        _cell = cell;
        _positions = positions;
        _indices = indices;
        for (int t = 0; t < indices.Length; t += 3)
        {
            var (a, b, c) = (positions[indices[t]], positions[indices[t + 1]], positions[indices[t + 2]]);
            var (lo, hi) = (Cell(Vector3.Min(a, Vector3.Min(b, c))), Cell(Vector3.Max(a, Vector3.Max(b, c))));
            for (int x = lo.Item1; x <= hi.Item1; x++)
                for (int y = lo.Item2; y <= hi.Item2; y++)
                    for (int z = lo.Item3; z <= hi.Item3; z++)
                        (_cells.TryGetValue((x, y, z), out var list) ? list : _cells[(x, y, z)] = []).Add(t);
        }
    }

    /// <summary>
    /// Whether a triangle lies within <paramref name="distance"/> (at most the cell side) of <paramref name="p"/>.
    /// </summary>
    public bool Within(Vector3 p, float distance)
    {
        var (cx, cy, cz) = Cell(p);
        for (int x = cx - 1; x <= cx + 1; x++)
            for (int y = cy - 1; y <= cy + 1; y++)
                for (int z = cz - 1; z <= cz + 1; z++)
                {
                    if (!_cells.TryGetValue((x, y, z), out var list)) continue;

                    foreach (int t in list)
                        if (Vector3.Distance(p, Closest(p, t)) < distance) return true;
                }

        return false;
    }

    /// <summary>
    /// The nearest point of any triangle within <paramref name="distance"/> (at most the cell side) of <paramref name="p"/>, null for none.
    /// </summary>
    public Vector3? Nearest(Vector3 p, float distance)
    {
        Vector3? best = null;
        float bestDistance = distance;
        var (cx, cy, cz) = Cell(p);
        for (int x = cx - 1; x <= cx + 1; x++)
            for (int y = cy - 1; y <= cy + 1; y++)
                for (int z = cz - 1; z <= cz + 1; z++)
                {
                    if (!_cells.TryGetValue((x, y, z), out var list)) continue;

                    foreach (int t in list)
                    {
                        var q = Closest(p, t);
                        float d = Vector3.Distance(p, q);
                        if (d < bestDistance) (best, bestDistance) = (q, d);
                    }
                }

        return best;
    }

    /// <summary>
    /// The cell holding <paramref name="p"/>.
    /// </summary>
    (int, int, int) Cell(Vector3 p) => ((int)MathF.Floor(p.X / _cell), (int)MathF.Floor(p.Y / _cell), (int)MathF.Floor(p.Z / _cell));

    /// <summary>
    /// The point of triangle <paramref name="t"/> nearest <paramref name="p"/>.
    /// </summary>
    Vector3 Closest(Vector3 p, int t) => SkinConform.ClosestOnTriangle(p, _positions[_indices[t]], _positions[_indices[t + 1]], _positions[_indices[t + 2]]);
}
