using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;

namespace BaseLib.Utils;

public class CustomBackgroundAssets : BackgroundAssets
{
    private static readonly MethodInfo BackgroundScenePathSetter =
        AccessTools.PropertySetter(typeof(BackgroundAssets), nameof(BackgroundScenePath));
    private static readonly MethodInfo FgLayerSetter =
        AccessTools.PropertySetter(typeof(BackgroundAssets), nameof(FgLayer));

    private const string FakeKey = "glory";
    
    public CustomBackgroundAssets() : base(FakeKey, Rng.Chaotic) {
        BgLayers.Clear();
    }
    
    /// <summary>
    /// Loads a set of assets from a provided directory for layers and path to a background scene.
    /// </summary>
    /// <param name="layersPath">Path to a directory containing assets with names containing _fg_ or _bg_
    /// and ending with a number, each of which should be an individual scene.
    /// _fg_ scenes can be named in any way.  _bg_ assets should be split into
    /// numbered layers such as myact_bg_00_a.tscn, myact_bg_01_a.tscn, myact_bg_01_b.tscn
    /// being two numbered sets, 00 and 01.
    /// A single random foreground scene will be chosen, and a random asset from each numbered layer will be chosen from
    /// the background scenes.
    /// </param>
    /// <param name="bgScenePath">.tscn file that will be a constant background.</param>
    /// <param name="rng">The rng passed to a method where this is called. In ActModel, GenerateBackgroundAssets.</param>
    /// <exception cref="InvalidOperationException"></exception>
    public CustomBackgroundAssets(string layersPath, string bgScenePath, Rng rng) : this()
    {
        var bgLayers = new Dictionary<string, List<string>>();
        var stringList = new List<string>();
        
        foreach (var asset in ResourceLoader.ListDirectory(layersPath))
        {
            if (asset == null) continue;
            
            if (asset.Contains("_fg_"))
            {
                stringList.Add($"{layersPath}/{asset}");
            }
            else
            {
                var key = asset.Contains("_bg_")
                    ? asset.Split("_bg_")[1].Split("_")[0]
                    : throw new InvalidOperationException("files must either contain '_fg_' or '_bg_'");
                if (!bgLayers.ContainsKey(key))
                    bgLayers.Add(key, []);
                bgLayers[key].Add($"{layersPath}/{asset}");
            }
        }
        
        BackgroundScenePathSetter.Invoke(this, [bgScenePath]);
        BgLayers.AddRange(SelectRandomBackgroundAssetLayers(rng, bgLayers));
        FgLayerSetter.Invoke(this, [SelectRandomForegroundAssetLayer(rng, stringList)]);
    }
    
    /// <summary>
    /// Use a fixed set of assets as background assets.
    /// All paths should be .tscn files.
    /// </summary>
    /// <param name="backgroundScenePath"></param>
    /// <param name="backgroundLayers"></param>
    /// <param name="foregroundLayer"></param>
    public CustomBackgroundAssets(string backgroundScenePath, List<string> backgroundLayers, string foregroundLayer) : this()
    {
        BackgroundScenePathSetter.Invoke(this, [backgroundScenePath]);
        BgLayers.AddRange(backgroundLayers);
        FgLayerSetter.Invoke(this, [foregroundLayer]);
    }
    
    /// <summary>
    /// Use a random subset of a fixed set of assets as background assets.
    /// All paths should be .tscn files.
    /// </summary>
    /// <param name="backgroundScenePath"></param>
    /// <param name="backgroundLayers">Set of layers; one random item will be taken from each layer.</param>
    /// <param name="foregroundLayer">One random .tscn will be used for the foreground layer.</param>
    public CustomBackgroundAssets(string backgroundScenePath, IEnumerable<IEnumerable<string>> backgroundLayers, IEnumerable<string> foregroundLayers, Rng rng) : this()
    {
        BackgroundScenePathSetter.Invoke(this, [backgroundScenePath]);
        BgLayers.AddRange(backgroundLayers.Select(layer => rng.NextItem(layer)!).ToList());
        FgLayerSetter.Invoke(this, [SelectRandomForegroundAssetLayer(rng, foregroundLayers)]);
    }
}