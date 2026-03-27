using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;

namespace BaseLib.Utils.NodeFactories;

/// <summary>
/// Factory for producing instances of scene scripts that are normally inaccessible in Godot editor when modding.
/// Will convert a given scene and nodes within the scene into valid types for target scene if it is possible to do so.
/// </summary>
/// <typeparam name="T"></typeparam>
public abstract class NodeFactory<T> : NodeFactory where T : Node, new()
{
    private static NodeFactory<T>? _instance;
    
    protected NodeFactory(IEnumerable<INodeInfo> namedNodes) : base(namedNodes)
    {
        _instance = this;
        MainFile.Logger.Info($"Created node factory for {typeof(T).Name}.");
    }

    public static T CreateFromResource(object resource)
    {
        if (_instance == null) throw new Exception($"No node factory found for type '{typeof(T).FullName}'");
        MainFile.Logger.Info($"Creating {typeof(T).Name} from resource {resource.GetType().Name}");
        var n = _instance.CreateBareFromResource(resource);
        _instance.ConvertScene(n, null);
        return n;
    }

    /// <summary>
    /// Create a root node, using resource in node creation.
    /// The root node's name is recommended to be set based on the given resource.
    /// This root node will them be passed to ConvertScene.
    /// </summary>
    /// <param name="resource"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    protected virtual T CreateBareFromResource(object resource)
    {
        throw new Exception($"Node factory for {typeof(T).Name} does not support generation from resource type {resource.GetType().Name}");
    }

    public static T CreateFromScene(string scenePath)
    {
        return CreateFromScene(PreloadManager.Cache.GetScene(scenePath));
    }
    public static T CreateFromScene(PackedScene scene)
    {
        if (_instance == null) throw new Exception($"No node factory found for type '{typeof(T).FullName}'");
        
        MainFile.Logger.Info($"Creating {typeof(T).Name} from scene {scene.ResourcePath}");
        var n = scene.Instantiate();
        if (n is T t) return t;
        
        //Attempt conversion.
        var node = new T();

        _instance.ConvertScene(node, n);
        
        return node;
    }

    /// <summary>
    /// Convert the root node. If there are additional properties to copy from the root node, that should be done here.
    /// </summary>
    /// <param name="target"></param>
    /// <param name="source"></param>
    protected virtual void ConvertScene(T target, Node? source)
    {
        if (source != null)
        {
            //Copy (some) root node properties. Ideally nothing would be missed, but that would require too many specific checks.
            //This method can be overriden if necessary.
            target.Name = source.Name;
            
            switch (target)
            {
                case Control targetControl when source is Control sourceControl:
                    CopyControlProperties(targetControl, sourceControl);
                    break;
                case CanvasItem targetItem when source is CanvasItem sourceItem:
                    CopyCanvasItemProperties(targetItem, sourceItem);
                    break;
            }
        }
        TransferAndCreateNodes(target, source);
    }

    protected virtual void TransferAndCreateNodes(T target, Node? source)
    {
        if (source != null)
        {
            if (FlexibleStructure)
            {
                //All named nodes use unique names, therefore exact paths are not important to match.
                target.AddChild(source);
                source.Owner = target;
                SetChildrenOwner(target, source);
            }
            else
            {
                //Transfer all nodes and set their owners.
                foreach (var child in source.GetChildren())
                {
                    source.RemoveChild(child);
                    target.AddChild(child);
                    child.Owner = target;
                    SetChildrenOwner(target, child);
                }
            
                source.QueueFree();
            }
        }
            
        //Verify existence of/create named nodes
        List<INodeInfo> uniqueNames = [];
        Node placeholder = new();
        foreach (var named in _namedNodes)
        {
            if (named.UniqueName) uniqueNames.Add(named);
            else
            {
                var node = target.GetNodeOrNull(named.Path);
                if (node != null)
                {
                    if (!named.IsValidType(node))
                    {
                        node.ReplaceBy(placeholder);
                        node = ConvertNodeType(node, named.NodeType());
                        placeholder.ReplaceBy(node);
                    }

                    if (named.MakeNameUnique)
                    {
                        node.UniqueNameInOwner = true;
                        node.Owner = target;
                    }
                }
                else
                {
                    GenerateNode(target, named);
                }
            }
        }
        placeholder.QueueFree();

        //Check all children for possible valid unique names
        foreach (var child in target.GetChildrenRecursive<Node>())
        {
            for (var index = 0; index < uniqueNames.Count; index++)
            {
                var unique = uniqueNames[index];
                if (!unique.IsValidUnique(child)) continue;
                
                child.UniqueNameInOwner = true;
                child.Owner = target;
                uniqueNames.Remove(unique);
                break;
            }
        }

        foreach (var missing in uniqueNames)
        {
            GenerateNode(target, missing);
        }
    }

