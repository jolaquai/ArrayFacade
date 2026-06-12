using System.Numerics;
using System.Runtime.Intrinsics;

namespace ArrayFacade.Tests;

// This file is only compiled for the net11.0 TFM

public partial class MinimumSafeSizeComputationTests
{
    [Theory]
    [InlineData(typeof(Vector256<byte>))]
    [InlineData(typeof(Vector512<byte>))]
    [InlineData(typeof(Matrix4x4))]
    [InlineData(typeof(Matrix3x2))]
    [InlineData(typeof(Vector<byte>))]
    [InlineData(typeof(decimal))]
    [InlineData(typeof(Int128))]
    public void UnsupportedType_Throws(Type type)
    {
        Assert.Throws<NotSupportedException>(() => (nuint)ArrayFacadeHandle.ComputeMinimumSafeSizeFor(type, 0));
    }
}
