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
    [InlineData(typeof(decimal))] // even an unsupported (>8 byte) element type: length 0 is always free
    [InlineData(typeof(Guid))]
    public void ZeroLength_IsAlwaysFreeAndTypeIndependent(Type type)
    {
        // A length-0 fake is never stamped, so it needs no backing memory and never consults
        // element-type support. Same answer for every T, supported or not.
        Assert.Equal((nuint)0, ArrayFacadeHandle.ComputeMinimumSafeSizeFor(type, 0));
    }
}