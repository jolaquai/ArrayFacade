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
    /// Whether the current runtime lays objects out the way this library stamps fakes.
    /// Determined once, by probing live array instances with reads only — nothing is stamped.
    /// </summary>
    internal static readonly bool IsSupported;

    static Factory()
    {
        IsSupported = VerifyRuntimeLayout();
    }

    /// <summary>
    /// Verifies, using only reads of real arrays, that this runtime matches the fabricated model:
    /// an SZ array reference points at the MethodTable* slot, its int32 length lives exactly one
    /// pointer past the reference, and its data exactly two. Runtimes with a different object model
    /// (Mono, IL2CPP) fail the first check; a future CLR layout change fails loudly here instead of
    /// corrupting silently.
    /// </summary>
    private static unsafe bool VerifyRuntimeLayout()
    {
        try
        {
            var probe1 = new byte[17];
            var probe2 = new byte[23];
            fixed (byte* data1 = probe1)
            fixed (byte* data2 = probe2)
            {
                var objRef1 = Unsafe.As<byte[], nint>(ref probe1);
                var objRef2 = Unsafe.As<byte[], nint>(ref probe2);

                // data must start exactly 2 words past the reference
                if ((nint)data1 - objRef1 != 2 * IntPtr.Size || (nint)data2 - objRef2 != 2 * IntPtr.Size)
                    return false;
                // the int32 length must live exactly 1 word past the reference
                if (*(int*)(objRef1 + IntPtr.Size) != 17 || *(int*)(objRef2 + IntPtr.Size) != 23)
                    return false;
                // the type identity word must be stable across instances of the same T
                return *(nint*)objRef1 == *(nint*)objRef2;
            }
        }
        catch
        {
            // a runtime exotic enough to throw on the probes is a runtime we don't support
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ObjectHeader
    {
        public nint SyncBlock;
        public nint MethodTablePointer;
        public int Length;
    }

    /// <summary>
    /// Establishes memory that can be treated as an array of <typeparamref name="T"/> starting at the memory pointed to by <paramref name="raw"/>, plus alignment padding upward, plus space for the fake's object header (starting at the sync block).
    /// </summary>
    internal static unsafe T[] FakeArray<T>(void* raw, int len, int sizeofRaw) where T : unmanaged // reserve >= 3*IntPtr.Size + (IntPtr.Size-1) + len*sizeof(T) at raw
    {
        // Mirror ComputeMinimumSafeSizeFor: validate length range, satisfy length 0 for any T
        // on any runtime with a real [] (no stamp, no checks), then validate runtime layout
        // and type support for length > 0.
        if (len < 0)
        {
            ThrowHelpers.ThrowLengthNegative();
            return null;
        }
        if (len > 0x7FFFFFC7)
        {
            ThrowHelpers.ThrowLengthGreaterThanArrayMaxLength();
            return null;
        }
        if (len == 0)
            return [];

        if (!IsSupported)
        {
            ThrowHelpers.ThrowPlatformNotSupported();
            return null;
        }

        Helpers.CheckTypeSupport<T>();

        if ((nuint)raw == 0)
        {
            ThrowHelpers.ThrowNullPointerRaw();
            return null;
        }

        var size = (nuint)IntPtr.Size;
        var o = Helpers.AlignUp((nuint)raw, size);
        var alignDiff = o - (nuint)raw;

        var required = alignDiff + 3 * (nuint)IntPtr.Size + (nuint)sizeof(T) * (nuint)len;
        if ((uint)sizeofRaw < required)
        {
            ThrowHelpers.ThrowSizeofRawTooSmall<T>(len, sizeofRaw, required);
            return null;
        }

        return FakeArrayTrusted<T>((void*)o, len);
    }

    /// <summary>
    /// Caches the MethodTable pointer of <typeparamref name="T"/>[], read from the header of a live probe instance.
    /// On .NET Framework, <c>typeof(T[]).TypeHandle.Value</c> is a tagged ArrayTypeDesc*, NOT the MethodTable*,
    /// so it must never be stamped into an object header; reading a real array's header works on every runtime.
    /// </summary>
    private static class ArrayMethodTable<T> where T : unmanaged
    {
        public static readonly nint Value = Read();
        private static unsafe nint Read()
        {
            var probe = new T[1];
            fixed (T* _ = probe)
            {
                return *(nint*)Unsafe.As<T[], nint>(ref probe);
            }
        }
    }

    /// <summary>
    /// Establishes memory that can be treated as an array of <typeparamref name="T"/> starting exactly at <paramref name="raw"/>, assuming alignment and enough space for the fake's object header guarantees (the sync block is established at <paramref name="raw"/>).
    /// Violating any of the constraints will probably yield an unusable array.
    /// </summary>
    internal static unsafe T[] FakeArrayTrusted<T>(void* raw, int len) where T : unmanaged
    {
        var size = (nuint)IntPtr.Size;
        ref var header = ref Unsafe.AsRef<ObjectHeader>(raw);
        header.SyncBlock = 0;
        header.MethodTablePointer = ArrayMethodTable<T>.Value;
        header.Length = len;
        var objRef = (nint)Unsafe.AsPointer(ref header.MethodTablePointer);
        return Unsafe.As<nint, T[]>(ref objRef);
    }

    internal static unsafe void Neutralize<T>(T[] array) where T : unmanaged
    {
        // Length is the only thing we need to nuke
        // .CopyTo/.Length/foreach become no-ops, indexing throws
        // GC tracing reads .Length == and does nothing since there's not data to track
        var objRef = Unsafe.As<T[], nint>(ref array);
        ref var header = ref Unsafe.AsRef<ObjectHeader>((void*)(objRef - IntPtr.Size));
        header.Length = 0;
    }
}
