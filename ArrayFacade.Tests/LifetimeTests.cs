namespace ArrayFacade.Tests;

/// <summary>
/// Lifetime, reuse, and post-mortem semantics: the in-flight guard must release on every
/// exit path, neutralized fakes must be inert-but-loud, and fakes must survive forced
/// compacting collections mid-action (the entire premise of the library).
/// </summary>
public unsafe class LifetimeTests
{
    [Fact]
    public void Handle_IsReusableSequentially_AcrossElementTypes()
    {
        var size = (int)ArrayFacadeHandle.ComputeMinimumSafeSizeFor<double>(8);
        var buf = stackalloc byte[size];
        var handle = new ArrayFacadeHandle(buf, size);

        var observed = 0.0;
        handle.Use<byte>(8, static a => a[0] = 1);
        handle.Use<int>(8, static a => a[0] = 2);
        handle.Use<double>(8, a =>
        {
            a[0] = 3.5;
            observed = a[0];
        });
        Assert.Equal(3.5, observed);
    }

    [Fact]
    public void SequentialReuse_ReinterpretsDataInPlace()
    {
        const int ByteLen = 64;
        var size = (int)ArrayFacadeHandle.ComputeMinimumSafeSizeFor<byte>(ByteLen);
        var buf = stackalloc byte[size];
        var handle = new ArrayFacadeHandle(buf, size);

        // distinct, non-periodic byte pattern so any offset/aliasing error shows up
        var pattern = new byte[ByteLen];
        for (var i = 0; i < ByteLen; i++)
            pattern[i] = (byte)((i * 7) + 3);

        var bytesAt = handle.Use<byte>(ByteLen, fake =>
        {
            for (var i = 0; i < ByteLen; i++)
                fake[i] = (byte)((i * 7) + 3);
        });

        // same handle, same memory, new element type: the data is reinterpreted in place
        var intsAt = handle.Use<int>(ByteLen / sizeof(int), fake =>
        {
            for (var i = 0; i < fake.Length; i++)
                Assert.Equal(BitConverter.ToInt32(pattern, i * sizeof(int)), fake[i]);
        });

        var ulongsAt = handle.Use<ulong>(ByteLen / sizeof(ulong), fake =>
        {
            for (var i = 0; i < fake.Length; i++)
                Assert.Equal(BitConverter.ToUInt64(pattern, i * sizeof(ulong)), fake[i]);
        });

        // "zero-work" is literal: element 0 of every fake sits at the same address
        // regardless of T, so nothing was copied or moved between the calls
        Assert.True((nint)bytesAt == (nint)intsAt);
        Assert.True((nint)intsAt == (nint)ulongsAt);
    }

    [Fact]
    public void ZeroLength_RunsActionWithRealEmptyArray_AndReleasesTheHandle()
    {
        var size = (int)ArrayFacadeHandle.ComputeMinimumSafeSizeFor<byte>(8);
        var buf = stackalloc byte[size];
        var handle = new ArrayFacadeHandle(buf, size);

        var hit = false;
        handle.Use<byte>(0, a =>
        {
            hit = true;
            Assert.NotNull(a);
            Assert.Empty(a);
        });
        Assert.True(hit);

        // the in-flight guard must be released by a zero-length call too
        var second = false;
        handle.Use<byte>(8, a =>
        {
            a[0] = 1;
            second = true;
        });
        Assert.True(second);
    }

    [Fact]
    public void Handle_IsReleased_WhenActionThrows()
    {
        var size = (int)ArrayFacadeHandle.ComputeMinimumSafeSizeFor<byte>(8);
        var buf = stackalloc byte[size];
        var handle = new ArrayFacadeHandle(buf, size);

        var caught = false;
        try
        {
            handle.Use<byte>(8, static _ => throw new FormatException("boom"));
        }
        catch (FormatException)
        {
            caught = true;
        }
        Assert.True(caught);

        var ranAgain = false;
        handle.Use<byte>(8, _ => ranAgain = true);
        Assert.True(ranAgain);
    }

    [Fact]
    public void Handle_IsReleased_WhenZeroLengthActionThrows()
    {
        var size = (int)ArrayFacadeHandle.ComputeMinimumSafeSizeFor<byte>(8);
        var buf = stackalloc byte[size];
        var handle = new ArrayFacadeHandle(buf, size);

        var caught = false;
        try
        {
            handle.Use<byte>(0, static _ => throw new FormatException("boom"));
        }
        catch (FormatException)
        {
            caught = true;
        }
        Assert.True(caught);

        var ranAgain = false;
        handle.Use<byte>(8, _ => ranAgain = true);
        Assert.True(ranAgain);
    }

    [Fact]
    public void Handle_IsReleased_WhenValidationFails()
    {
        var size = (int)ArrayFacadeHandle.ComputeMinimumSafeSizeFor<byte>(8);
        var buf = stackalloc byte[size];
        var handle = new ArrayFacadeHandle(buf, size);

        var caught = false;
        try
        {
            handle.Use<byte>(-1, static _ => { });
        }
        catch (ArgumentOutOfRangeException)
        {
            caught = true;
        }
        Assert.True(caught);

        var ranAgain = false;
        handle.Use<byte>(8, _ => ranAgain = true);
        Assert.True(ranAgain);
    }

    [Fact]
    public void NeutralizedFake_IsInertButFaultsLoudly()
    {
        const int Len = 8;
        var size = (int)ArrayFacadeHandle.ComputeMinimumSafeSizeFor<byte>(Len);
        var buf = stackalloc byte[size];

        // Deliberate contract violation: the reference escapes the action. The backing
        // stackalloc is still this frame's live memory, so inspecting the husk is safe
        // here (and only here) — this pins the documented post-neutralization behavior.
        byte[] leaked = null;
        new ArrayFacadeHandle(buf, size).Use<byte>(Len, f =>
        {
            for (var i = 0; i < Len; i++)
                f[i] = 0xAB;
            leaked = f;
        });

        // intentionally probing the .Length field itself (the neutralization contract), not emptiness-by-enumeration
#pragma warning disable xUnit2013
        Assert.Equal(0, leaked.Length);
#pragma warning restore xUnit2013

        var threw = false;
        try
        {
            _ = leaked[0];
        }
        catch (IndexOutOfRangeException)
        {
            threw = true;
        }
        Assert.True(threw);

        var iterated = 0;
        foreach (var _ in leaked)
            iterated++;
        Assert.Equal(0, iterated);

        var dest = new byte[Len];
        leaked.CopyTo(dest, 0); // length 0 → no-op
        Assert.All(dest, static b => Assert.Equal((byte)0, b));
    }

    [Fact]
    public void Fake_SurvivesForcedCompactingGC_MidAction()
    {
        const int Len = 32;
        var size = (int)ArrayFacadeHandle.ComputeMinimumSafeSizeFor<long>(Len);
        var buf = stackalloc byte[size];
        new ArrayFacadeHandle(buf, size).Use<long>(Len, fake =>
        {
            for (var i = 0; i < Len; i++)
                fake[i] = i * 1234567L;

            // churn the heap so the collection actually moves things
            var garbage = new object[256];
            for (var i = 0; i < garbage.Length; i++)
                garbage[i] = new byte[256];

            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.KeepAlive(garbage);

            Assert.Equal(Len, fake.Length);
            for (var i = 0; i < Len; i++)
                Assert.Equal(i * 1234567L, fake[i]);
        });
    }
}
