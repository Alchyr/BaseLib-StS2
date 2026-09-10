using BaseLib.Extensions;
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
            BaseLibMain.Logger.Warn($"Created {target.GetType().FullName} missing required children {string.Join(" ", missing)}");
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


    public class CurveBuilder()
    {
        private bool _locked = false;
        private readonly Curve _curve = new();
        private readonly List<float> _hashValues = [];

        /// <summary>
        /// Retrieve the defined curve.
        /// Once the curve is retrieved, attempting to modify it will throw an exception.
        /// </summary>
        public Curve Curve
        {
            get
            {
                _locked = true;
                return _curve;
            }
        }

        private void CheckLocked()
        {
            if (_locked)
                throw new InvalidOperationException("Curve has already been retrieved and is no longer" +
                                                    "allowed to be modified.");
        }

        /// <summary>
        /// Sets the minimum x coordinate of this curve's points.
        /// Default 0.
        /// </summary>
        public CurveBuilder SetMinDomain(float minDomain)
        {
            CheckLocked();
            _curve.SetMinDomain(minDomain);
            return this;
        }
        /// <summary>
        /// Sets the maximum x coordinate of this curve's points.
        /// Default 1.
        /// </summary>
        public CurveBuilder SetMaxDomain(float maxDomain)
        {
            CheckLocked();
            _curve.SetMaxDomain(maxDomain);
            return this;
        }
        
        /// <summary>
        /// Sets the minimum y coordinate of this curve's points.
        /// Default 0.
        /// </summary>
        public CurveBuilder SetMinVal(float minVal)
        {
            CheckLocked();
            _curve.SetMinValue(minVal);
            return this;
        }
        /// <summary>
        /// Sets the maximum y coordinate of this curve's points.
        /// Default 1.
        /// </summary>
        public CurveBuilder SetMaxVal(float maxVal)
        {
            CheckLocked();
            _curve.SetMaxValue(maxVal);
            return this;
        }

        /// <summary>
        /// Sets the number of points to cache when calculating the curve.
        /// </summary>
        public CurveBuilder SetResolution(int resolution)
        {
            CheckLocked();
            _curve.SetBakeResolution(resolution);
            return this;
        }

        /// <summary>
        /// Adds a point to the curve. An exception is thrown if the x or y values are outside of the curve's
        /// defined domain/values.
        /// </summary>
        public CurveBuilder AddPoint(float x, float y, float leftTangent = 0, float rightTangent = 0, 
            Curve.TangentMode leftMode = Curve.TangentMode.Free, Curve.TangentMode rightMode = Curve.TangentMode.Free)
        {
            CheckLocked();
            
            if (x < _curve.MinDomain || x > _curve.MaxDomain)
                throw new ArgumentOutOfRangeException(nameof(x), $"x {x} outside of curve's range ({_curve.MinDomain}, {_curve.MaxDomain})");
            if (y < _curve.MinValue || y > _curve.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(y), $"y {y} outside of curve's range ({_curve.MinValue}, {_curve.MaxValue})");
            _curve.AddPoint(new Vector2(x, y), leftTangent, rightTangent, leftMode, rightMode);
            _hashValues.Add(x);
            _hashValues.Add(y);
            _hashValues.Add(leftTangent);
            _hashValues.Add(rightTangent);
            _hashValues.Add((int)leftMode);
            _hashValues.Add((int)rightMode);
            return this;
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return ((List<float>)
            [
                _curve.MinDomain, _curve.MaxDomain, _curve.MinValue, _curve.MaxValue,
                .._hashValues
            ]).GetSequenceHashCode();
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            if (obj is null) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj.GetType() == GetType() && Equals((CurveBuilder)obj);
        }
        private bool Equals(CurveBuilder other)
        {
            return GetHashCode() == other.GetHashCode();
        }
    }
}
