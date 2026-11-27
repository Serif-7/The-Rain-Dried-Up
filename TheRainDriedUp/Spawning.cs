using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Population;

namespace TheRainDriedUp;

/// <summary>
/// The recruit window's spawn: the character record, where (the camera target when F10 opened the window), and the characters selected then
/// (the first player character's squad takes the recruit).
/// </summary>
sealed record RecruitCommand(string CharacterId, Vector3 At, IReadOnlyList<CharacterId> Selected) : SimCommand;

/// <summary>
/// Spawns the recruit window's characters into a player squad (the selected character's, else the first). Serial steps only.
/// </summary>
sealed class RecruitSystem(PopulationSystem? population) : ITickSystem
{
    /// <summary>
    /// Runs the tick's recruits.
    /// </summary>
    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        foreach (var command in commands.OfType<RecruitCommand>()) Recruit(world, command);
    }

    /// <summary>
    /// One recruit, or a notice that says why it failed.
    /// </summary>
    void Recruit(World world, RecruitCommand command)
    {
        string name = Characters.All.FirstOrDefault(c => c.CharacterId == command.CharacterId)?.Name ?? command.CharacterId;
        if (population is null)
        {
            world.Post($"The Rain Dried Up: no population in this world, so {name} can't be spawned.");
            return;
        }

        int squad = PlayerSquad(world, command.Selected);
        var at = new Vector2(command.At.X, command.At.Z);
        if (population.SpawnIntoPlayerSquad(world, squad, command.CharacterId, at).IsNone)
            world.Post($"The Rain Dried Up: could not spawn {name} there (point the camera at a walkable spot near your squad).");
    }

    /// <summary>
    /// The squad of the first selected player character, else the first player squad; -1 when there is none.
    /// </summary>
    static int PlayerSquad(World world, IReadOnlyList<CharacterId> selected)
    {
        var table = world.Characters;
        return selected
            .Select(id => table.TryResolveNext(id, out int slot) ? table.Cold(slot) : null)
            .FirstOrDefault(c => c is { IsPlayer: true })?.SquadId
            ?? SquadManagement.PlayerSquads(world).FirstOrDefault()?.Id
            ?? -1;
    }
}
