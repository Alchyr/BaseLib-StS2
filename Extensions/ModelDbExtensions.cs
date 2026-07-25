using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Extensions;

public static class ModelDbExtensions
{
    //Will require language version set to 14 to use as an extension method.
    extension(ModelDb)
    {
        /// <summary>
        /// Obtains a specific CardModifier.
        /// </summary>
        public static T CardModifier<T>() where T : CardModifier
        {
            return ModelDb.CardModifier<T>(false);
        }
        
        /// <summary>
        /// Obtains a specific CardModifier. By default, will return a mutable clone.
        /// </summary>
        public static T CardModifier<T>(bool mutableClone = true) where T : CardModifier
        { 
            T mod = ModelDb.Get<T>();
            if (mutableClone)
            {
                return (T) mod.MutableClone();
            }

            return mod;
        }
    }
}