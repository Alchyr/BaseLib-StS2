using System.Reflection;
using System.Reflection.Emit;

namespace BaseLib.Utils.Patching.AsyncMethodSections;

public class AsyncMethodContext
{
    public required ILGenerator Generator;
    public required FieldInfo StateField;
    public required FieldInfo BuilderField;

    public required Type StateMachineType;

    public int NextStateIndex = 0;
}