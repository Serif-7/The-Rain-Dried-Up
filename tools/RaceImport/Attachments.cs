using System.Numerics;

namespace RaceImport;

/// <summary>
/// Static parts merged into a fitted body (docs/race-import.md "Attachments"): each <see cref="Attachment"/> scaled, turned and placed by its
/// spec, weighted to its bones, and textured from a tile of the body texture, which grows to an atlas: the body's own texture in the left half,
/// the parts' in up to four tiles of the right half.
/// </summary>
public static class Attachments
{
    #region Merge

    /// <summary>
    /// Most parts one body takes (the right half of the atlas, two by two).
    /// </summary>
    public const int MaxParts = 4;

    /// <summary>
    /// <paramref name="mesh"/> with <paramref name="parts"/> appended and its own uvs moved into the atlas's left half (the grafted head's,
    /// below 0, stay), and each vertex's source vertex (<paramref name="source"/>, then -1 for the parts'). Unchanged without parts.
    /// </summary>
    public static (SkinnedMesh Mesh, int[] Source) Apply(SkinnedMesh mesh, int[] source, IReadOnlyList<Attachment> parts, KenshiSkeleton kenshi)
    {
        if (parts.Count == 0) return (mesh, source);
        if (parts.Count > MaxParts) throw new InvalidDataException($"at most {MaxParts} parts fit a body's texture, not {parts.Count}");

        var positions = new List<Vector3>(mesh.Positions);
        var normals = new List<Vector3>(mesh.Normals);
        var uvs = new List<Vector2>(mesh.Uvs.Select(BodyUv));
        var indices = new List<int>(mesh.Indices);
        var weights = new List<Dictionary<ushort, float>>(mesh.Weights);
        for (int k = 0; k < parts.Count; k++)
        {
            var (placed, turned) = Place(parts[k], mesh, kenshi);
            int first = positions.Count;
            AddVertices(parts[k], k, placed, turned, kenshi, positions, normals, uvs, weights);
            AddTriangles(parts[k], first, indices);
        }

        var merged = mesh with { Positions = [.. positions], Normals = [.. normals], Uvs = [.. uvs], Indices = [.. indices], Weights = [.. weights] };
        return (merged, [.. source, .. Enumerable.Repeat(-1, positions.Count - source.Length)]);
    }

    /// <summary>
    /// A body uv in the atlas: the left half (the grafted head's, below 0, unchanged).
    /// </summary>
    public static Vector2 BodyUv(Vector2 uv) => uv.Y < 0 ? uv : new Vector2(Math.Clamp(uv.X, 0, 1) * 0.5f, uv.Y);

    /// <summary>
    /// A part's uv in the atlas: its tile (<paramref name="tile"/>, row by row) of the right half.
    /// </summary>
    public static Vector2 PartUv(Vector2 uv, int tile) =>
        new(0.5f + (tile % 2 + Math.Clamp(uv.X, 0, 1)) * 0.25f, (tile / 2 + Math.Clamp(uv.Y, 0, 1)) * 0.5f);

    /// <summary>
    /// Appends a part's placed vertices (twice, the copy facing back, for a double-sided part).
    /// </summary>
    static void AddVertices(Attachment part, int tile, Vector3[] placed, Vector3[] turned, KenshiSkeleton kenshi, List<Vector3> positions,
        List<Vector3> normals, List<Vector2> uvs, List<Dictionary<ushort, float>> weights)
    {
        float[] facings = part.DoubleSided ? [1, -1] : [1];
        foreach (float facing in facings)
        {
            for (int v = 0; v < placed.Length; v++)
            {
                positions.Add(placed[v]);
                normals.Add(turned[v] * facing);
                uvs.Add(PartUv(part.Uvs[v], tile));
                weights.Add(Weights(part.Spec.Bones, placed[v].Y, kenshi));
            }
        }
    }

