using Godot;

namespace BaseLib.Utils.NodeFactories;

internal class ControlFactory : NodeFactory<Control>
{
    internal ControlFactory() : base([])
    {
    }

    protected override Control CreateBareFromResource(object resource)
    {
        switch (resource)
        {
            case Texture2D img:
                var imgSize = img.GetSize();
                
                var control = new Control()
                {
                    Name = img.ResourcePath
                };

                var visuals = new Sprite2D()
                {
                    Name = "Image",
                    Texture = img,
                    Position = new(-imgSize.X * 0.5f, -imgSize.Y * 0.5f)
                };
                control.AddChild(visuals);

                return control;
        }

        return base.CreateBareFromResource(resource);
    }

    protected override void GenerateNode(Node target, INodeInfo required)
    {
        
    }
}