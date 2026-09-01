using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Utils.ModInterop.DynamicWrappers;

public static class DynamicWrapperExtensions
{
    /// <summary>
    /// Extension methods for <see cref="IEnumerable{T}"/>
    /// </summary>
    /// <typeparam name="TSource">The type of elements in the sequence.</typeparam>
    /// <typeparam name="TReturn">The type of elements to filter by.</typeparam>
    /// <param name="enumerable">The sequence of elements to filter.</param>
    extension<TSource, TReturn>(IEnumerable<TSource> enumerable)
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

            foreach (TSource item in enumerable)
            {
                if (item is TReturn t)
                {
                    yield return t;
                }
                else if (item is IWrappable wrappable && wrappable.TargetModId == targetModId)
                {
                    if (DynamicWrapper.InteropLookup.TryGetValue(new(targetModId, wrappable.InterfaceType), out InteropData data))
                    {
                        yield return DynamicWrapper.Wrap<TReturn>(targetModId, wrappable, data.TargetInterface); // typeof(TReturn) == data.TargetInterface
                    }
                }
            }
        }
    }

    // Provide a dedicated extension for this, so you can call it with only a single generic type (identical to OfType<T>). It does not accept a TReturn parameter so compiler cant infer TReturn.
    /// <summary>
    /// Extension methods for <see cref="IEnumerable{T}"/>
    /// </summary>
    /// <typeparam name="TReturn">The type of elements to filter by.</typeparam>
    /// <param name="enumerable">The sequence of elements to filter.</param>
    extension<TReturn>(IEnumerable<AbstractModel> enumerable)
    {
        /// <inheritdoc cref="OfTypeDynamic{TSource, TReturn}(IEnumerable{TSource}, string)"/>
        public IEnumerable<TReturn> OfTypeDynamic(string targetModId)
        {
            return OfTypeDynamic<AbstractModel, TReturn>(enumerable, targetModId);
        }
    }
}