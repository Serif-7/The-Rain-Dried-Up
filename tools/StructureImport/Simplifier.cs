using System.Numerics;

namespace StructureImport;

/// <summary>
/// A triangle soup reduced to its shape: welded positions and three indices per triangle.
/// </summary>
public sealed record TriangleShape(Vector3[] Points, int[] Triangles);

/// <summary>
/// Reduces a model to a few triangles for its collision, by quadric-error edge collapse (Garland and Heckbert): positions only, open edges held
/// in place by planes across them, collapses that would turn a triangle over refused.
/// </summary>
public sealed class Simplifier
{
    /// <summary>
    /// How strongly an open edge resists moving, relative to the surface's own planes.
    /// </summary>
    const double BoundaryWeight = 100;

    /// <summary>
    /// The least cosine between a triangle's normal before and after a collapse.
    /// </summary>
    const float FlipCosine = 0.2f;

    /// <summary>
    /// The vertex positions.
    /// </summary>
    readonly Vector3[] _points;

    /// <summary>
    /// Each vertex's error quadric.
    /// </summary>
    readonly Quadric[] _quadrics;

    /// <summary>
    /// Bumped whenever a vertex moves or merges, so queued collapses that knew its old state are skipped.
    /// </summary>
    readonly int[] _versions;

    /// <summary>
    /// Whether a vertex was merged away.
    /// </summary>
    readonly bool[] _gone;

    /// <summary>
    /// The triangles, three vertices each.
    /// </summary>
    readonly int[] _triangles;

    /// <summary>
    /// Whether a triangle collapsed to nothing.
    /// </summary>
    readonly bool[] _dead;

    /// <summary>
    /// The triangles round each vertex (may still list dead ones).
    /// </summary>
    readonly List<int>[] _around;

    /// <summary>
    /// The candidate collapses, cheapest first.
    /// </summary>
    readonly PriorityQueue<(int A, int B, int VersionA, int VersionB, Vector3 Target), double> _queue = new();

    /// <summary>
    /// The triangles still alive.
    /// </summary>
    int _alive;

    /// <summary>
    /// Sets up the reduction of <paramref name="shape"/>.
    /// </summary>
    Simplifier(TriangleShape shape)
    {
        _points = (Vector3[])shape.Points.Clone();
        _triangles = (int[])shape.Triangles.Clone();
        _quadrics = new Quadric[_points.Length];
        _versions = new int[_points.Length];
        _gone = new bool[_points.Length];
        _dead = new bool[_triangles.Length / 3];
        _around = new List<int>[_points.Length];
        for (int i = 0; i < _around.Length; i++) _around[i] = [];
        _alive = _dead.Length;
    }

    /// <summary>
    /// Welds equal positions (to 0.001 units) of the meshes into one shape, dropping triangles that weld to a line.
    /// </summary>
    public static TriangleShape Weld(IEnumerable<SurfaceMesh> meshes)
    {
        var index = new Dictionary<(long, long, long), int>();
        var points = new List<Vector3>();
        var triangles = new List<int>();
        foreach (var mesh in meshes)
            for (int t = 0; t < mesh.Indices.Count; t += 3)
            {
                int a = PointIndex(mesh.Vertices[mesh.Indices[t]].Position, index, points);
                int b = PointIndex(mesh.Vertices[mesh.Indices[t + 1]].Position, index, points);
                int c = PointIndex(mesh.Vertices[mesh.Indices[t + 2]].Position, index, points);
                if (a == b || b == c || a == c) continue;

                triangles.AddRange([a, b, c]);
            }

        return new TriangleShape([.. points], [.. triangles]);
    }

    /// <summary>
    /// <paramref name="shape"/> with at most <paramref name="maxTriangles"/> triangles (fewer collapses are possible on a very open mesh).
    /// </summary>
    public static TriangleShape Reduce(TriangleShape shape, int maxTriangles)
    {
        if (shape.Triangles.Length / 3 <= maxTriangles) return shape;

        var s = new Simplifier(shape);
        s.BuildQuadrics();
        s.QueueAllEdges();
        s.Collapse(maxTriangles);
        return s.Result();
    }

    /// <summary>
    /// The index of a welded position, adding it when new.
    /// </summary>
    static int PointIndex(Vector3 p, Dictionary<(long, long, long), int> index, List<Vector3> points)
    {
        var key = ((long)MathF.Round(p.X * 1000), (long)MathF.Round(p.Y * 1000), (long)MathF.Round(p.Z * 1000));
        if (index.TryGetValue(key, out int i)) return i;

        index[key] = points.Count;
        points.Add(p);
        return points.Count - 1;
    }

