using BaseLib.Extensions;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace BaseLib.Utils.NodeFactories;

internal class NCreatureVisualsFactory : NodeFactory<NCreatureVisuals>
{
    internal NCreatureVisualsFactory() : base([
        new NodeInfo<Node2D>("%Visuals"),
        new NodeInfo<Node2D>("%PhobiaModeVisuals"),
        new NodeInfo<Control>("Bounds"), //Although it will use uniqueName, NCreature requires fixed path.
        new NodeInfo<Marker2D>("%CenterPos"),
        new NodeInfo<Marker2D>("IntentPos"),
        new NodeInfo<Marker2D>("%OrbPos"),
        new NodeInfo<Marker2D>("%TalkPos")
    ])
    { }

    protected override NCreatureVisuals CreateBareFromResource(object resource)
    {
        switch (resource)
        {
            case Texture2D img:
                MainFile.Logger.Info("Creating NCreatureVisuals from Texture2D");
                
                var imgSize = img.GetSize();
                var boundsSize = img.GetSize() * 1.1f;
            
                var visualsNode = new NCreatureVisuals();
            
                var bounds = new Control();
                visualsNode.AddUnique(bounds, "Bounds");
                bounds.Position = new(-boundsSize.X / 2, -boundsSize.Y);
                bounds.Size = boundsSize;

                var visuals = new Sprite2D();
                visualsNode.AddUnique(visuals, "Visuals");
                visuals.Texture = img;
                visuals.Position = new(0, -imgSize.Y * 0.5f); //Sprite2D position is centered

                return visualsNode;
        }

        return base.CreateBareFromResource(resource);
    }

    protected override void GenerateNode(Node target, INodeInfo required)
    {
        switch (required.Path)
        {
            case "%Bounds":
                var bounds = new Control();
                bounds.Size = new(240, 280);
                bounds.Position = new(-120, -280);
                target.AddUnique(bounds, "Bounds");
                break;
            case "%Visuals":
                MainFile.Logger.Warn("'Visuals' node must be provided for NCreatureVisuals");
                break;
            case "%IntentPos":
                bounds = target.GetNode<Control>("%Bounds");
                
                var intent = new Marker2D();
                target.AddUnique(intent, "IntentPos");
                intent.Position = bounds.Position + (bounds.Size * new Vector2(0.5f, 0f)) + new Vector2(0, -70);
                break;
            case "%CenterPos":
                bounds = target.GetNode<Control>("%Bounds");
                
                var center = new Marker2D();
                target.AddUnique(center, "CenterPos");
                center.Position = bounds.Position + (bounds.Size * new Vector2(0.5f, 0.6f));
                break;
        }
    }
}