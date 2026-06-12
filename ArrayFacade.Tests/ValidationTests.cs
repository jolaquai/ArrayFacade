using System.Runtime.CompilerServices;

namespace ArrayFacade.Tests;

/// <summary>
/// Input validation and size math: the size formula, its Type-based twin, the exact
/// accept/reject boundary of FakeArray (including the misalignment interplay), and
/// rejection of unsupported element types.
/// </summary>
public unsafe class ValidationTests
{
    // 16 bytes — only its size matters, hence the unused fields
#pragma warning disable CS0649
    private struct TooBig
    {
        public long A, B;
    }
#pragma warning restore CS0649

    [Fact]
    public void ComputeMinimumSafeSizeFor_MatchesWorstCaseFormula()
    {
        Assert.Equal((nuint)(IntPtr.Size - 1 + (3 * IntPtr.Size) + 10), ArrayFacadeHandle.ComputeMinimumSafeSizeFor<byte>(10));
        Assert.Equal((nuint)(IntPtr.Size - 1 + (3 * IntPtr.Size) + (5 * sizeof(double))), ArrayFacadeHandle.ComputeMinimumSafeSizeFor<double>(5));
        Assert.Equal((nuint)0, ArrayFacadeHandle.ComputeMinimumSafeSizeFor<long>(0)); // length 0 is free
        Assert.Equal((nuint)(IntPtr.Size - 1 + (3 * IntPtr.Size) + sizeof(long)), ArrayFacadeHandle.ComputeMinimumSafeSizeFor<long>(1)); // but length 1 still reserves header + pad
    }

    [Fact]
    public void ComputeMinimumSafeSizeFor_TypeOverload_AgreesWithGeneric()
    {
        Assert.Equal(ArrayFacadeHandle.ComputeMinimumSafeSizeFor<byte>(33), ArrayFacadeHandle.ComputeMinimumSafeSizeFor(typeof(byte), 33));
        Assert.Equal(ArrayFacadeHandle.ComputeMinimumSafeSizeFor<int>(33), ArrayFacadeHandle.ComputeMinimumSafeSizeFor(typeof(int), 33));
        Assert.Equal(ArrayFacadeHandle.ComputeMinimumSafeSizeFor<double>(33), ArrayFacadeHandle.ComputeMinimumSafeSizeFor(typeof(double), 33));
    }

