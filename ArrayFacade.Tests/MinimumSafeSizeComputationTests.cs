namespace ArrayFacade.Tests;

public partial class MinimumSafeSizeComputationTests
{
    [Theory]
    [InlineData(typeof(byte))]
    [InlineData(typeof(short))]
    [InlineData(typeof(int))]
    [InlineData(typeof(long))]
    [InlineData(typeof(float))]
    [InlineData(typeof(double))]
    public void ZeroLength_IsNotTypeDependent(Type type)
    {
        var size = ArrayFacadeHandle.ComputeMinimumSafeSizeFor(type, 0);
        Assert.Equal(3 * (nuint)IntPtr.Size, size);
    }
}