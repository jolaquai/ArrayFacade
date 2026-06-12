using System.Collections.Concurrent;
using System.Reflection;

namespace ArrayFacade;

internal static class Helpers
{

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static nuint AlignUp(nuint value, nuint alignment)
    {
        var a = alignment - 1;
        return (value + a) & ~a;
    }

    public static unsafe void CheckTypeSupport<T>() where T : unmanaged
    {
        var type = typeof(T);
        var size = sizeof(T);
        if (size > 8)
        {
            ThrowHelpers.ThrowTAlignmentUnsupported(type);
            return;
        }
    }
    private static readonly MethodInfo _checkTypeSupportBase = typeof(Helpers)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .First(static m => m.Name == nameof(CheckTypeSupport) && m.IsGenericMethodDefinition);

    private static readonly ConcurrentDictionary<Type, Action> _checkFns = new();

    public static void CheckTypeSupport(Type type)
    {
        if (type == null)
            throw new ArgumentNullException("type");
        _checkFns.GetOrAdd(type, static t =>
            (Action)Delegate.CreateDelegate(typeof(Action),
                _checkTypeSupportBase.MakeGenericMethod(t)))();
    }
}