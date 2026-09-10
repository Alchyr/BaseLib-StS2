namespace BaseLib.Utils;

/// <summary>
/// Utility class for holding a single instance of a type for use as a variable in lambda expressions.
/// </summary>
public class Holder<T>
{
    public T? Val { get; set; }
}