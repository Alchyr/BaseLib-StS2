using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using BaseLib.Config.UI;
using BaseLib.Extensions;
using BaseLib.Utils;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using Environment = System.Environment;

namespace BaseLib.Config;

public abstract partial class ModConfig
{
    private const string SettingsTheme = "res://themes/settings_screen_line_header.tres";
    public event EventHandler? ConfigChanged;

    private readonly string _path;

    protected readonly List<PropertyInfo> ConfigProperties = [];

    public ModConfig(string? filename = null)
    {
        _path = GetType().GetRootNamespace();
        if (_path == "") _path = "Unknown";
        
        _path = SpecialCharRegex().Replace(_path, "");
        
        filename = filename == null ? _path : SpecialCharRegex().Replace(filename, "");
        if (!filename.Contains('.')) filename += ".cfg";

        string? appData;
        if (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS())
        {
            // On Android, use Godot's user data directory
            appData = OS.GetUserDataDir();
        }
        else
        {
            appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (appData == "") appData = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
        }   
        var libFolder = OperatingSystem.IsMacOS() ? "Library" : ".baselib";
        _path = Path.Combine(appData, libFolder, _path, filename);

        CheckConfigProperties();
        Init();
    }

    public bool HasSettings() => ConfigProperties.Count > 0;

    private void CheckConfigProperties()
    {
        var configType = GetType();

        ConfigProperties.Clear();
        foreach (var property in configType.GetProperties())
        {
            if (!property.CanRead || !property.CanWrite || property.GetMethod?.IsStatic != true) continue;
            ConfigProperties.Add(property);
        }
    }
    
    public abstract void SetupConfigUI(Control optionContainer);

    private void Init()
    {
        //Check for existing config file.
        //If it doesn't exist, save it. If it does, laod it.
        _ = File.Exists(_path) ? Load() : Save();
    }

    public void Changed()
    {
        ConfigChanged?.Invoke(this, EventArgs.Empty);
    }
    
    //Would be slightly more straightforward to directly serialize/deserialize the class,
    //But it would require slightly more setup on the user's part.
    private bool _fileActive = false;
    
    public async Task Save()
    {
        if (_fileActive) return;
        
        _fileActive = true;
        
        Dictionary<string, string> values = [];
        foreach (var property in ConfigProperties)
        {
            var value = property.GetValue(null);
            if (value != null) values.Add(property.Name, value.ToString() ?? string.Empty);
        }

        try
        {
            new FileInfo(_path).Directory?.Create();
            await using var fileStream = File.Create(_path);
            await JsonSerializer.SerializeAsync(fileStream, values);
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"Failed to save config {GetType().Name};");
            MainFile.Logger.Error(e.ToString());
        }

