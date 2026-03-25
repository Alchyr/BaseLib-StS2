using BaseLib.Utils.NodeFactories;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace BaseLib.Utils;

public static class GodotUtils
{
    /// <summary>
    /// Creatures an NCreatureVisuals from an image.
    /// </summary>
    /// <param name="path">Filepath to an image that can be loaded as a Texture2D.</param>
    /// <returns></returns>
    [Obsolete("Use NodeFactory<NCreatureVisuals>.CreateFromResource instead.")]
    public static NCreatureVisuals CreatureVisualsFromImage(string path)
    {
        if (!ResourceLoader.Exists(path))
            throw new Exception("$Attempted to create NCreatureVisuals from path that doesn't exist {path}");
            
        var img = PreloadManager.Cache.GetTexture2D(path);
        return NodeFactory<NCreatureVisuals>.CreateFromResource(img);
    }
    
    [Obsolete("Use NodeFactory<NCreatureVisuals>.CreateFromScene instead.")]
    public static NCreatureVisuals CreatureVisualsFromScene(string path)
    {
        return NodeFactory<NCreatureVisuals>.CreateFromScene(path);
    }

    public static T TransferAllNodes<T>(this T obj, string sourceScene, params string[] uniqueNames) where T : Node
    {
        var target = PreloadManager.Cache.GetScene(sourceScene).Instantiate();
        var missing = TransferNodes(obj, target, uniqueNames);
        if (missing.Count > 0)
        {
            MainFile.Logger.Warn($"Created {target.GetType().FullName} missing required children {string.Join(" ", missing)}");
        }
        return obj;
    }
    

    private static List<string> TransferNodes(Node target, Node source, params string[] names)
    {
        return TransferNodes(target, source, true, names);
    }
    private static List<string> TransferNodes(Node target, Node source, bool uniqueNames, params string[] names)
    {
        target.Name = source.Name;

        /*if (target is Control targetControl && source is Control sourceControl)
        {
            transfer node properties?
        }*/

        List<string> requiredNames = [.. names];
        foreach (var child in source.GetChildren())
        {
            source.RemoveChild(child);
            if (requiredNames.Remove(child.Name) && uniqueNames) child.UniqueNameInOwner = true;
            target.AddChild(child);
            child.Owner = target;

            SetChildrenOwner(target, child);
        }

        source.QueueFree();
        return requiredNames;
    }

    private static void SetChildrenOwner(Node target, Node child)
    {
        foreach (var grandchild in child.GetChildren())
        {
            grandchild.Owner = target;
            SetChildrenOwner(target, grandchild);
        }
    }
}
