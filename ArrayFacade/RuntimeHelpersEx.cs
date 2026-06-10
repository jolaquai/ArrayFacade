#if NETSTANDARD2_0
using System.Reflection;
#endif

namespace ArrayFacade;

internal static class RuntimeHelpersEx
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsReferenceOrContainsReferences<T>()
#if NET10_0_OR_GREATER
        where T : allows ref struct
#endif
#if NETSTANDARD2_0
        => TypeData<T>.IsReferenceOrContainsReferences;
#else
        => RuntimeHelpers.IsReferenceOrContainsReferences<T>();
#endif
}

/// <summary>
/// Attaches arbitrary data to type <typeparamref name="T"/> statically.
/// </summary>
internal static class TypeData<T>
{
#if NETSTANDARD2_0
    public static readonly bool IsReferenceOrContainsReferences = ComputeIsReferenceOrContainsReferences(typeof(T));

    private static bool ComputeIsReferenceOrContainsReferences(Type t)
    {
        if (t.IsPointer || t.IsByRef)
            return false; // not GC-tracked
        if (!t.IsValueType)
            return true;   // reference type
        if (t.IsPrimitive || t.IsEnum)
            return false; // incl. IntPtr/UIntPtr (IsPrimitive==true)

        var fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        foreach (var field in fields)
            if (ComputeIsReferenceOrContainsReferences(field.FieldType))
                return true;
        return false;
    }
#endif
}