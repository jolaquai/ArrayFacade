namespace ArrayFacade.Tests;

/// <summary>
/// Core behavioral matrix: every supported element width round-trips through a fake,
/// the fake aliases the caller's memory bidirectionally, type identity is carried by the
/// stamped MethodTable, and placement holds for every possible misalignment of the backing pointer.
/// </summary>
public unsafe class MatrixTests
{
    private const int Len = 16;

    internal struct Rgb(byte r, byte g, byte b) : IEquatable<Rgb>
    {
        public byte R = r, G = g, B = b;
        public readonly bool Equals(Rgb other) => R == other.R && G == other.G && B == other.B;
        public override readonly bool Equals(object obj) => obj is Rgb other && Equals(other);
        public override readonly int GetHashCode() => R | (G << 8) | (B << 16);
    }
    internal struct PairInt(int x, int y) : IEquatable<PairInt>
    {
        public int X = x, Y = y;
        public readonly bool Equals(PairInt other) => X == other.X && Y == other.Y;
        public override readonly bool Equals(object obj) => obj is PairInt other && Equals(other);
        public override readonly int GetHashCode() => X ^ Y;
    }

    [Fact]
    public void CustomStructSizes_AreWhatTheMatrixAssumes()
    {
        Assert.Equal(3, sizeof(Rgb));
        Assert.Equal(8, sizeof(PairInt));
    }

    [Fact] public void RoundTrip_Byte() => RoundTrip(static i => (byte)(i * 3 + 1));
    [Fact] public void RoundTrip_SByte() => RoundTrip(static i => (sbyte)(-i * 3));
    [Fact] public void RoundTrip_Bool() => RoundTrip(static i => i % 3 == 0);
    [Fact] public void RoundTrip_Short() => RoundTrip(static i => (short)(i * -257));
    [Fact] public void RoundTrip_UShort() => RoundTrip(static i => (ushort)(i * 521));
    [Fact] public void RoundTrip_Char() => RoundTrip(static i => (char)('A' + i));
    [Fact] public void RoundTrip_Int() => RoundTrip(static i => i * -100003);
    [Fact] public void RoundTrip_UInt() => RoundTrip(static i => (uint)i * 0xDEADBEu);
    [Fact] public void RoundTrip_Float() => RoundTrip(static i => i * 0.5f - 3f);
    [Fact] public void RoundTrip_Long() => RoundTrip(static i => i * -0x1_0000_0001L);
    [Fact] public void RoundTrip_ULong() => RoundTrip(static i => (ulong)i * 0xFEED_FACE_CAFEUL);
    [Fact] public void RoundTrip_Double() => RoundTrip(static i => i * 0.25 - 9.5);
    [Fact] public void RoundTrip_NInt() => RoundTrip(static i => (nint)(i * 7919));
    [Fact] public void RoundTrip_3ByteStruct() => RoundTrip(static i => new Rgb((byte)i, (byte)(i * 2), (byte)(i * 3)));
    [Fact] public void RoundTrip_8ByteStruct() => RoundTrip(static i => new PairInt(i, ~i));

    private static void RoundTrip<T>(Func<int, T> factory) where T : unmanaged
    {
        var size = (int)ArrayFacadeHandle.ComputeMinimumSafeSizeFor<T>(Len);
        var buf = stackalloc byte[size];
        var handle = new ArrayFacadeHandle(buf, size);
        var dataAddr = (nint)handle.DataOffset;
        Assert.True(dataAddr != 0);

        var returned = handle.Use<T>(Len, fake =>
        {
            // type identity is carried entirely by the stamped MethodTable
            object o = fake;
            Assert.True(o is T[]);
            Assert.True(o is Array);
            Assert.Equal(typeof(T[]), o.GetType());
            Assert.Equal(1, fake.Rank);
            Assert.Equal(Len, fake.Length);

            for (var i = 0; i < Len; i++)
                fake[i] = factory(i);
            for (var i = 0; i < Len; i++)
                Assert.Equal(factory(i), fake[i]);

            // the fake and the raw pointer must alias the same bytes, in both directions
            ((T*)dataAddr)[0] = factory(Len);
            Assert.Equal(factory(Len), fake[0]);
            fake[0] = factory(0);

            // fixed-pinning is documented as legal and must resolve to the data start
            fixed (T* pinned = fake)
                Assert.True(pinned == (T*)dataAddr);

            // bounds checks read the fabricated Length
            AssertIndexThrows(fake, -1);
            AssertIndexThrows(fake, Len);
        });

        Assert.True(returned != null);
        Assert.True((nint)returned == dataAddr);
        for (var i = 0; i < Len; i++)
            Assert.Equal(factory(i), returned[i]);
    }

    // no Assert.Throws here: that would capture the fake in a closure, which the contract forbids
    private static void AssertIndexThrows<T>(T[] array, int index) where T : unmanaged
    {
        try
        {
            _ = array[index];
        }
        catch (IndexOutOfRangeException)
        {
            return;
        }
        Assert.Fail($"Expected IndexOutOfRangeException for index {index}.");
    }

    public static TheoryData<int> Misalignments()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < IntPtr.Size; i++)
            data.Add(i);
        return data;
    }

    [Theory]
    [MemberData(nameof(Misalignments))]
    public void Use_AtEveryMisalignment_PlacesAndReportsDataCorrectly(int offset)
    {
        const int N = 12;
        var size = (int)ArrayFacadeHandle.ComputeMinimumSafeSizeFor<int>(N);
        var buf = stackalloc byte[size + (2 * IntPtr.Size)];
        var raw = (byte*)AlignUp(buf) + offset;

        var handle = new ArrayFacadeHandle(raw, size);
        var data = handle.Use<int>(N, fake =>
        {
            for (var i = 0; i < N; i++)
                fake[i] = (i * 31) - 7;
        });

        Assert.True(data != null);
        Assert.True((nint)data == (nint)handle.DataOffset);
        // elements must land IntPtr-aligned no matter how 'raw' was misaligned
        Assert.True((nuint)data % (nuint)IntPtr.Size == 0);
        for (var i = 0; i < N; i++)
            Assert.Equal((i * 31) - 7, data[i]);
    }

    private static void* AlignUp(void* p) => (void*)(((nuint)p + (nuint)IntPtr.Size - 1) & ~((nuint)IntPtr.Size - 1));
}
