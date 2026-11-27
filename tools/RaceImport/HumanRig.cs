namespace RaceImport;

/// <summary>
/// One of the game's two human rigs a body is fitted to: its skeleton, the human body bound to it (the reference clothing is modelled around) and
/// that body's part map. The bone names (<c>Bip01 ...</c>) and handles are the same in both.
/// </summary>
/// <param name="Skeleton">The skeleton file the fitted mesh links.</param>
/// <param name="Body">The human body mesh bound to it.</param>
/// <param name="Mask">The human body's part map.</param>
public sealed record HumanRig(string Skeleton, string Body, string Mask)
{
    /// <summary>
    /// The male rig: every male human body links it.
    /// </summary>
    public static readonly HumanRig Male = new("male_skeleton.skeleton", "human_male.mesh", "MaleMask_Default.png");

    /// <summary>
    /// The female rig: shorter-shouldered, its chest higher and further forward than the male's.
    /// </summary>
    public static readonly HumanRig Female = new("female_skeleton.skeleton", "human_female.mesh", "FemaleMask_Default.png");

    /// <summary>
    /// The rig named <c>male</c> or <c>female</c>, null for any other name.
    /// </summary>
    public static HumanRig? Named(string name) => name switch
    {
        "male" => Male,
        "female" => Female,
        _ => null,
    };
}
