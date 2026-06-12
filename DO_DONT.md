# Do

First and foremost: **DO try to avoid having to use this library in the first place.** Chances are you're here because you're stuck on netfx for reason X and need method Y to hit infinitesimal performance goals. `Array.AsSpan()` works as normal even for array fakes. If you can, try to use everything else and `Span<T>` over it BEFORE resorting to using this library.

- **DO back it with stack or native memory only** (`stackalloc`; `NativeMemory.Alloc` / `Marshal.AllocHGlobal`). The facade is sound *only* because a non-heap address fails the GC's heap range check, so the collector skips any reference to it and never inspects your header.
- **DO consume the array entirely within the synchronous scope that owns the backing memory** (read / copy / synchronous-blocking API) and retain nothing.
- **DO restrict to APIs that finish using the buffer before they return** (e.g. blocking `FileStream.Read` in sync mode). Only a blocking call keeps your frame alive for the whole operation and retains nothing after, so use-after-free is structurally impossible.
- **DO treat memory provenance as an invariant enforced at the allocation site**. Only *you*, the caller, know where the `void*` came from that `ArrayFacadeHandle` received. The library cannot protect you from giving it heap memory.
- **DO allocate enough memory and request sane lengths.** The `int` parameter `ArrayFacadeHandle.Use<T>(int, Action<T[]>)` specifies the number of elements, i.e. the length the returned `T[]` will report. Values smaller than the effective capacity of the memory region given to `ArrayFacadeHandle` on creation are safe. Larger values will write into memory you don't own.

# Do not

- **DO NOT back it with GC-heap memory** (writing the fake header into a real/rented `byte[]`, a pinned object, etc.). On the heap the collector believes the bytes are its own, and since you never went through the allocator, its bookkeeping and your forgery disagree → corruption on the next collection. **There is no safe heap variant.**
- **DO NOT let the reference escape its owning scope.** No storing in a field/longer-lived local, no closure capture, no returning, no boxing, no handing it to anything deferred (`async`/`Task`-returning APIs, thread-pool work, a retaining `IDisposable`, a later callback). Heap-backing corrupts *immediately*; an escaped stack/native reference corrupts *eventually* (dangling pointer to a popped frame or freed block). A `T[]` parameter/delegate contract cannot enforce this.
- **DO NOT pass a fake to deferred/capturing/cross-thread APIs.** `BeginRead`/`*Async`/socket async, anything stashing the array in a field or queueing it. The buffer outlives your frame and any references to the memory dangle.
- **DO NOT pin through `GCHandle.Alloc`.** Behavior on a non-heap target is undefined (retail may no-op; checked/debug runtimes assert heap membership) and it buys nothing over `fixed` (or just using the pointer you already had).
- **DO NOT attempt to circumvent `Array.MaxLength`.** The field inside `Array` that holds the length is always `int`.