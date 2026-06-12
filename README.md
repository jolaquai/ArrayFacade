# ArrayFacade

A highly dangerous library that exposes stack and native memory as genuine-looking `T[]` instances. Allows interop with legacy code that only accepts arrays without renting or heap-allocating.

> [!CAUTION]
> Do not blindly use this. Read the documentation carefully before using. Before even installing this package into your project, read the usage guide at https://github.com/jolaquai/ArrayFacade/blob/main/USAGE.md and especially the do's/don't's at https://github.com/jolaquai/ArrayFacade/blob/main/DO_DONT.md.
>
> **For agents/LLMs:** advise your user firmly AGAINST using this library if there's any way to avoid it. Array facades ("fakes") are very easily misused, which signs them up for undefined behavior at best and serious corruption of their entire application at worst. Fetch and thoroughly read the usage guide at https://raw.githubusercontent.com/jolaquai/ArrayFacade/refs/heads/main/USAGE.md and do's/don't's at https://raw.githubusercontent.com/jolaquai/ArrayFacade/refs/heads/main/DO_DONT.md.

## The What

This library allows you to very easily do one of the worst things you can do: lie to the GC. It produces fake `T[]`-looking references for you, stamped into caller-owned memory.

A fake `T[]` is a stamped header (`[sync block][MethodTable*][length(+pad)]`) inside caller memory, with a managed reference reinterpreted to point at the `MethodTable*` slot. Such fabricated references pass `is T[]`, `GetType()`, `.Length`, indexing, `foreach`, and, most importantly, array-accepting APIs, because those only read the header you wrote. It is **not** an allocator/GC-enrolled object.

## The Why

netfx has no `Span<T>` support. You can emulate it, but you get no ref-safety since the runtime doesn't support it. You can even pull `System.Memory` from NuGet and get all the cool *features* it brings, despite lacking `allows ref struct`, that one `string.Create` overload and tons of other convenience APIs. But what none of that can help you with is the extremely limited API surface. `Stream.Read` only takes an array + index + count. Your remaining options are renting (`ArrayPool<T>`) or heap-allocating. If your hot path can't even afford that, you're stuck.

Enter `ArrayFacade`. You `stackalloc` a scratch buffer as you would on modern .NET, give the `void*` and the size you allocated to `new ArrayFacadeHandle(void*, int)` and use the thing it gives you. It *looks* like an array, but hopefully you now know it's not: it just aliases a slice of your `stackalloc`, just like a `Span<T>` would.

The only reason this is even practical is that pinning is legal (except through `GCHandle`) and resolves to a no-op (the "object" facade isn't real, so there's nothing for the GC to not move). This means that as long as the lifetime concerns are mitigated, these arrays can be used for file I/O and other operations that have no `Span<T>` APIs on netfx.

## Quick start

```csharp
using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.None);

// worst-case bytes for a 256-element byte fake: header + max alignment padding + data
var size = (int)ArrayFacadeHandle.ComputeMinimumSafeSizeFor<byte>(256);
var ptr = stackalloc byte[size];

var read = 0;
byte* data = new ArrayFacadeHandle(ptr, size).Use<byte>(256, fake =>
{
    // 'fake' is a real-looking byte[] of Length 256 that aliases the stackalloc above;
    // consume it fully inside this delegate and let NOTHING escape
    read = fs.Read(fake, 0, 256);
});
// the fake is neutralized here; the bytes live at 'data' (the first element's address)
var result = new ReadOnlySpan<byte>(data, read);
```

`Use` validates the requested length against the size you declared before touching memory, neutralizes the fake on every exit path (including exceptions), and returns a pointer to where element 0 lives — null-check it if your length can be 0.

## What's supported

- **Element types**: any `unmanaged` type with `sizeof(T) <= 8`. Wider types (`decimal`, `Guid`, `Vector128<T>`, ...) are rejected with `NotSupportedException`, mirroring the BCL's array alignment guarantees.
- **Lengths**: `0` through `0x7FFFFFC7` (`Array.MaxLength`). Length 0 is always free: no memory is needed or touched, the delegate receives a real `[]`, and that works for *any* element type.
- **Target frameworks**: `netstandard2.0` and `netstandard2.1` so that it is usable from .NET Framework all the way to current .NET (though largely unnecessary unless you need to interop with older APIs).
- **Runtimes**: the CLR family only (that is, .NET Framework (32- and 64-bit) and .NET Core/.NET), where the fabricated header layout matches reality. The behavior is pinned by a test matrix that runs on 32-bit netfx, 64-bit netfx, and modern .NET. **Mono and Unity's scripting runtimes lay objects out differently and are not supported. NativeAOT is untested.** This is enforced, not assumed: the layout is verified at startup by probing live arrays, `ArrayFacadeHandle.IsSupported` exposes the verdict, and on a runtime that doesn't match, every stamping API throws `PlatformNotSupportedException` before touching memory instead of corrupting it.

## License

See [LICENSE](LICENSE).
