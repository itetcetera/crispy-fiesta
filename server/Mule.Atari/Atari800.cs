namespace Mule.Atari;

/// <summary>Source of the POKEY RANDOM register.</summary>
public interface IRandomByteSource
{
    byte NextByte();
}

/// <summary>
/// A scanline-level Atari 800 (NTSC): 6502, ANTIC display list and DMA, display list and vertical blank
/// interrupts, GTIA player/missile/playfield collisions, POKEY RANDOM, joysticks. There is no picture or
/// sound output: the machine only produces what the game logic can observe.
/// </summary>
public sealed class Atari800 : IBus
{
    public const int CyclesPerLine = 114, Lines = 262, VblankLine = 248, FirstDisplayLine = 8;

    public readonly byte[] Ram = new byte[65536];
    public readonly Cpu6502 Cpu;
    public IRandomByteSource Random;

    /// <summary>Joystick directions per port, 4 bits, a clear bit means pushed (1 up, 2 down, 4 left, 8 right).</summary>
    public readonly byte[] Sticks = { 15, 15, 15, 15 };
    /// <summary>Joystick buttons per port (true = pressed).</summary>
    public readonly bool[] Buttons = new bool[4];

    /// <summary>Called for each RANDOM read with the address of the reading instruction.</summary>
    public Func<ushort, byte?>? RandomOverride;

    public long Frame { get; private set; }

    /// <summary>Checked after every instruction; when it returns true the current scanline stops early.</summary>
    public Func<Cpu6502, bool>? Halt;
    public bool Halted { get; private set; }

    /// <summary>Runs after every instruction.</summary>
    public Action<Cpu6502>? AfterStep;

    /// <summary>True while the CPU is inside the vertical blank interrupt (until its RTI).</summary>
    public bool InVblank { get; private set; }
    byte _vblankS;
    public int Line { get; private set; }

    // GTIA
    readonly byte[] _hposp = new byte[4], _hposm = new byte[4], _sizep = new byte[4], _grafp = new byte[4];
    byte _sizem, _grafm, _prior, _vdelay, _gractl;
    readonly byte[] _collision = new byte[16]; // M0PF..M3PF, P0PF..P3PF, M0PL..M3PL, P0PL..P3PL
    readonly byte[] _color = new byte[9];      // COLPM0..3, COLPF0..3, COLBK

    // ANTIC
    byte _dmactl, _chactl, _hscrol, _vscrol, _pmbase, _chbase, _nmien, _nmist = 0x1F;
    ushort _dlist, _dlPc, _memScan;
    bool _wsync;
    int _modeLineLeft;      // scanlines left in the current mode line
    int _modeLineRow;       // scanline index within the current mode line
    byte _mode, _dlInstr;
    bool _dlJvbWait;
    int _cycleDebt;

    /// <summary>Playfield pixels for the current scanline in color clocks (0 = background, 1..4 = PF0..PF3).</summary>
    readonly byte[] _pf = new byte[228];

    /// <summary>The POKEY sound registers as last written: AUDF1, AUDC1 ... AUDF4, AUDC4, AUDCTL.</summary>
    public readonly byte[] Pokey = new byte[9];

    public Atari800(byte[] memoryImage, IRandomByteSource random)
    {
        if (memoryImage.Length != 65536) throw new ArgumentException("64 KB image required");
        memoryImage.CopyTo(Ram, 0);
        Random = random;
        Cpu = new Cpu6502(this);
        // Hardware registers start from the shadow values the OS would copy in.
        _dlist = (ushort)(Ram[0x230] | (Ram[0x231] << 8));
        _dmactl = Ram[0x22F];
        _chbase = Ram[0x2F4];
        _chactl = Ram[0x2F3];
        _prior = Ram[0x26F];
        _nmien = 0xC0;
    }

    // ---------------------------------------------------------------- bus

