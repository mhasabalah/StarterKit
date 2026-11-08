using System.Reflection;
using System.Reflection.Emit;

namespace Api.Tests.Architecture;

/// <summary>
/// A minimal IL reader for the architecture tests. It lists every method a type calls (<c>call</c> / <c>callvirt</c> /
/// <c>newobj</c> / <c>ldftn</c>), including calls inside its lambdas, local functions and async state machines (nested
/// compiler-generated types).
/// </summary>
internal static class IlCalls
{
    private const BindingFlags AllDeclared =
        BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(o => o.Value);

    public static IEnumerable<MethodBase> AllCalls(Type type) =>
        SelfAndNested(type)
            .SelectMany(t => t.GetMethods(AllDeclared).Cast<MethodBase>().Concat(t.GetConstructors(AllDeclared)))
            .SelectMany(CalledMethods);

    private static IEnumerable<Type> SelfAndNested(Type type) =>
        new[] { type }.Concat(type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).SelectMany(SelfAndNested));

    private static IEnumerable<MethodBase> CalledMethods(MethodBase method)
    {
        byte[]? il;
        try
        {
            il = method.GetMethodBody()?.GetILAsByteArray();
        }
        catch
        {
            yield break;
        }

        if (il is null)
            yield break;

        var position = 0;
        while (position < il.Length)
        {
            short value = il[position++];
            if (value == 0xFE && position < il.Length)
                value = unchecked((short)(0xFE00 | il[position++]));

            if (!OpCodesByValue.TryGetValue(value, out var opCode))
                yield break; // unknown opcode: stop rather than misread operands

            if (opCode.OperandType == OperandType.InlineMethod)
            {
                var token = BitConverter.ToInt32(il, position);
                MethodBase? target = null;
                try
                {
                    target = method.Module.ResolveMethod(
                        token,
                        method.DeclaringType?.IsGenericType == true ? method.DeclaringType.GetGenericArguments() : null,
                        method.IsGenericMethod ? method.GetGenericArguments() : null);
                }
                catch
                {
                    // Unresolvable (e.g. open generic context) - ignore.
                }

                if (target is not null)
                    yield return target;
            }

            position += OperandSize(opCode, il, position);
        }
    }

    private static int OperandSize(OpCode opCode, byte[] il, int position) => opCode.OperandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, position),
        _ => 4
    };
}