    /// <summary>
    /// Each vertex's quadric: the area-weighted planes of its triangles, plus planes across each open edge.
    /// </summary>
    void BuildQuadrics()
    {
        var edgeUse = new Dictionary<(int, int), int>();
        for (int t = 0; t < _dead.Length; t++)
        {
            var (a, b, c) = Corners(t);
            foreach (int v in (ReadOnlySpan<int>)[a, b, c]) _around[v].Add(t);

            var plane = Quadric.FromPlane(_points[a], _points[b], _points[c], 1);
            _quadrics[a] += plane;
            _quadrics[b] += plane;
            _quadrics[c] += plane;
            foreach (var edge in (ReadOnlySpan<(int, int)>)[(a, b), (b, c), (c, a)])
                edgeUse[Key(edge.Item1, edge.Item2)] = edgeUse.GetValueOrDefault(Key(edge.Item1, edge.Item2)) + 1;
        }

        for (int t = 0; t < _dead.Length; t++) AddBoundaryPlanes(t, edgeUse);
    }

    /// <summary>
    /// Adds, for each edge of triangle <paramref name="t"/> that no other triangle shares, the plane through it square to the triangle.
    /// </summary>
    void AddBoundaryPlanes(int t, Dictionary<(int, int), int> edgeUse)
    {
        var (a, b, c) = Corners(t);
        var normal = Vector3.Cross(_points[b] - _points[a], _points[c] - _points[a]);
        foreach (var (p, q) in (ReadOnlySpan<(int, int)>)[(a, b), (b, c), (c, a)])
        {
            if (edgeUse[Key(p, q)] != 1) continue;

            var across = _points[p] + normal;
            var plane = Quadric.FromPlane(_points[p], _points[q], across, BoundaryWeight);
            _quadrics[p] += plane;
            _quadrics[q] += plane;
        }
    }

    /// <summary>
    /// Queues a collapse for every edge.
    /// </summary>
    void QueueAllEdges()
    {
        var seen = new HashSet<(int, int)>();
        for (int t = 0; t < _dead.Length; t++)
        {
            var (a, b, c) = Corners(t);
            foreach (var (p, q) in (ReadOnlySpan<(int, int)>)[(a, b), (b, c), (c, a)])
                if (seen.Add(Key(p, q))) Queue(p, q);
        }
    }

    /// <summary>
    /// Collapses the cheapest valid edges until at most <paramref name="maxTriangles"/> triangles are alive or none can go.
    /// </summary>
    void Collapse(int maxTriangles)
    {
        while (_alive > maxTriangles && _queue.TryDequeue(out var c, out _))
        {
            if (_gone[c.A] || _gone[c.B] || _versions[c.A] != c.VersionA || _versions[c.B] != c.VersionB) continue;
            if (Flips(c.A, c.B, c.Target) || Flips(c.B, c.A, c.Target)) continue;

            Merge(c.A, c.B, c.Target);
        }
    }

    /// <summary>
    /// Merges vertex <paramref name="b"/> into <paramref name="a"/> at <paramref name="target"/>, killing the triangles they shared.
    /// </summary>
    void Merge(int a, int b, Vector3 target)
    {
        _points[a] = target;
        _quadrics[a] += _quadrics[b];
        _gone[b] = true;
        foreach (int t in _around[b])
        {
            if (_dead[t]) continue;

            var (x, y, z) = Corners(t);
            if (x == a || y == a || z == a)
            {
                _dead[t] = true;
                _alive--;
                continue;
            }

            for (int k = 0; k < 3; k++)
                if (_triangles[t * 3 + k] == b) _triangles[t * 3 + k] = a;
            _around[a].Add(t);
        }

        _around[a].RemoveAll(t => _dead[t]);
        _versions[a]++;
        foreach (int n in Neighbours(a)) Queue(a, n);
    }

    /// <summary>
    /// Whether moving <paramref name="v"/> to <paramref name="target"/> turns over (or squashes) a triangle round it that doesn't hold
    /// <paramref name="other"/>.
    /// </summary>
    bool Flips(int v, int other, Vector3 target)
    {
        foreach (int t in _around[v])
        {
            if (_dead[t]) continue;

            var (a, b, c) = Corners(t);
            if (a == other || b == other || c == other) continue;

            var before = Vector3.Cross(_points[b] - _points[a], _points[c] - _points[a]);
            Vector3 P(int i) => i == v ? target : _points[i];
            var after = Vector3.Cross(P(b) - P(a), P(c) - P(a));
            if (after.LengthSquared() < 1e-12f) return true;
            if (before.LengthSquared() > 0 && Vector3.Dot(Vector3.Normalize(before), Vector3.Normalize(after)) < FlipCosine) return true;
        }

        return false;
    }

    /// <summary>
    /// Queues the collapse of edge (<paramref name="a"/>, <paramref name="b"/>) at its cheapest point.
    /// </summary>
    void Queue(int a, int b)
    {
        var q = _quadrics[a] + _quadrics[b];
        var target = q.Minimum(_points[a], _points[b]);
        _queue.Enqueue((a, b, _versions[a], _versions[b], target), q.Error(target));
    }

