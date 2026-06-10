namespace ArrayFacade;

/// <summary>
/// Provides factory methods for the underlying array fakes.
/// </summary>
/// <remarks>
/// Assume that ANYTHING leaving this class is NOT a real array. Using it as such will (in most cases) lead to undefined behavior at best and serious, application-wide memory and GC corruption at worst.
/// </remarks>
internal static class Factory
{
    /// <summary>
    /// Establishes memory that can be treated as an array of <typeparamref name="T"/> starting at the memory pointed to by <paramref name="raw"/>.
    /// 
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="raw"></param>
    /// <param name="len"></param>
    /// <param name="sizeofRaw"></param>
    /// <returns></returns>
    internal static unsafe T[] FakeArray<T>(void* raw, int len, nuint sizeofRaw) where T : unmanaged // reserve >= 3*IntPtr.Size + (IntPtr.Size-1) + len*sizeof(T) at raw
    {
        var size = (nuint)IntPtr.Size;
        if (len < 0 || sizeofRaw < 3 * size + (size - 1) + (nuint)(uint)len * (nuint)sizeof(T))
            ThrowSizeofRawTooSmall<T>(len, sizeofRaw);

        var o = ((nuint)raw + size + (size - 1)) & ~(size - 1);
        *(nint*)(o - size) = 0;
        *(nint*)o = typeof(T[]).TypeHandle.Value;
        *(int*)(o + size) = len;
        return Unsafe.As<nuint, T[]>(ref o);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
#if !NETSTANDARD2_0
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
#endif
    private static void ThrowUnsupportedT(Type type) => throw new NotSupportedException($"Type '{type.FullName}' is not supported for array facades. Only blittable (unmanaged) types are supported.");
    [MethodImpl(MethodImplOptions.NoInlining)]
#if !NETSTANDARD2_0
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
#endif
    private static void ThrowSizeofRawTooSmall<T>(int len, nuint sizeofRaw) => throw new ArgumentException($"The provided size of raw ({sizeofRaw} bytes) is too small to establish an array facade of type '{typeof(T).FullName}' with length {len}. At least {4 * IntPtr.Size + (len * Unsafe.SizeOf<T>())} bytes are required.");

    static unsafe byte[] FakeArray(byte* raw, int len) // reserve >= 4*IntPtr.Size + len at raw
    {
        var size = (nuint)IntPtr.Size;
        var o = ((nuint)raw + size + (size - 1)) & ~(size - 1); // IntPtr-aligned, header room before
        *(nint*)(o - size) = 0;                         // sync block
        *(nint*)o = typeof(byte[]).TypeHandle.Value;    // MethodTable*
        *(int*)(o + size) = len;                        // length
        return Unsafe.As<nuint, byte[]>(ref o);
    }
}
