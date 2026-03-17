using Godot;

namespace BaseLib.Config.UI;

// ReSharper disable once Godot.MissingParameterlessConstructor

// Wrapper class that takes a control (e.g. toggle, slider) and adds a label and layout with margins to it
public partial class NConfigOptionRow : MarginContainer
{
    public Control SettingControl { get; private set; }

    public NConfigOptionRow(string name, Control label, Control settingControl)
    {
        Name = name;
        SettingControl = settingControl;

        AddThemeConstantOverride("margin_left", 24);
        AddThemeConstantOverride("margin_right", 24);
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(0, 64);

        label.CustomMinimumSize = new Vector2(0, 64);

        AddChild(label);
        AddChild(settingControl);
    }
}