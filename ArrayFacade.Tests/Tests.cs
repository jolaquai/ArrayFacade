namespace ArrayFacade.Tests;

/// <summary>
/// Tests for HashList factory methods and HashListOptions.
/// </summary>
public class Tests
{
    private static readonly byte[] _sample10, _sample100, _sample1000;
    static Tests()
    {
        _sample10 = new byte[10];
        _sample100 = new byte[100];
        _sample1000 = new byte[1000];

        for (var i = 999; i >= 0; i--)
        {
            _sample1000[i] = (byte)(i % 256);
            if (i < 100)
                _sample100[i] = (byte)i;
            if (i < 10)
                _sample10[i] = (byte)i;
        }
    }

    [Fact]
    public unsafe void ReadFromMemoryStream_ThroughFake_Works()
    {
        using var ms = new MemoryStream(_sample10);
        var size = (int)ArrayFacadeHandle.ComputeMinimumSafeSizeFor<byte>(10);
        var ptr = stackalloc byte[size];

        var read = -1;
        var dataAt = new ArrayFacadeHandle(ptr, size).Use<byte>(10, fake =>
        {
            read = ms.Read(buffer: fake, offset: 0, count: 10);
        });
        Assert.Equal(10, read);

        var readBytes = new ReadOnlySpan<byte>(dataAt, 10).ToArray();
        Assert.Equal(_sample10, readBytes);
    }

    [Fact]
    public unsafe void ReadSyncFileIO_ThroughFake_Works()
    {
        var tempFile = Path.GetTempFileName();
        File.WriteAllBytes(tempFile, _sample10);

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
        Assert.Equal(_sample10, readBytes);
    }
}
