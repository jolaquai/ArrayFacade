using System.Runtime.InteropServices;
using System.Text;

namespace ArrayFacade.Tests;

/// <summary>
/// The actual point of the library: array-only BCL APIs must accept fakes as if they were
/// real arrays, in both source and destination position. Everything here completes
/// synchronously before returning, per the documented contract.
/// </summary>
public unsafe class BclInteropTests
{
    private static readonly byte[] _bytes = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

    [Fact]
    public void MemoryStream_WritesFromFake()
    {
        var size = (int)ArrayFacadeHandle.ComputeMinimumSafeSizeFor<byte>(10);
        var buf = stackalloc byte[size];
        using var ms = new MemoryStream();
        new ArrayFacadeHandle(buf, size).Use<byte>(10, fake =>
        {
            for (var i = 0; i < 10; i++)
                fake[i] = (byte)(i + 1);
            ms.Write(fake, 0, 10);
        });
        Assert.Equal(_bytes, ms.ToArray());
    }

    [Fact]
    public void MemoryStream_ReadsFromFake()
    {
        using var ms = new MemoryStream(_bytes);
        var size = (int)ArrayFacadeHandle.ComputeMinimumSafeSizeFor<byte>(10);
        var ptr = stackalloc byte[size];

        var read = -1;
        var dataAt = new ArrayFacadeHandle(ptr, size).Use<byte>(10, fake =>
        {
            read = ms.Read(buffer: fake, offset: 0, count: 10);
        });
        Assert.Equal(10, read);

        var readBytes = new ReadOnlySpan<byte>(dataAt, 10).ToArray();
        Assert.Equal(_bytes, readBytes);
    }

    [Fact]
    public void FileStream_ReadsFromFake()
    {
        var tempFile = Path.GetTempFileName();
        File.WriteAllBytes(tempFile, _bytes);

        using var fs = new FileStream(tempFile, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.None);

        var size = (int)ArrayFacadeHandle.ComputeMinimumSafeSizeFor<byte>(10);
        var ptr = stackalloc byte[size];

        var read = -1;
        var dataAt = new ArrayFacadeHandle(ptr, size).Use<byte>(10, fake =>
        {
            read = fs.Read(fake, 0, 10);
        });
        Assert.Equal(10, read);

        var readBytes = new ReadOnlySpan<byte>(dataAt, 10).ToArray();
        Assert.Equal(_bytes, readBytes);
    }

    [Fact]
    public void String_CopyTo_And_Ctor_RoundTripThroughCharFake()
    {
        const string S = "ArrayFacade!";
        var size = (int)ArrayFacadeHandle.ComputeMinimumSafeSizeFor<char>(S.Length);
        var buf = stackalloc byte[size];
        new ArrayFacadeHandle(buf, size).Use<char>(S.Length, fake =>
        {
            S.CopyTo(0, fake, 0, S.Length);
            Assert.Equal(S, new string(fake));
            Assert.Equal(S, new string(fake, 0, fake.Length));
        });
    }

    [Fact]
    public void Encoding_WritesIntoAndReadsFromFake()
    {
        const string S = "facade01";
        var size = (int)ArrayFacadeHandle.ComputeMinimumSafeSizeFor<byte>(S.Length);
        var buf = stackalloc byte[size];
        new ArrayFacadeHandle(buf, size).Use<byte>(S.Length, fake =>
        {
            var written = Encoding.ASCII.GetBytes(S, 0, S.Length, fake, 0);
            Assert.Equal(S.Length, written);
            Assert.Equal(S, Encoding.ASCII.GetString(fake, 0, S.Length));
        });
    }

    [Fact]
    public void Convert_ToBase64String_FromFake()
    {
        var size = (int)ArrayFacadeHandle.ComputeMinimumSafeSizeFor<byte>(10);
        var buf = stackalloc byte[size];
        new ArrayFacadeHandle(buf, size).Use<byte>(10, fake =>
        {
            for (var i = 0; i < 10; i++)
                fake[i] = (byte)(i + 1);
            Assert.Equal(Convert.ToBase64String(_bytes), Convert.ToBase64String(fake));
        });
    }

