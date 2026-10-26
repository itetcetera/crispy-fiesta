using System.Collections;

namespace Mule.Engine.Core;

/// <summary>
/// Runs a phase written as an iterator, one frame at a time. Inside a phase, <c>yield return null</c>
/// waits for the next vertical blank, and <c>yield return</c> of another iterator runs it in place.
/// </summary>
public sealed class Coroutine
{
    readonly Stack<IEnumerator> _stack = new();

    public Coroutine(IEnumerable body) => _stack.Push(body.GetEnumerator());

    public bool Done => _stack.Count == 0;

    /// <summary>Runs the main program until it waits for the next frame (or ends).</summary>
    public void Step()
    {
        while (_stack.Count > 0)
        {
            var top = _stack.Peek();
            if (!top.MoveNext()) { _stack.Pop(); continue; }
            switch (top.Current)
            {
                case null: return;
                case IEnumerable e: _stack.Push(e.GetEnumerator()); break;
                case IEnumerator e: _stack.Push(e); break;
                default: throw new InvalidOperationException("phases yield null or an iterator");
            }
        }
    }
}
