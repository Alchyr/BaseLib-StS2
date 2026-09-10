using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;

namespace BaseLib.Utils;


/// <summary>
/// Utility class for defining an <see cref="NVfxParticleSystem"/> in code, representing a set of
/// one shot particle emitters.
/// If using the same visuals repeatedly, it is recommended to define your builder once and call
/// <see cref="Build"/> to create a new instance when necessary.
/// </summary>
public class ParticleSystemBuilder
{
    private static Dictionary<Vector4, CurveTexture> _basicCurveDictionary = [];
    
    private class OneShotParticleData()
    {
        public required Func<Texture2D> Texture { get; set; }
        public int Amount { get; set; } = 1;
        public float Lifetime { get; set; } = 1f;
        
        public event Action<ParticleProcessMaterial>? SetupMaterial;


        public GpuParticles2D Build()
        {
            GpuParticles2D emitter = new();
            emitter.Texture = Texture();
            emitter.Amount = Amount;

            var processMaterial = new ParticleProcessMaterial();
            processMaterial.ParticleFlagDisableZ = true;
            SetupMaterial?.Invoke(processMaterial);
            
            emitter.ProcessMaterial = new ParticleProcessMaterial();
            
            return emitter;
        }
    }

    private List<OneShotParticleData> _particles = [];
    
    public ParticleSystemBuilder AddParticle(string texturePath)
    {
        return AddParticle(() => PreloadManager.Cache.GetTexture2D(texturePath));
    }
    
    public ParticleSystemBuilder AddParticle(Func<Texture2D> baseTexture, int particleCount = 1, float lifetime = 1f,
        Action<ParticleProcessMaterial>? setupParticleProcess = null)
    {
        var particleData = new OneShotParticleData()
        {
            Texture = baseTexture,
            Amount = particleCount,
            Lifetime = lifetime
        };
        particleData.SetupMaterial += setupParticleProcess;
        
        _particles.Add(particleData);
        return this;
    }

    /// <summary>
    /// Sets up a particle to grow and then fade out.
    /// The parameters are used to define a start and end point for a curve for the particle's scale.
    /// </summary>
    /// <param name="initialScale">Recommended to be between 0 and 1.</param>
    /// <param name="curveIn">0 means the curve will start by moving horizontally;
    /// a positive value moves upward initially,
    /// a negative value moves downwards initially.
    /// Almost any value can be used, but values within +-3 are generally enough.</param>
    /// <param name="finalScale">Recommended to be between 0 and 1.</param>
    /// <param name="curveOut">0 means the curve will end by moving horizontally;
    /// a positive value ends the curve by moving upwards,
    /// a negative value ends the curve by moving downwards.
    /// Almost any value can be used, but values within +-3 are generally enough.</param>
    public ParticleSystemBuilder GrowFade(float initialScale = 0.4f, float curveIn = 0.76f, float finalScale = 0.65f, float curveOut = 0f)
    {
        if (_particles.Count == 0)
            throw new InvalidOperationException("Cannot set particle process type without first adding a particle.");
        
        Vector4 curveData = new(initialScale, curveIn, finalScale, curveOut);
        if (!_basicCurveDictionary.TryGetValue(curveData, out var curveTex))
        {
            Curve curve = new();
            curve.AddPoint(new Vector2(0, initialScale), rightTangent: curveIn);
            curve.AddPoint(new Vector2(1, finalScale), leftTangent: curveOut);

            curveTex = new();
            curveTex.Width = 128;
            curveTex.Curve = curve;
                
            _basicCurveDictionary.Add(curveData, curveTex);
        }
        
        var particle = _particles[^1];
        particle.SetupMaterial += processMaterial =>
        {
            processMaterial.Gravity = new(0, 0, 0);
            processMaterial.ScaleCurve = curveTex;
        };
        return this;
    }
    public ParticleSystemBuilder GrowFade(CurveTexture curve)
    {
        if (_particles.Count == 0)
            throw new InvalidOperationException("Cannot set particle process type without first adding a particle.");
        
        var particle = _particles[^1];
        particle.SetupMaterial += processMaterial =>
        {
            processMaterial.Gravity = new(0, 0, 0);
            processMaterial.ScaleCurve = curve;
        };
        return this;
    }

    public NVfxParticleSystem Build()
    {
        NVfxParticleSystem root = new();
        
        foreach (var particle in _particles)
        {
            root.AddChild(particle.Build());
        }

        return root;
    }
}