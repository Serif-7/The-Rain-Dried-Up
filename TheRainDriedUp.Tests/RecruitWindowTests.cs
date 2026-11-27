using System.Numerics;
using Meitou.Data.Ui;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace TheRainDriedUp.Tests;

/// <summary>
/// The F10 row opens the recruit window with one Recruit button per character; a press queues that character's spawn at the camera target
/// and selection the row was pressed with; Close and the row again close it.
/// </summary>
public sealed class RecruitWindowTests
{
    /// <summary>
    /// A small screen.
    /// </summary>
    const int ScreenWidth = 1280, ScreenHeight = 720;

    [Fact]
    public void The_window_shows_only_after_the_row_and_closes_again()
    {
        var (host, window, _) = Host();
        Assert.Empty(Arrange(host));

        window.Toggle(Row(new Vector3(1, 2, 3)));
        Assert.Equal(Characters.All.Count, Arrange(host).Count(c => c.Caption == "Recruit"));

        host.Dispatch(Arrange(host).Single(c => c.Caption == "Close"));
        Assert.Empty(Arrange(host));
    }

    [Fact]
    public void A_recruit_press_queues_that_character_at_the_rows_camera_target()
    {
        var (host, window, sent) = Host();
        var selected = new CharacterId(5, 1);
        window.Toggle(Row(new Vector3(10, 0, 20), selected));

        host.Dispatch(Arrange(host).Single(c => c.Caption == "Recruit" && c.Argument == Characters.RaidenFull.CharacterId));

        var command = Assert.IsType<RecruitCommand>(Assert.Single(sent));
        Assert.Equal(Characters.RaidenFull.CharacterId, command.CharacterId);
        Assert.Equal(new Vector3(10, 0, 20), command.At);
        Assert.Equal(selected, Assert.Single(command.Selected));
        Assert.Equal(7, command.Tick);
    }

    [Fact]
    public void The_open_window_with_every_character_fits_the_small_screen()
    {
        var (_, window, _) = Host();
        window.Toggle(Row(Vector3.Zero));

        Assert.True(90 + window.Window().Placement.Height.Pixels <= ScreenHeight);
    }

    /// <summary>
    /// A press of the F10 row with the camera on <paramref name="at"/>.
    /// </summary>
    static DebugCommandContext Row(Vector3 at, params CharacterId[] selected) => new("", selected, at, 7, _ => { });

    /// <summary>
    /// A host with the recruit window, and the commands it queues.
    /// </summary>
    static (UiHost Host, RecruitWindow Window, List<SimCommand> Sent) Host()
    {
        var sent = new List<SimCommand>();
        var window = new RecruitWindow(sent.Add, () => 7);
        var host = new UiHost();
        host.Register(window);
        return (host, window, sent);
    }

    /// <summary>
    /// The host's controls on the small screen.
    /// </summary>
    static IReadOnlyList<UiControl> Arrange(UiHost host) =>
        host.Arrange(UiMode.Meitou, ScreenWidth, ScreenHeight, new UiBindings(),
            new Dictionary<string, UiRect> { [UiSlots.Overlay] = new(0, 0, ScreenWidth, ScreenHeight) });
}
