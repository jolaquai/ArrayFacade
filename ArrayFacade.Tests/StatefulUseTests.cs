namespace ArrayFacade.Tests;

/// <summary>
/// Validates the stateful <c>Use</c> overloads and the public <see cref="ArrayFacadeHandle.Neutralize{T}"/> method.
/// </summary>
public unsafe class StatefulUseTests
{
    // -------------------------------------------------------------------------
    // Use<T, TState>(int, TState, Action<T[], TState>) — value-state overload
    // -------------------------------------------------------------------------

    [Fact]
    public void ValueState_IsPassedToAction()
    {
        const int Len = 4;
        var size = (int)ArrayFacadeHandle.ComputeMinimumSafeSizeFor<int>(Len);
        var buf = stackalloc byte[size];
        var handle = new ArrayFacadeHandle(buf, size);

        // 'static' modifier proves no captures: context arrives via state, not a closure
        var data = handle.Use<int, int>(Len, 0xBEEF, static (fake, state) =>
        {
            for (var i = 0; i < fake.Length; i++)
                fake[i] = state + i;
        });

        Assert.True(data != null);
        for (var i = 0; i < Len; i++)
            Assert.Equal(0xBEEF + i, data[i]);
    }

    [Fact]
    public void ValueState_ZeroLength_PassesRealEmptyArray()
    {
        var buf = stackalloc byte[1];
        var hit = false;
        new ArrayFacadeHandle(buf, 0).Use<byte, int>(0, 42, (fake, state) =>
        {
            hit = true;
            Assert.Empty(fake);
            Assert.Equal(42, state);
        });
        Assert.True(hit);
    }

    [Fact]
    public void ValueState_GuardIsReleasedOnThrow()
    {
        const int Len = 4;
        var size = (int)ArrayFacadeHandle.ComputeMinimumSafeSizeFor<byte>(Len);
        var buf = stackalloc byte[size];
        var handle = new ArrayFacadeHandle(buf, size);

        var caught = false;
        try
        {
            handle.Use<byte, int>(Len, 0, static (_, __) => throw new FormatException("boom"));
        }
        catch (FormatException)
        {
            caught = true;
        }
        Assert.True(caught);

        var ranAgain = false;
        handle.Use<byte, int>(Len, 0, (_, __) => ranAgain = true);
        Assert.True(ranAgain);
    }

    [Fact]
    public void ValueState_FakeIsNeutralizedAfterAction()
    {
        const int Len = 4;
        var size = (int)ArrayFacadeHandle.ComputeMinimumSafeSizeFor<byte>(Len);
        var buf = stackalloc byte[size];

        byte[] leaked = null;
        new ArrayFacadeHandle(buf, size).Use<byte, int>(Len, 0, (fake, _) => leaked = fake);

#pragma warning disable xUnit2013
        Assert.Equal(0, leaked.Length);
#pragma warning restore xUnit2013
    }

    // -------------------------------------------------------------------------
    // Use<T, TState>(int, ref TState, ArrayRefAction<T, TState>) — ref-state overload
    // -------------------------------------------------------------------------

    [Fact]
    public void RefState_IsMutatedInPlace()
    {
        const int Len = 8;
        var size = (int)ArrayFacadeHandle.ComputeMinimumSafeSizeFor<byte>(Len);
        var buf = stackalloc byte[size];
        var handle = new ArrayFacadeHandle(buf, size);

        // static lambda + ref state: no closure, result written back through the ref
        var total = 0;
        handle.Use<byte, int>(Len, ref total, static (fake, ref state) =>
        {
            for (var i = 0; i < fake.Length; i++)
            {
                fake[i] = (byte)(i + 1);
                state += fake[i];
            }
        });

        Assert.Equal(36, total); // 1+2+...+8 = 36
    }

    [Fact]
    public void RefState_ZeroLength_PassesRealEmptyArray()
    {
        var buf = stackalloc byte[1];
        var hit = false;
        var sentinel = 99;
        new ArrayFacadeHandle(buf, 0).Use<byte, int>(0, ref sentinel, (fake, ref state) =>
        {
            hit = true;
            Assert.Empty(fake);
            Assert.Equal(99, state);
            state = 0;
        });
        Assert.True(hit);
        Assert.Equal(0, sentinel); // ref mutation visible to caller
    }

    [Fact]
    public void RefState_GuardIsReleasedOnThrow()
    {
        const int Len = 4;
        var size = (int)ArrayFacadeHandle.ComputeMinimumSafeSizeFor<byte>(Len);
        var buf = stackalloc byte[size];
        var handle = new ArrayFacadeHandle(buf, size);

        var caught = false;
        var dummy = 0;
        try
        {
            handle.Use<byte, int>(Len, ref dummy, static (_, ref __) => throw new FormatException("boom"));
        }
        catch (FormatException)
        {
            caught = true;
        }
        Assert.True(caught);

        var ranAgain = false;
        handle.Use<byte, int>(Len, ref dummy, (_, ref __) => ranAgain = true);
        Assert.True(ranAgain);
    }

    [Fact]
    public void RefState_FakeIsNeutralizedAfterAction()
    {
        const int Len = 4;
        var size = (int)ArrayFacadeHandle.ComputeMinimumSafeSizeFor<byte>(Len);
        var buf = stackalloc byte[size];
        var dummy = 0;

        byte[] leaked = null;
        new ArrayFacadeHandle(buf, size).Use<byte, int>(Len, ref dummy, (fake, ref _) => leaked = fake);

#pragma warning disable xUnit2013
        Assert.Equal(0, leaked.Length);
#pragma warning restore xUnit2013
    }

    // -------------------------------------------------------------------------
    // Neutralize<T>
    // -------------------------------------------------------------------------

    [Fact]
    public void Neutralize_RendersLiveFakeInert()
    {
        const int Len = 8;
        var size = (int)ArrayFacadeHandle.ComputeMinimumSafeSizeFor<byte>(Len);
        var buf = stackalloc byte[size];

        // Stamp without Use's managed scope so Neutralize has a live fake to kill.
        // Factory.FakeArray is accessible via InternalsVisibleTo.
        var fake = Factory.FakeArray<byte>(buf, Len, size);
        Assert.Equal(Len, fake.Length);

        ArrayFacadeHandle.Neutralize(fake);

#pragma warning disable xUnit2013
        Assert.Equal(0, fake.Length);
#pragma warning restore xUnit2013

        var threw = false;
        try { _ = fake[0]; }
        catch (IndexOutOfRangeException) { threw = true; }
        Assert.True(threw);
    }

    [Fact]
    public void Neutralize_NullIsIgnored()
    {
        ArrayFacadeHandle.Neutralize<byte>(null);
    }

    [Fact]
    public void Neutralize_AlreadyZeroLengthIsIgnored()
    {
        const int Len = 4;
        var size = (int)ArrayFacadeHandle.ComputeMinimumSafeSizeFor<byte>(Len);
        var buf = stackalloc byte[size];

        // Use neutralizes; calling Neutralize again on the husk must be a no-op
        byte[] leaked = null;
        new ArrayFacadeHandle(buf, size).Use<byte>(Len, f => leaked = f);
        Assert.Equal(0, leaked.Length);

        ArrayFacadeHandle.Neutralize(leaked); // must not throw or corrupt
        Assert.Equal(0, leaked.Length);
    }
}
