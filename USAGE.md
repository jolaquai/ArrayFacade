# Usage Guide

The core type you interact with to use `ArrayFacade` (as a library) is `ref struct ArrayFacadeHandle`.
The thing it "handles" shall be referred to as a "fake"; that is, instance of array facades, memory simply typed as `T[] where T : unmanaged` despite it not really being such an array.

## General information

* The scoping using a `ref struct` facilitates type-level security that at least the wrapper cannot be misused (violating ref-safety rules would be one of those misuses, and most things that would invalidate a `ref struct` would also invalidate a fake).
* Use of a fake outside the scope of an invocation of the `Action<T>` you pass to `ArrayFacadeHandle.Use<T>(int, Action<T[]>)` will lead to undefined behavior and most likely memory corruption.
* Pinning a fake's memory specifically through `GCHandle.Alloc` is disallowed; it likely throws or silently corrupts memory because the GC is now aware of a "root" to an object that is not actually on the heap.
  * Pinning through a `fixed` statement is allowed since the GC range-checks pinned `byref`s and skips non-heap memory (since only heap memory is volatile, as in, moveable, the only thing it does is check the fake's length to hand you a null pointer if it's zero).
  * Pinning that happens automatically as part of synchronous P/Invokes, as well as `System.Runtime.InteropServices.Marshal` copy/call APIs pin using the same underlying mechanism, making those safe as well.
  * What are *not* safe are APIs such as `Marshal.StructureToPtr` that enable custom managed->unmanaged marshalling since they may need to pin the object given to them.

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

After invocation, the facade is neutralized (its header is cleared), so a stored reference is no longer a usable array; any access pattern faults rather than returning data. More importantly, the backing memory is the caller's: once `Use` returns, it may be a popped stack frame or freed native block, so that any retained reference is a dangling pointer.

"Escape" happens whenever the reference leaves the `Action<T[]>` synchronously-live scope, e.g.:
- assigning it to a field or any local that is *not* inside the delegate,
- capturing it in a closure, returning it, or boxing it,
- handing it to anything deferred: `Task`-returning/`async` APIs, thread-pool work, an `IDisposable` that retains it, a callback invoked later, etc.

The safe shape is the same one that makes the facade valid in the first place: consume the array entirely within the call (read/copy/synchronous-blocking-API), retain nothing, **ever**.
Note the `Action<T[]>` contract gives no protection. The compiler cannot stop a `T[]` from escaping a delegate. Treat the no-escape rule as a hard precondition you uphold, not one the type enforces.

A typical use of `ArrayFacadeHandle` should look as follows. The shorter the time an instance is alive, the better. Only EVER store `ArrayFacadeHandle` into a local if absolutely necessary to avoid having to redo validations. NEVER store an instance into a field.
```csharp
// .Use returned a T-typed pointer to the location where the first element in the fake would be if its Length is > 0
// DO NOT use that pointer without a nullptr-guard if your length isn't guaranteed to be > 0
byte* firstElementPtr = new ArrayFacadeHandle(ptr, size).Use<byte>(10, fake =>
{
    read = ms.Read(buffer: fake, offset: 0, count: 10);
});
```

At most, keep it around to get at the location of the data as a `ref` directly:
```csharp
ArrayFacadeHandle handle = new(ptr, size);
handle.Use<byte>(10, fake =>
{
    read = ms.Read(buffer: fake, offset: 0, count: 10);
});
// Same as the pointer case, except as a ref; use System.Runtime.CompilerServices.Unsafe to use/traverse
ref byte firstElement = ref handle.DataOffsetRef;
```