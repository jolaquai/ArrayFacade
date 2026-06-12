# Usage Guide

The core type you interact with to use `ArrayFacade` (as a library) is `ref struct ArrayFacadeHandle`.
The thing it "handles" shall be referred to as a "fake"; that is, an instance of an array facade: memory simply typed as `T[] where T : unmanaged` despite it not really being such an array.

## API surface

```csharp
public unsafe ref struct ArrayFacadeHandle(void* raw, int sizeofRaw)
{
    public readonly void* DataOffset { get; }
    public readonly ref byte DataOffsetRef { get; }

    public T* Use<T>(int length, Action<T[]> action) where T : unmanaged;

    public static nuint ComputeMinimumSafeSizeFor<T>(int length) where T : unmanaged;
    public static nuint ComputeMinimumSafeSizeFor(Type type, int length);
}
```

- The **constructor** does no work and no validation; it only records the pointer and the size you *declare* it points at. All validation happens inside `Use` against these recorded values, so they are only as truthful as you are.
- **`Use<T>(length, action)`** stamps a fake of `length` elements into the memory, invokes `action` with it, and neutralizes the fake on every exit path (including when `action` throws). It returns a `T*` to where element 0 lives. Null-check the pointer before use and treat it as meaningless when `length` was 0.
- **`DataOffset` / `DataOffsetRef`** give you the same first-element location as a `void*` or `ref byte` without calling `Use`, e.g. to read results after the fact. Both return null pointers when the declared region has no room for data.
- **`ComputeMinimumSafeSizeFor`** returns the worst-case byte requirement for a fake of the given length and element type: header + maximum possible alignment padding + elements. Because it assumes a maximally misaligned pointer, its result is valid for *any* pointer you end up backing the handle with. For `length` 0 it returns 0 (see below).

## Sizing and memory layout

The fake's manufactured object header is stamped at `raw` aligned **up** to `IntPtr.Size`; elements follow the header:

```
[ pad: 0..IntPtr.Size-1 ][ sync block ][ MethodTable* ][ length(+pad) ][ element 0 ... element N-1 ]
^                        ^ header starts at AlignUp(raw, IntPtr.Size)
| raw points here
```

The managed reference handed to your `Action<T[]>` points at the `MethodTable*` slot, exactly like a real object reference. Element 0 therefore lives at `AlignUp(raw) + 3 * IntPtr.Size`, which is what `Use`'s return value and `DataOffset` report. **Do not assume your data starts at `raw`.**

`Use` validates that the declared size fits `alignment padding + header + length * sizeof(T)` for the *actual* pointer and throws `ArgumentException` before touching memory if it doesn't. Sizing with `ComputeMinimumSafeSizeFor` guarantees this check passes.

> [!WARNING]
> `ComputeMinimumSafeSizeFor` does no sanity checking of `length * sizeof(T)` as a whole. It will happily return multiple gigabytes. Do not feed its result to `stackalloc` or even some native memory allocation mechanism without thinking; you will blow the stack or balloon memory long before the math overflows.

### Element types

Supported element types are `unmanaged` types with `sizeof(T) <= 8`, mirroring the BCL's array alignment guarantees. Anything wider (`decimal`, `Guid`, `Int128`, `Vector128<T>` and up, large custom structs) is rejected with `NotSupportedException`. Lengths are validated against `Array.MaxLength` (`0x7FFFFFC7`).

### Zero length

Length 0 is always free and always valid, for **any** element type, on every API:

- `ComputeMinimumSafeSizeFor<T>(0)` returns 0 since a zero-length fake is never stamped, so it needs no backing memory.
- `Use<T>(0, action)` invokes `action` with a real `T[]` (`[]` or `Array.Empty<T>()`), touches nothing, and never consults element-type support.

Element-type support is a *stamp-time* concern; it is checked exactly when something would be stamped, i.e. for lengths > 0.

## General information

- The `ref struct` scoping provides type-level assurance that at least the *wrapper* cannot be misused: most things that would invalidate a `ref struct` (capturing, boxing, storing in fields of classes) would also invalidate a fake, and the compiler rejects them outright.
- Use of a fake outside the scope of an invocation of the `Action<T[]>` you pass to `ArrayFacadeHandle.Use<T>(int, Action<T[]>)` will lead to undefined behavior and most likely memory corruption.
- Pinning a fake's memory specifically through `GCHandle.Alloc` is disallowed; it likely throws or silently corrupts memory because the GC is now aware of a "root" to an object that is not actually on the heap.
  - Pinning through a `fixed` statement is allowed: the GC range-checks pinned byrefs and skips non-heap memory. `fixed (T* p = fake)` behaves exactly as on a real array, reading the fake's length and handing you element 0's address (or a null pointer for length 0).
  - Pinning that happens automatically as part of synchronous P/Invokes, as well as `System.Runtime.InteropServices.Marshal` copy/call APIs, uses the same underlying mechanism, making those safe as well.
  - What are *not* safe are APIs such as `Marshal.StructureToPtr` that enable custom managed→unmanaged marshalling, since they may need to pin the object given to them.

## Backing memory

