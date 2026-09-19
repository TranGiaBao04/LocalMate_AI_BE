namespace LocalMateAI.Domain.Constants;

public static class ReviewQuickTags
{
    public const string WorthVisiting = nameof(WorthVisiting);
    public const string NearMetro = nameof(NearMetro);
    public const string EasyToReach = nameof(EasyToReach);
    public const string GoodValue = nameof(GoodValue);
    public const string NiceAtmosphere = nameof(NiceAtmosphere);
    public const string GoodForGroups = nameof(GoodForGroups);
    public const string TooCrowded = nameof(TooCrowded);
    public const string HardToFind = nameof(HardToFind);
    public const string Overpriced = nameof(Overpriced);
    public const string BelowExpectations = nameof(BelowExpectations);
    public const string InaccurateDescription = nameof(InaccurateDescription);
    public const string WantsReplacement = nameof(WantsReplacement);

    public static IReadOnlyList<string> All { get; } =
    [
        WorthVisiting,
        NearMetro,
        EasyToReach,
        GoodValue,
        NiceAtmosphere,
        GoodForGroups,
        TooCrowded,
        HardToFind,
        Overpriced,
        BelowExpectations,
        InaccurateDescription,
        WantsReplacement
    ];
}
