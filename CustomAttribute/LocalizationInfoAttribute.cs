namespace BaseLib.CustomAttribute
{
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public class LocalizationInfoAttribute(string language = "eng") : Attribute
    {
        public string Language { get; init; } = language;
    }
}
