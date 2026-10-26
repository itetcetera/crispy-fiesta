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

    public bool Finished => _main == null || _main.Done;

    /// <summary>Runs one frame: the vertical blank with this frame's controls, then the main program.</summary>
    public void RunFrame(ReadOnlySpan<PlayerInput> input)
    {
        Frame++;
        Vbi.Tick(State, input);
        _main?.Step();
    }

    public void Emit(GameEvent e) => Events.Add(e);

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
