using System.Numerics;
using System.Text;

namespace RaceImport;

/// <summary>
/// Writes a skinned Ogre binary mesh, <c>[MeshSerializer_v1.100]</c>: one submesh with its own geometry (position, normal, uv, tangent), up to
/// four bone assignments per vertex, a link to the game's skeleton, the bounds and any poses (engine docs/formats/ogre-mesh.md).
/// </summary>
public static class SkinnedMeshWriter
{
    /// <summary>
    /// The header string.
    /// </summary>
    const string Version = "[MeshSerializer_v1.100]";

    /// <summary>
    /// Chunk ids: header, mesh, submesh, submesh operation, bone assignment, geometry, declaration, element, vertex buffer, buffer data,
    /// skeleton link, bounds, poses, pose, pose vertex.
    /// </summary>
    const ushort Header = 0x1000, MeshChunk = 0x3000, SubMesh = 0x4000, SubMeshOperation = 0x4010, BoneAssignment = 0x4100, Geometry = 0x5000,
        Declaration = 0x5100, Element = 0x5110, VertexBuffer = 0x5200, BufferData = 0x5210, SkeletonLink = 0x6000, Bounds = 0x9000,
        PosesChunk = 0xC000, PoseChunk = 0xC100, PoseVertex = 0xC111;

    /// <summary>
    /// Element types (float2, float3, float4) and semantics (position, normal, texture coordinates, tangent).
    /// </summary>
    const ushort Float2 = 1, Float3 = 2, Float4 = 3, PositionSemantic = 1, NormalSemantic = 4, UvSemantic = 7, TangentSemantic = 9;

    /// <summary>
    /// Bytes per vertex: position 12, normal 12, uv 8, tangent 16.
    /// </summary>
    const ushort Stride = 48;

    /// <summary>
    /// Most bone assignments a vertex keeps (the game's skinning shader blends four).
    /// </summary>
    const int MaxWeights = 4;

    /// <summary>
    /// Writes the mesh to <paramref name="path"/>, its submesh using <paramref name="material"/> and linking <paramref name="skeleton"/>.
    /// </summary>
    public static void Write(string path, SkinnedMesh mesh, string material, string skeleton)
    {
        using var stream = File.Create(path);
        using var w = new BinaryWriter(stream);
        w.Write(Header);
        WriteString(w, Version);
        Chunk(w, MeshChunk, () =>
        {
            w.Write((byte)1); // skeletally animated
            WriteSubMesh(w, mesh, material);
            Chunk(w, SkeletonLink, () => WriteString(w, skeleton));
            WriteBounds(w, mesh.Positions);
            if (mesh.Poses.Length > 0) WritePoses(w, mesh.Poses);
        });
    }

    /// <summary>
    /// The poses, each on the submesh's vertices (target 1) without normal offsets, as the game's human bodies store them.
    /// </summary>
    static void WritePoses(BinaryWriter w, MeshPose[] poses) => Chunk(w, PosesChunk, () =>
    {
        foreach (var pose in poses) WritePose(w, pose);
    });

    /// <summary>
    /// One pose: name, target, no normals, then its vertices' offsets.
    /// </summary>
    static void WritePose(BinaryWriter w, MeshPose pose) => Chunk(w, PoseChunk, () =>
    {
        WriteString(w, pose.Name);
        w.Write((ushort)1);
        w.Write(false);
        foreach (var (vertex, offset) in pose.Offsets)
        {
            Chunk(w, PoseVertex, () =>
            {
                w.Write((uint)vertex);
                WriteVector(w, offset);
            });
        }
    });

    /// <summary>
    /// The submesh: material, indices, geometry, operation and bone assignments.
    /// </summary>
    static void WriteSubMesh(BinaryWriter w, SkinnedMesh mesh, string material) => Chunk(w, SubMesh, () =>
    {
        WriteString(w, material);
        w.Write((byte)0); // own vertices, not shared
        w.Write(mesh.Indices.Length);
        bool wide = mesh.Positions.Length > ushort.MaxValue;
        w.Write(wide);
        foreach (int index in mesh.Indices)
        {
            if (wide) w.Write(index);
            else w.Write((ushort)index);
        }

        WriteGeometry(w, mesh);
        Chunk(w, SubMeshOperation, () => w.Write((ushort)4)); // triangle list
        WriteBoneAssignments(w, mesh.Weights);
    });

