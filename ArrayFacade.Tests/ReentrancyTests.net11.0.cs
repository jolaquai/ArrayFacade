using System.Runtime.CompilerServices;

namespace ArrayFacade.Tests;

/// <summary>
/// The in-flight aliasing guard. Re-entering Use on the same handle is only reachable by
/// smuggling the handle's address past the ref-struct capture rules, which needs the
/// 'allows ref struct' Unsafe surface — hence net11.0-only (see the csproj Compile Remove).
/// </summary>
public unsafe class ReentrancyTests
{
    [Fact]
    public void Use_WhileInFlight_ThrowsAndRecovers()
    {
        var size = (int)ArrayFacadeHandle.ComputeMinimumSafeSizeFor<byte>(4);
        var buf = stackalloc byte[size];
        var handle = new ArrayFacadeHandle(buf, size);
        var handleAddr = (nint)Unsafe.AsPointer(ref handle);

        var sawGuard = false;
        handle.Use<byte>(4, _ =>
        {
            try
            {
                Unsafe.AsRef<ArrayFacadeHandle>((void*)handleAddr).Use<byte>(4, static __ => { });
            }
            catch (InvalidOperationException)
            {
                sawGuard = true;
            }
        });
        Assert.True(sawGuard);

        // the guard must be released once the outer call completes
        var ranAgain = false;
        handle.Use<byte>(4, _ => ranAgain = true);
        Assert.True(ranAgain);
    }
}
