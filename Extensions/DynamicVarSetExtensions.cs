using BaseLib.Cards.Variables;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Extensions;

public static class DynamicVarSetExtensions
{
    extension(DynamicVarSet vars)
    {
        /// <summary>
        /// Get a power var initialized with its default name.
        /// </summary>
        public DynamicVar Power<T>() where T : PowerModel
        {
            return vars[typeof(T).Name];
        }

        /// <summary>
        /// Returns the first variable of the specified type, or a specific
        /// named variable of the specified type, if a name is provided.
        /// If no matching variable is found, an exception is thrown.
        /// </summary>
        public T Var<T>(string? name = null) where T : DynamicVar
        {
            if (name != null)
            {
                if (vars.TryGetValue(name, out var resultVar))
                {
                    if (resultVar is T tResult)
                    {
                        return tResult;
                    }
                    throw new ArgumentException(
                        $"Found dynamic variable of type {resultVar.GetType().Name} instead of type {typeof(T).Name} with name {name}");
                }
                throw new ArgumentException(
                    $"Failed to find dynamic variable of type {typeof(T).Name} with name {name}");
            }

            var maybeResult = vars.Select(entry => entry.Value).OfType<T>().FirstOrDefault();
            if (maybeResult == null)
            {
                throw new ArgumentException($"No dynamic variables of type {typeof(T).Name} found.");
            }
            return maybeResult;
        }
    }
    
    
    /// <summary>
    /// Get the Scry var initialized with its default name.
    /// </summary>
    public static ScryVar Scry(this DynamicVarSet vard)
    {
        return (ScryVar)vard._vars[nameof(Scry)];
    }
    
}