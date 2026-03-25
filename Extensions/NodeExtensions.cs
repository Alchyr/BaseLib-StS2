using Godot;

namespace BaseLib.Extensions;

public static class NodeExtensions
{
    public static void AddUnique(this Node n, Node child, string? name = null)
    {
        if (name != null) child.Name = name;
        child.UniqueNameInOwner = true;
        n.AddChild(child);
        child.Owner = n;
    }
}