using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;

namespace BaseLib.Config.UI;

public partial class NConfigDropdown : NSettingsDropdown
{
    private List<NConfigDropdownItem.ConfigDropdownItem>? _items;
    private int _currentDisplayIndex = -1;
    private float _lastGlobalY;

    private static readonly FieldInfo DropdownContainerField = AccessTools.Field(typeof(NDropdown), "_dropdownContainer");

    public NConfigDropdown()
    {
        SetCustomMinimumSize(new(324, 64));
        SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        SizeFlagsVertical = SizeFlags.Fill;
        FocusMode = FocusModeEnum.All;
    }

    public override void _Process(double delta)
    {
        base._Process(delta);

        if (DropdownContainerField.GetValue(this) is Control { Visible: true } &&
            Mathf.Abs(_lastGlobalY - GlobalPosition.Y) > 0.5f)
        {
            CloseDropdown();
        }

        _lastGlobalY = GlobalPosition.Y;
    }

    public void SetItems(List<NConfigDropdownItem.ConfigDropdownItem> items, int initialIndex)
    {
        _items = items;
        _currentDisplayIndex = initialIndex;
    }

    public override void _Ready()
    {
        ConnectSignals();
        ClearDropdownItems();

        if (_items == null) throw new Exception("Created config dropdown without setting items");

        for (var i = 0; i < _items.Count; i++)
        {
            NConfigDropdownItem child = NConfigDropdownItem.Create(_items[i]);
            _dropdownItems.AddChildSafely(child);
            child.Connect(NDropdownItem.SignalName.Selected,
                Callable.From(new Action<NDropdownItem>(OnDropdownItemSelected)));
            child.Init(i);

            if (i == _currentDisplayIndex)
            {
                _currentOptionLabel.SetTextAutoSize(child.Data.Text);
            }
        }
        

        _dropdownItems.GetParent<NDropdownContainer>().RefreshLayout();

        if (DropdownContainerField.GetValue(this) is Control container)
        {
            container.VisibilityChanged += () => {
                container.TopLevel = container.Visible;
                container.GlobalPosition = GlobalPosition + new Vector2(0, Size.Y);
            };
        }
    }
    
    private void OnDropdownItemSelected(NDropdownItem nDropdownItem)
    {
        var configDropdownItem = nDropdownItem as NConfigDropdownItem;
        if (configDropdownItem == null)
            return;
        
        CloseDropdown();
        _currentOptionLabel.SetTextAutoSize(configDropdownItem.Data.Text);
        configDropdownItem.Data.OnSet();
    }
}