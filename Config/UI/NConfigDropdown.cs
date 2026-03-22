using System.Reflection;
using BaseLib.Utils;
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
    private NodePath _selfNodePath = new(".");

    private static readonly FieldInfo DropdownContainerField = AccessTools.Field(typeof(NDropdown), "_dropdownContainer");

    public NConfigDropdown()
    {
        SetCustomMinimumSize(new(324, 64));
        SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        SizeFlagsVertical = SizeFlags.Fill;
        FocusMode = FocusModeEnum.All;

        this.TransferAllNodes(SceneHelper.GetScenePath("screens/settings_dropdown"));
    }

    public override void _Process(double delta)
    {
        base._Process(delta);

        // Hacky, but this is overwritten and causes issues. Setting it in _Ready and on row creation isn't enough.
        if (FocusNeighborLeft != _selfNodePath || FocusNeighborRight != _selfNodePath)
        {
            FocusNeighborLeft = _selfNodePath;
            FocusNeighborRight = _selfNodePath;
        }

        if (DropdownContainerField.GetValue(this) is Control { Visible: true } container)
        {
            // Ensure the list of items follows the dropdown itself when the parent container is scrolled.
            // GlobalPosition/TopLevel is used to override the clipping from NModConfigPopup's edges.
            container.GlobalPosition = GlobalPosition + new Vector2(0, Size.Y);
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

                // Focus the last selected entry (base class always selects the first)
                if (_currentDisplayIndex < 0 || _currentDisplayIndex >= _items.Count) return;
                var entry = _dropdownItems.GetChildOrNull<NConfigDropdownItem>(_currentDisplayIndex);
                entry?.TryGrabFocus();
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
        _currentDisplayIndex = configDropdownItem.DisplayIndex; 
        configDropdownItem.Data.OnSet();
    }
}