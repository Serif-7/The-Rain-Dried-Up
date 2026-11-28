using System.Numerics;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace TheRainDriedUp;

/// <summary>
/// The recruit window (README "Testing in game"): the F10 row opens it, it lists <see cref="Characters.All"/> with a Recruit button each, and a
/// press spawns that character into the player's squad at the camera target the row was pressed with. State lives on the host's thread.
/// </summary>
sealed class RecruitWindow(Action<SimCommand> enqueue, Func<long> tick) : IUiExtension
{
    #region Window

    /// <summary>
    /// The window's width, in UI units.
    /// </summary>
    const float Width = 560;

    /// <summary>
    /// A row's height.
    /// </summary>
    const float Row = 30;

    /// <summary>
    /// The window's inner margin.
    /// </summary>
    const float Margin = 14;

    /// <summary>
    /// A button's width.
    /// </summary>
    const float ButtonWidth = 100;

    /// <summary>
    /// The F10 row's label.
    /// </summary>
    public const string Label = "The Rain Dried Up: recruit window";

    /// <summary>
    /// Whether the window is open.
    /// </summary>
    bool _open;

    /// <summary>
    /// The camera target and selection when the window was opened: where recruits appear and which squad takes them.
    /// </summary>
    (Vector3 At, IReadOnlyList<CharacterId> Selected) _spot = (Vector3.Zero, []);

    /// <summary>
    /// The last recruit's line, shown under the list.
    /// </summary>
    string _status = "";

    /// <summary>
    /// The registered style and action keys.
    /// </summary>
    Keys _keys = null!;

    /// <summary>
    /// The mod's id, which namespaces the window's keys.
    /// </summary>
    public string Id => TheRainDriedUpMod.Id;

    /// <summary>
    /// The F10 row: opens the window at the camera target, or closes it.
    /// </summary>
    public string Toggle(DebugCommandContext d)
    {
        _open = !_open;
        _spot = (d.Target, d.Selected);
        _status = "";
        return _open ? "Recruit window open: recruits appear at the camera target." : "Recruit window closed.";
    }

    /// <summary>
    /// Registers the window's styles, actions and control, and puts it in the HUD's overlay.
    /// </summary>
    public void Register(UiRegistration registration)
    {
        _keys = new Keys(
            registration.RegisterStyle("recruit-window", new UiStyle { Background = new Vector4(0.07f, 0.07f, 0.06f, 0.94f) }),
            registration.RegisterStyle("recruit-title", Text(new Vector4(0.95f, 0.85f, 0.55f, 1), 1.15f)),
            registration.RegisterStyle("recruit-text", Text(new Vector4(0.92f, 0.92f, 0.88f, 1), 1)),
            registration.RegisterStyle("recruit-hint", Text(new Vector4(0.7f, 0.7f, 0.66f, 1), 0.9f)),
            registration.RegisterStyle("recruit-button", Button()),
            registration.RegisterAction("recruit-close", _ => _open = false),
            registration.RegisterAction("recruit", Recruit),
            registration.Key("recruit"));

        string control = registration.RegisterControl("recruit", _ => Window());
        registration.Contribute(UiSlots.Overlay, frame => frame.CreateControl(control), order: 10);
    }

    /// <summary>
    /// The window's node this frame: hidden while closed.
    /// </summary>
    internal UiNode Window()
    {
        if (!_open) return new UiNode(_keys.Window) { Visible = false };

        var nodes = new List<UiNode>();
        float top = Margin;
        nodes.Add(LabelNode(nodes, "Recruit a character", _keys.Title, Width - 2 * Margin - ButtonWidth, Margin, top));
        nodes.Add(ButtonNode(nodes, "Close", _keys.Close, "", Width - Margin - ButtonWidth, top));
        top += Row + 6;

        foreach (var character in Characters.All)
        {
            nodes.Add(LabelNode(nodes, character.Name, _keys.Text, Width - 3 * Margin - ButtonWidth, Margin, top));
            nodes.Add(ButtonNode(nodes, "Recruit", _keys.Recruit, character.CharacterId, Width - Margin - ButtonWidth, top));
            top += Row;
            nodes.Add(LabelNode(nodes, character.Note, _keys.Hint, Width - 2 * Margin, Margin, top));
            top += Row;
        }

        string hint = _status.Length > 0 ? _status : "Recruits join your selected character's squad at the camera target.";
        nodes.Add(LabelNode(nodes, hint, _keys.Hint, Width - 2 * Margin, Margin, top + 4));
        top += Row + 4;

        var placement = new UiPlacement(Width, top + Margin, Left: new UiLength(-Width / 2, 0.5f), Top: 90);
        var window = new UiNode(_keys.Window) { Placement = placement, Style = _keys.WindowStyle };
        window.Children.AddRange(nodes);
        return window;
    }

    #endregion

    #region Actions

    /// <summary>
    /// A Recruit press: queues the spawn of the character its argument names.
    /// </summary>
    void Recruit(UiActionContext context)
    {
        var character = Characters.All.FirstOrDefault(c => c.CharacterId == context.Argument);
        if (character is null) return;

        enqueue(new RecruitCommand(character.CharacterId, _spot.At, _spot.Selected) { Tick = tick() });
        _status = $"Recruiting {character.Name}.";
    }

    #endregion

    #region Nodes

    /// <summary>
    /// The window's registered keys.
    /// </summary>
    sealed record Keys(string WindowStyle, string Title, string Text, string Hint, string ButtonStyle, string Close, string Recruit, string Window);

    /// <summary>
    /// A label style in <paramref name="colour"/> at <paramref name="scale"/>.
    /// </summary>
    static UiStyle Text(Vector4 colour, float scale) => new() { Text = colour, TextScale = scale, TextAlign = "Left VCenter" };

    /// <summary>
    /// The buttons' style.
    /// </summary>
    static UiStyle Button() => new()
    {
        Background = new Vector4(0.24f, 0.21f, 0.16f, 1),
        Hover = new Vector4(0.36f, 0.31f, 0.22f, 1),
        Text = new Vector4(0.95f, 0.92f, 0.85f, 1),
        TextAlign = "Center",
    };

    /// <summary>
    /// A label at a place in the window.
    /// </summary>
    UiNode LabelNode(List<UiNode> nodes, string text, string style, float width, float left, float top) =>
        new($"{_keys.Window}.label{nodes.Count}", UiNodeKind.Label)
        {
            Placement = new UiPlacement(width, Row, Left: left, Top: top), Style = style, Text = text,
        };

    /// <summary>
    /// A button at a place in the window.
    /// </summary>
    UiNode ButtonNode(List<UiNode> nodes, string caption, string action, string argument, float left, float top) =>
        new($"{_keys.Window}.button{nodes.Count}", UiNodeKind.Button)
        {
            Placement = new UiPlacement(ButtonWidth, Row - 4, Left: left, Top: top + 2), Style = _keys.ButtonStyle, Text = caption,
            Action = action, Argument = argument,
        };

    #endregion
}
