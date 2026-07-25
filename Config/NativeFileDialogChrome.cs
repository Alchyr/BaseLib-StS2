using Godot;

namespace BaseLib.Config;

internal static class NativeFileDialogChrome
{
    private const int FileDialogLayer = 132;

    public static void Popup(FileDialog dialog, float centeredRatio = 0.55f)
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree?.Root == null)
        {
            dialog.QueueFree();
            return;
        }

        var viewport = tree.Root.GetViewport();
        var previousFocus = viewport?.GuiGetFocusOwner();
        var previousMouseMode = Input.MouseMode;

        var layer = new CanvasLayer
        {
            Name = "BaseLibNativeFileDialogModal",
            Layer = FileDialogLayer,
        };
        tree.Root.AddChild(layer);

        var shield = new Control
        {
            Name = "FileDialogShieldRoot",
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        layer.AddChild(shield);

        var dim = new ColorRect
        {
            Name = "FileDialogDim",
            Color = new Color(0f, 0f, 0f, 0.55f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        shield.AddChild(dim);

        layer.AddChild(dialog);
        ConfigureDialog(dialog);
        Callable.From(FitShieldToViewport).CallDeferred();
        if (viewport != null)
            viewport.SizeChanged += FitShieldToViewport;

        dialog.Canceled += CloseDialog;
        dialog.CloseRequested += CloseDialog;
        dialog.TreeExiting += RestoreMouseAndFocus;

        Input.MouseMode = Input.MouseModeEnum.Visible;
        dialog.PopupCenteredRatio(centeredRatio);
        return;

        void FitShieldToViewport()
        {
            if (!GodotObject.IsInstanceValid(shield) || viewport == null)
                return;

            var rect = viewport.GetVisibleRect();
            shield.Position = rect.Position;
            shield.Size = rect.Size;
        }

        void CloseDialog()
        {
            if (GodotObject.IsInstanceValid(dialog))
                dialog.QueueFree();
        }

        void RestoreMouseAndFocus()
        {
            if (GodotObject.IsInstanceValid(viewport))
                viewport.SizeChanged -= FitShieldToViewport;

            Input.MouseMode = previousMouseMode;

            if (GodotObject.IsInstanceValid(layer))
                layer.QueueFree();

            var target = previousFocus;
            if (target == null || !GodotObject.IsInstanceValid(target) || !target.IsVisibleInTree())
                return;

            Callable.From(() =>
            {
                if (GodotObject.IsInstanceValid(target) && target.IsVisibleInTree())
                    target.GrabFocus();
            }).CallDeferred();
        }
    }

    private static void ConfigureDialog(FileDialog dialog)
    {
        dialog.Name = "BaseLibNativeFileDialog";
        dialog.Exclusive = true;
        dialog.Unresizable = false;
        dialog.Transparent = false;
        dialog.MinSize = new Vector2I(760, 520);
        dialog.Size = dialog.MinSize;
    }
}