> [!CAUTION]
> It is impossible on the managed side to detect reliably whether a `void*` points to heap, stack or native memory.
> **Backing an array fake with heap memory is an instant GC corruption.**
>
> The facade only stays sound because a non-heap address *fails the GC's heap range check*, so the collector skips over any reference to it and never inspects the header the library stamped to fabricate the fake.
> Put that header on the GC heap and you lose this property: the collector now believes the bytes belong to it and tries to reconcile the forgery with its own bookkeeping. Since the fake never went through the allocator that would have enrolled a real object, the next collection corrupts the heap.
>
> - **The memory belongs to a live object** (e.g. a rented/pinned `byte[]` you wrote the fake header into): two object headers now claim the same bytes. The GC walks that segment linearly using the *real* object's size, so the fake header lands mid-object; a reference to the fake makes the collector read an object boundary where there is none. The walk desyncs and corrupts.
> - **The memory was "free"** (uncommitted, or reclaimed): the GC's bookkeeping says those bytes are not an object start. A reference into them passes the range check but points at a non-object; on collection the GC reads the fake `MethodTable` and follows it as a real allocation, with a size/layout that doesn't match the surrounding bookkeeping.

There is no "safe" way to place the fake on the heap. Fakes are exclusively for **stack or native** memory the GC has agreed to ignore. (Not that doing so would be useful since the fakes' only purpose is the scenario where hot paths cannot afford heap allocations.)

## Lifetime management

An array reference provided during `ArrayFacadeHandle.Use<T>(int, Action<T[]>)` calls **must not escape the scope of the `Action<T[]>`'s invocation**.

After invocation, the facade is neutralized: its length is zeroed in the header. Instead of returning stale data, stored references are no longer usable arrays in the sense that:
- `Length` reports 0,
- indexing throws `IndexOutOfRangeException`,
- `foreach`/`CopyTo`-style operations become no-ops.
- More importantly, the backing memory is the caller's: once `Use` returns, it may be a popped stack frame or freed native block, so any retained reference is a dangling pointer **regardless of the neutralization**.

"Escape" happens whenever the reference leaves the `Action<T[]>`'s synchronously-live scope, e.g.:
- assigning it to a field or any local that is *not* inside the delegate,
- capturing it in a closure, returning it, or boxing it,
- handing it to anything deferred: `Task`-returning/`async` APIs, thread-pool work, an `IDisposable` that retains it, a callback invoked later, etc.

The safe shape is the same one that makes the facade valid in the first place: consume the array entirely within the call (read/copy/synchronous-blocking-API), retain nothing, **ever**.
Note the `Action<T[]>` contract gives no protection. The compiler cannot stop a `T[]` from escaping a delegate. Treat the no-escape rule as a hard precondition you uphold, not one the type enforces.

A typical use of `ArrayFacadeHandle` should look as follows. The shorter the time an instance is alive, the better; ideally it never even hits a local:

```csharp
// Best practice: precompute a guaranteed-safe size, then use it everywhere
var size = (int)ArrayFacadeHandle.ComputeMinimumSafeSizeFor<byte>(10);
var ptr = stackalloc byte[size];

var read = 0;
// Use returns a T* to where element 0 lives (NOT raw itself, see the layout above).
// DO NOT use that pointer without a null-guard if your length isn't guaranteed to be > 0.
byte* firstElementPtr = new ArrayFacadeHandle(ptr, size).Use<byte>(10, fake =>
{
    read = ms.Read(buffer: fake, offset: 0, count: 10);
});
var result = new ReadOnlySpan<byte>(firstElementPtr, read);
```

At most, keep the handle in a local to get at the data location afterwards, as a pointer or a `ref` directly:

```csharp
ArrayFacadeHandle handle = new(ptr, size);
handle.Use<byte>(10, fake =>
{
    read = ms.Read(buffer: fake, offset: 0, count: 10);
});
// Same as the pointer case, except as a ref; use System.Runtime.CompilerServices.Unsafe to use/traverse
ref byte firstElement = ref handle.DataOffsetRef;
```

## Re-entrancy and reuse

A handle is freely **reusable sequentially**, including across different element types `T` by design; each `Use` call stamps, runs, and neutralizes independently. This allows effectively zero-work reinterprets of data written into the fake's backing memory: neutralization only zeroes the header's length field, never the data, and element 0 of *every* fake sits at the same address (`AlignUp(raw) + 3 * IntPtr.Size`) regardless of `T`.

```csharp
var size = (int)ArrayFacadeHandle.ComputeMinimumSafeSizeFor<byte>(64);
var ptr = stackalloc byte[size];
ArrayFacadeHandle handle = new(ptr, size);

// fill 64 bytes through a byte fake...
byte* bytesAt = handle.Use<byte>(64, fake => ms.Read(fake, 0, 64));

// ...then reinterpret those same 64 bytes as 16 ints, in place:
// fine, the previous fake was neutralized
int* intsAt = handle.Use<int>(16, fake => Process(fake));

// "zero-work" is literal: nothing was copied or moved between the two calls
Debug.Assert((void*)bytesAt == (void*)intsAt);
```

The declared region serves any element type since the data offset is type-independent; `Use` re-validates `length * sizeof(T)` against it on every call, so the reinterpreting view's total byte count must fit the same declared size.

However, handles are *not* re-entrant: calling `Use` on a handle while another `Use` call on it is still in flight throws `InvalidOperationException`. Two live fakes over the same memory would alias each other, and neutralizing one would corrupt the other's header. The guard releases on every exit path, so a failed or throwing call never wedges the handle.

## Supported runtimes

The fabricated header must match the runtime's real object layout, byte for byte. That holds for the CLR family on which the library's test matrix runs: .NET Framework (32- and 64-bit) and .NET Core/.NET. **Mono and Unity's scripting runtimes lay objects out differently and are not supported. NativeAOT is untested.**
