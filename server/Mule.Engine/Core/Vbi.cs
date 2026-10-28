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
    static readonly byte[] SoundFreq = { 0x10, 0x18, 0x20, 0x28, 0x30, 0x60, 0x80 }; // $5E28

    /// <summary>The POKEY sound registers as the game last wrote them: AUDF1, AUDC1 ... AUDF4, AUDC4, AUDCTL.</summary>
    public readonly byte[] Pokey = new byte[9];
    public const int Audctl = 8;
    public static int Audf(int voice) => 2 * (voice - 1);
    public static int Audc(int voice) => 2 * (voice - 1) + 1;

    /// <summary>The RANDOM register read in a vertical blank for looks and sound only (the original's
    /// hardware noise, not the game's random numbers); the tests answer it from the original.</summary>
    public Func<byte> Noise = () => (byte)System.Random.Shared.Next(256);

    /// <summary>The music player, on the same registers.</summary>
    public readonly Music Music;

    public Vbi() => Music = new Music(Pokey);

    /// <summary>sub_136D: all four voices silent (frequencies and controls to 0).</summary>
    public void Silence() => Array.Clear(Pokey, 0, 8);

    // The low-time warning (sub_5E98, voices 1 and 2): on while a turn runs ($B33C).
    public byte Warn;                                   // $B33C
    public byte WarnTime = 0x1E;                        // $B33A: it sounds once the time is below this
    public byte WarnCount, WarnLevel, WarnTone;         // $B33E, $B33F, $B340
    /// <summary>sub_3101: the warning armed, sounding below 30.</summary>
    public void StartWarning()
    {
        WarnTime = 0x1E;
        WarnCount = 1;
        Warn = 1;
        WarnTone = 0;
    }

    /// <summary>The time left ($D3), which the warning reads.</summary>
    public Func<byte> TurnTime = () => 0;

    /// <summary>Online only (not in the original): seats whose player dropped out a while ago. The "press
    /// all buttons" waits count them as pressed, so the others are not held up (Marcus, 2026-10-27).</summary>
    public readonly bool[] Away = new bool[4];

    /// <summary>Away seats that have not pressed since the last reset.</summary>
    public int AwayNotPressed()
    {
        int n = 0;
        for (int x = 0; x < 4; x++) if (Away[x] && ButtonPushed[x] != 0) n++;
        return n;
    }

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
        if (Warn != 0) WarnTick();
        if ((Sound & 0x80) == 0) SoundTick();
    }

    /// <summary>sub_5E98: below the warning time, a two-tone alarm on voices 1 and 2 that pulses faster as
    /// the time runs out; at no time left, both voices off and the warning with them.</summary>
    void WarnTick()
    {
        byte time = TurnTime();
        if (time == 0)
        {
            Pokey[Audf(1)] = Pokey[Audc(1)] = Pokey[Audf(2)] = Pokey[Audc(2)] = 0;
            Warn = 0;
            return;
        }
        if (time >= WarnTime) return;
        if (--WarnCount == 0)
        {
            Pokey[Audc(1)] = WarnLevel = 0xA2;
            Pokey[Audc(2)] = 0x81;
            WarnTone = (byte)((WarnTone & 1) ^ 1);
            Pokey[Audf(1)] = Pokey[Audf(2)] = WarnTones[WarnTone];
            WarnCount = time;
            return;
        }
        byte level = (byte)(WarnLevel - 1);
        if (level < 0xA0) level = 0xA0;
        Pokey[Audc(1)] = WarnLevel = level;
        if (level < 0xA1) return;
        if (level == 0xA1) Pokey[Audc(2)] = 0;
        Pokey[Audf(1)] = WarnTones[WarnTone];
    }

    static readonly byte[] WarnTones = { 0x0F, 0x0A }; // $5F05

    /// <summary>sub_5E3D: a new effect starts only when the previous one has run out.</summary>
    void SoundTick()
    {
        int x = Sound;
        if (SoundLeft == 0)
        {
            Pokey[Audctl] = 0;
            SoundLeft = SoundLength[x];
            Pokey[Audf(4)] = SoundFreq[x];
            Pokey[Audc(4)] = SoundLevel = SoundControl[x];
            return;
        }
        if (x < 6)
        {
            if (--SoundLeft == 0) { EndSound(); return; }
            if (SoundLeft < 14) Pokey[Audc(4)] = --SoundLevel;
            return;
        }
        SoundLeft >>= 1;
        Pokey[Audf(4)] = SoundLeft;
        SoundLevel += 2;
        if (SoundLevel >= 12) { EndSound(); return; }
        Pokey[Audc(4)] = SoundLevel;
    }

    void EndSound()
    {
        Sound = 0xFF;
        SoundLevel = 0;
        Pokey[Audf(4)] = Pokey[Audc(4)] = 0;
    }
}
