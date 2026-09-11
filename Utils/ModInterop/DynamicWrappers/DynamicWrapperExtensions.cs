using System.Collections;

namespace BaseLib.Utils.ModInterop.DynamicWrappers;

/// <summary>
/// Defines extension methods for use with DynamicWrappers
/// </summary>
public static class DynamicWrapperExtensions
{
    /// <summary>
    /// Extension methods for <see cref="IEnumerable"/>
    /// </summary>
    /// <typeparam name="TReturn">The type of elements to filter by.</typeparam>
    /// <param name="enumerable">The sequence of elements to filter.</param>
    extension<TReturn>(IEnumerable enumerable)
    {
        /// <summary>
        /// Returns all elements in the sequence that are either of type <typeparamref name="TReturn"/>, or implement <see cref="IWrappable"/> and can be wrapped into a <typeparamref name="TReturn"/> for <paramref name="targetModId"/>.
        /// </summary>
        /// <remarks>Intended for use with combatState?.IterateHookListeners().OfTypeDynamic&lt;T&gt;()</remarks>
        /// <param name="targetModId">The modId to wrap elements for, if able.</param>
        /// <returns>A new sequence filtered to elements of type <typeparamref name="TReturn"/>.</returns>
        public IEnumerable<TReturn> OfTypeDynamic(string targetModId)
        {
            ArgumentNullException.ThrowIfNull(enumerable);

            foreach (object item in enumerable)
            {
                if (item is TReturn t)
                {
                    yield return t;
                }
                else if (DynamicWrapper.TryWrap(targetModId, item, out TReturn? wrapper))
                {
                    yield return wrapper;
                }
            }
        }
    }
}