using System.Collections.Concurrent;
using System.Reflection;

namespace ArrayFacade;

/// <summary>
/// Creates a wrapper for a raw pointer to memory that can be treated as arrays of <see langword="unmanaged"/> types.
/// </summary>
/// <param name="raw">A pointer to the memory to be treated as an array. Must be writable and kept alive for the duration of the wrapper's use.</param>
/// <param name="sizeofRaw">The size (in bytes) that <paramref name="raw"/> points at. Must be large enough to house the array fake's manufactured object header as well as the elements of any fake created through this handle.</param>
public unsafe ref struct ArrayFacadeHandle(void* raw, int sizeofRaw)
{
    /// <summary>
    /// Gets a <see langword="void"/> pointer to the first <see langword="byte"/> of the data portion of the array fake (that is, where the first element would be).
    /// This may be a null pointer if there is no space at that location.
    /// </summary>
    public readonly void* DataOffset
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (3 * IntPtr.Size >= sizeofRaw)
                return default;
            // Mirror Factory.FakeArray: the header is stamped at AlignUp(raw), not at raw itself
            var data = Helpers.AlignUp((nuint)raw, (nuint)IntPtr.Size) + 3 * (nuint)IntPtr.Size;
            return data >= (nuint)raw + (nuint)(uint)sizeofRaw ? default : (void*)data;
        }
    }

    /// <summary>
    /// Gets a <see langword="ref"/> to the first <see langword="byte"/> of the data portion of the array fake (that is, where the first element would be).
    /// This may be a null <see langword="ref"/> if there is no space at that location.
    /// </summary>
    public readonly ref byte DataOffsetRef
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            var off = DataOffset;
            return ref off == default ? ref Unsafe.NullRef<byte>() : ref Unsafe.AsRef<byte>(off);
        }
    }

    private int call;

    /// <summary>
    /// Creates an array fake and executes an <see cref="Action{T}"/> that is passed a reference to that fake.
    /// </summary>
    /// <typeparam name="T">The element type of the array fake. Must be an unmanaged type.</typeparam>
    /// <param name="length">The number of elements of type <typeparamref name="T"/> that the array fake should report as its <see cref="Array.Length"/>. Must be non-negative.</param>
    /// <param name="action">The action to execute, which is passed a reference to the array fake. Extracting that reference anywhere outside the scope of the <paramref name="action"/> will lead to memory corruption (free-after-use).</param>
    /// <returns>A pointer to the location in memory where the first element of the array fake would be. Must be null-checked before use.</returns>
    public T* Use<T>(int length, Action<T[]> action) where T : unmanaged
    {
        if (Interlocked.Exchange(ref call, 1) == 1)
        {
            ThrowHelpers.ThrowAttemptedAliasing();
            return default;
        }

        try
        {
            if (length == 0)
                action([]);
            else
            {
                var array = Factory.FakeArray<T>(raw, length, sizeofRaw);
                try
                {
                    Debug.Assert(array is not null);

                    action(array);
                }
                finally
                {
                    Factory.Neutralize(array);
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref call, 0);
        }

        return (T*)DataOffset;
    }

    /// <summary>
    /// Creates an array fake and executes an <see cref="Action{T1, T2}"/> that is passed a reference to that fake
    /// alongside a caller-supplied <paramref name="state"/> value.
    /// Passing context via <paramref name="state"/> instead of capturing variables in a lambda
    /// eliminates the closure allocation that would otherwise occur on hot paths.
    /// </summary>
    /// <typeparam name="T">The element type of the array fake. Must be an unmanaged type.</typeparam>
    /// <typeparam name="TState">The type of the caller-supplied state value passed through to <paramref name="action"/>.</typeparam>
    /// <param name="length">The number of elements of type <typeparamref name="T"/> that the array fake should report as its <see cref="Array.Length"/>. Must be non-negative.</param>
    /// <param name="state">A value passed through verbatim to <paramref name="action"/>. Not inspected or modified by this method.</param>
    /// <param name="action">The action to execute, which is passed a reference to the array fake and <paramref name="state"/>. Extracting the array reference anywhere outside the scope of the <paramref name="action"/> will lead to memory corruption (free-after-use).</param>
    /// <returns>A pointer to the location in memory where the first element of the array fake would be. Must be null-checked before use.</returns>
    public T* Use<T, TState>(int length, TState state, Action<T[], TState> action) where T : unmanaged
    {
        if (Interlocked.Exchange(ref call, 1) == 1)
        {
            ThrowHelpers.ThrowAttemptedAliasing();
            return default;
        }

        try
        {
            if (length == 0)
                action([], state);
            else
            {
                var array = Factory.FakeArray<T>(raw, length, sizeofRaw);
                try
                {
                    Debug.Assert(array is not null);

                    action(array, state);
                }
                finally
                {
                    Factory.Neutralize(array);
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref call, 0);
        }

        return (T*)DataOffset;
    }

    /// <summary>
    /// Creates an array fake and executes an <see cref="ArrayRefAction{TElement, TState}"/> that is passed a reference to that fake
    /// alongside a by-reference caller-supplied <paramref name="state"/> value.
    /// Passing context by reference via <paramref name="state"/> avoids both the closure allocation and any copy of the state value,
    /// and allows the action to write results back to the caller without additional allocation.
    /// </summary>
    /// <typeparam name="T">The element type of the array fake. Must be an unmanaged type.</typeparam>
    /// <typeparam name="TState">The type of the by-reference caller-supplied state value passed through to <paramref name="action"/>.</typeparam>
    /// <param name="length">The number of elements of type <typeparamref name="T"/> that the array fake should report as its <see cref="Array.Length"/>. Must be non-negative.</param>
    /// <param name="state">A by-reference value passed through to <paramref name="action"/>. May be read and mutated by the action; changes are visible to the caller after <paramref name="action"/> returns.</param>
    /// <param name="action">The action to execute, which is passed a reference to the array fake and a by-reference <paramref name="state"/>. Extracting the array reference anywhere outside the scope of the <paramref name="action"/> will lead to memory corruption (free-after-use).</param>
    /// <returns>A pointer to the location in memory where the first element of the array fake would be. Must be null-checked before use.</returns>
    public T* Use<T, TState>(int length, ref TState state, ArrayRefAction<T, TState> action) where T : unmanaged
    {
        if (Interlocked.Exchange(ref call, 1) == 1)
        {
            ThrowHelpers.ThrowAttemptedAliasing();
            return default;
        }

        try
        {
            if (length == 0)
                action([], ref state);
            else
            {
                var array = Factory.FakeArray<T>(raw, length, sizeofRaw);
                try
                {
                    Debug.Assert(array is not null);

                    action(array, ref state);
                }
                finally
                {
                    Factory.Neutralize(array);
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref call, 0);
        }

        return (T*)DataOffset;
    }

    /// <summary>
    /// Zeroes the length field of a fake array, rendering it inert:
    /// all enumeration becomes a no-op, all indexed access throws <see cref="IndexOutOfRangeException"/>,
    /// and <see cref="Array.Length"/> returns <c>0</c>.
    /// </summary>
    /// <typeparam name="T">The element type of the array fake. Must be an unmanaged type.</typeparam>
    /// <param name="fake">
    /// The fake array to neutralize.
    /// A <see langword="null"/> reference or a fake whose length is already <c>0</c> is silently ignored.
    /// </param>
    /// <remarks>
    /// <para>
    /// The <see cref="Use{T}(int, Action{T[]})"/> family of methods neutralize the fake automatically on every
    /// exit path, including exceptions. Call <see cref="Neutralize{T}"/> directly only when the fake was obtained
    /// through a path that bypasses managed lifetime scoping (such as code that stamps the header manually).
    /// </para>
    /// <para>
    /// Always call this from a <see langword="finally"/> block to guarantee neutralization even when the body throws:
    /// <code>
    /// T[] fake = /* stamped by manual means */;
    /// try
    /// {
    ///     fs.Read(fake, 0, count);
    /// }
    /// finally
    /// {
    ///     ArrayFacadeHandle.Neutralize(fake);
    /// }
    /// </code>
    /// </para>
    /// <para>
    /// Only pass fake arrays obtained from this library to this method. Passing a real heap-allocated
    /// array is undefined behavior.
    /// </para>
    /// </remarks>
    public static void Neutralize<T>(T[] fake) where T : unmanaged
    {
        if (fake == null || fake.Length == 0)
            return;
        Factory.Neutralize(fake);
    }

    /// <summary>
    /// Gets whether the current runtime lays objects out the way this library fabricates them.
    /// This is verified once at startup by probing live array instances (reads only; nothing is stamped).
    /// When <see langword="false"/>, any attempt to create a fake with a length greater than 0, as well as
    /// any size computation for one, throws <see cref="PlatformNotSupportedException"/> before touching memory.
    /// Zero-length use remains valid on any runtime.
    /// </summary>
    public static bool IsSupported => Factory.IsSupported;

    private static readonly nuint _worstAlignDiff = (nuint)IntPtr.Size - 1;

    /// <summary>
    /// Calculates the minimum size in <see langword="byte"/>s a region of memory must be to safely house an array fake of a given <paramref name="length"/> and element type <typeparamref name="T"/>.
    /// The calculation assumes the worst case scenario for alignment padding.
    /// </summary>
    /// <typeparam name="T">The element type of the array fake. Must be an unmanaged type.</typeparam>
    /// <param name="length">The number of elements of type <typeparamref name="T"/> that the array fake should report as its <see cref="Array.Length"/>. Must be non-negative.</param>
    /// <returns>The minimum size required for a region of memory to safely house an array fake of the specified length and element type. Returns <c>0</c> for a <paramref name="length"/> of <c>0</c>, for any <typeparamref name="T"/>, since a zero-length fake is never stamped and is satisfied by a real empty array.</returns>
    /// <remarks>
    /// This deliberately returns <see langword="nuint"/> to technically allow returns larger than <see cref="int.MaxValue"/>. They would require massive native allocations, but they're possible nonetheless.
    /// <para/>It is NOT safe to pass this directly to a <see langword="stackalloc"/> initializer, for example. This method does no sanity checking for the combination of <paramref name="length"/> <see langword="sizeof"/>(<typeparamref name="T"/>), meaning you'll very easily blow the stack. More or less safe when giving to methods that allocate native memory, but you should still sanity check before doing so.
    /// </remarks>
    public static nuint ComputeMinimumSafeSizeFor<T>(int length) where T : unmanaged
    {
        if (length < 0)
        {
            ThrowHelpers.ThrowLengthNegative();
            return 0;
        }
        if (length > 0x7FFFFFC7)
        {
            ThrowHelpers.ThrowLengthGreaterThanArrayMaxLength();
            return 0;
        }
        // A length-0 fake is never stamped (Use hands back a real []), so it needs no
        // backing memory and is valid for any T on any runtime. Runtime layout and
        // element-type support are stamp-time concerns, checked only when length > 0.
        if (length == 0)
            return 0;

        if (!Factory.IsSupported)
        {
            ThrowHelpers.ThrowPlatformNotSupported();
            return 0;
        }

        Helpers.CheckTypeSupport<T>();

        return _worstAlignDiff + 3 * (nuint)IntPtr.Size + (nuint)sizeof(T) * (nuint)length;
    }

    private static readonly MethodInfo _computeGenericBase = typeof(ArrayFacadeHandle)
    .GetMethods(BindingFlags.Public | BindingFlags.Static)
    .First(static m => m.Name == nameof(ComputeMinimumSafeSizeFor) && m.IsGenericMethodDefinition);

    private static readonly ConcurrentDictionary<Type, Func<int, nuint>> _sizeFns = new();

    /// <inheritdoc cref="ComputeMinimumSafeSizeFor{T}(int)"/>
    public static nuint ComputeMinimumSafeSizeFor(Type type, int length)
    {
        if (type == null)
            throw new ArgumentNullException(nameof(type));
        return _sizeFns.GetOrAdd(type, static t =>
            (Func<int, nuint>)Delegate.CreateDelegate(typeof(Func<int, nuint>),
                _computeGenericBase.MakeGenericMethod(t)))(length);
    }
}
