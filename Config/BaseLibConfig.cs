using MegaCrit.Sts2.Core.Entities.Cards;

namespace BaseLib.Config;

// Try to generate hover tips for all properties.
// Use [ConfigHoverTip(false)] for exceptions; warnings will be printed for each property
// that is missing a corresponding settings_ui.json localization string (see below)
[HoverTipsByDefault]
internal class BaseLibConfig : SimpleModConfig
{
    public enum StartingActEnum
    {
        Overgrowth, Underdocks
    }

    // Note: In all the example localization strings below, BASELIB is used because this file is in BaseLib!
    // Your own mod name would be there if you copied this class over to your mod.

    // BASELIB-FIRST_SECTION.title in settings_ui.json
    [ConfigSection("FirstSection")]

    // BASELIB-ENABLE_DEBUG_LOGGING.title in settings_ui.json
    public static bool EnableDebugLogging { get; set; } = false;

    public static bool AllowDuplicateRelics { get; set; } = false;

    // Would generate a hover tip if BASELIB-CREATED_CARD_KEYWORD.hover.desc exists
    // Doesn't do anything in this example because the class already has [HoverTipsByDefault]
    [ConfigHoverTip]
    public static CardKeyword CreatedCardKeyword { get; set; } = CardKeyword.None;

    [ConfigSection("SecondSection")]
    [SliderRange(0.1, 4.0, 0.05)]
    [SliderLabelFormat("{0:0.00}x")]
    public static double EnemyDamageMultiplier { get; set; } = 1.25;

    // The type is double, but doubles perfectly represent integers up to 2^53, so there are no floating point
    // accuracy/rounding issues to worry about here. Use an integer step and simply cast to int if required.
    [SliderRange(-50, 50, 5)]
    [SliderLabelFormat("{0:+0;-0;0} HP")] // Force a + sign in front of positive numbers
    [ConfigHoverTip(false)] // Don't try to generate a hover tip despite [HoverTipsByDefault] on the class
    public static double StartingHealthOffset { get; set; } = -10;

    [ConfigHoverTip(false)]
    public static StartingActEnum StartingAct { get; set; } = StartingActEnum.Overgrowth;

    [SliderRange(0, 10)] // Default step value is 1
    [ConfigHoverTip(false)]
    public static double MinimumElitesPerAct { get; set; } = 6;

    [ConfigSection("LogSection")]
    public static bool OpenLogWindowOnStartup { get; set; } = false;

    [SliderRange(128, 2048, 64)]
    [SliderLabelFormat("{0:0}")]
    public static double LimitedLogSize { get; set; } = 256;
}
