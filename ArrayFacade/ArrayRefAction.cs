namespace ArrayFacade;

/// <summary>
/// Represents a method that is passed a fake array and a caller-supplied state value by reference
/// during a call to <see cref="ArrayFacadeHandle.Use{T, TState}(int, ref TState, ArrayRefAction{T, TState})"/>.
/// </summary>
/// <typeparam name="TElement">The element type of the array.</typeparam>
/// <typeparam name="TState">The type of the by-reference state value.</typeparam>
/// <param name="array">
/// The fake array to operate on.
/// Must not be stored or used outside the body of the method — doing so leads to memory corruption.
/// </param>
/// <param name="state">
/// A by-reference caller-supplied value. May be read and mutated in place; changes are
/// visible to the caller after the delegate returns.
/// Passing context via <paramref name="state"/> rather than capturing variables in a lambda
/// eliminates the closure allocation that would otherwise occur on hot paths.
/// </param>
public delegate void ArrayRefAction<TElement, TState>(TElement[] array, ref TState state);
