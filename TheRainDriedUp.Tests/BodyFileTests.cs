using Meitou.Data.Ogre;

namespace TheRainDriedUp.Tests;

/// <summary>
/// Every character's body files are committed where its race points (items/races, which Play copies), and each mesh is skinned to the game's
/// male skeleton with at most the four bones a vertex blends.
/// </summary>
public sealed class BodyFileTests
{
    public static TheoryData<string> Stems => [.. Characters.All.Select(c => c.Stem)];

    [Theory]
    [MemberData(nameof(Stems))]
    public void The_body_files_are_committed(string stem)
    {
        foreach (string suffix in new[] { ".mesh", "_d.dds", "_n.dds", "_parts.png" })
            Assert.True(File.Exists(Path.Combine(Folder(), stem + suffix)), stem + suffix);
    }

    [Theory]
    [MemberData(nameof(Stems))]
    public void The_mesh_is_skinned_to_the_male_skeleton(string stem)
    {
        var mesh = OgreMeshReader.Read(File.ReadAllBytes(Path.Combine(Folder(), stem + ".mesh")));
        var sub = Assert.Single(mesh.SubMeshes);

        Assert.Equal("male_skeleton.skeleton", mesh.SkeletonName);
        Assert.Equal(sub.VertexData!.VertexCount, (uint)sub.BoneAssignments.Select(a => a.VertexIndex).Distinct().Count());
        Assert.True(sub.BoneAssignments.GroupBy(a => a.VertexIndex).All(g => g.Count() <= 4));
    }

    [Fact]
    public void The_retopologized_body_is_within_the_character_budget_and_the_full_one_is_not()
    {
        Assert.InRange(Triangles(Characters.Raiden.Stem), 5_000, 8_000);
        Assert.True(Triangles(Characters.RaidenFull.Stem) > 40_000);
    }

    /// <summary>
    /// A committed body's triangles.
    /// </summary>
    static int Triangles(string stem) =>
        OgreMeshReader.Read(File.ReadAllBytes(Path.Combine(Folder(), stem + ".mesh"))).SubMeshes.Sum(s => s.Indices.Indices.Length / 3);

    /// <summary>
    /// The repo's TheRainDriedUp/items/races, found above the test's output folder.
    /// </summary>
    static string Folder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string path = Path.Combine(dir.FullName, "TheRainDriedUp", "items", "races");
            if (Directory.Exists(path)) return path;
        }

        throw new DirectoryNotFoundException("TheRainDriedUp/items/races");
    }
}
