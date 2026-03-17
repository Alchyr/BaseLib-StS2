using System.Reflection;
using BaseLib.Config.UI;
using Godot;
// ReSharper disable MemberCanBePrivate.Global

namespace BaseLib.Config;

public class SimpleModConfig : ModConfig
{
    // Auto-generate a UI from the properties used. Should be enough for the vast majority of mods,
    // but you can also subclass SimpleModConfig and override this to get access to the helpers below (in addition
    // to the raw Create*Control methods from ModConfig), without an auto-generated UI.
    public override void SetupConfigUI(Control optionContainer)
    {
        VBoxContainer options = new();

        MainFile.Logger.Info($"Setting up SimpleModConfig {GetType().FullName}");
        
        options.Size = optionContainer.Size;
        options.AddThemeConstantOverride("separation", 8);

        // Add "margin" to the top, to keep the edge-element distance same as on the left and right
        var spacer = new Control();
        spacer.CustomMinimumSize = new Vector2(0, 16);
        optionContainer.AddChild(spacer);

        optionContainer.AddChild(options);

        Control? currentSetting = null;
        string? currentSection = null;

        var properties = ConfigProperties.ToArray();
        for (var i = 0; i < properties.Length; i++)
        {
            var property = properties[i];
            var nextProperty = i < properties.Length - 1 ? properties[i + 1] : null;

            // Create a section header if this property starts a new section
            var sectionName = property.GetCustomAttribute<ConfigSectionAttribute>()?.Name;
            if (sectionName != null && sectionName != currentSection)
            {
                currentSection = sectionName;
                var isFirstChild = options.GetChildCount() == 0;
                options.AddChild(CreateSectionHeader(currentSection, alignToTop: isFirstChild));
            }

            // Generate the option row and set up focus handling
            try
            {
                var newRow = GenerateOptionFromProperty(property);
                options.AddChild(newRow);

                var previousSetting = currentSetting;
                currentSetting = newRow.SettingControl;

                if (previousSetting != null)
                {
                    NodePath path = currentSetting.GetPathTo(previousSetting);
                    currentSetting.FocusNeighborLeft ??= path;
                    currentSetting.FocusNeighborTop ??= path;

                    path = previousSetting.GetPathTo(currentSetting);
                    previousSetting.FocusNeighborRight ??= path;
                    previousSetting.FocusNeighborBottom ??= path;
                }
            }
            catch (NotSupportedException ex)
            {
                MainFile.Logger.Error($"Not creating UI for unsupported property '{property.Name}': {ex.Message}");
                continue;
            }

            // Add a divider unless the next property starts a new section (or there is no next)
            var nextSectionName = nextProperty?.GetCustomAttribute<ConfigSectionAttribute>()?.Name;
            var nextIsSameSection = nextSectionName == null || nextSectionName == currentSection;
            if (nextProperty != null && nextIsSameSection)
            {
                options.AddChild(CreateDividerControl());
            }
        }
    }

    // Create a standard, layout-ready toggle
    protected NConfigOptionRow CreateToggleOption(PropertyInfo property)
    {
        var control = CreateRawTickboxControl(property);
        var label = CreateRawLabelControl(GetLabelText(property.Name), 28);
        return new NConfigOptionRow("Toggle_" + property.Name, label, control);
    }

    // Create a standard, layout-ready slider
    protected NConfigOptionRow CreateSliderOption(PropertyInfo property)
    {
        var control = CreateRawSliderControl(property);
        var label = CreateRawLabelControl(GetLabelText(property.Name), 28);
        return new NConfigOptionRow("Slider_" + property.Name, label, control);
    }

    // Create a standard, layout-ready dropdown
    protected NConfigOptionRow CreateDropdownOption(PropertyInfo property)
    {
        var control = CreateRawDropdownControl(property);
        var label = CreateRawLabelControl(GetLabelText(property.Name), 28);
        return new NConfigOptionRow("Dropdown_" + property.Name, label, control);
    }

    // Create a standard, layout-ready section header
    protected MarginContainer CreateSectionHeader(string labelName, bool alignToTop = false)
    {
        MarginContainer container = new();
        container.Name = "Container_" + labelName.Replace(" ", "");
        container.AddThemeConstantOverride("margin_left", 24);
        container.AddThemeConstantOverride("margin_right", 24);
        container.MouseFilter = Control.MouseFilterEnum.Ignore;

        var label = CreateRawLabelControl($"[center][b]{GetLabelText(labelName)}[/b][/center]", 40);
        label.Name = "SectionLabel_" + labelName.Replace(" ", "");
        label.CustomMinimumSize = new Vector2(0, 64);

        if (alignToTop) label.VerticalAlignment = VerticalAlignment.Top;

        container.AddChild(label);
        return container;
    }

    // Automated generator: used by default by SimpleModConfig subclasses to create a layout with no additional UI code
    protected NConfigOptionRow GenerateOptionFromProperty(PropertyInfo property)
    {
        var propertyType = property.PropertyType;

        NConfigOptionRow optionRow;
        if (propertyType == typeof(bool)) optionRow = CreateToggleOption(property);
        else if (propertyType == typeof(double)) optionRow = CreateSliderOption(property);
        else if (propertyType.IsEnum) optionRow = CreateDropdownOption(property);
        else throw new NotSupportedException($"Type {propertyType.FullName} is not supported by SimpleModConfig.");

        return optionRow;
    }
}