        _fileActive = false;
    }
    
    public async Task Load()
    {
        if (_fileActive ||!File.Exists(_path)) return;
        
        _fileActive = true;
        var hadError = false;
        
        try
        {
            await using var fileStream = File.OpenRead(_path);
            var values = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(fileStream);

            if (values != null)
            {
                foreach (var property in ConfigProperties)
                {
                    if (values.TryGetValue(property.Name, out var value))
                    {
                        var converter = TypeDescriptor.GetConverter(property.PropertyType);
                        try
                        {
                            var configVal = converter.ConvertFromString(value);
                            if (configVal == null)
                            {
                                MainFile.Logger.Warn($"Failed to load saved config value \"{value}\" for property {property.Name}");
                                hadError = true;
                                continue;
                            }
                        
                            var oldVal = property.GetValue(null);
                            if (!configVal.Equals(oldVal))
                            {
                                property.SetValue(null, configVal);
                            }
                        }
                        catch (Exception)
                        {
                            MainFile.Logger.Warn($"Failed to load saved config value \"{value}\" for property {property.Name}");
                            hadError = true;
                        }
                    }
                }
                
                MainFile.Logger.Info($"Loaded config {GetType().Name} successfully");
            }
        }
        catch (Exception)
        {
            MainFile.Logger.Error("Failed to load config; most likely config types were changed.");
            hadError = true;
        }
        
        _fileActive = false;

        if (hadError)
        {
            MainFile.Logger.Error("Error occured loading config; saving new config.");
            await Save();
        }
    }

    protected string GetLabelText(string labelName)
    {
        if (labelName.Contains(' ')) return labelName;
        var prefix = GetType().GetPrefix();
        var loc = LocString.GetIfExists("settings_ui", prefix + StringHelper.Slugify(labelName) + ".title");
        return loc != null ? loc.GetFormattedText() : labelName;
    }

    // Creates a raw toggle control, with no layout (see SimpleModConfig.CreateToggleOption unless you want custom layout)
    protected NConfigTickbox CreateRawTickboxControl(PropertyInfo property)
    {
        var tickbox = new NConfigTickbox().TransferAllNodes(SceneHelper.GetScenePath("screens/settings_tickbox"));
        tickbox.Initialize(this, property);
        return tickbox;
    }

    // Creates a raw slider control, with no layout (see SimpleModConfig.CreateSliderOption unless you want custom layout)
    protected NConfigSlider CreateRawSliderControl(PropertyInfo property)
    {
        var slider = new NConfigSlider().TransferAllNodes(SceneHelper.GetScenePath("screens/settings_slider"));
        slider.Initialize(this, property);
        return slider;
    }

    // Creates a raw dropdown control, with no layout (see SimpleModConfig.CreateDropdownOption unless you want custom layout)
    private static readonly FieldInfo DropdownNode = AccessTools.DeclaredField(typeof(NDropdownPositioner), "_dropdownNode");
    protected NDropdownPositioner CreateRawDropdownControl(PropertyInfo property)
    {
        var dropdown = new NConfigDropdown().TransferAllNodes(SceneHelper.GetScenePath("screens/settings_dropdown"));
        var items = CreateDropdownItems(property, out var currentIndex);
        dropdown.SetItems(items, currentIndex);
        
        var dropdownPositioner = new NDropdownPositioner();
        dropdownPositioner.SetCustomMinimumSize(new(320, 64));
        dropdownPositioner.FocusMode = Control.FocusModeEnum.All;
        dropdownPositioner.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        dropdownPositioner.SizeFlagsVertical = Control.SizeFlags.Fill;
        DropdownNode.SetValue(dropdownPositioner, dropdown);

        dropdownPositioner.AddChild(dropdown);
        dropdownPositioner.MouseFilter = Control.MouseFilterEnum.Ignore;

        return dropdownPositioner;
    }

    private List<NConfigDropdownItem.ConfigDropdownItem> CreateDropdownItems(PropertyInfo property, out int currentIndex)
    {
        List<NConfigDropdownItem.ConfigDropdownItem> items = [];
        var type = property.PropertyType;
        var prefix = GetType().GetPrefix();
        var currentValue = property.GetValue(null);
        int count = 0;
        currentIndex = 0;
        
        if (type.IsEnum)
        {
            foreach (var value in type.GetEnumValues())
            {
                if (currentValue != null && currentValue.Equals(value))
                {
                    currentIndex = count;
                }
                ++count;
                var loc = LocString.GetIfExists("settings_ui", $"{prefix}{StringHelper.Slugify(property.Name)}.{value}");
                items.Add(new(loc?.GetRawText() ?? value?.ToString() ?? "UNKNOWN", () => property.SetValue(null, value)));
            }
        }
        else //Check for dropdown options attribute
        {
            throw new NotSupportedException("Dropdown only supports enum types currently");
        }

        return items;
    }

    // Creates a raw label control, with no layout (see SimpleModConfig.Create*Option and CreateSectionHeader for
    // layout-ready controls)
    protected static MegaRichTextLabel CreateRawLabelControl(string labelText, int fontSize)
    {
        var kreonNormal = PreloadManager.Cache.GetAsset<Font>("res://themes/kreon_regular_shared.tres");
        var kreonBold = PreloadManager.Cache.GetAsset<Font>("res://themes/kreon_bold_shared.tres");

        MegaRichTextLabel label = new()
        {
            Name = "Label",
            Theme = PreloadManager.Cache.GetAsset<Theme>(SettingsTheme),
            AutoSizeEnabled = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            BbcodeEnabled = true,
            ScrollActive = false,
            VerticalAlignment = VerticalAlignment.Center,
            Text = labelText
        };

        label.AddThemeFontOverride("normal_font", kreonNormal);
        label.AddThemeFontOverride("bold_font", kreonBold);
        label.AddThemeFontSizeOverride("normal_font_size", fontSize);
        label.AddThemeFontSizeOverride("bold_font_size", fontSize);
        label.AddThemeFontSizeOverride("bold_italics_font_size", fontSize);
        label.AddThemeFontSizeOverride("italics_font_size", fontSize);
        label.AddThemeFontSizeOverride("mono_font_size", fontSize);

        return label;
    }

    protected static ColorRect CreateDividerControl()
    {
        return new ColorRect
        {
            Name = "Divider",
            CustomMinimumSize = new Vector2(0, 2),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Color = new Color(0.909804f, 0.862745f, 0.745098f, 0.25098f)
        };
    }

    [GeneratedRegex("[^a-zA-Z0-9_]")]
    private static partial Regex SpecialCharRegex();
}