    [Fact]
    public void ComputeMinimumSafeSizeFor_RejectsBadArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(static () => ArrayFacadeHandle.ComputeMinimumSafeSizeFor<byte>(-1));
        Assert.Throws<ArgumentOutOfRangeException>(static () => ArrayFacadeHandle.ComputeMinimumSafeSizeFor<byte>(0x7FFFFFC8));
        Assert.Throws<ArgumentNullException>(static () => ArrayFacadeHandle.ComputeMinimumSafeSizeFor(null, 1));
    }

    [Fact]
    public void TypesWiderThan8Bytes_AreRejected()
    {
        Assert.Throws<NotSupportedException>(static () => ArrayFacadeHandle.ComputeMinimumSafeSizeFor<decimal>(1));
        Assert.Throws<NotSupportedException>(static () => ArrayFacadeHandle.ComputeMinimumSafeSizeFor<Guid>(1));
        Assert.Throws<NotSupportedException>(static () => ArrayFacadeHandle.ComputeMinimumSafeSizeFor<TooBig>(1));
        Assert.Throws<NotSupportedException>(static () => ArrayFacadeHandle.ComputeMinimumSafeSizeFor(typeof(decimal), 1));

        var ex = Record.Exception(static () =>
        {
            var buf = stackalloc byte[128];
            new ArrayFacadeHandle(buf, 128).Use<decimal>(2, static _ => { });
        });
        Assert.IsType<NotSupportedException>(ex);
    }

    [Fact]
    public void UnsupportedType_AtLengthZero_IsAllowedEverywhere()
    {
        // The consolidated rule: length 0 is always free and never consults type support.
        Assert.Equal((nuint)0, ArrayFacadeHandle.ComputeMinimumSafeSizeFor<decimal>(0));
        Assert.Equal((nuint)0, ArrayFacadeHandle.ComputeMinimumSafeSizeFor(typeof(decimal), 0));

        var ran = false;
        var buf = stackalloc byte[1];
        new ArrayFacadeHandle(buf, 0).Use<decimal>(0, a =>
        {
            Assert.Empty(a);
            ran = true;
        });
        Assert.True(ran);
    }

    [Fact]
    public void IsSupported_IsTrueOnEveryRuntimeInTheTestMatrix()
    {
        // Every cell of the CI matrix is a CLR-family runtime, so the startup layout
        // probe must pass. On a non-CLR runtime (Mono, IL2CPP) this is the assert that
        // fails first — by design, instead of memory corruption later.
        Assert.True(ArrayFacadeHandle.IsSupported);
    }

    [Fact]
    public void Use_RejectsNullPointer()
    {
        var ex = Record.Exception(static () => new ArrayFacadeHandle(null, 64).Use<byte>(1, static _ => { }));
        Assert.IsType<ArgumentNullException>(ex);
    }

    [Fact]
    public void Use_RejectsBadLengths()
    {
        var buf = stackalloc byte[64];
        var bufAddr = (nint)buf;

        var negative = Record.Exception(() => new ArrayFacadeHandle((void*)bufAddr, 64).Use<byte>(-1, static _ => { }));
        Assert.IsType<ArgumentOutOfRangeException>(negative);

        var huge = Record.Exception(() => new ArrayFacadeHandle((void*)bufAddr, 64).Use<byte>(0x7FFFFFC8, static _ => { }));
        Assert.IsType<ArgumentOutOfRangeException>(huge);
    }

    [Fact]
    public void Use_SizeBoundary_IsExactForAlignedPointers()
    {
        const int N = 4;
        var exact = (3 * IntPtr.Size) + (N * sizeof(int));
        var buf = stackalloc byte[exact + (2 * IntPtr.Size)];
        var aligned = (nint)AlignUp(buf);

        // exactly enough: succeeds
        var ran = false;
        new ArrayFacadeHandle((void*)aligned, exact).Use<int>(N, a =>
        {
            a[N - 1] = 7;
            ran = true;
        });
        Assert.True(ran);

        // one byte short: rejected before any memory is touched
        var ex = Record.Exception(() => new ArrayFacadeHandle((void*)aligned, exact - 1).Use<int>(N, static _ => { }));
        Assert.IsType<ArgumentException>(ex);
    }

    [Fact]
    public void Use_SizeBoundary_AccountsForMisalignment()
    {
        const int N = 4;
        var exact = (3 * IntPtr.Size) + (N * sizeof(int));
        var buf = stackalloc byte[exact + (3 * IntPtr.Size)];
        var misaligned = (nint)AlignUp(buf) + 1;

        // 'exact' suffices for an aligned pointer but NOT for one misaligned by 1
        var ex = Record.Exception(() => new ArrayFacadeHandle((void*)misaligned, exact).Use<int>(N, static _ => { }));
        Assert.IsType<ArgumentException>(ex);

        // the worst-case-padded size always works
        var padded = (int)ArrayFacadeHandle.ComputeMinimumSafeSizeFor<int>(N);
        var ran = false;
        new ArrayFacadeHandle((void*)misaligned, padded).Use<int>(N, a =>
        {
            a[0] = 1;
            ran = true;
        });
        Assert.True(ran);
    }

    [Fact]
    public void DataOffset_IsNullWhenThereIsNoRoomForData()
    {
        var buf = stackalloc byte[3 * IntPtr.Size];

        var headerOnly = new ArrayFacadeHandle(buf, 3 * IntPtr.Size);
        Assert.True(headerOnly.DataOffset == null);
        Assert.True(Unsafe.IsNullRef(ref headerOnly.DataOffsetRef));

        var negativeSize = new ArrayFacadeHandle(buf, -5);
        Assert.True(negativeSize.DataOffset == null);
    }

    private static void* AlignUp(void* p) => (void*)(((nuint)p + (nuint)IntPtr.Size - 1) & ~((nuint)IntPtr.Size - 1));
}
