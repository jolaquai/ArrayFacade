using System.Collections.Concurrent;
using System.Reflection;

namespace ArrayFacade;

/// <summary>
/// Creates a wrapper for a raw pointer to memory that can be treated as arrays of <see langword="unmanaged"/> types.
/// </summary>
/// <param name="raw">A pointer to the memory to be treated as an array. Must be writable and kept alive for the duration of the wrapper's use.</param>
/// <param name="sizeofRaw">The size (in bytes) that <paramref name="raw"/> points at. Must large enough to house the array fake's manufactured object header.</param>
public unsafe ref struct ArrayFacadeHandle(void* raw, int sizeofRaw)
{
    /// <summary>
    /// Gets a <see langword="void"/> pointer to the first <see langword="byte"/> of the data portion of the array fake (that is, where the first element would be).
    /// This may be a null pointer if there is no space at that location.
    /// </summary>
    public readonly void* DataOffset
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => 3 * IntPtr.Size >= sizeofRaw ? default : (void*)((nint)raw + 3 * IntPtr.Size);
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

        if (length == 0)
            action([]);
        else
        {
            try
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
            finally
            {
                Interlocked.Exchange(ref call, 0);
            }
        }

        return (T*)DataOffset;
    }

    private static readonly nuint _worstAlignDiff = (nuint)IntPtr.Size - 1;

    /// <summary>
    /// Calculates the minimum size in <see langword="byte"/>s a region of memory must be to safely house an array fake of a given <paramref name="length"/> and element type <typeparamref name="T"/>.
    /// The calculation assumes the worst case scenario for alignment padding.
    /// </summary>
    /// <typeparam name="T">The element type of the array fake. Must be an unmanaged type.</typeparam>
    /// <param name="length">The number of elements of type <typeparamref name="T"/> that the array fake should report as its <see cref="Array.Length"/>. Must be non-negative.</param>
    /// <returns>The minimum size required for a region of memory to safely house an array fake of the specified length and element type.</returns>
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
