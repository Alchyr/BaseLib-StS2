using System.Reflection;
using BaseLib.Abstracts;
using BaseLibCompatibilityHelper.BaseLibCompatibilityHelperCode.Intermediary;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;

namespace BaseLib.BaseLibScenes;

/// <summary>
/// Internal adapter that reuses the vanilla character select button scene for a custom entry.
/// </summary>
internal sealed class NCustomCharacterSelectEntryButton : SelectButtonDelegateIntermediary
{
    private const string ButtonScenePath = "res://scenes/screens/char_select/char_select_button.tscn";

    private static readonly FieldInfo? DelegateField = AccessTools.Field(typeof(NCharacterSelectButton), "_delegate");
    private static readonly FieldInfo? CharacterField = AccessTools.Field(typeof(NCharacterSelectButton), "_character");
    private static readonly FieldInfo? LockedField = AccessTools.Field(typeof(NCharacterSelectButton), "_isLocked");

    private readonly NCharacterSelectScreen _screen;
    private readonly Action<NCustomCharacterSelectEntryButton> _onSelected;

    /// <summary>
    /// Creates a custom entry button that uses the vanilla character select button scene.
    /// </summary>
    public NCustomCharacterSelectEntryButton(
        CustomCharacterSelectEntry entry,
        NCharacterSelectScreen screen,
        Action<NCustomCharacterSelectEntryButton> onSelected)
    {
        Entry = entry;
        _screen = screen;
        _onSelected = onSelected;

        var scene = ResourceLoader.Load<PackedScene>(ButtonScenePath)
                    ?? throw new InvalidOperationException($"Failed to load {ButtonScenePath}.");
        Button = scene.Instantiate<NCharacterSelectButton>(PackedScene.GenEditState.Disabled);
        Button.Name = $"{entry.EntryId}_entry_button";
        Button.SetMeta("BaseLibCustomCharacterSelectEntry", entry.EntryId);

        DelegateField?.SetValue(Button, this);
        UpdateInteractionState();
    }

    /// <summary>
    /// The entry represented by this button.
    /// </summary>
    public CustomCharacterSelectEntry Entry { get; }

    /// <summary>
    /// The instantiated vanilla button node.
    /// </summary>
    public NCharacterSelectButton Button { get; }

    /// <inheritdoc />
    public override StartRunLobby Lobby => _screen.Lobby;

    /// <summary>
    /// Whether this entry is currently locked.
    /// </summary>
    public bool IsLocked => Button.IsLocked;

    /// <summary>
    /// The character whose vanilla lock semantics are reused by this entry, if any.
    /// </summary>
    public CharacterModel? LockSourceCharacter => Entry.AvailabilitySourceCharacter;

    /// <summary>
    /// Enables the underlying button.
    /// </summary>
    public void Enable()
    {
        Button.Enable();
        UpdateInteractionState();
    }

    /// <summary>
    /// Disables the underlying button.
    /// </summary>
    public void Disable()
    {
        Button.Disable();
    }

    /// <summary>
    /// Clears the selected visual state.
    /// </summary>
    public void Deselect()
    {
        Button.Deselect();
    }

    /// <summary>
    /// Attempts to return focus to the underlying button.
    /// </summary>
    public void TryGrabFocus()
    {
        if (Button.Visible && Button.IsInsideTree())
        {
            Button.GrabFocus();
        }
    }

    public override void SelectCharacter(NCharacterSelectButton charSelectButton)
    {
        _onSelected(this);
    }

    private void ApplyVisuals()
    {
        var icon = Button.GetNodeOrNull<TextureRect>("%Icon");
        icon?.Texture = ResourceLoader.Load<Texture2D>(Entry.ButtonIconPath);

        var iconAdd = Button.GetNodeOrNull<TextureRect>("%IconAdd");
        iconAdd?.Texture = icon?.Texture;

        var lockIcon = Button.GetNodeOrNull<TextureRect>("%Lock");
        lockIcon?.Visible = IsLocked;
    }

    private void UpdateInteractionState()
    {
        CharacterField?.SetValue(Button, LockSourceCharacter ?? ModelDb.Character<Ironclad>());
        LockedField?.SetValue(Button, !Entry.UnlockedInCharacterSelect);
        ApplyVisuals();
    }
}
