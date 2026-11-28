using System.Numerics;
using Meitou.Data.Ogre;

namespace RaceImport;

/// <summary>
/// The head of a rig's human body mesh (docs/race-import.md "Headless"): the vertices whose uv lies below 0, which the body shader colours from
/// the HEAD record's texture (engine docs/characters.md "Shading, poses and LOD"), with their triangles, weights and the mesh's face poses (the
/// face sliders' morph targets). A headless import grafts it onto the fitted body in place of the model's own head.
/// </summary>
public sealed class HumanHead
{
    /// <summary>
    /// Bind positions.
    /// </summary>
    public required Vector3[] Positions { get; init; }

    /// <summary>
    /// Normals.
    /// </summary>
    public required Vector3[] Normals { get; init; }

    /// <summary>
    /// Texture coordinates (below 0: the head texture).
    /// </summary>
    public required Vector2[] Uvs { get; init; }

    /// <summary>
    /// Each vertex's bone weights by handle.
    /// </summary>
    public required Dictionary<ushort, float>[] Weights { get; init; }

    /// <summary>
    /// The head's triangles, three indices each into its own vertices.
    /// </summary>
    public required int[] Indices { get; init; }

    /// <summary>
    /// The face poses, their offsets indexed by the head's own vertices.
    /// </summary>
    public required MeshPose[] Poses { get; init; }

    /// <summary>
    /// The head of <paramref name="rig"/>'s body in the game in <paramref name="kenshiFolder"/>. Throws when the body has no head.
    /// </summary>
    public static HumanHead Load(string kenshiFolder, HumanRig rig)
    {
        var mesh = OgreMeshReader.Read(File.ReadAllBytes(KenshiSkeleton.GameFile(kenshiFolder, rig.Body)));
        var sub = mesh.SubMeshes.Single();
        var data = sub.VertexData ?? throw new InvalidDataException($"{rig.Body}: no own vertices");
        var uvs = Pairs(data, OgreVertexSemantic.TextureCoordinates);
        var head = Enumerable.Range(0, uvs.Length).Where(v => uvs[v].Y < 0).ToArray();
        if (head.Length == 0) throw new InvalidDataException($"{rig.Body}: no head vertices");

        var local = head.Select((v, i) => (v, i)).ToDictionary(p => (uint)p.v, p => p.i);
        var positions = data.ReadPositions() ?? [];
        var normals = Triples(data, OgreVertexSemantic.Normal);
        return new HumanHead
        {
            Positions = [.. head.Select(v => positions[v])],
            Normals = [.. head.Select(v => normals[v])],
            Uvs = [.. head.Select(v => uvs[v])],
            Weights = WeightsOf(sub.BoneAssignments, head),
            Indices = Triangles(sub.Indices.Indices, local),
            Poses = [.. mesh.Poses.Where(p => p.Target == 1).Select(p => Pose(p, local))],
        };
    }

    /// <summary>
    /// An element's two-component values per vertex.
    /// </summary>
    static Vector2[] Pairs(OgreVertexData data, OgreVertexSemantic semantic)
    {
        var element = data.Find(semantic) ?? throw new InvalidDataException($"the human body has no {semantic}");
        var floats = data.ReadFloats(element);
        int stride = element.Type.ComponentCount();
        return [.. Enumerable.Range(0, floats.Length / stride).Select(v => new Vector2(floats[v * stride], floats[v * stride + 1]))];
    }

    /// <summary>
    /// An element's three-component values per vertex.
    /// </summary>
    static Vector3[] Triples(OgreVertexData data, OgreVertexSemantic semantic)
    {
        var element = data.Find(semantic) ?? throw new InvalidDataException($"the human body has no {semantic}");
        var floats = data.ReadFloats(element);
        int stride = element.Type.ComponentCount();
        return [.. Enumerable.Range(0, floats.Length / stride).Select(v => new Vector3(floats[v * stride], floats[v * stride + 1], floats[v * stride + 2]))];
    }

    /// <summary>
    /// The head vertices' weights by bone handle.
    /// </summary>
    static Dictionary<ushort, float>[] WeightsOf(List<OgreBoneAssignment> assignments, int[] head)
    {
        var byVertex = assignments.GroupBy(a => a.VertexIndex).ToDictionary(g => g.Key, g => g.ToList());
        return [.. head.Select(v => Gathered(byVertex.GetValueOrDefault((uint)v) ?? []))];
    }

    /// <summary>
    /// One vertex's assignments summed by bone.
    /// </summary>
    static Dictionary<ushort, float> Gathered(List<OgreBoneAssignment> assignments) =>
        assignments.GroupBy(a => a.BoneIndex).ToDictionary(g => g.Key, g => g.Sum(a => a.Weight));

    /// <summary>
    /// The triangles whose three vertices are the head's, in its own indices.
    /// </summary>
    static int[] Triangles(uint[] source, Dictionary<uint, int> local)
    {
        var indices = new List<int>();
        for (int i = 0; i + 2 < source.Length; i += 3)
        {
            if (!local.TryGetValue(source[i], out int a) || !local.TryGetValue(source[i + 1], out int b) || !local.TryGetValue(source[i + 2], out int c))
                continue;

            indices.AddRange([a, b, c]);
        }

        return [.. indices];
    }

    /// <summary>
    /// A pose's offsets on head vertices, in the head's own indices.
    /// </summary>
    static MeshPose Pose(OgrePose pose, Dictionary<uint, int> local) =>
        new(pose.Name, [.. pose.Vertices.Where(p => local.ContainsKey(p.Index)).Select(p => (local[p.Index], p.Offset))]);
}

/// <summary>
/// A named morph target of a written mesh: position offsets by vertex index.
/// </summary>
public sealed record MeshPose(string Name, (int Vertex, Vector3 Offset)[] Offsets);
