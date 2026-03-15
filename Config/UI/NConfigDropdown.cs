using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;

namespace BaseLib.Config.UI;

public partial class NConfigDropdown : NSettingsDropdown
{
    private List<NConfigDropdownItem.ConfigDropdownItem>? _items;
    private int _currentDisplayIndex = -1;

    public NConfigDropdown()
    {
        SetCustomMinimumSize(new(324, 64));
        SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        SizeFlagsVertical = SizeFlags.Fill;
        FocusMode = FocusModeEnum.All;
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
    }
    
    private void OnDropdownItemSelected(NDropdownItem nDropdownItem)
    {
        var configDropdownItem = nDropdownItem as NConfigDropdownItem;
        if (configDropdownItem == null || configDropdownItem.DisplayIndex == _currentDisplayIndex)
            return;
        
        CloseDropdown();
        _currentOptionLabel.SetTextAutoSize(configDropdownItem.Data.Text);
        _currentDisplayIndex = configDropdownItem.DisplayIndex; 
        configDropdownItem.Data.OnSet();
    }
}