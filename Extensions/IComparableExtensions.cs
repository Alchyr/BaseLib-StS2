namespace BaseLib.Extensions;

public static class IComparableExtensions
{
    extension<T> (T a) where T : IComparable<T>
    {
        public bool GreaterThan(T b)
        {
            return a.CompareTo(b) > 0;
        }
        public bool GreaterThanOrEqual(T b)
        {
            return a.CompareTo(b) >= 0;
        }
        public bool LessThan(T b)
        {
            return a.CompareTo(b) < 0;
        }
        public bool LessThanOrEqual(T b)
        {
            return a.CompareTo(b) <= 0;
        }
    }
}