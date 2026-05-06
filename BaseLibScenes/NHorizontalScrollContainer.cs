using BaseLib.Utils;
using Godot;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Helpers;

namespace BaseLib.BaseLibScenes;

public partial class NHorizontalScrollContainer : Control
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="contents">Control that will be moved horizontally for scrolling. Must be added as a child after Create
    /// is called. Should have TopLeft anchor.</param>
    /// <returns></returns>
    public static NHorizontalScrollContainer Create(string name, Control contents, Action<Control> setupPositionAndSize)
    {
        NHorizontalScrollContainer container = new NHorizontalScrollContainer();
        container.Name = name;
        container.MouseFilter = MouseFilterEnum.Pass;
        
        setupPositionAndSize(container);
        
        container.ScrollContents = contents;

        return container;
    }
    
    private float _controllerScrollAmount = 400f;
    private float _startDragPosX = 0;
    private float _targetDragPosX = 0;
    public bool IsDragging { get; protected set; } = false;

    public Control? ScrollContents { get; set; }

    public float ContentSize => ScrollContents != null ? ScrollContents.Size.X : 0.0f;

    public float ScrollLimit => Math.Min(0, Size.X - ContentSize);

    public float ScrollPosition => ScrollContents != null ? ScrollContents.Position.X : 0.0f;
    public float TargetPosition
    {
        get => _targetDragPosX;
        set
        {
            _targetDragPosX = Math.Clamp(value, ScrollLimit, 0);
        }
    }

    public override void _GuiInput(InputEvent inputEvent)
    {
        if (!IsVisibleInTree())
            return;
        ProcessMouseEvent(inputEvent);
        ProcessScrollEvent(inputEvent);
    }
    
    public override void _Input(InputEvent inputEvent)
    {
        if (!IsVisibleInTree())
            return;
        var focus = GetViewport()?.GuiGetFocusOwner();
        
        if (focus != null && focus != this)
            return;
        ProcessControllerEvent(inputEvent);
    }

    public void ProcessControllerEvent(InputEvent inputEvent)
    {
        if (inputEvent.IsActionPressed(MegaInput.left))
        {
            _targetDragPosX += _controllerScrollAmount;
        }
        else if (inputEvent.IsActionPressed(MegaInput.right))
        {
            _targetDragPosX -= _controllerScrollAmount;
        }
    }
    
    public void ProcessMouseEvent(InputEvent inputEvent)
    {
        if (ScrollContents == null)
            return;
        
        switch (inputEvent)
        {
            case InputEventMouseMotion eventMouseMotion:
                if (!IsDragging)
                    break;
                _targetDragPosX += eventMouseMotion.Relative.X;
                break;
            case InputEventMouseButton eventMouseButton:
                IsDragging = eventMouseButton.Pressed;
                if (!eventMouseButton.Pressed)
                    break;
                _startDragPosX = ScrollPosition;
                _targetDragPosX = _startDragPosX;
                break;
        }
    }

    public void ProcessScrollEvent(InputEvent inputEvent)
    {
        _targetDragPosX += ScrollHelper.GetDragForScrollEvent(inputEvent);
    }
    
    public Action<NHorizontalScrollContainer>? CustomProcess { get; set; }
    public override void _Process(double delta)
    {
        if (!IsVisibleInTree())
            return;
        CustomProcess?.Invoke(this);
        UpdateScrollPosition(delta);
    }

    protected void UpdateScrollPosition(double delta)
    {
        if (ScrollContents == null)
            return;
        
        float target = _targetDragPosX;
        if (!Mathf.IsEqualApprox(ScrollPosition, target))
        {
            float newX = Mathf.Lerp(ScrollPosition, target, (float) delta * 15f);
            ScrollContents.Position = ScrollContents.Position with
            {
                X = newX
            };
            //Snap to position
            if (Mathf.Abs(ScrollContents.Position.X - target) < 0.5)
            {
                ScrollContents.Position = ScrollContents.Position with
                {
                    X = target
                };
            }
        }
        
        if (IsDragging)
            return;
        
        if (_targetDragPosX > 0.0f)
        {
            _targetDragPosX = Mathf.Lerp(_targetDragPosX, 0, (float) delta * 12f);
        }
        else if (_targetDragPosX < ScrollLimit)
        {
            _targetDragPosX = Mathf.Lerp(_targetDragPosX, ScrollLimit, (float) delta * 12f);
        }
    }
}