    /// <summary>
    /// The live vertices sharing a triangle with <paramref name="v"/>.
    /// </summary>
    HashSet<int> Neighbours(int v)
    {
        var set = new HashSet<int>();
        foreach (int t in _around[v])
        {
            if (_dead[t]) continue;

            var (a, b, c) = Corners(t);
            set.UnionWith([a, b, c]);
        }

        set.Remove(v);
        return set;
    }

    /// <summary>
    /// The surviving triangles over their vertices, renumbered.
    /// </summary>
    TriangleShape Result()
    {
        var map = new Dictionary<int, int>();
        var points = new List<Vector3>();
        var triangles = new List<int>();
        for (int t = 0; t < _dead.Length; t++)
        {
            if (_dead[t]) continue;

            for (int k = 0; k < 3; k++)
            {
                int v = _triangles[t * 3 + k];
                if (!map.TryGetValue(v, out int n))
                {
                    map[v] = n = points.Count;
                    points.Add(_points[v]);
                }

                triangles.Add(n);
            }
        }

        return new TriangleShape([.. points], [.. triangles]);
    }

    /// <summary>
    /// The three vertices of triangle <paramref name="t"/>.
    /// </summary>
    (int, int, int) Corners(int t) => (_triangles[t * 3], _triangles[t * 3 + 1], _triangles[t * 3 + 2]);

    /// <summary>
    /// An edge's key, the lower vertex first.
    /// </summary>
    static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);
}

/// <summary>
/// A symmetric 4 × 4 error quadric: the sum of squared distances to a set of planes, as its ten distinct entries.
/// </summary>
public readonly record struct Quadric(double Xx, double Xy, double Xz, double Xw, double Yy, double Yz, double Yw, double Zz, double Zw, double Ww)
{
    /// <summary>
    /// The quadric of the plane through three points, times <paramref name="weight"/> and the triangle's area; zero for a degenerate triangle.
    /// </summary>
    public static Quadric FromPlane(Vector3 a, Vector3 b, Vector3 c, double weight)
    {
        var cross = Vector3.Cross(b - a, c - a);
        double length = cross.Length();
        if (length < 1e-12) return default;

        double nx = cross.X / length, ny = cross.Y / length, nz = cross.Z / length;
        double d = -(nx * a.X + ny * a.Y + nz * a.Z);
        double w = weight * length / 2;
        return new Quadric(w * nx * nx, w * nx * ny, w * nx * nz, w * nx * d, w * ny * ny, w * ny * nz, w * ny * d, w * nz * nz, w * nz * d, w * d * d);
    }

    /// <summary>
    /// The sum of two quadrics.
    /// </summary>
    public static Quadric operator +(Quadric p, Quadric q) => new(p.Xx + q.Xx, p.Xy + q.Xy, p.Xz + q.Xz, p.Xw + q.Xw, p.Yy + q.Yy, p.Yz + q.Yz,
        p.Yw + q.Yw, p.Zz + q.Zz, p.Zw + q.Zw, p.Ww + q.Ww);

    /// <summary>
    /// The error at point <paramref name="v"/>.
    /// </summary>
    public double Error(Vector3 v)
    {
        double x = v.X, y = v.Y, z = v.Z;
        return Xx * x * x + 2 * Xy * x * y + 2 * Xz * x * z + 2 * Xw * x + Yy * y * y + 2 * Yz * y * z + 2 * Yw * y + Zz * z * z + 2 * Zw * z + Ww;
    }

    /// <summary>
    /// The point of least error, or the best of <paramref name="a"/>, <paramref name="b"/> and their midpoint when the quadric has no unique
    /// minimum.
    /// </summary>
    public Vector3 Minimum(Vector3 a, Vector3 b)
    {
        double det = Xx * (Yy * Zz - Yz * Yz) - Xy * (Xy * Zz - Yz * Xz) + Xz * (Xy * Yz - Yy * Xz);
        if (Math.Abs(det) > 1e-10)
        {
            double x = -(Xw * (Yy * Zz - Yz * Yz) - Xy * (Yw * Zz - Yz * Zw) + Xz * (Yw * Yz - Yy * Zw)) / det;
            double y = -(Xx * (Yw * Zz - Zw * Yz) - Xw * (Xy * Zz - Yz * Xz) + Xz * (Xy * Zw - Yw * Xz)) / det;
            double z = -(Xx * (Yy * Zw - Yz * Yw) - Xy * (Xy * Zw - Yw * Xz) + Xw * (Xy * Yz - Yy * Xz)) / det;
            var solved = new Vector3((float)x, (float)y, (float)z);

            // A nearly singular system can put the point far away; keep it only near the edge.
            float reach = Vector3.Distance(a, b) * 2 + 1e-3f;
            if (Vector3.Distance(solved, (a + b) / 2) <= reach) return solved;
        }

        var mid = (a + b) / 2;
        double ea = Error(a), eb = Error(b), em = Error(mid);
        return em <= ea && em <= eb ? mid : ea <= eb ? a : b;
    }
}
