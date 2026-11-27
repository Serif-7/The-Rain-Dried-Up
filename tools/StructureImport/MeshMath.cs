using System.Numerics;

namespace StructureImport;

/// <summary>
/// Geometry steps of the import: normals for models without them, placing the model in game units, and tangents in the game's convention.
/// </summary>
public static class MeshMath
{
    /// <summary>
    /// World units per metre: the game's unit is 0.1 m (its collision files' gravity is 98.1 per second squared).
    /// </summary>
    public const float UnitsPerMetre = 10;

    /// <summary>
    /// Gives the vertices from <paramref name="first"/> on area-weighted normals of the triangles that use them (glTF says a mesh without
    /// normals is flat shaded; shared vertices of such a file are rare, and smooth normals are what the game's own meshes carry).
    /// </summary>
    public static void FlatNormals(SurfaceMesh mesh, int first)
    {
        var sums = new Vector3[mesh.Vertices.Count - first];
        for (int t = 0; t < mesh.Indices.Count; t += 3)
        {
            int a = mesh.Indices[t], b = mesh.Indices[t + 1], c = mesh.Indices[t + 2];
            if (a < first) continue;

            var face = Vector3.Cross(mesh.Vertices[b].Position - mesh.Vertices[a].Position, mesh.Vertices[c].Position - mesh.Vertices[a].Position);
            sums[a - first] += face;
            sums[b - first] += face;
            sums[c - first] += face;
        }

        for (int i = 0; i < sums.Length; i++)
        {
            var v = mesh.Vertices[first + i];
            mesh.Vertices[first + i] = v with { Normal = sums[i].LengthSquared() > 0 ? Vector3.Normalize(sums[i]) : Vector3.UnitY };
        }
    }

    /// <summary>
    /// The scale from model units (metres) to game units: <paramref name="height"/> metres tall when given, else the model's own size.
    /// </summary>
    public static float Scale(IReadOnlyList<SurfaceMesh> meshes, float? height)
    {
        var (min, max) = Bounds(meshes);
        float tall = max.Y - min.Y;
        if (height is null) return UnitsPerMetre;
        if (tall <= 0) throw new InvalidDataException("The model is flat: it has no height to scale to.");

        return height.Value * UnitsPerMetre / tall;
    }

    /// <summary>
    /// The scale from model units (metres) to game units: <paramref name="size"/> metres along the longest side when given, else the model's own
    /// size.
    /// </summary>
    public static float ScaleLongest(IReadOnlyList<SurfaceMesh> meshes, float? size)
    {
        var (min, max) = Bounds(meshes);
        var extent = max - min;
        float longest = MathF.Max(extent.X, MathF.Max(extent.Y, extent.Z));
        if (size is null) return UnitsPerMetre;
        if (longest <= 0) throw new InvalidDataException("The model has no size to scale to.");

        return size.Value * UnitsPerMetre / longest;
    }

    /// <summary>
    /// Moves every mesh into the building's space: turned <paramref name="yawDegrees"/> about Y, scaled by <paramref name="scale"/>, centred on
    /// X and Z, its lowest point <paramref name="sinkUnits"/> below the ground (y = 0, where the game places a building's origin).
    /// </summary>
    public static void Place(IReadOnlyList<SurfaceMesh> meshes, float scale, float yawDegrees, float sinkUnits)
    {
        var turn = Matrix4x4.CreateRotationY(yawDegrees * MathF.PI / 180);
        Transform(meshes, turn * Matrix4x4.CreateScale(scale));

        var (min, max) = Bounds(meshes);
        var centre = (min + max) / 2;
        Transform(meshes, Matrix4x4.CreateTranslation(-centre.X, -min.Y - sinkUnits, -centre.Z));
    }

    /// <summary>
    /// The smallest box holding every vertex.
    /// </summary>
    public static (Vector3 Min, Vector3 Max) Bounds(IEnumerable<SurfaceMesh> meshes)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var v in meshes.SelectMany(m => m.Vertices))
        {
            min = Vector3.Min(min, v.Position);
            max = Vector3.Max(max, v.Position);
        }

        return (min, max);
    }

    /// <summary>
    /// Per-vertex tangents (xyz) and handedness (w) in the game's convention: the tangent follows increasing U, and its building shader's
    /// binormal cross(tangent, normal) × w follows increasing V (verified against the game's meshes, docs/structures.md "Tangents").
    /// </summary>
    public static Vector4[] Tangents(SurfaceMesh mesh)
    {
        var alongU = new Vector3[mesh.Vertices.Count];
        var alongV = new Vector3[mesh.Vertices.Count];
        AccumulateUvDirections(mesh, alongU, alongV);

        var tangents = new Vector4[mesh.Vertices.Count];
        for (int i = 0; i < tangents.Length; i++)
            tangents[i] = Tangent(mesh.Vertices[i].Normal, alongU[i], alongV[i]);

        return tangents;
    }

    /// <summary>
    /// Adds each triangle's surface directions of increasing U and V to its three vertices.
    /// </summary>
    static void AccumulateUvDirections(SurfaceMesh mesh, Vector3[] alongU, Vector3[] alongV)
    {
        for (int t = 0; t < mesh.Indices.Count; t += 3)
        {
            int a = mesh.Indices[t], b = mesh.Indices[t + 1], c = mesh.Indices[t + 2];
            var e1 = mesh.Vertices[b].Position - mesh.Vertices[a].Position;
            var e2 = mesh.Vertices[c].Position - mesh.Vertices[a].Position;
            var d1 = mesh.Vertices[b].Uv - mesh.Vertices[a].Uv;
            var d2 = mesh.Vertices[c].Uv - mesh.Vertices[a].Uv;
            float r = d1.X * d2.Y - d2.X * d1.Y;
            if (MathF.Abs(r) < 1e-12f) continue;

            var u = (e1 * d2.Y - e2 * d1.Y) / r;
            var v = (e2 * d1.X - e1 * d2.X) / r;
            foreach (int i in (ReadOnlySpan<int>)[a, b, c])
            {
                alongU[i] += u;
                alongV[i] += v;
            }
        }
    }

    /// <summary>
    /// The tangent of one vertex: <paramref name="u"/> made perpendicular to the normal (any perpendicular when it has no UV direction), w so that
    /// cross(tangent, normal) × w points along <paramref name="v"/>.
    /// </summary>
    static Vector4 Tangent(Vector3 normal, Vector3 u, Vector3 v)
    {
        var t = u - normal * Vector3.Dot(normal, u);
        if (t.LengthSquared() < 1e-20f)
            t = Vector3.Cross(MathF.Abs(normal.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX, normal);
        t = Vector3.Normalize(t);

        float w = Vector3.Dot(Vector3.Cross(t, normal), v) < 0 ? -1 : 1;
        return new Vector4(t, w);
    }

    /// <summary>
    /// Applies <paramref name="m"/> to every position and normal.
    /// </summary>
    static void Transform(IReadOnlyList<SurfaceMesh> meshes, Matrix4x4 m)
    {
        foreach (var mesh in meshes)
            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                var v = mesh.Vertices[i];
                var normal = Vector3.TransformNormal(v.Normal, m);
                normal = normal.LengthSquared() > 0 ? Vector3.Normalize(normal) : v.Normal;
                mesh.Vertices[i] = v with { Position = Vector3.Transform(v.Position, m), Normal = normal };
            }
    }
}
