using Mule.Engine.Rules;

namespace Mule.Engine.Core;

/// <summary>
/// The music player ($2527 to $2D42) on POKEY's four voices: the score screen's tune (sub_2527, played by
/// v03 at $25BE) and the pub's (sub_2CE1, played by $2D29). It works on its own block of memory, $24A0 to
/// $2CE0, copied from the disk, so the code below reads and writes the original's addresses.
/// </summary>
public sealed class Music
{
    readonly byte[] m = (byte[])MusicData.Image.Clone();
    readonly byte[] pokey;
    // Each voice's note pointer: $43/$44, $45/$46, $47/$48, $D0/$D1.
    readonly ushort[] p = new ushort[4];

    public Music(byte[] pokey) => this.pokey = pokey;

    byte this[int a]
    {
        get => m[a - MusicData.Start];
        set => m[a - MusicData.Start] = value;
    }

    /// <summary>$24A6: the score's tune is on (the scrolling message's handler v15 plays it too).</summary>
    public byte On
    {
        get => this[0x24A6];
        set => this[0x24A6] = value;
    }

    /// <summary>sub_2527: the score's tune, longer each quarter of the game; the last round's starts two
    /// notes earlier. It clears the presses and the press sound ($95, $4E), which the caller does.</summary>
    public void Init(byte round)
    {
        this[0x24A0] = 2;
        int y = (round >> 2) * 2;
        this[0x24A2] = this[0x251F + y];
        this[0x24A3] = this[0x2520 + y];
        if (round >= 12) this[0x24A0] = 0;
        for (int v = 3; v >= 0; v--) this[0x24FA + v] = this[0x24A7 + v];
        Restart();
    }

    /// <summary>$2559: the voices' pointers from the start offset, the end, and every voice due a note.</summary>
    void Restart()
    {
        pokey[Vbi.Audctl] = 0;
        On = 3;
        int off = this[0x24A0] | this[0x24A1] << 8;
        p[0] = (ushort)(0x28B2 + off);
        p[1] = (ushort)(0x29D3 + off);
        p[2] = (ushort)(0x2B58 + off);
        p[3] = (ushort)(0x2CD0 + (this[0x24A1] << 8));
        int end = p[0] + (this[0x24A2] | this[0x24A3] << 8);
        this[0x24A4] = (byte)end;
        this[0x24A5] = (byte)(end >> 8);
        Due();
    }

    /// <summary>sub_25AF: every voice takes its next note on the next frame.</summary>
    void Due()
    {
        for (int v = 0; v < 4; v++) this[0x251A + v] = 0;
    }

    /// <summary>sub_2CE1: the pub's tune, from its own place in the voices' notes, quicker and a step quieter.</summary>
    public void InitPub()
    {
        p[0] = 0x29BD;
        p[1] = 0x2B3A;
        p[2] = 0x2CB2;
        p[3] = 0x2CD0;
        this[0x2506] = 0x0C;
        this[0x2507] = this[0x2508] = 0x06;
        for (int v = 3; v >= 0; v--) this[0x24FA + v] = (byte)(this[0x24A7 + v] - 1);
        Due();
    }

    /// <summary>v03 ($25BE): one frame of the score's tune. At the end it plays again, from the start
    /// offset 4 the second time and after.</summary>
    public void Tick()
    {
        if (this[0x251A] == 0)
        {
            if (p[0] == (this[0x24A4] | this[0x24A5] << 8))
            {
                pokey[Vbi.Audc(1)] = pokey[Vbi.Audc(2)] = pokey[Vbi.Audc(3)] = pokey[Vbi.Audc(4)] = 0;
                if (this[0x24A0] == 2)
                {
                    this[0x24A0] += 2;
                    this[0x24A2] -= 2;
                }
                Restart();
                return;
            }
            Note1();
        }
        Rest();
    }

    /// <summary>$2D29: one frame of the pub's tune; true once voice 1 reaches its last note ($29D2), which
    /// still plays this frame (the caller then silences the voices).</summary>
    public bool PubTick()
    {
        bool end = false;
        if (this[0x251A] == 0)
        {
            end = p[0] == 0x29D2;
            Note1();
        }
        Rest();
        return end;
    }

    // $25F8: voice 1's next note.
    void Note1() => Note(0, 0x24AB);

    // $2646 on: voices 2 to 4's next notes when due (2 and 3 wrap round their own loops, 4 too), then
    // every voice's envelope.
    void Rest()
    {
        if (this[0x251B] == 0)
        {
            if (p[1] == 0x2B57) p[1] = 0x29D3;
            Note(1, 0x24C9);
        }
        if (this[0x251C] == 0)
        {
            if (p[2] == 0x2CCF) p[2] = 0x2B58;
            Note(2, 0x24C9);
        }
        if (this[0x251D] == 0)
        {
            if (p[3] == 0x2CE0) p[3] = 0x2CD0;
            pokey[Vbi.Audf(4)] = this[p[3]++];
            Start(3, this[0x24FD]);
        }
        for (int v = 0; v < 4; v++) Envelope(v);
    }

    /// <summary>A note byte: the low six bits pick the frequency, bit 7 means a length byte follows and
    /// bit 6 makes it a rest.</summary>
    void Note(int v, int notes)
    {
        byte b = this[p[v]];
        pokey[Vbi.Audf(v + 1)] = this[notes + (b & 0x3F)];
        if ((b & 0x80) != 0) this[0x2506 + v] = this[++p[v]];
        p[v]++;
        Start(v, (b & 0x40) != 0 ? (byte)0 : this[0x24FA + v]);
    }

    void Start(int v, byte volume)
    {
        this[0x250A + v] = volume;
        this[0x250E + v] = this[0x24FE + v];
        this[0x2512 + v] = this[0x2502 + v];
        this[0x2516 + v] = this[0x2506 + v];
        this[0x251A + v] = 1;
    }

    /// <summary>$2760 on, per voice: the volume holds, then falls a step every so many frames (or drops
    /// straight to silence with no decay rate) until the note's length runs out.</summary>
    void Envelope(int v)
    {
        int c = Vbi.Audc(v + 1);
        pokey[c] = this[0x250A + v];
        if (this[0x250E + v] != 0) this[0x250E + v]--;
        else if ((this[0x250A + v] & 0x0F) != 0)
        {
            if (this[0x2502 + v] == 0) pokey[c] = (byte)(this[0x24FA + v] & 0xF0);
            else if (this[0x2512 + v] != 0) this[0x2512 + v]--;
            else
            {
                this[0x2512 + v] = this[0x2502 + v];
                pokey[c] = --this[0x250A + v];
            }
        }
        if (this[0x2516 + v] == 1) this[0x251A + v] = 0;
        else this[0x2516 + v]--;
    }
}
