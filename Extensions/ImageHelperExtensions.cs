using System.Reflection;

namespace BaseLib.Extensions;

public static class ImageHelperExtensions
{
    // TODO: Bug alch about switching to c# 14 and .NET-10 for baselib dev
    // would be nice to use the extension(ImageHelper imageHelper) syntax and define static methods

    /// <summary>
    /// Tries to get the image for a given path, including the mod name
    /// </summary>
    /// <param name="innerPath">The path of the .png or otherwise file, without the ModName/images/ section</param>
    /// <param name="type">Optional parameter for a type from the desired assembly</param>
    /// <example>
    /// For example:\n
    /// In a mod assembly called MyMod.dll
    /// <code>
    /// public static string MyPath => GetModImagePath("ui/reward_screen/card_transform_reward.png")
    /// </code>
    /// results in <c>MyPath = "res://MyMod/images/ui/reward_screen/card_transform_reward.png"</c>
    /// </example>
    public static string GetModImagePath(string innerPath, Type type = null)
    {
        if (innerPath.StartsWith('/'))
        {
            string text = innerPath;
            innerPath = text[1..]; // range shorthand?
        }
        // is Assembly.GetCallingAssembly() safe/reliable?
        return "res://" + (type != null ? type.GetRootNamespace() : Assembly.GetCallingAssembly().GetName().Name) + "/images/" + innerPath;
    }
}
