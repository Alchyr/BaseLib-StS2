using BaseLib.Hooks;
using Godot;

namespace BaseLib.Utils;

/// <summary>
///     Factory helpers for Godot materials using game shaders (HSV, doom health bar strip, etc.).
/// </summary>
/// <remarks>
///     Independent of other mods; duplicate of game-aligned setup may exist elsewhere under different types.
/// </remarks>
public static class ShaderUtils
{
    private const string HsvShaderPath = "res://shaders/hsv.gdshader";
    private const string DoomBarShaderPath = "res://scenes/combat/doom_bar.gdshader";

    private static NoiseTexture2D? _vanillaDoomBarNoiseTexture;

    private static Shader? GameHsvShader => (Shader?)GD.Load<Shader>(HsvShaderPath)?.Duplicate();

    private static Shader? GameDoomBarShader => (Shader?)GD.Load<Shader>(DoomBarShaderPath)?.Duplicate();

    private static NoiseTexture2D VanillaDoomBarNoiseTexture =>
        _vanillaDoomBarNoiseTexture ??= CreateVanillaDoomBarNoiseTexture();

    /// <summary>
    ///     Convenience method to quickly generate a <c>ShaderMaterial</c> using the game's HSV shader.
    /// </summary>
    /// <param name="h">Hue shader parameter.</param>
    /// <param name="s">Saturation shader parameter.</param>
    /// <param name="v">Value shader parameter.</param>
    /// <returns>Configured material.</returns>
    public static ShaderMaterial GenerateHsv(float h, float s, float v)
    {
        var shader = GameHsvShader;
        if (shader == null)
            throw new InvalidOperationException($"Failed to load HSV shader ({HsvShaderPath}).");

        var material = new ShaderMaterial { Shader = shader };

        material.SetShaderParameter("h", h);
        material.SetShaderParameter("s", s);
        material.SetShaderParameter("v", v);

        return material;
    }

    /// <summary>
    ///     Builds a <c>ShaderMaterial</c> using the game's doom health bar shader (<c>doom_bar.gdshader</c>) with the same
    ///     noise settings as <c>health_bar.tscn</c> and a caller-supplied gradient.
    /// </summary>
    /// <param name="gradientTexture">Gradient passed to the shader as <c>gradient_tex</c>.</param>
    /// <returns>Material with <c>noise_tex</c> and <c>gradient_tex</c> set.</returns>
    /// <remarks>
    ///     Typical use: <see cref="HealthBarForecastSegment.OverlayMaterial" /> so a forecast strip matches the vanilla doom
    ///     look; you can start from <see cref="CreateVanillaDoomBarGradientTexture" /> or a custom <see cref="Gradient" />.
    /// </remarks>
    public static ShaderMaterial CreateDoomBarShaderMaterial(GradientTexture1D gradientTexture)
    {
        ArgumentNullException.ThrowIfNull(gradientTexture);

        var shader = GameDoomBarShader;
        if (shader == null)
            throw new InvalidOperationException($"Failed to load doom bar shader ({DoomBarShaderPath}).");

        var material = new ShaderMaterial { Shader = shader };
        material.SetShaderParameter("noise_tex", VanillaDoomBarNoiseTexture);
        material.SetShaderParameter("gradient_tex", gradientTexture);
        return material;
    }

    /// <summary>
    ///     Gradient texture matching the vanilla doom bar segment in <c>health_bar.tscn</c>.
    /// </summary>
    public static GradientTexture1D CreateVanillaDoomBarGradientTexture()
    {
        var gradient = new Gradient();
        gradient.AddPoint(0f, new Color(0.300863f, 0.162626f, 0.528347f));
        gradient.AddPoint(0.514583f, new Color(0.513726f, 0.254902f, 0.505882f));
        gradient.AddPoint(1f, new Color(0.354657f, 0.0421873f, 0.437114f));
        return new GradientTexture1D { Gradient = gradient };
    }

    /// <summary>
    ///     Noise texture matching <c>health_bar.tscn</c> (Perlin, frequency 0.0383).
    /// </summary>
    public static NoiseTexture2D CreateVanillaDoomBarNoiseTexture()
    {
        var noise = new FastNoiseLite
        {
            NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin,
            Frequency = 0.0383f
        };

        return new NoiseTexture2D { Noise = noise };
    }
}