    public byte Read(ushort a)
    {
        if (a < 0xD000 || a >= 0xD800) return Ram[a];
        switch (a & 0xFF00)
        {
            case 0xD000: // GTIA
                {
                    int r = a & 0x1F;
                    if (r < 0x10) return _collision[r];
                    if (r >= 0x10 && r <= 0x13) return Buttons[r - 0x10] ? (byte)0 : (byte)1;
                    if (r == 0x14) return 0x0F; // PAL: NTSC
                    if (r == 0x1F) return 0x07; // CONSOL: no keys
                    return 0xFF;
                }
            case 0xD200: // POKEY
                {
                    int r = a & 0x0F;
                    if (r == 0x0A)
                    {
                        var o = RandomOverride?.Invoke(Cpu.OpPC);
                        return o ?? Random.NextByte();
                    }
                    if (r == 0x0E) return 0xFF; // IRQST: nothing pending
                    if (r == 0x0F) return 0xFF; // SKSTAT
                    if (r == 0x09) return 0xFF; // KBCODE
                    if (r < 8) return 228;      // POTs: paddles not connected
                    return 0xFF;
                }
            case 0xD300: // PIA
                {
                    int r = a & 3;
                    if (r == 0) return (byte)(Sticks[0] | (Sticks[1] << 4));
                    if (r == 1) return (byte)(Sticks[2] | (Sticks[3] << 4));
                    return 0x3C;
                }
            case 0xD400: // ANTIC
                {
                    int r = a & 0x0F;
                    if (r == 0x0B) return (byte)(Line >> 1);
                    if (r == 0x0F) return _nmist;
                    return 0xFF;
                }
        }
        return 0xFF;
    }

    public void Write(ushort a, byte v)
    {
        if (a < 0xD000 || a >= 0xD800) { Ram[a] = v; return; }
        switch (a & 0xFF00)
        {
            case 0xD000:
                {
                    int r = a & 0x1F;
                    if (r < 4) _hposp[r] = v;
                    else if (r < 8) _hposm[r - 4] = v;
                    else if (r < 0x0C) _sizep[r - 8] = v;
                    else if (r == 0x0C) _sizem = v;
                    else if (r < 0x11) _grafp[r - 0x0D] = v;
                    else if (r == 0x11) _grafm = v;
                    else if (r >= 0x12 && r <= 0x1A) _color[r - 0x12] = v;
                    else if (r == 0x1B) _prior = v;
                    else if (r == 0x1C) _vdelay = v;
                    else if (r == 0x1D) _gractl = v;
                    else if (r == 0x1E) Array.Clear(_collision);
                    break;
                }
            case 0xD200: // POKEY: the sound registers, for the sound tests
                {
                    int r = a & 0x0F;
                    if (r < Pokey.Length) Pokey[r] = v;
                    break;
                }
            case 0xD400:
                {
                    int r = a & 0x0F;
                    switch (r)
                    {
                        case 0x00: _dmactl = v; break;
                        case 0x01: _chactl = v; break;
                        case 0x02: _dlist = (ushort)((_dlist & 0xFF00) | v); break;
                        case 0x03: _dlist = (ushort)((_dlist & 0x00FF) | (v << 8)); break;
                        case 0x04: _hscrol = (byte)(v & 15); break;
                        case 0x05: _vscrol = (byte)(v & 15); break;
                        case 0x07: _pmbase = v; break;
                        case 0x09: _chbase = v; break;
                        case 0x0A: _wsync = true; break;
                        case 0x0E: _nmien = v; break;
                        case 0x0F: _nmist = 0x1F; break; // NMIRES
                    }
                    break;
                }
        }
    }

    // ---------------------------------------------------------------- timing

    /// <summary>
    /// Calls the subroutine at <paramref name="addr"/> with the machine running (interrupts, DMA) and
    /// returns when it returns. The scanline in progress is left unfinished.
    /// </summary>
    public void CallRoutine(ushort addr, long maxFrames = 100_000)
    {
        const ushort sentinel = 0xFFFE;
        byte startS = Cpu.S;
        Ram[0x100 + Cpu.S--] = (byte)((sentinel - 1) >> 8);
        Ram[0x100 + Cpu.S--] = (byte)((sentinel - 1) & 0xFF);
        Cpu.PC = addr;
        Halted = false;
        var outer = Halt;
        Halt = c => c.PC == sentinel && c.S == startS;
        long end = Frame + maxFrames;
        try
        {
            while (!Halted)
            {
                RunLine();
                if (Frame > end) throw new TimeoutException($"Routine ${addr:X4} did not return (PC=${Cpu.PC:X4})");
            }
        }
        finally { Halt = outer; }
    }

