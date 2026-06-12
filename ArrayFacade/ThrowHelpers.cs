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
    public static unsafe void ThrowSizeofRawTooSmall<T>(int len, int sizeofRaw, nuint required) where T : unmanaged => throw new ArgumentException($"The provided size of raw ({sizeofRaw} bytes) is too small to establish an array facade of type '{typeof(T).FullName}' with length {len}. At least {required} bytes are required at this address ({3 * IntPtr.Size + (IntPtr.Size - 1) + (len * sizeof(T))} worst-case for arbitrary alignment).");

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
    public static void ThrowPlatformNotSupported() => throw new PlatformNotSupportedException($"The current runtime does not lay objects out the way {nameof(ArrayFacade)} fabricates them (verified at startup by probing live array instances); array fakes cannot function here and no memory was touched. Only CLR-family runtimes (.NET Framework, .NET Core/.NET) are supported. Check {nameof(ArrayFacadeHandle)}.{nameof(ArrayFacadeHandle.IsSupported)} before use; zero-length use remains valid on any runtime.");
}