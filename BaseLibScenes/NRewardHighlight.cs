using Godot;

namespace BaseLib.BaseLibScenes;

/// <summary>
/// Allows highlighting of rewards in the reward screen. Similar to card highlights.
/// </summary>
[GlobalClass]
public partial class NRewardHighlight : TextureRect
{
    private static readonly StringName _shaderParameterWidth = new StringName("width");
    private static readonly StringName _shaderParameterEase = new StringName("ease");
    private static readonly StringName _shaderParameterModuloWidth = new StringName("modulo_width");
    private static readonly StringName _shaderParameterRippleSpeed = new StringName("ripple_speed");


    
    public static readonly Color gold = new Color(1f, 0.784f, 0f, 0.98f);
    public static readonly Color red = new Color(0.93f, 0f, 0.13f, 0.98f);

    private Tween? _curTween;

    private ShaderMaterial _shaderMaterial;

    public override void _Ready()
    {
        _shaderMaterial = new ShaderMaterial();
        _shaderMaterial.Shader = GD.Load<Shader>("res://shaders/card_ripple.gdshader");
        _shaderMaterial.SetShaderParameter(_shaderParameterEase, 0.005f);
        _shaderMaterial.SetShaderParameter(_shaderParameterModuloWidth, 0.02f);
        _shaderMaterial.SetShaderParameter(_shaderParameterRippleSpeed, 0.01f);
        _shaderMaterial.SetShaderParameter(_shaderParameterWidth, 0.0f);
        Material = _shaderMaterial;
    }

    public void AnimShow()
    {
        _curTween?.Kill();
        _curTween = CreateTween();
        _curTween.TweenMethod(Callable.From<float>(SetShaderParameter), GetShaderParameter(), 0.075f, 0.5).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
    }

    public void AnimHide()
    {
        _curTween?.Kill();
        _curTween = CreateTween();
        _curTween.TweenMethod(Callable.From<float>(SetShaderParameter), GetShaderParameter(), 0.0, 0.5);
    }

    public void AnimHideInstantly()
    {
        _curTween?.Kill();
        SetShaderParameter(0f);
    }

    public void AnimFlash()
    {
        _curTween?.Kill();
        _curTween = CreateTween();
        _curTween.TweenMethod(Callable.From<float>(SetShaderParameter), GetShaderParameter(), 0.15f, 0.1);
        _curTween.TweenMethod(Callable.From<float>(SetShaderParameter), 0.15f, 0.075f, 0.35).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
    }

    private float GetShaderParameter()
    {
        return _shaderMaterial.GetShaderParameter(_shaderParameterWidth).AsSingle();
    }

    private void SetShaderParameter(float val)
    {
        _shaderMaterial.SetShaderParameter(_shaderParameterWidth, val);
    }
}
