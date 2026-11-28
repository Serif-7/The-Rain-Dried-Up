using System.Numerics;
using System.Text.Json;
using SharpGLTF.Schema2;

namespace RaceImport;

/// <summary>
/// Where and how a static part sits on a body (docs/race-import.md "Attachments"), read from <c>parts/&lt;name&gt;.json</c>. Lengths are game
/// units; each piece is placed as the right-hand one (+X) and mirrored for the left, so <see cref="At"/>'s X, <see cref="Roll"/> and
/// <see cref="Yaw"/> point outward on both sides.
/// </summary>
public sealed record AttachmentSpec
{
    /// <summary>
    /// The bones the part rides, top to bottom: a vertex between two of them by height is weighted between the two, above the first or below
    /// the last wholly on that one.
    /// </summary>
    public required string[] Bones { get; init; }

    /// <summary>
    /// The height of the whole model once placed.
    /// </summary>
    public required float Height { get; init; }

    /// <summary>
    /// Whether the model is two mirrored pieces split at X = 0 (a pair of ears or wings) rather than one.
    /// </summary>
    public bool Split { get; init; } = true;

    /// <summary>
    /// The point of each piece put at <see cref="At"/>, as shares of its bounds: X from its inner edge (0) to its outer one (1), Y from its
    /// bottom to its top, Z from its back to its front.
    /// </summary>
    public float[] Anchor { get; init; } = [0.5f, 0, 0.5f];

    /// <summary>
    /// What <see cref="At"/> is measured from: <c>HeadTop</c> (the top of the head's vertices, centred) or a bone's joint.
    /// </summary>
    public string RelativeTo { get; init; } = "HeadTop";

    /// <summary>
    /// The anchor's place from <see cref="RelativeTo"/>: outward, up, forward.
    /// </summary>
    public float[] At { get; init; } = [0, 0, 0];

    /// <summary>
    /// Degrees each piece's top tilts outward, about the forward axis through the anchor.
    /// </summary>
    public float Roll { get; init; }

    /// <summary>
    /// Degrees each piece's top tips back, about the sideways axis through the anchor (after <see cref="Roll"/>).
    /// </summary>
    public float Pitch { get; init; }

    /// <summary>
    /// Degrees each piece's outer edge swings back, about the vertical axis through the anchor (last).
    /// </summary>
    public float Yaw { get; init; }

    /// <summary>
    /// When set, each piece is moved front to back (ignoring <see cref="At"/>'s Z) until its nearest point lies this far behind the body's
    /// back (the vertices <see cref="Bones"/> carry most): for parts worn on the back, such as wings.
    /// </summary>
    public float? Behind { get; init; }

    /// <summary>
    /// The spec of a part's model: <c>parts/&lt;its file's stem&gt;.json</c> beside the tool. Throws when there is none.
    /// </summary>
    public static AttachmentSpec For(string model)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "parts", Path.GetFileNameWithoutExtension(model) + ".json");
        if (!File.Exists(path)) throw new FileNotFoundException($"no part spec {path} for {model}", path);

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };
        return JsonSerializer.Deserialize<AttachmentSpec>(File.ReadAllText(path), options)
            ?? throw new InvalidDataException($"{path}: empty part spec");
    }
}

/// <summary>
/// A static (unskinned) part model from a .glb, in its own units, with its spec and material images: ears, wings, horns
/// (docs/race-import.md "Attachments").
/// </summary>
public sealed class Attachment
{
    /// <summary>
    /// Where the part goes.
    /// </summary>
    public required AttachmentSpec Spec { get; init; }

    /// <summary>
    /// Vertex positions, model units, every mesh node's transform applied.
    /// </summary>
    public required Vector3[] Positions { get; init; }

    /// <summary>
    /// Unit vertex normals.
    /// </summary>
    public required Vector3[] Normals { get; init; }

    /// <summary>
    /// Texture coordinates (v down from the top).
    /// </summary>
    public required Vector2[] Uvs { get; init; }

    /// <summary>
    /// Triangles, three vertex indices each.
    /// </summary>
    public required int[] Indices { get; init; }

    /// <summary>
    /// Whether the material is drawn from both sides (the game culls back faces, so the triangles are doubled).
    /// </summary>
    public bool DoubleSided { get; init; }

    /// <summary>
    /// The base colour image (PNG or JPEG bytes).
    /// </summary>
    public required byte[] BaseColourImage { get; init; }

    /// <summary>
    /// The metallic-roughness image, null without one.
    /// </summary>
    public byte[]? MetallicRoughnessImage { get; init; }

    /// <summary>
    /// The tangent-space normal map, null without one.
    /// </summary>
    public byte[]? NormalImage { get; init; }

    /// <summary>
    /// Every triangle primitive of <paramref name="path"/>'s mesh nodes, with the spec <see cref="AttachmentSpec.For"/> finds. Throws when the
    /// model has no triangles or no base colour texture.
    /// </summary>
    public static Attachment Load(string path) => Load(path, AttachmentSpec.For(path));

    /// <summary>
    /// Every triangle primitive of <paramref name="path"/>'s mesh nodes, placed by <paramref name="spec"/>.
    /// </summary>
    public static Attachment Load(string path, AttachmentSpec spec)
    {
        var model = ModelRoot.Load(path);
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var indices = new List<int>();
        foreach (var node in model.LogicalNodes.Where(n => n.Mesh is not null))
            foreach (var primitive in node.Mesh.Primitives.Where(p => p.DrawPrimitiveType == PrimitiveType.TRIANGLES))
                Add(primitive, node.WorldMatrix, positions, normals, uvs, indices);

        if (indices.Count == 0) throw new InvalidDataException($"{path}: no triangles");

        var material = model.LogicalMaterials.FirstOrDefault();
        return new Attachment
        {
            Spec = spec,
            Positions = [.. positions],
            Normals = [.. normals],
            Uvs = [.. uvs],
            Indices = [.. indices],
            DoubleSided = material?.DoubleSided ?? false,
            BaseColourImage = SourceBody.Image(material?.FindChannel("BaseColor")) ?? throw new InvalidDataException($"{path}: no base colour texture"),
            MetallicRoughnessImage = SourceBody.Image(material?.FindChannel("MetallicRoughness")),
            NormalImage = SourceBody.Image(material?.FindChannel("Normal")),
        };
    }

    /// <summary>
    /// Appends one primitive, brought out of its node by <paramref name="world"/>.
    /// </summary>
    static void Add(MeshPrimitive primitive, Matrix4x4 world, List<Vector3> positions, List<Vector3> normals, List<Vector2> uvs, List<int> indices)
    {
        var position = primitive.GetVertexAccessor("POSITION").AsVector3Array();
        var normal = primitive.GetVertexAccessor("NORMAL")?.AsVector3Array();
        var uv = primitive.GetVertexAccessor("TEXCOORD_0")?.AsVector2Array();

        int first = positions.Count;
        for (int i = 0; i < position.Count; i++)
        {
            positions.Add(Vector3.Transform(position[i], world));
            var n = Vector3.TransformNormal(normal?[i] ?? Vector3.UnitY, world);
            normals.Add(n.LengthSquared() > 0 ? Vector3.Normalize(n) : Vector3.UnitY);
            uvs.Add(uv?[i] ?? Vector2.Zero);
        }

        foreach (var (a, b, c) in primitive.GetTriangleIndices())
        {
            indices.Add(first + a);
            indices.Add(first + b);
            indices.Add(first + c);
        }
    }
}
