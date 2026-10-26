namespace Mule.Engine.Core;

/// <summary>One player's controls for one frame.</summary>
public readonly record struct PlayerInput(bool Button, byte Stick = 15)
{
    public static readonly PlayerInput None = new(false);
}

/// <summary>
/// The game-visible work of the vertical blank interrupt ($5BB5), run once per 1/60 s frame before the
/// main program continues: the jiffy clock, controls, the four countdown timers, button latching, the
/// current phase's per-frame handler and the one-voice effect sound.
/// </summary>
public sealed class Vbi
{
    public byte RtClock;                                // $14
    public readonly byte[] Timers = new byte[4];        // $C3-$C6, count down on frames where RtClock % 4 == 0
    public readonly byte[] Button = { 1, 1, 1, 1 };     // $B2: 0 = held this frame (CPUs always 1)
    public readonly byte[] Stick = { 15, 15, 15, 15 };  // $BD
    public readonly byte[] ButtonPushed = { 1, 1, 1, 1 }; // $B6: 0 = pressed since the last reset (latched)
    public byte Presses;                                // $95
    public byte SoundOnPress;                           // $4E: a latched press also plays that player's sound

    public byte Sound = 0xFF;                           // $9E: effect playing (bit 7 set = none)
    public byte SoundLeft;                              // $C8
    public byte SoundLevel;                             // $C9

    /// <summary>The current phase's handler ($C1 and the $5D7E table); null = none ($C1 negative).</summary>
    public Action? Handler;

    static readonly byte[] SoundLength = { 2, 2, 2, 2, 21, 8, 128 }; // $5E2F
    static readonly byte[] SoundControl = { 173, 173, 173, 173, 173, 170, 2 }; // $5E36

    /// <summary>initPlayersButtons ($131C).</summary>
    public void InitButtons()
    {
        for (int i = 0; i < 4; i++) ButtonPushed[i] = 1;
        Presses = 0;
    }

    public void Tick(GameState g, ReadOnlySpan<PlayerInput> input)
    {
        RtClock++;
        for (int x = 3; x >= 0; x--)
        {
            if (g.IsCpu(x)) { Button[x] = 1; continue; }
            Button[x] = input[x].Button ? (byte)0 : (byte)1;
            Stick[x] = input[x].Stick;
        }
        if ((RtClock & 3) == 0)
            for (int x = 3; x >= 0; x--)
                if (Timers[x] != 0) Timers[x]--;
        for (int x = 3; x >= 0; x--)
        {
            if (ButtonPushed[x] == 0) continue;
            ButtonPushed[x] = Button[x];
            if (ButtonPushed[x] != 0) continue;
            Presses++;
            if (SoundOnPress != 0) Sound = (byte)x;
        }
        Handler?.Invoke();
        if ((Sound & 0x80) == 0) SoundTick();
    }

    /// <summary>sub_5E3D: a new effect starts only when the previous one has run out.</summary>
    void SoundTick()
    {
        int x = Sound;
        if (SoundLeft == 0)
        {
            SoundLeft = SoundLength[x];
            SoundLevel = SoundControl[x];
            return;
        }
        if (x < 6)
        {
            if (--SoundLeft == 0) { EndSound(); return; }
            if (SoundLeft < 14) SoundLevel--;
            return;
        }
        SoundLeft >>= 1;
        SoundLevel += 2;
        if (SoundLevel >= 12) EndSound();
    }

    void EndSound()
    {
        Sound = 0xFF;
        SoundLevel = 0;
    }
}