    [Fact]
    public void Marshal_Copy_FromFakeToNative()
    {
        var size = (int)ArrayFacadeHandle.ComputeMinimumSafeSizeFor<byte>(10);
        var buf = stackalloc byte[size];
        var dest = stackalloc byte[10];
        var destAddr = (nint)dest;
        new ArrayFacadeHandle(buf, size).Use<byte>(10, fake =>
        {
            for (var i = 0; i < 10; i++)
                fake[i] = (byte)(i + 1);
            Marshal.Copy(fake, 0, destAddr, 10);
        });
        for (var i = 0; i < 10; i++)
            Assert.Equal((byte)(i + 1), dest[i]);
    }

    [Fact]
    public void ArrayStatics_AcceptFakes()
    {
        var size = (int)ArrayFacadeHandle.ComputeMinimumSafeSizeFor<int>(4);
        var buf = stackalloc byte[size];
        new ArrayFacadeHandle(buf, size).Use<int>(4, fake =>
        {
            fake[0] = 10;
            fake[1] = 20;
            fake[2] = 30;
            fake[3] = 40;

            Assert.Equal(2, Array.IndexOf(fake, 30));

            var real = new int[4];
            fake.CopyTo(real, 0);
            Assert.Equal([10, 20, 30, 40], real);

            var clone = (int[])fake.Clone();
            Assert.Equal([10, 20, 30, 40], clone);

            Assert.Equal(4 * sizeof(int), Buffer.ByteLength(fake));
            var blk = new int[4];
            Buffer.BlockCopy(fake, 0, blk, 0, 4 * sizeof(int));
            Assert.Equal([10, 20, 30, 40], blk);

            Array.Reverse(fake);
            Assert.Equal([40, 30, 20, 10], fake);

            Array.Sort(fake);
            Assert.Equal([10, 20, 30, 40], fake);

            Array.Copy(new[] { 5, 6, 7, 8 }, fake, 4);
            Assert.Equal([5, 6, 7, 8], fake);

            Array.Clear(fake, 0, 4);
            Assert.Equal([0, 0, 0, 0], fake);
        });
    }

    [Fact]
    public void InterfaceAndLinqPaths_ReadFakes()
    {
        var size = (int)ArrayFacadeHandle.ComputeMinimumSafeSizeFor<int>(4);
        var buf = stackalloc byte[size];
        new ArrayFacadeHandle(buf, size).Use<int>(4, fake =>
        {
            fake[0] = 10;
            fake[1] = 20;
            fake[2] = 30;
            fake[3] = 40;

            object o = fake;
            Assert.True(o is IList<int>);
            Assert.True(o is IReadOnlyList<int>);

            var list = (IList<int>)fake;
            Assert.Equal(4, list.Count);
            Assert.Equal(30, list[2]);
            Assert.Equal(3, list.IndexOf(40));
            Assert.Contains(20, (ICollection<int>)fake);

            var direct = 0;
            foreach (var v in fake)
                direct += v;
            Assert.Equal(100, direct);

            var viaGeneric = 0;
            foreach (var v in (IEnumerable<int>)fake)
                viaGeneric += v;
            Assert.Equal(100, viaGeneric);

            var viaWeak = 0;
            foreach (int v in (System.Collections.IEnumerable)fake)
                viaWeak += v;
            Assert.Equal(100, viaWeak);

            Assert.Equal(100, fake.Sum());
            Assert.Equal([10, 20, 30, 40], fake.ToArray());
            Assert.True(fake.SequenceEqual([10, 20, 30, 40]));
        });
    }

    [Fact]
    public void Spans_OverFakes_AliasTheSameMemory()
    {
        var size = (int)ArrayFacadeHandle.ComputeMinimumSafeSizeFor<int>(4);
        var buf = stackalloc byte[size];
        new ArrayFacadeHandle(buf, size).Use<int>(4, fake =>
        {
            fake[0] = 1;
            fake[1] = 2;
            fake[2] = 3;
            fake[3] = 4;

            var span = fake.AsSpan();
            Assert.Equal(4, span.Length);
            Assert.True(span.SequenceEqual(new ReadOnlySpan<int>([1, 2, 3, 4])));
            span[1] = 99;
            Assert.Equal(99, fake[1]);

            var slice = new Span<int>(fake, 2, 2);
            slice[0] = 77;
            Assert.Equal(77, fake[2]);
        });
    }
}