    /// <summary>Runs one whole frame (262 scanlines).</summary>
    public void RunFrame()
    {
        for (int i = 0; i < Lines; i++) RunLine();
    }

    /// <summary>Runs one scanline: ANTIC/GTIA work for the line, interrupts, then the CPU's share of cycles.</summary>
    public void RunLine()
    {
        int dma = 9; // memory refresh
        if (Line == 0)
        {
            _dlPc = _dlist;
            _modeLineLeft = 0;
            _dlJvbWait = false;
        }
        bool display = Line >= FirstDisplayLine && Line < VblankLine && (_dmactl & 0x20) != 0;
        Array.Clear(_pf);
        if (Picture != null) Array.Clear(_hires);
        _lineHires = false;
        bool dli = false;
        if (display && !_dlJvbWait)
        {
            if (_modeLineLeft == 0) dma += FetchDisplayListInstruction();
            if (_modeLineLeft > 0)
            {
                dma += RenderPlayfieldLine();
                _lineHires = _mode is 2 or 3 && PlayfieldWidth != 0;
                if (_modeLineLeft == 1 && (_dlInstr & 0x80) != 0) dli = true;
                _modeLineRow++;
                _modeLineLeft--;
            }
        }
        if (Line >= FirstDisplayLine && Line < VblankLine) dma += LoadPlayerMissileData();
        ComputeCollisions();
        if (Picture != null) DrawLine();

        if (dli && (_nmien & 0x80) != 0) { _nmist = (byte)(0x80 | 0x1F); Cpu.Nmi(); }
        if (Line == VblankLine)
        {
            _nmist = 0x40 | 0x1F;
            if ((_nmien & 0x40) != 0)
            {
                if (!InVblank) { InVblank = true; _vblankS = Cpu.S; }
                Cpu.Nmi();
            }
        }

        int budget = CyclesPerLine - dma - _cycleDebt;
        int used = 0;
        _wsync = false;
        while (used < budget)
        {
            used += Cpu.Step();
            if (InVblank && Cpu.S == _vblankS) InVblank = false;
            AfterStep?.Invoke(Cpu);
            if (Halt != null && Halt(Cpu)) { Halted = true; return; }
            if (_wsync) { _wsync = false; used = Math.Max(used, budget); break; }
        }
        _cycleDebt = Math.Max(0, used - budget);

        Line++;
        if (Line == Lines)
        {
            Line = 0;
            Frame++;
            if (Picture != null)
            {
                Array.Copy(_drawing, Picture, Picture.Length);
                Array.Copy(_drawingPf, PicturePlayfield!, Picture.Length);
                Array.Copy(_drawingPm, PictureSprites!, Picture.Length);
                for (int i = 0; i < Lines; i++) _lineColors[i].CopyTo(LineColors[i], 0);
            }
        }
    }

    // ---------------------------------------------------------------- ANTIC

    int FetchDisplayListInstruction()
    {
        int dma = 1;
        _dlInstr = Ram[_dlPc++];
        _mode = (byte)(_dlInstr & 0x0F);
        _modeLineRow = 0;
        if (_mode == 0)
        {
            _modeLineLeft = ((_dlInstr >> 4) & 7) + 1;
            return dma;
        }
        if (_mode == 1)
        {
            ushort target = (ushort)(Ram[_dlPc] | (Ram[(ushort)(_dlPc + 1)] << 8));
            dma += 2;
            _dlPc = target;
            if ((_dlInstr & 0x40) != 0) { _dlJvbWait = true; _modeLineLeft = 0; }
            else _modeLineLeft = 1; // a JMP shows one blank line
            return dma;
        }
        if ((_dlInstr & 0x40) != 0)
        {
            _memScan = (ushort)(Ram[_dlPc] | (Ram[(ushort)(_dlPc + 1)] << 8));
            _dlPc += 2;
            dma += 2;
        }
        _modeLineLeft = ModeHeight(_mode);
        return dma;
    }

