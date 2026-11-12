using BaseLib.Extensions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;

namespace BaseLib.Utils;

public class OptionPools
{
    private WeightedList<AncientOption>[] _pools; 
    
    /// <summary>
    /// Constructor for an ancient's options that uses a separate pool for each option.
    /// </summary>
    public OptionPools(WeightedList<AncientOption> pool1, WeightedList<AncientOption> pool2, WeightedList<AncientOption> pool3)
    {
        _pools = [pool1, pool2, pool3];
    }
    
    /// <summary>
    /// Constructor for an ancient's options that uses one pool for its first two options and a second pool for its third option.
    /// </summary>
    public OptionPools(WeightedList<AncientOption> pool12, WeightedList<AncientOption> pool3)
    {
        _pools = [pool12, pool12, pool3];
    }

    /// <summary>
    /// Constructor for an ancient's options using one pool for all three options.
    /// </summary>
    /// <remarks>
    /// The same pool object is stored in all three slots, so the <c>pool != _pools[1]</c> guards in
    /// <see cref="Roll"/> never re-filter and all three rolls draw from the one <c>rollPool</c>.
    /// <c>GetRandom(rng, true)</c> removes each pick, so the three options are drawn
    /// <b>without replacement</b> — a pool of N options yields three distinct ones, and a pool of
    /// fewer than three eligible options cannot fill the set (see <see cref="Roll"/>).
    /// This is deliberate, not a copy-paste of the three-pool case.
    /// </remarks>
    public OptionPools(WeightedList<AncientOption> pool)
    {
        _pools = [pool, pool, pool];
    }
    
    public IEnumerable<AncientOption> AllOptions => _pools.SelectMany(pool => pool);
    
    public List<AncientOption> Roll(Rng rng, AncientEventModel ancient)
    {
        List<AncientOption> result = [];

        var pool = _pools[0];
        WeightedList<AncientOption> rollPool = [..pool.Where(option => option.ModelForOption.RelicCanSpawnAtCustomAncient(ancient))];

        // WeightedList.GetRandom throws IndexOutOfRangeException on an empty list, and the filter
        // above can empty a small pool entirely. This does NOT make an under-provisioned ancient
        // work: fewer than three eligible options is a mod-authoring error, and there is no
        // non-arbitrary way to invent the missing ones. What changes is the failure mode — a bare
        // IndexOutOfRangeException from inside WeightedList becomes a message that names the
        // ancient and the filter that emptied it.
        result.Add(TakeOrThrow(rollPool, rng, ancient));

        if (pool != _pools[1])
        {
            pool = _pools[1];
            rollPool = [..pool.Where(option => option.ModelForOption.RelicCanSpawnAtCustomAncient(ancient))];
        }
        result.Add(TakeOrThrow(rollPool, rng, ancient));

        if (pool != _pools[2])
        {
            pool = _pools[2];
            rollPool = [..pool.Where(option => option.ModelForOption.RelicCanSpawnAtCustomAncient(ancient))];
        }
        result.Add(TakeOrThrow(rollPool, rng, ancient));

        return result;
    }

    private static AncientOption TakeOrThrow(WeightedList<AncientOption> pool, Rng rng, AncientEventModel ancient)
    {
        if (pool.Count == 0)
            throw new InvalidOperationException(
                $"Ancient '{ancient.Id}' has fewer eligible options than the 3 it must present " +
                $"(after filtering by RelicCanSpawnAtCustomAncient). Add more options or loosen the filter.");

        // A non-empty pool can still be unrollable: WeightedList.GetRandom calls rng.NextInt(0) when
        // every weight is 0 and then throws "Roll 0 failed to get a value in list of total weight 0",
        // a message that never names the ancient. Zero weight is a legal declaration but such an
        // option can never be selected, so this is an authoring error and must name the ancient the
        // same way the empty-pool case above does. Sum in long so the check cannot overflow itself.
        var totalWeight = pool.Sum(option => (long)option.Weight);
        if (totalWeight <= 0)
            throw new InvalidOperationException(
                $"Ancient '{ancient.Id}' has {pool.Count} eligible option(s) with a total weight of " +
                $"{totalWeight}; at least one of them must weigh more than 0 before it can be rolled.");

        return pool.GetRandom(rng, true);
    }
}