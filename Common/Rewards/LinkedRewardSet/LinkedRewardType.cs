namespace BaseLib.Common.Rewards.LinkedRewardSet;

/// <summary>
/// Reward gain rules for the Linked Reward
/// </summary>
public enum LinkedRewardType
{
    /// <summary>
    /// Do not use
    /// </summary>
    None,
    /// <summary>
    /// You may only choose 1 option
    /// </summary>
    Exclusive,
    /// <summary>
    /// You either choose All or No options
    /// </summary>
    Bundled
}
