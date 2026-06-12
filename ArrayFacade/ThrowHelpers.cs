namespace ArrayFacade;

internal static class ThrowHelpers
{
    [MethodImpl(MethodImplOptions.NoInlining)]
#if !NETSTANDARD2_0
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
#endif
    public static void ThrowLengthNegative() => throw new ArgumentOutOfRangeException("len", "Length cannot be negative.");
    [MethodImpl(MethodImplOptions.NoInlining)]
#if !NETSTANDARD2_0
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
#endif
    public static void ThrowLengthGreaterThanArrayMaxLength() => throw new ArgumentOutOfRangeException("len", "Length cannot be greater than the maximum array length 0x7FFFFFC7 (2147483591).");
    [MethodImpl(MethodImplOptions.NoInlining)]
#if !NETSTANDARD2_0
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
#endif
    public static void ThrowNullPointerRaw() => throw new ArgumentNullException("raw", "Null pointer passed for raw.");
    [MethodImpl(MethodImplOptions.NoInlining)]
#if !NETSTANDARD2_0
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
#endif
    public static unsafe void ThrowSizeofRawTooSmall<T>(int len, int sizeofRaw) where T : unmanaged => throw new ArgumentException($"The provided size of raw ({sizeofRaw} bytes) is too small to establish an array facade of type '{typeof(T).FullName}' with length {len}. At least {4 * IntPtr.Size + (len * sizeof(T))} bytes are required.");

    [MethodImpl(MethodImplOptions.NoInlining)]
#if !NETSTANDARD2_0
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
#endif
    public static void ThrowUnsupportedT(Type type) => throw new NotSupportedException($"Type '{type.FullName}' is not supported for array facades. Only blittable (unmanaged) types are supported.");

    [MethodImpl(MethodImplOptions.NoInlining)]
#if !NETSTANDARD2_0
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
#endif
    public static void ThrowTAlignmentUnsupported(Type type) => throw new NotSupportedException($"Type '{type.FullName}' does not have a supported alignment. {nameof(ArrayFacade)} mirrors the BCL's array alignment guarantees; alignment requirements > 8 are not supported.");

    [MethodImpl(MethodImplOptions.NoInlining)]
#if !NETSTANDARD2_0
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
#endif
    public static void ThrowAttemptedAliasing() => throw new InvalidOperationException($"An attempt was made to call {nameof(ArrayFacadeHandle)}.{nameof(ArrayFacadeHandle.Use)} on the same handle while a call to that method was already in-flight. Memory aliasing through two separate array fakes is not allowed.");
    [MethodImpl(MethodImplOptions.NoInlining)]
#if !NETSTANDARD2_0
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
#endif
    internal static void ThrowNotOnStack() => throw new ArgumentOutOfRangeException("raw", "The pointer is not on the stack.");
}