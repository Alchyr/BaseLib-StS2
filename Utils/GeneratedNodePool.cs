using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Pooling;

namespace BaseLib.Utils;

/// <summary>
/// Utility class for adding custom poolables to <seealso cref="NodePool"/>
/// specifically for poolables with a custom generation method rather than using a scene file
/// </summary>
public class GeneratedNodePool
{
    private static Dictionary<Type, INodePool>? _pools;
    internal static readonly Variant NameStr = Variant.CreateFrom("name");
    internal static readonly Variant CallableStr = Variant.CreateFrom("callable");
    internal static readonly Variant SignalStr = Variant.CreateFrom("signal");

    public static GeneratedNodePool<T> Init<T>(Func<T> constructor, int prewarmCount) where T : Node, IPoolable
    {
        var typeFromHandle = typeof(T);
        
        _pools ??= (Dictionary<Type, INodePool>?)AccessTools.DeclaredField(typeof(NodePool), "_pools").GetValue(null);

        if (_pools == null)
        {
            throw new Exception("Failed to access _pools from NodePool");
        }

        if (_pools.TryGetValue(typeFromHandle, out _))
        {
            throw new InvalidOperationException($"Tried to init GeneratedNodePool for type {typeof(T)} but it's already initialized!");
        }

        GeneratedNodePool<T> nodePool = new(constructor, prewarmCount);
        _pools[typeFromHandle] = nodePool;
        return nodePool;
    }
}


public class GeneratedNodePool<T> : INodePool where T : Node, IPoolable
{

    private readonly Func<T> _constructor;

    private readonly List<T> _freeObjects = new List<T>();
    private readonly HashSet<T> _usedObjects = new HashSet<T>();

    public IReadOnlyList<T> DebugFreeObjects => _freeObjects;

    public GeneratedNodePool(Func<T> constructor, int prewarmCount = 0)
    {
        _constructor = constructor;
        for (int i = 0; i < prewarmCount; i++)
        {
            _freeObjects.Add(Instantiate());
        }
    }

    IPoolable INodePool.Get()
    {
        return Get();
    }

    void INodePool.Free(IPoolable poolable)
    {
        Free((T)poolable);
    }

    public T Get()
    {
        T val;
        if (_freeObjects.Count > 0)
        {
            List<T> freeObjects = _freeObjects;
            val = freeObjects[^1];
            _freeObjects.RemoveAt(_freeObjects.Count - 1);
        }
        else
        {
            val = Instantiate();
        }

        _usedObjects.Add(val);
        val.OnReturnedFromPool();
        return val;
    }

    public void Free(T obj)
    {
        if (!_usedObjects.Contains(obj))
        {
            if (_freeObjects.Contains(obj))
            {
                Log.Error($"Tried to free object {obj} ({obj.GetType()}) back to pool {typeof(GeneratedNodePool<T>)} but it's already been freed!");
            }
            else
            {
                Log.Error($"Tried to free object {obj} ({obj.GetType()}) back to pool {typeof(GeneratedNodePool<T>)} but it's not part of the pool!");
            }

            obj.QueueFreeSafelyNoPool();
        }
        else
        {
            DisconnectIncomingAndOutgoingSignals(obj);
            _usedObjects.Remove(obj);
            _freeObjects.Add(obj);
            obj.OnFreedToPool();
        }
    }

    private T Instantiate()
    {
        T val = _constructor();
        val.OnInstantiated();
        return val;
    }

    private void DisconnectIncomingAndOutgoingSignals(Node obj)
    {
        foreach (Godot.Collections.Dictionary signal4 in obj.GetSignalList())
        {
            StringName signal = signal4[GeneratedNodePool.NameStr].AsStringName();
            foreach (Godot.Collections.Dictionary signalConnection in obj.GetSignalConnectionList(signal))
            {
                Callable callable = signalConnection[GeneratedNodePool.CallableStr].AsCallable();
                Signal signal2 = signalConnection[GeneratedNodePool.SignalStr].AsSignal();
                DisconnectSignal(callable, signal2);
            }
        }

        foreach (Godot.Collections.Dictionary incomingConnection in obj.GetIncomingConnections())
        {
            Callable callable2 = incomingConnection[GeneratedNodePool.CallableStr].AsCallable();
            Signal signal3 = incomingConnection[GeneratedNodePool.SignalStr].AsSignal();
            DisconnectSignal(callable2, signal3);
        }

        for (int i = 0; i < obj.GetChildCount(); i++)
        {
            DisconnectIncomingAndOutgoingSignals(obj.GetChild(i));
        }
    }

    private void DisconnectSignal(Callable callable, Signal signal)
    {
        GodotObject? target = callable.Target;
        if (target == null && callable.Method == null)
        {
            return;
        }

        var name = signal.Name;
        var node = target as Node;
        if (node != null && !node.IsInsideTree()) return;
        
        var owner = signal.Owner;
        var node2 = owner as Node;
        if (node != null && node.HasSignal(name) && node.IsConnected(name, callable))
        {
            node.Disconnect(name, callable);
        }
        else if (node2 != null && node2.HasSignal(name) && node2.IsConnected(name, callable))
        {
            node2.Disconnect(name, callable);
        }
    }
}