    static int ModeHeight(int mode) => mode switch
    {
        2 => 8, 3 => 10, 4 => 8, 5 => 16, 6 => 8, 7 => 16,
        8 => 8, 9 => 4, 10 => 4, 11 => 2, 12 => 1, 13 => 2, 14 => 1, 15 => 1,
        _ => 1,
    };

    /// <summary>Bytes fetched per mode line at normal width.</summary>
    static int ModeBytes(int mode) => mode switch
    {
        2 or 3 or 4 or 5 => 40, 6 or 7 => 20, 8 or 9 => 10, 10 or 11 or 12 => 20, _ => 40,
    };

    int PlayfieldWidth => (_dmactl & 3) switch { 1 => 128, 2 => 160, 3 => 192, _ => 0 };
    int PlayfieldLeft => (_dmactl & 3) switch { 1 => 64, 2 => 48, 3 => 32, _ => 0 };

    int RenderPlayfieldLine()
    {
        if (_mode < 2 || PlayfieldWidth == 0) return 0;
        int width = PlayfieldWidth;
        int bytes = ModeBytes(_mode) * width / 160;
        bool hs = (_dlInstr & 0x10) != 0;
        int fetchBytes = hs && (_dmactl & 3) != 3 ? bytes + bytes / 4 : bytes;
        int left = PlayfieldLeft - (hs ? (16 - 2 * _hscrol) : 0);
        bool lastRow = _modeLineLeft == 1;
        int dma = 0;
        bool charMode = _mode <= 7;
        if (_modeLineRow == 0) dma += fetchBytes;
        int clocksPerByte = width / bytes; // color clocks per fetched byte
        for (int i = 0; i < fetchBytes; i++)
        {
            byte b = Ram[(ushort)(_memScan + i)];
            int x0 = left + i * clocksPerByte;
            if (charMode)
            {
                int row = CharRow();
                RenderChar(b, row, x0, clocksPerByte);
            }
            else RenderMapByte(b, x0, clocksPerByte);
        }
        if (charMode) dma += fetchBytes;
        if (lastRow) _memScan = (ushort)((_memScan & 0xF000) | ((_memScan + fetchBytes) & 0x0FFF));
        return dma;
    }

    int CharRow()
    {
        int h = ModeHeight(_mode);
        int r = _mode == 5 || _mode == 7 ? _modeLineRow >> 1 : _modeLineRow;
        if ((_chactl & 4) != 0) r = (_mode == 5 || _mode == 7 ? 7 : (h - 1)) - r;
        return r;
    }

    void RenderChar(byte name, int row, int x0, int clocks)
    {
        if (_mode == 6 || _mode == 7)
        {
            int baseAddr = (_chbase & 0xFE) << 8;
            byte bits = Ram[(ushort)(baseAddr + (name & 0x3F) * 8 + (row & 7))];
            byte color = (byte)((name >> 6) + 1);
            for (int p = 0; p < 8; p++)
                if ((bits & (0x80 >> p)) != 0) Plot(x0 + p * clocks / 8, Math.Max(1, clocks / 8), color);
            return;
        }
        int fontBase = (_chbase & 0xFC) << 8;
        int r = row;
        if (_mode == 3) r = row < 8 ? row : row - 8; // simplified descenders
        byte data = Ram[(ushort)(fontBase + (name & 0x7F) * 8 + (r & 7))];
        if (_mode == 2 || _mode == 3)
        {
            if ((name & 0x80) != 0 && (_chactl & 2) != 0) data ^= 0xFF;
            if ((name & 0x80) != 0 && (_chactl & 1) != 0) data = 0;
            // Hi-res pixels collide as PF2; two pixels per color clock.
            for (int p = 0; p < 4; p++)
                if ((data & (0xC0 >> (2 * p))) != 0) Plot(x0 + p, 1, 3);
            if (Picture != null)
                for (int p = 0; p < 8; p++)
                    if ((data & (0x80 >> p)) != 0 && x0 * 2 + p is >= 0 and < 456) _hires[x0 * 2 + p] = 1;
            return;
        }
        // Modes 4/5: four 2-bit pixels per character.
        for (int p = 0; p < 4; p++)
        {
            int v = (data >> (6 - 2 * p)) & 3;
            if (v == 0) continue;
            byte color = v == 3 && (name & 0x80) != 0 ? (byte)4 : (byte)v;
            Plot(x0 + p, 1, color);
        }
    }

