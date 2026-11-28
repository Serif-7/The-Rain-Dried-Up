using System.Numerics;
using System.Text;

namespace StructureImport;

/// <summary>
/// Writes an Ogre binary mesh, <c>[MeshSerializer_v1.8]</c>, the layout the game's own building meshes use (engine
/// docs/formats/ogre-mesh.md): one submesh with its own vertices (position, normal, UV, tangent with handedness in w) and a triangle list; a
/// worn item's mesh also links a skeleton and gives each vertex its bones (<see cref="MeshSkin"/>).
/// </summary>
public static class OgreMeshWriter
{
    /// <summary>
    /// The header's version string.
    /// </summary>
    public const string Version = "[MeshSerializer_v1.8]";

    /// <summary>
    /// The submesh material the game's building meshes name; a building part's MATERIAL_SPEC replaces it at run time.
    /// </summary>
    public const string MaterialName = "StaticObject";

    /// <summary>
    /// Chunk ids (Ogre <c>MeshChunkID</c>).
    /// </summary>
    const ushort Header = 0x1000, Mesh = 0x3000, SubMesh = 0x4000, SubMeshOperation = 0x4010, Geometry = 0x5000, VertexDeclaration = 0x5100,
        VertexElement = 0x5110, VertexBuffer = 0x5200, VertexBufferData = 0x5210, BoneAssignment = 0x4100, SkeletonLink = 0x6000,
        MeshBounds = 0x9000;

    /// <summary>
    /// Vertex element types and semantics (Ogre <c>VertexElementType</c>, <c>VertexElementSemantic</c>).
    /// </summary>
    const ushort Float2 = 1, Float3 = 2, Float4 = 3, Position = 1, Normal = 4, TexCoord = 7, Tangent = 9;

    /// <summary>
    /// Bytes per vertex: position 12, normal 12, UV 8, tangent 16.
    /// </summary>
    const ushort VertexSize = 48;

    /// <summary>
    /// Writes <paramref name="mesh"/> with its <paramref name="tangents"/> to <paramref name="path"/>, skinned by <paramref name="skin"/> when given.
    /// </summary>
    public static void Write(SurfaceMesh mesh, Vector4[] tangents, string path, MeshSkin? skin = null)
    {
        using var stream = File.Create(path);
        using var w = new BinaryWriter(stream, Encoding.UTF8);
        w.Write(Header);
        WriteString(w, Version);
        Chunk(w, Mesh, () =>
        {
            w.Write(skin is not null); // skeletally animated
            Chunk(w, SubMesh, () => WriteSubMesh(w, mesh, tangents, skin));
            if (skin is not null) Chunk(w, SkeletonLink, () => WriteString(w, skin.Skeleton));
            Chunk(w, MeshBounds, () => WriteBounds(w, mesh));
        });
    }

    /// <summary>
    /// The submesh: material, own vertices, indices (16-bit while the vertices fit), geometry, the triangle-list operation, then the bone
    /// assignments of a skinned mesh.
    /// </summary>
    static void WriteSubMesh(BinaryWriter w, SurfaceMesh mesh, Vector4[] tangents, MeshSkin? skin)
    {
        WriteString(w, MaterialName);
        w.Write(false); // own vertices, not shared
        w.Write((uint)mesh.Indices.Count);
        bool wide = mesh.Vertices.Count > ushort.MaxValue;
        w.Write(wide);
        foreach (int index in mesh.Indices)
        {
            if (wide) w.Write((uint)index);
            else w.Write((ushort)index);
        }

        Chunk(w, Geometry, () => WriteGeometry(w, mesh, tangents));
        Chunk(w, SubMeshOperation, () => w.Write((ushort)4)); // triangle list
        if (skin is not null) WriteBoneAssignments(w, skin);
    }

    /// <summary>
    /// One assignment per vertex and bone of non-zero weight.
    /// </summary>
    static void WriteBoneAssignments(BinaryWriter w, MeshSkin skin)
    {
        for (int v = 0; v < skin.Weights.Length; v++)
        {
            foreach (var (bone, weight) in skin.Weights[v])
            {
                if (weight <= 0) continue;

                Chunk(w, BoneAssignment, () =>
                {
                    w.Write((uint)v);
                    w.Write(bone);
                    w.Write(weight);
                });
            }
        }
    }

    /// <summary>
    /// The vertex count, the declaration and one interleaved buffer.
    /// </summary>
    static void WriteGeometry(BinaryWriter w, SurfaceMesh mesh, Vector4[] tangents)
    {
        w.Write((uint)mesh.Vertices.Count);
        Chunk(w, VertexDeclaration, () =>
        {
            Element(w, Float3, Position, 0);
            Element(w, Float3, Normal, 12);
            Element(w, Float2, TexCoord, 24);
            Element(w, Float4, Tangent, 32);
        });
        Chunk(w, VertexBuffer, () =>
        {
            w.Write((ushort)0); // bind index
            w.Write(VertexSize);
            Chunk(w, VertexBufferData, () => WriteVertices(w, mesh, tangents));
        });
    }

    /// <summary>
    /// The interleaved vertex data, in the declaration's order.
    /// </summary>
    static void WriteVertices(BinaryWriter w, SurfaceMesh mesh, Vector4[] tangents)
    {
        for (int i = 0; i < mesh.Vertices.Count; i++)
        {
            var v = mesh.Vertices[i];
            WriteVector(w, v.Position);
            WriteVector(w, v.Normal);
            w.Write(v.Uv.X);
            w.Write(v.Uv.Y);
            WriteVector(w, new Vector3(tangents[i].X, tangents[i].Y, tangents[i].Z));
            w.Write(tangents[i].W);
        }
    }

    /// <summary>
    /// One declaration element, all in source 0.
    /// </summary>
    static void Element(BinaryWriter w, ushort type, ushort semantic, ushort offset) => Chunk(w, VertexElement, () =>
    {
        w.Write((ushort)0);
        w.Write(type);
        w.Write(semantic);
        w.Write(offset);
        w.Write((ushort)0);
    });

    /// <summary>
    /// The bounding box and the radius of the sphere round the origin that holds every vertex.
    /// </summary>
    static void WriteBounds(BinaryWriter w, SurfaceMesh mesh)
    {
        var (min, max) = MeshMath.Bounds([mesh]);
        float radius = mesh.Vertices.Max(v => v.Position.Length());
        WriteVector(w, min);
        WriteVector(w, max);
        w.Write(radius);
    }

    /// <summary>
    /// A chunk: id, then its length including the 6-byte header (patched once <paramref name="body"/> has written).
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
    /// A newline-terminated string, as Ogre's serializer writes them.
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
/// A skinned mesh's skeleton (its file name, which the mesh links) and each vertex's bone handles and weights (summing to 1).
/// </summary>
public sealed record MeshSkin(string Skeleton, (ushort Bone, float Weight)[][] Weights);
