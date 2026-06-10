# Usage Guide

The core type you interact with to use `ArrayFacade` (as a library) is `ArrayFacadeHandle<T>`.
To more easily differentiate what I mean, from now on, that type name will be merely referred to as a "handle".
The thing it "handles" shall be referred to as a "fake"; that is, an instance of an array facade, memory simply typed as `T[] where T : unmanaged` despite it not really being such an array.

* The split into multiple types facilitates type-level security that at least the wrapper cannot be misused (violating ref-safety rules would be one of those misuses).
* Use of a fake 