    void RenderMapByte(byte b, int x0, int clocks)
    {
        // Map modes: 2 bits per pixel (modes 8, 10, 13, 14) or 1 bit (9, 11, 12, 15).
        bool twoBit = _mode is 8 or 10 or 13 or 14;
        int pixels = twoBit ? 4 : 8;
        int w = Math.Max(1, clocks / pixels);
        for (int p = 0; p < pixels; p++)
        {
            int v = twoBit ? (b >> (6 - 2 * p)) & 3 : (b >> (7 - p)) & 1;
            if (v == 0) continue;
            byte color = twoBit ? (byte)v : (byte)(_mode == 15 ? 3 : 1);
            Plot(x0 + p * w, w, color);
        }
    }

    void Plot(int x, int w, byte color)
    {
        int left = PlayfieldLeft, right = left + PlayfieldWidth;
        for (int i = 0; i < w; i++)
        {
            int xx = x + i;
            if (xx >= left && xx < right && xx >= 0 && xx < 228) _pf[xx] = color;
        }
    }

    // ---------------------------------------------------------------- GTIA

    int LoadPlayerMissileData()
    {
        int dma = 0;
        bool single = (_dmactl & 0x10) != 0;
        int pmBase = single ? (_pmbase & 0xF8) << 8 : (_pmbase & 0xFC) << 8;
        int lineIndex = single ? Line : Line >> 1;
        if ((_dmactl & 0x04) != 0 || (_dmactl & 0x08) != 0)
        {
            dma++;
            if ((_gractl & 1) != 0)
            {
                int ma = single ? pmBase + 0x300 + lineIndex : pmBase + 0x180 + lineIndex;
                _grafm = Ram[(ushort)ma];
            }
        }
        if ((_dmactl & 0x08) != 0)
        {
            dma += 4;
            if ((_gractl & 2) != 0)
                for (int n = 0; n < 4; n++)
                {
                    bool delayed = !single && (_vdelay & (0x10 << n)) != 0 && (Line & 1) == 0;
                    if (delayed) continue;
                    int pa = single ? pmBase + 0x400 + n * 0x100 + lineIndex : pmBase + 0x200 + n * 0x80 + lineIndex;
                    _grafp[n] = Ram[(ushort)pa];
                }
        }
        return dma;
    }

    readonly byte[] _pmMask = new byte[228]; // bit n = player n, bit 4+n = missile n

    /// <summary>For studying the display: called on each displayed scanline before its collisions.</summary>
    public Action<int>? OnLine;
    public byte[] DebugPlayfield => _pf;
    public byte[] DebugHposP => _hposp;
    public byte[] DebugGrafP => _grafp;
    public byte[] DebugSizeP => _sizep;
    public byte DebugGrafM => _grafm;
    public byte[] DebugHposM => _hposm;
    public byte DebugSizeM => _sizem;
    /// <summary>The display list as text: each mode line's first scanline, mode, and for the text modes
    /// its characters (internal codes as ASCII, colour bits as a digit prefix in modes 6 and 7).</summary>
    public List<string> DebugDisplayList()
    {
        var r = new List<string>();
        ushort pc = (ushort)(Ram[0x230] | Ram[0x231] << 8), scan = 0;
        int line = FirstDisplayLine;
        for (int guard = 0; guard < 300 && line < VblankLine; guard++)
        {
            byte ins = Ram[pc++];
            int mode = ins & 15;
            if (mode == 0) { line += ((ins >> 4) & 7) + 1; continue; }
            if (mode == 1)
            {
                if ((ins & 0x40) != 0) break;
                pc = (ushort)(Ram[pc] | Ram[(ushort)(pc + 1)] << 8);
                line++;
                continue;
            }
            if ((ins & 0x40) != 0) { scan = (ushort)(Ram[pc] | Ram[(ushort)(pc + 1)] << 8); pc += 2; }
            int bytes = ModeBytes(mode);
            var sb = new System.Text.StringBuilder($"{line,3} m{mode:X}{((ins & 0x80) != 0 ? "*" : " ")} {scan:X4} pf {Convert.ToHexString(LineColors[line], 4, 5)} ");
            if (mode <= 7)
                for (int i = 0; i < bytes; i++)
                {
                    byte c = Ram[(ushort)(scan + i)];
                    int code = mode >= 6 ? c & 0x3F : c & 0x7F;
                    char ch = code < 64 ? (char)(code + 32) : '~';
                    if (mode >= 6 && c != 0) sb.Append((char)('0' + (c >> 6)));
                    sb.Append(ch);
                }
            r.Add(sb.ToString().TrimEnd());
            line += ModeHeight(mode);
            scan += (ushort)bytes;
        }
        return r;
    }

