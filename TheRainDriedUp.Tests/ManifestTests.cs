using System.Text.Json;
using Meitou.Modding;

namespace TheRainDriedUp.Tests;

/// <summary>
/// The manifest and the code agree: the loader refuses a mod whose entry is missing or whose id differs from what its code registers under.
/// </summary>
public sealed class ManifestTests
{
    [Fact]
    public void The_manifest_names_the_entry_and_its_id()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "meitou.json")));
        var root = manifest.RootElement;

        var entry = typeof(TheRainDriedUpMod).Assembly.GetType(root.GetProperty("entry").GetString()!);

        Assert.NotNull(entry);
        Assert.True(typeof(IMod).IsAssignableFrom(entry));
        Assert.True(typeof(IModDataPhase).IsAssignableFrom(entry));
        Assert.Equal(TheRainDriedUpMod.Id, root.GetProperty("id").GetString());
    }
}