    /// <summary>
    /// This method should convert the given node into the target type, or call the base method if unsupported.
    /// The given node should either be freed or incorporated as a child of the generated node.
    /// </summary>
    /// <param name="node"></param>
    /// <param name="targetType"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    protected virtual Node ConvertNodeType(Node node, Type targetType)
    {
        throw new InvalidOperationException(
            $"Node factory for {typeof(T).Name} does not support conversion of {node.GetType().Name} '{node.Name}' to {targetType.Name}");
    }

    /// <summary>
    /// Generate a new instance of the specified node type as a child of target based on the INodeInfo given.
    /// This method is used called when a named node is not found in the provided scene,
    /// which will be most named nodes if a scene is built from a resource.
    /// Optional nodes may be ignored.
    /// Required nodes that are unsupported should throw an exception.
    /// </summary>
    /// <param name="target"></param>
    /// <param name="required"></param>
    protected abstract void GenerateNode(Node target, INodeInfo required);
}

public abstract class NodeFactory
{
    public static void Init()
    {
        new ControlFactory();
        new NCreatureVisualsFactory();
        new NEnergyCounterFactory();
    }
    
    protected interface INodeInfo
    {
        string Path { get; }
        bool UniqueName { get; }
        bool MakeNameUnique { get; }
        bool IsValidType(Node node);
        bool IsValidUnique(Node n);
        Type NodeType();
    }
    protected record NodeInfo<T>(string Path, bool MakeNameUnique = true) : INodeInfo
    {
        public bool UniqueName { get; init; } = Path.StartsWith('%');
        public StringName StringName { get; init; } = new(Path.StartsWith('%') ? Path[1..] : Path);

        public bool IsValidType(Node node)
        {
            return node is T;
        }

        public bool IsValidUnique(Node n)
        {
            if (!UniqueName) return false;
            return n is T && n.Name.Equals(StringName);
        }

        public Type NodeType()
        {
            return typeof(T);
        }
    }
    
    /// <summary>
    /// Nodes that will be looked for in the generated type.
    /// Not all of these are necessarily required.
    /// </summary>
    protected readonly List<INodeInfo> _namedNodes;
    
    /// <summary>
    /// If true, then will simply add entire root node of a scene as child of a new instance of target scene type.
    /// Otherwise, will need to replace root node.
    /// </summary>
    protected readonly bool FlexibleStructure;
    
    protected NodeFactory(IEnumerable<INodeInfo> namedNodes)
    {
        _namedNodes = namedNodes.ToList();
        FlexibleStructure = _namedNodes.All(info => info.UniqueName);
    }
    
    
    protected static void CopyControlProperties(Control target, Control source)
    {
        CopyCanvasItemProperties(target, source);
        target.LayoutMode = source.LayoutMode;
        target.AnchorLeft = source.AnchorLeft;
        target.AnchorTop = source.AnchorTop;
        target.AnchorRight = source.AnchorRight;
        target.AnchorBottom = source.AnchorBottom;
        target.OffsetLeft = source.OffsetLeft;
        target.OffsetTop = source.OffsetTop;
        target.OffsetRight = source.OffsetRight;
        target.OffsetBottom = source.OffsetBottom;
        target.GrowHorizontal = source.GrowHorizontal;
        target.GrowVertical = source.GrowVertical;
        target.Size = source.Size;
        target.CustomMinimumSize = source.CustomMinimumSize;
        target.PivotOffset = source.PivotOffset;
        target.MouseFilter = source.MouseFilter;
        target.FocusMode = source.FocusMode;
        target.ClipContents = source.ClipContents;
    }

    protected static void CopyCanvasItemProperties(CanvasItem target, CanvasItem source)
    {
        target.Visible = source.Visible;
        target.Modulate = source.Modulate;
        target.SelfModulate = source.SelfModulate;
        target.ShowBehindParent = source.ShowBehindParent;
        target.TopLevel = source.TopLevel;
        target.ZIndex = source.ZIndex;
        target.ZAsRelative = source.ZAsRelative;
        target.YSortEnabled = source.YSortEnabled;
        target.TextureFilter = source.TextureFilter;
        target.TextureRepeat = source.TextureRepeat;
        target.Material = source.Material;
        target.UseParentMaterial = source.UseParentMaterial;

        if (target is Node2D targetNode2D && source is Node2D sourceNode2D)
        {
            targetNode2D.Position = sourceNode2D.Position;
            targetNode2D.Rotation = sourceNode2D.Rotation;
            targetNode2D.Scale = sourceNode2D.Scale;
            targetNode2D.Skew = sourceNode2D.Skew;
        }
    }

    protected static void SetChildrenOwner(Node target, Node child)
    {
        foreach (var grandchild in child.GetChildren())
        {
            grandchild.Owner = target;
            SetChildrenOwner(target, grandchild);
        }
    }
}