    public string DebugPm => $"dmactl {_dmactl:X2} gractl {_gractl:X2} pmbase {_pmbase:X2} prior {_prior:X2} hposp {Convert.ToHexString(_hposp)} colors {Convert.ToHexString(_color)}";

    void ComputeCollisions()
    {
        if (Line < FirstDisplayLine || Line >= VblankLine) return;
        OnLine?.Invoke(Line);
        Array.Clear(_pmMask);
        for (int n = 0; n < 4; n++)
        {
            byte g = _grafp[n];
            if (g == 0) continue;
            int w = (_sizep[n] & 3) switch { 1 => 2, 3 => 4, _ => 1 };
            for (int b = 0; b < 8; b++)
                if ((g & (0x80 >> b)) != 0)
                    for (int i = 0; i < w; i++)
                    {
                        int x = _hposp[n] + b * w + i;
                        if (x < 228) _pmMask[x] |= (byte)(1 << n);
                    }
        }
        for (int n = 0; n < 4; n++)
        {
            int bits = (_grafm >> (2 * n)) & 3;
            if (bits == 0) continue;
            int w = ((_sizem >> (2 * n)) & 3) switch { 1 => 2, 3 => 4, _ => 1 };
            for (int b = 0; b < 2; b++)
                if ((bits & (2 >> b)) != 0)
                    for (int i = 0; i < w; i++)
                    {
                        int x = _hposm[n] + b * w + i;
                        if (x < 228) _pmMask[x] |= (byte)(0x10 << n);
                    }
        }
        for (int x = 34; x < 222; x++)
        {
            byte m = _pmMask[x];
            if (m == 0) continue;
            byte pf = _pf[x];
            byte pfBit = pf == 0 ? (byte)0 : (byte)(1 << (pf - 1));
            byte players = (byte)(m & 0x0F);
            for (int n = 0; n < 4; n++)
            {
                if ((m & (1 << n)) != 0)
                {
                    _collision[4 + n] |= pfBit;
                    _collision[12 + n] |= (byte)(players & ~(1 << n));
                }
                if ((m & (0x10 << n)) != 0)
                {
                    _collision[n] |= pfBit;
                    _collision[8 + n] |= players;
                }
            }
        }
    }

    // ---------------------------------------------------------------- picture

    /// <summary>Picture width (half color clocks 64 to 448, as ANTIC's wide playfield) and height (the
    /// displayed lines 8 to 247).</summary>
    public const int PictureWidth = 384, PictureHeight = 240, PictureLeft = 32;

    /// <summary>The last whole frame as 0xRRGGBB pixels, once <see cref="EnablePicture"/> was called;
    /// for reference shots of the original's screens (tests only, not the game).</summary>
    public uint[]? Picture { get; private set; }
    /// <summary>The same frame without the players and missiles.</summary>
    public uint[]? PicturePlayfield { get; private set; }
    /// <summary>The same frame's players and missiles alone, as 0xAARRGGBB (0 where none shows).</summary>
    public uint[]? PictureSprites { get; private set; }
    uint[] _drawing = Array.Empty<uint>(), _drawingPf = Array.Empty<uint>(), _drawingPm = Array.Empty<uint>();
    readonly byte[] _hires = new byte[456];
    bool _lineHires;

    public void EnablePicture()
    {
        Picture = new uint[PictureWidth * PictureHeight];
        _drawing = new uint[PictureWidth * PictureHeight];
        PicturePlayfield = new uint[PictureWidth * PictureHeight];
        _drawingPf = new uint[PictureWidth * PictureHeight];
        PictureSprites = new uint[PictureWidth * PictureHeight];
        _drawingPm = new uint[PictureWidth * PictureHeight];
    }

