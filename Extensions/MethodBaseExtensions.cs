using System.Reflection;
using MegaCrit.Sts2.Core.Extensions;

namespace BaseLib.Extensions;

public static class MethodBaseExtensions
{
    public static int ArgIndex(this MethodBase method, string paramName)
    {
        var paramInfos = method.GetParameters();
        var index = paramInfos.FirstIndex(param => param.Name == paramName);
        if (index == -1)
            throw new ArgumentException($"Failed to find parameter in method {method.Name} with name {paramName}.");
        return index + (method.IsStatic ? 0 : 1);
    }
}