    /// <summary>
    /// Appends a part's triangles from vertex <paramref name="first"/> (and the back-facing copy's, wound the other way).
    /// </summary>
    static void AddTriangles(Attachment part, int first, List<int> indices)
    {
        indices.AddRange(part.Indices.Select(i => first + i));
        if (!part.DoubleSided) return;

        int back = first + part.Positions.Length;
        for (int t = 0; t < part.Indices.Length; t += 3)
        {
            indices.Add(back + part.Indices[t]);
            indices.Add(back + part.Indices[t + 2]);
            indices.Add(back + part.Indices[t + 1]);
        }
    }

    /// <summary>
    /// The weights of a part vertex at height <paramref name="y"/>: between the two of <paramref name="bones"/> (top to bottom) whose joints
    /// it lies between, else wholly on the nearer end one.
    /// </summary>
    public static Dictionary<ushort, float> Weights(string[] bones, float y, KenshiSkeleton kenshi)
    {
        for (int i = 0; i + 1 < bones.Length; i++)
        {
            float top = kenshi.Position(bones[i]).Y;
            float bottom = kenshi.Position(bones[i + 1]).Y;
            if (y >= top) break;
            if (y < bottom) continue;

            float share = (y - bottom) / (top - bottom);
            return new() { [kenshi.Handle(bones[i])] = share, [kenshi.Handle(bones[i + 1])] = 1 - share };
        }

        bool above = bones.Length == 1 || y >= kenshi.Position(bones[0]).Y;
        return new() { [kenshi.Handle(above ? bones[0] : bones[^1])] = 1 };
    }

    #endregion

    #region Placement

    /// <summary>
    /// A part's vertices placed on <paramref name="body"/> by its spec, and its normals turned with them.
    /// </summary>
    public static (Vector3[] Positions, Vector3[] Normals) Place(Attachment part, SkinnedMesh body, KenshiSkeleton kenshi)
    {
        var spec = part.Spec;
        float height = part.Positions.Max(p => p.Y) - part.Positions.Min(p => p.Y);
        float scale = spec.Height / height;
        var reference = Reference(spec.RelativeTo, body, kenshi);
        var turn = Matrix4x4.CreateRotationZ(-Radians(spec.Roll)) * Matrix4x4.CreateRotationX(-Radians(spec.Pitch)) *
            Matrix4x4.CreateRotationY(Radians(spec.Yaw));

        var positions = new Vector3[part.Positions.Length];
        var normals = new Vector3[part.Positions.Length];
        foreach (var piece in Pieces(part))
        {
            var mirror = piece.Left ? new Vector3(-1, 1, 1) : Vector3.One;
            var local = piece.Vertices.Select(v => part.Positions[v] * mirror * scale).ToArray();
            var anchor = Anchor(local, spec.Anchor);
            var target = reference + new Vector3(spec.At[0], spec.At[1], spec.At[2]);
            for (int i = 0; i < local.Length; i++)
            {
                int v = piece.Vertices[i];
                positions[v] = (Vector3.Transform(local[i] - anchor, turn) + target) * mirror;
                normals[v] = Vector3.Normalize(Vector3.TransformNormal(part.Normals[v] * mirror, turn) * mirror);
            }

            if (spec.Behind is { } gap) MoveBehind(positions, piece.Vertices, Carried(body, spec.Bones, kenshi), gap);
        }

        return (positions, normals);
    }

    /// <summary>
    /// The body vertices carried most by one of <paramref name="bones"/> (the torso a back part rides, not the arms swinging past it).
    /// </summary>
    static Vector3[] Carried(SkinnedMesh body, string[] bones, KenshiSkeleton kenshi)
    {
        var handles = bones.Select(kenshi.Handle).ToHashSet();
        return [.. Enumerable.Range(0, body.Positions.Length)
            .Where(v => body.Weights[v].Count > 0 && handles.Contains(body.Weights[v].MaxBy(p => p.Value).Key))
            .Select(v => body.Positions[v])];
    }