    /// <summary>Composes the current scanline: playfield (hi-res text as playfield 1's luminance on
    /// playfield 2), then the players and missiles by the priority register.</summary>
    /// <summary>The colour registers on each scanline of the last whole frame (COLPM0-3, COLPF0-3, COLBK).</summary>
    public readonly byte[][] LineColors = Enumerable.Range(0, Lines).Select(_ => new byte[9]).ToArray();
    readonly byte[][] _lineColors = Enumerable.Range(0, Lines).Select(_ => new byte[9]).ToArray();

    void DrawLine()
    {
        int y = Line - FirstDisplayLine;
        if (y < 0 || y >= PictureHeight) return;
        _color.CopyTo(_lineColors[Line], 0);
        bool fifth = (_prior & 0x10) != 0;
        for (int px = 0; px < PictureWidth; px++)
        {
            int x = PictureLeft + px / 2;
            byte pf = _pf[x];
            byte c = pf == 0 ? _color[8] : _color[3 + pf];
            if (_lineHires && x >= PlayfieldLeft && x < PlayfieldLeft + PlayfieldWidth)
                c = _hires[PictureLeft * 2 + px] != 0 ? (byte)((_color[6] & 0xF0) | (_color[5] & 0x0E)) : _color[6];
            _drawingPf[y * PictureWidth + px] = Rgb(c);
            uint sprite = 0;
            byte m = _pmMask[x];
            if (m != 0)
            {
                int pl = m & 0x0F, mi = m >> 4;
                if (fifth && mi != 0) { sprite = 0xFF000000 | Rgb(_color[7]); if (pf == 0 || (_prior & 4) == 0) c = _color[7]; mi = 0; }
                pl |= mi; // a missile shows in its player's color
                if (pl != 0)
                {
                    int n = (pl & 1) != 0 ? 0 : (pl & 2) != 0 ? 1 : (pl & 4) != 0 ? 2 : 3;
                    byte sc = _color[n];
                    if ((_prior & 0x20) != 0 && (n == 0 && (pl & 2) != 0 || n == 2 && (pl & 8) != 0)) sc |= _color[n + 1];
                    sprite = 0xFF000000 | Rgb(sc);
                    bool pfFirst = pf != 0 && ((_prior & 4) != 0 || ((_prior & 8) != 0 && pf <= 2) || ((_prior & 2) != 0 && n >= 2));
                    if (!pfFirst) c = sc;
                }
            }
            _drawing[y * PictureWidth + px] = Rgb(c);
            _drawingPm[y * PictureWidth + px] = sprite;
        }
    }

    static readonly double[] LumaMult =
    {
        0.6941, 0.7091, 0.7241, 0.7401, 0.7560, 0.7741, 0.7931, 0.8121,
        0.8260, 0.8470, 0.8700, 0.8930, 0.9160, 0.9420, 0.9690, 1.0000,
    };

    /// <summary>An NTSC Atari color (hue in the high nibble, luminance in the low) as 0xRRGGBB, by the
    /// Atari800 emulator's default NTSC palette (colorburst at 303 degrees in YIQ, 26.8 degrees a hue,
    /// gamma 2.35).</summary>
    public static uint Rgb(byte c)
    {
        int hue = c >> 4, lum = c & 0x0F;
        double angle = (303 + (hue - 1) * 26.8) * Math.PI / 180, sat = hue == 0 ? 0 : 0.175;
        double i = Math.Cos(angle) * sat, q = Math.Sin(angle) * sat;
        double y = (LumaMult[lum] - LumaMult[0]) / (LumaMult[15] - LumaMult[0]);
        static int Ch(double v)
        {
            v = Math.Pow(Math.Clamp(v, 0, 1), 2.35);
            v = v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055;
            return (int)Math.Clamp(v * 255, 0, 255);
        }
        int r = Ch(y + 0.9563 * i + 0.6210 * q), g = Ch(y - 0.2721 * i - 0.6474 * q), b = Ch(y - 1.1070 * i + 1.7046 * q);
        return (uint)(r << 16 | g << 8 | b);
    }
}
