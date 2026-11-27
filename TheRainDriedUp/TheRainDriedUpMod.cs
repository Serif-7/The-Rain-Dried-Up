using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Population;

namespace TheRainDriedUp;

/// <summary>
/// The mod's entry: adds the characters' races and characters in the data phase (<see cref="CharacterRecords"/>), so the shipped
/// TheRainDriedUp.mod stays empty; then the F10 row that opens the recruit window (<see cref="RecruitWindow"/>) and the system that spawns its
/// recruits (<see cref="RecruitSystem"/>).
/// </summary>
public sealed class TheRainDriedUpMod : IMod, IModDataPhase
{
    /// <summary>
    /// The mod's manifest id.
    /// </summary>
    public const string Id = "the.rain.dried.up";

    public void EditData(ModDataContext c) => CharacterRecords.Add(c);

    public void Initialize(ModContext c)
    {
        var window = new RecruitWindow(c.Enqueue, () => c.Tick);
        c.AddUi(window);
        c.AddDebugCommand(RecruitWindow.Label, window.Toggle);
        c.AddSystem("recruits", b => new RecruitSystem(b.Find<PopulationSystem>()), SystemAnchor.End);
        if (c.Data.Find(Characters.Raiden.CharacterId) is null) c.LogError("The Rain Dried Up's records are missing: the data phase did not add them.");
    }
}