    /// <summary>
    /// A part's pieces: its two halves split at X = 0 (the left one flagged, to be mirrored), or the whole model as one right-hand piece.
    /// </summary>
    static IEnumerable<(bool Left, int[] Vertices)> Pieces(Attachment part)
    {
        var all = Enumerable.Range(0, part.Positions.Length);
        if (!part.Spec.Split)
        {
            yield return (false, [.. all]);
            yield break;
        }

        yield return (false, [.. all.Where(v => part.Positions[v].X >= 0)]);
        yield return (true, [.. all.Where(v => part.Positions[v].X < 0)]);
    }

    /// <summary>
    /// The point of a piece's bounds at the given shares of each axis.
    /// </summary>
    static Vector3 Anchor(Vector3[] piece, float[] shares)
    {
        var min = piece.Aggregate(Vector3.Min);
        var max = piece.Aggregate(Vector3.Max);
        return min + (max - min) * new Vector3(shares[0], shares[1], shares[2]);
    }

    /// <summary>
    /// The point a spec's offset is measured from, on the centre line: the top of the head (the highest vertex carried most by
    /// <c>Bip01 Head</c>, at the mean depth of those near it) or a bone's joint.
    /// </summary>
    public static Vector3 Reference(string relativeTo, SkinnedMesh body, KenshiSkeleton kenshi)
    {
        if (relativeTo != "HeadTop") return kenshi.Position(relativeTo) with { X = 0 };

        ushort head = kenshi.Handle("Bip01 Head");
        var onHead = Enumerable.Range(0, body.Positions.Length)
            .Where(v => body.Weights[v].Count > 0 && body.Weights[v].MaxBy(p => p.Value).Key == head)
            .Select(v => body.Positions[v])
            .ToArray();
        if (onHead.Length == 0) return kenshi.Position("Bip01 Head") with { X = 0 };

        float top = onHead.Max(p => p.Y);
        float depth = onHead.Where(p => p.Y > top - 0.2f).Average(p => p.Z);
        return new Vector3(0, top, depth);
    }

    /// <summary>
    /// How near a body vertex must lie across and up to count as in front of a part vertex, in game units.
    /// </summary>
    const float Column = 0.4f;

    /// <summary>
    /// Moves a piece front to back until its nearest point lies <paramref name="gap"/> behind the <paramref name="body"/> vertices in front of it
    /// (those within <see cref="Column"/> across and up).
    /// </summary>
    static void MoveBehind(Vector3[] positions, int[] piece, Vector3[] body, float gap)
    {
        var backs = new Dictionary<(int, int), float>();
        foreach (var p in body)
        {
            var cell = Cell(p);
            backs[cell] = backs.TryGetValue(cell, out float z) ? MathF.Min(z, p.Z) : p.Z;
        }

        float shift = float.MinValue;
        foreach (int v in piece)
            if (Back(backs, positions[v]) is { } back) shift = MathF.Max(shift, positions[v].Z - (back - gap));

        if (shift == float.MinValue) return;

        foreach (int v in piece) positions[v].Z -= shift;
    }

    /// <summary>
    /// The back of the body across from a point (the least depth in its cell and the eight around it), null where no body is.
    /// </summary>
    static float? Back(Dictionary<(int, int), float> backs, Vector3 p)
    {
        var (x, y) = Cell(p);
        float? back = null;
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                if (backs.TryGetValue((x + dx, y + dy), out float z)) back = back is { } b ? MathF.Min(b, z) : z;

        return back;
    }

    /// <summary>
    /// The across-and-up grid cell of a point.
    /// </summary>
    static (int, int) Cell(Vector3 p) => ((int)MathF.Floor(p.X / Column), (int)MathF.Floor(p.Y / Column));

    /// <summary>
    /// Degrees in radians.
    /// </summary>
    static float Radians(float degrees) => degrees * MathF.PI / 180;

    #endregion
}
