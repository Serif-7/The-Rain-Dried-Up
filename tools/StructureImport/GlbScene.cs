using System.Numerics;
using SharpGLTF.Schema2;

namespace StructureImport;

/// <summary>
/// One vertex of an imported mesh: position, normal and first texture coordinate (glTF: origin at the image's top left, as in Ogre).
/// </summary>
public record struct MeshVertex(Vector3 Position, Vector3 Normal, Vector2 Uv);

/// <summary>
/// A glTF material's look, as far as the game's building shader can show it (docs/structures.md "Textures").
/// </summary>
public sealed class SurfaceLook
{
    /// <summary>
    /// The material's name in the file ("" when it has none).
    /// </summary>
    public string Name { get; init; } = "";

    /// <summary>
    /// The base colour factor, multiplied into the base colour texture (RGBA, linear as glTF stores it).
    /// </summary>
    public Vector4 BaseColour { get; init; } = Vector4.One;

    /// <summary>
    /// The encoded base colour image (PNG or JPEG), null when there is none.
    /// </summary>
    public byte[]? BaseColourImage { get; init; }

    /// <summary>
    /// The metallic factor.
    /// </summary>
    public float Metallic { get; init; }

    /// <summary>
    /// The roughness factor.
    /// </summary>
    public float Roughness { get; init; } = 1;

    /// <summary>
    /// The encoded metallic-roughness image (G roughness, B metalness), null when there is none.
    /// </summary>
    public byte[]? MetallicRoughnessImage { get; init; }

    /// <summary>
    /// The encoded tangent-space normal image (OpenGL convention: green points up the image), null when there is none.
    /// </summary>
    public byte[]? NormalImage { get; init; }

    /// <summary>
    /// The alpha cut-off (0..1) when the material is cut out (glTF MASK or BLEND, which the game can only cut), else null.
    /// </summary>
    public float? AlphaCutoff { get; init; }
}

/// <summary>
/// The triangles of one material, in the model's space after its node transforms (metres, Y up).
/// </summary>
public sealed class SurfaceMesh
{
    /// <summary>
    /// The material.
    /// </summary>
    public required SurfaceLook Look { get; init; }

    /// <summary>
    /// The vertices.
    /// </summary>
    public List<MeshVertex> Vertices { get; } = [];

    /// <summary>
    /// Three vertex indices per triangle, counter-clockwise from the front.
    /// </summary>
    public List<int> Indices { get; } = [];
}

/// <summary>
/// Reads a <c>.glb</c> (or <c>.gltf</c>) model's default scene into one <see cref="SurfaceMesh"/> per material.
/// </summary>
public static class GlbScene
{
    /// <summary>
    /// The model's meshes, one per material, in world space. Throws when the file has no triangles.
    /// </summary>
    public static List<SurfaceMesh> Load(string path)
    {
        var model = ModelRoot.Load(path);
        var scene = model.DefaultScene ?? model.LogicalScenes.FirstOrDefault()
            ?? throw new InvalidDataException($"{path}: no scene");

        var byMaterial = new Dictionary<int, SurfaceMesh>();
        foreach (var node in scene.VisualChildren) AddNode(node, byMaterial);

        var meshes = byMaterial.OrderBy(p => p.Key).Select(p => p.Value).Where(m => m.Indices.Count > 0).ToList();
        if (meshes.Count == 0) throw new InvalidDataException($"{path}: no triangles");

        return meshes;
    }

    /// <summary>
    /// Adds the triangles of <paramref name="node"/> and its children.
    /// </summary>
    static void AddNode(Node node, Dictionary<int, SurfaceMesh> byMaterial)
    {
        if (node.Mesh is { } mesh)
        {
            var world = node.WorldMatrix;
            foreach (var primitive in mesh.Primitives) AddPrimitive(primitive, world, byMaterial);
        }

        foreach (var child in node.VisualChildren) AddNode(child, byMaterial);
    }

    /// <summary>
    /// Adds one primitive's triangles to its material's mesh, transformed by <paramref name="world"/>.
    /// </summary>
    static void AddPrimitive(MeshPrimitive primitive, Matrix4x4 world, Dictionary<int, SurfaceMesh> byMaterial)
    {
        if (primitive.DrawPrimitiveType is not (PrimitiveType.TRIANGLES or PrimitiveType.TRIANGLE_STRIP or PrimitiveType.TRIANGLE_FAN)) return;
        var positionAccessor = primitive.GetVertexAccessor("POSITION");
        if (positionAccessor is null) return;

        int key = primitive.Material?.LogicalIndex ?? -1;
        if (!byMaterial.TryGetValue(key, out var target))
            byMaterial[key] = target = new SurfaceMesh { Look = Look(primitive.Material) };

        var positions = positionAccessor.AsVector3Array();
        var normals = primitive.GetVertexAccessor("NORMAL")?.AsVector3Array();
        var uvs = primitive.GetVertexAccessor("TEXCOORD_0")?.AsVector2Array();
        Matrix4x4.Invert(world, out var inverse);
        var normalMatrix = Matrix4x4.Transpose(inverse);
        bool mirrored = world.GetDeterminant() < 0;

        int first = target.Vertices.Count;
        for (int i = 0; i < positions.Count; i++)
        {
            var normal = normals is null ? Vector3.Zero : Vector3.Normalize(Vector3.TransformNormal(normals[i], normalMatrix));
            target.Vertices.Add(new MeshVertex(Vector3.Transform(positions[i], world), normal, uvs?[i] ?? Vector2.Zero));
        }

        foreach (var (a, b, c) in primitive.GetTriangleIndices())
        {
            // A mirroring transform turns the winding round; keep the fronts facing out.
            target.Indices.Add(first + a);
            target.Indices.Add(first + (mirrored ? c : b));
            target.Indices.Add(first + (mirrored ? b : c));
        }

        if (normals is null) MeshMath.FlatNormals(target, first);
    }

    /// <summary>
    /// The look of a glTF material; a primitive without one is plain white.
    /// </summary>
    static SurfaceLook Look(Material? material)
    {
        if (material is null) return new SurfaceLook();

        var baseColour = material.FindChannel("BaseColor");
        var metalRough = material.FindChannel("MetallicRoughness");
        var normal = material.FindChannel("Normal");
        return new SurfaceLook
        {
            Name = material.Name ?? "",
            BaseColour = baseColour?.Color ?? Vector4.One,
            BaseColourImage = Image(baseColour),
            Metallic = metalRough?.GetFactor("MetallicFactor") ?? 0,
            Roughness = metalRough?.GetFactor("RoughnessFactor") ?? 1,
            MetallicRoughnessImage = Image(metalRough),
            NormalImage = Image(normal),
            AlphaCutoff = material.Alpha switch
            {
                AlphaMode.MASK => material.AlphaCutoff,
                AlphaMode.BLEND => 0.5f,
                _ => null,
            },
        };
    }

    /// <summary>
    /// The encoded image of a channel's texture, null when it has none.
    /// </summary>
    static byte[]? Image(MaterialChannel? channel) =>
        channel?.Texture?.PrimaryImage?.Content is { IsEmpty: false } image ? image.Content.ToArray() : null;
}
