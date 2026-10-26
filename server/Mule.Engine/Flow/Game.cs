using System.Collections;
using Mule.Engine.Core;

namespace Mule.Engine.Flow;

/// <summary>A running game: state, the frame clock, the random source and the events for the clients.</summary>
public sealed class Game
{
    public readonly GameState State;
    public readonly Vbi Vbi = new();
    public readonly IRandomSource Rng;
    public readonly List<GameEvent> Events = new();
    public long Frame { get; private set; }

    Coroutine? _main;

    public Game(GameState state, IRandomSource rng)
    {
        State = state;
        Rng = rng;
    }

    public void Start(IEnumerable main) => _main = new Coroutine(main);

    /// <summary>Starts <paramref name="main"/> and runs it up to its first wait now, before the next
    /// frame's vertical blank, as a routine called from the original's main program does.</summary>
    public void StartNow(IEnumerable main)
    {
        _main = new Coroutine(main);
        _main.Step();
    }

    public bool Finished => _main == null || _main.Done;

    /// <summary>A vertical blank that holds the machine for frames (the wampus catch, $8C1C, waits
    /// inside the interrupt): called each frame with the controls, true while it still holds. On its
    /// last frame it finishes the interrupt's work and the main program runs.</summary>
    public delegate bool HoldStep(ReadOnlySpan<PlayerInput> input);
    public HoldStep? Hold;
    /// <summary>Frames spent inside a held vertical blank (no clock, no timers, no main program).</summary>
    public long HeldFrames { get; private set; }

    /// <summary>Runs one frame: the vertical blank with this frame's controls, then the main program.</summary>
    public void RunFrame(ReadOnlySpan<PlayerInput> input)
    {
        Frame++;
        if (Hold != null)
        {
            HeldFrames++;
            if (Hold(input)) return;
            Hold = null;
        }
        else
        {
            Vbi.Tick(State, input);
            if (Hold != null) return;
        }
        _main?.Step();
    }

    public void Emit(GameEvent e) => Events.Add(e);

    /// <summary>Diagnostics for tests: named points in the phases.</summary>
    public Action<string>? Trace;

    /// <summary>For tests: how a cycle race of the original went (see docs/PLAN.md, timing effects).
    /// Unset, the port takes the way the original nearly always goes.</summary>
    public Func<string, bool>? Race;

    /// <summary>For tests: the frames a CPU player's working out took in the original. Unset, the
    /// port uses its measured estimate.</summary>
    public Func<string, int?>? Think;

    /// <summary>The development turn under way, if any.</summary>
    public Mule.Engine.Rules.Turn? Turn;

    /// <summary>waitJiffy ($1310): until the clock has moved on <paramref name="n"/> frames.</summary>
    public IEnumerable WaitJiffy(int n)
    {
        if (n == 0) yield break;
        byte target = (byte)(Vbi.RtClock + n);
        while (Vbi.RtClock != target) yield return null;
    }

    /// <summary>waitJiffyLevel ($5988): 160 frames in the first round of the beginner game, else 128.</summary>
    public IEnumerable WaitJiffyLevel() => WaitJiffy(State.Level == 0 && State.NumRound == 1 ? 160 : 128);

    /// <summary>sub_108D: waits for the effect playing to end, then starts effect 4.</summary>
    public IEnumerable WaitSoundThenBeep()
    {
        while ((Vbi.Sound & 0x80) == 0) yield return null;
        Play(4);
    }

    /// <summary>sub_1378: <paramref name="times"/> times effect 4 to its end, one frame apart, with no phase handler.</summary>
    public IEnumerable Beeps(int times)
    {
        var handler = Vbi.Handler;
        Vbi.Handler = null;
        for (int i = 0; i < times; i++)
        {
            Play(4);
            while ((Vbi.Sound & 0x80) == 0) yield return null;
            yield return WaitJiffy(1);
        }
        Vbi.Handler = handler;
    }

    public void Play(int sound)
    {
        Vbi.Sound = (byte)sound;
        Emit(new SoundEvent(sound));
    }

    /// <summary>Frames the original spends drawing (display work that runs past vertical blanks).</summary>
    public IEnumerable Frames(int n)
    {
        for (int i = 0; i < n; i++) yield return null;
    }
}