    /// <summary>
    /// The vertex count, the declaration and one interleaved buffer.
    /// </summary>
    static void WriteGeometry(BinaryWriter w, SkinnedMesh mesh) => Chunk(w, Geometry, () =>
    {
        w.Write(mesh.Positions.Length);
        Chunk(w, Declaration, () =>
        {
            WriteElement(w, Float3, PositionSemantic, 0);
            WriteElement(w, Float3, NormalSemantic, 12);
            WriteElement(w, Float2, UvSemantic, 24);
            WriteElement(w, Float4, TangentSemantic, 32);
        });
        Chunk(w, VertexBuffer, () =>
        {
            w.Write((ushort)0); // bind index
            w.Write(Stride);
            Chunk(w, BufferData, () => WriteVertices(w, mesh));
        });
    });

    /// <summary>
    /// The interleaved vertex data.
    /// </summary>
    static void WriteVertices(BinaryWriter w, SkinnedMesh mesh)
    {
        for (int i = 0; i < mesh.Positions.Length; i++)
        {
            WriteVector(w, mesh.Positions[i]);
            WriteVector(w, mesh.Normals[i]);
            w.Write(mesh.Uvs[i].X);
            w.Write(mesh.Uvs[i].Y);
            WriteVector(w, new Vector3(mesh.Tangents[i].X, mesh.Tangents[i].Y, mesh.Tangents[i].Z));
            w.Write(mesh.Tangents[i].W);
        }
    }

    /// <summary>
    /// One declaration element in source 0.
    /// </summary>
    static void WriteElement(BinaryWriter w, ushort type, ushort semantic, ushort offset) => Chunk(w, Element, () =>
    {
        w.Write((ushort)0); // source
        w.Write(type);
        w.Write(semantic);
        w.Write(offset);
        w.Write((ushort)0); // index
    });

    /// <summary>
    /// Each vertex's heaviest <see cref="MaxWeights"/> bones, their weights rescaled to sum to 1.
    /// </summary>
    static void WriteBoneAssignments(BinaryWriter w, Dictionary<ushort, float>[] weights)
    {
        for (int v = 0; v < weights.Length; v++)
        {
            var kept = weights[v].OrderByDescending(p => p.Value).Take(MaxWeights).ToArray();
            float total = kept.Sum(p => p.Value);
            if (total <= 0) total = 1;

            foreach (var (bone, weight) in kept)
            {
                Chunk(w, BoneAssignment, () =>
                {
                    w.Write(v);
                    w.Write(bone);
                    w.Write(weight / total);
                });
            }
        }
    }

    /// <summary>
    /// The box and the radius from the origin.
    /// </summary>
    static void WriteBounds(BinaryWriter w, Vector3[] positions) => Chunk(w, Bounds, () =>
    {
        var min = positions.Aggregate(Vector3.Min);
        var max = positions.Aggregate(Vector3.Max);
        WriteVector(w, min);
        WriteVector(w, max);
        w.Write(positions.Max(p => p.Length()));
    });

    /// <summary>
    /// A chunk: its id, its length including the six-byte header, then the body.
    /// </summary>
    static void Chunk(BinaryWriter w, ushort id, Action body)
    {
        long start = w.BaseStream.Position;
        w.Write(id);
        w.Write(0u);
        body();
        long end = w.BaseStream.Position;
        w.BaseStream.Position = start + 2;
        w.Write((uint)(end - start));
        w.BaseStream.Position = end;
    }

    /// <summary>
    /// A string ended by a newline.
    /// </summary>
    static void WriteString(BinaryWriter w, string text) => w.Write(Encoding.UTF8.GetBytes(text + "\n"));

    /// <summary>
    /// Three floats.
    /// </summary>
    static void WriteVector(BinaryWriter w, Vector3 v)
    {
        w.Write(v.X);
        w.Write(v.Y);
        w.Write(v.Z);
    }
}

/// <summary>
/// A fitted body ready to write: per-vertex position, normal, uv, tangent (w the bitangent's sign) and bone weights by handle; triangle indices.
/// </summary>
public sealed record SkinnedMesh(Vector3[] Positions, Vector3[] Normals, Vector2[] Uvs, Vector4[] Tangents, int[] Indices,
    Dictionary<ushort, float>[] Weights)
{
    /// <summary>
    /// Morph targets of its vertices (the human head's face poses, <see cref="HeadGraft"/>).
    /// </summary>
    public MeshPose[] Poses { get; init; } = [];
}
