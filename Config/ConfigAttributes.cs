namespace BaseLib.Config;

[AttributeUsage(AttributeTargets.Property)]
public class ConfigSectionAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

[AttributeUsage(AttributeTargets.Property)]
public class SliderRangeAttribute : Attribute
{
    public double Min { get; }
    public double Max { get; }
    public double Step { get; }

    public SliderRangeAttribute(double min, double max, double step = 1.0)
    {
        if (min > max)
        {
            throw new ArgumentException($"SliderRange: Min ({min}) cannot be greater than Max ({max}).");
        }

        if (step <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(step), "SliderRange: Step must be greater than 0.");
        }

        Min = min;
        Max = max;
        Step = step;
    }
}

[AttributeUsage(AttributeTargets.Property)]
public class SliderLabelFormatAttribute(string format) : Attribute
{
    public string Format { get; } = format;
}

/// <summary>
/// <para>Show a tooltip for this setting on hover. Requires a settings_ui.json localization file.
/// The names used are typically:</para>
/// YOURMOD-PROPERTY_NAME.hover.title (optional, if missing, no title will be shown)<br/>
/// YOURMOD-PROPERTY_NAME.hover.desc (required)
/// </summary>
/// <param name="enabled">Enable hover tip for this property. Can be set to false in order to add exceptions for <see cref="HoverTipsByDefaultAttribute"/></param>
[AttributeUsage(AttributeTargets.Property)]
public class ConfigHoverTipAttribute(bool enabled = true) : Attribute
{
    public bool Enabled { get; } = enabled;
}

/// <summary>
/// Attempt to add hover tips to ALL config properties in this class. Use <see cref="ConfigHoverTipAttribute"/> with
/// enabled=false to add exceptions. Still requires .hover.desc entries in your localization file, see <see cref="ConfigHoverTipAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class HoverTipsByDefaultAttribute : Attribute;

/// <summary>
/// Completely ignores this property. It will not be loaded or saved, and will not be shown for auto-generated UI.<br/>
/// Intended for properties that aren't configuration options at all.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class ConfigIgnoreAttribute : Attribute { }

/// <summary>
/// Saves and loads this property to the config file normally, but generate no UI for it in SimpleModConfig.<br/>
/// Intended for when you want to create the UI manually, or easily persist things that aren't user-configurable (e.g.
/// total gold gained / number of runs played with the mod active).
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class ConfigHideInUI : Attribute { }