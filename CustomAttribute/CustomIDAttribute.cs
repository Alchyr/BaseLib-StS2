namespace BaseLib.CustomAttribute
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public class CustomIDAttribute(string? Id = null) : Attribute
    {
        public string? ID { get; init; } = Id;
    }
}
