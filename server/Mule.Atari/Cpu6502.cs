namespace Mule.Atari;

/// <summary>Memory and I/O as seen by the CPU.</summary>
public interface IBus
{
    byte Read(ushort addr);
    void Write(ushort addr, byte value);
}

/// <summary>
/// NMOS 6502 core (documented opcodes, decimal mode) with base cycle counts.
/// </summary>
public sealed class Cpu6502
{
    readonly IBus _bus;
    public byte A, X, Y, S = 0xFF;
    public ushort PC;
    public bool C, Z, I, D, V, N;
    public long Cycles;
    /// <summary>Address of the instruction being executed.</summary>
    public ushort OpPC;

    // Base cycle counts per opcode (page-crossing penalties ignored); 0 = unused opcode.
    static readonly byte[] CycleTable =
    {
        7,6,0,0,0,3,5,0,3,2,2,0,0,4,6,0, 2,5,0,0,0,4,6,0,2,4,0,0,0,4,7,0,
        6,6,0,0,3,3,5,0,4,2,2,0,4,4,6,0, 2,5,0,0,0,4,6,0,2,4,0,0,0,4,7,0,
        6,6,0,0,0,3,5,0,3,2,2,0,3,4,6,0, 2,5,0,0,0,4,6,0,2,4,0,0,0,4,7,0,
        6,6,0,0,0,3,5,0,4,2,2,0,5,4,6,0, 2,5,0,0,0,4,6,0,2,4,0,0,0,4,7,0,
        0,6,0,0,3,3,3,0,2,0,2,0,4,4,4,0, 2,6,0,0,4,4,4,0,2,5,2,0,0,5,0,0,
        2,6,2,0,3,3,3,0,2,2,2,0,4,4,4,0, 2,5,0,0,4,4,4,0,2,4,2,0,4,4,4,0,
        2,6,0,0,3,3,5,0,2,2,2,0,4,4,6,0, 2,5,0,0,0,4,6,0,2,4,0,0,0,4,7,0,
        2,6,0,0,3,3,5,0,2,2,2,0,4,4,6,0, 2,5,0,0,0,4,6,0,2,4,0,0,0,4,7,0,
    };

    public Cpu6502(IBus bus) => _bus = bus;

    public byte Read(ushort a) => _bus.Read(a);
    public void Write(ushort a, byte v) => _bus.Write(a, v);

    ushort Read16(ushort a) => (ushort)(Read(a) | (Read((ushort)(a + 1)) << 8));
    ushort Read16Zp(byte a) => (ushort)(Read(a) | (Read((byte)(a + 1)) << 8));

    void Push(byte v) { Write((ushort)(0x100 + S), v); S--; }
    byte Pop() { S++; return Read((ushort)(0x100 + S)); }

    public byte P
    {
        get => (byte)((N ? 0x80 : 0) | (V ? 0x40 : 0) | 0x20 | (D ? 0x08 : 0) | (I ? 0x04 : 0) | (Z ? 0x02 : 0) | (C ? 0x01 : 0));
        set { N = (value & 0x80) != 0; V = (value & 0x40) != 0; D = (value & 0x08) != 0; I = (value & 0x04) != 0; Z = (value & 0x02) != 0; C = (value & 0x01) != 0; }
    }

    void SetNZ(byte v) { Z = v == 0; N = (v & 0x80) != 0; }

    /// <summary>Non-maskable interrupt (ANTIC display list or vertical blank).</summary>
    public void Nmi()
    {
        Push((byte)(PC >> 8)); Push((byte)PC); Push((byte)(P & ~0x10));
        I = true;
        PC = Read16(0xFFFA);
        Cycles += 7;
    }

    /// <summary>
    /// Calls the subroutine at <paramref name="addr"/> and runs until it returns (or reaches
    /// <paramref name="stopAt"/>). <paramref name="tick"/> runs between instructions (hardware timing).
    /// </summary>
    public void Call(ushort addr, long maxSteps = 50_000_000, ushort? stopAt = null, Action? tick = null)
    {
        const ushort sentinel = 0xFFFE;
        var startS = S;
        Push((byte)((sentinel - 1) >> 8));
        Push((byte)((sentinel - 1) & 0xFF));
        PC = addr;
        for (long i = 0; i < maxSteps; i++)
        {
            if (PC == sentinel && S == startS) return;
            if (stopAt.HasValue && PC == stopAt.Value) { S = startS; return; }
            tick?.Invoke();
            Step();
        }
        throw new TimeoutException($"Routine ${addr:X4} did not return (PC=${PC:X4})");
    }

    /// <summary>Executes one instruction and returns its cycle count.</summary>
    public int Step()
    {
        OpPC = PC;
        byte op = Read(PC++);
        int cyc = CycleTable[op] == 0 ? 2 : CycleTable[op];
        Cycles += cyc;
        switch (op)
        {
            // LDA
            case 0xA9: A = Imm(); SetNZ(A); break;
            case 0xA5: A = Read(Zp()); SetNZ(A); break;
            case 0xB5: A = Read(ZpX()); SetNZ(A); break;
            case 0xAD: A = Read(Abs()); SetNZ(A); break;
            case 0xBD: A = Read(AbsX()); SetNZ(A); break;
            case 0xB9: A = Read(AbsY()); SetNZ(A); break;
            case 0xA1: A = Read(IndX()); SetNZ(A); break;
            case 0xB1: A = Read(IndY()); SetNZ(A); break;
            // LDX
            case 0xA2: X = Imm(); SetNZ(X); break;
            case 0xA6: X = Read(Zp()); SetNZ(X); break;
            case 0xB6: X = Read(ZpY()); SetNZ(X); break;
            case 0xAE: X = Read(Abs()); SetNZ(X); break;
            case 0xBE: X = Read(AbsY()); SetNZ(X); break;
            // LDY
            case 0xA0: Y = Imm(); SetNZ(Y); break;
            case 0xA4: Y = Read(Zp()); SetNZ(Y); break;
            case 0xB4: Y = Read(ZpX()); SetNZ(Y); break;
            case 0xAC: Y = Read(Abs()); SetNZ(Y); break;
            case 0xBC: Y = Read(AbsX()); SetNZ(Y); break;
            // STA
            case 0x85: Write(Zp(), A); break;
            case 0x95: Write(ZpX(), A); break;
            case 0x8D: Write(Abs(), A); break;
            case 0x9D: Write(AbsX(), A); break;
            case 0x99: Write(AbsY(), A); break;
            case 0x81: Write(IndX(), A); break;
            case 0x91: Write(IndY(), A); break;
            // STX / STY
            case 0x86: Write(Zp(), X); break;
            case 0x96: Write(ZpY(), X); break;
            case 0x8E: Write(Abs(), X); break;
            case 0x84: Write(Zp(), Y); break;
            case 0x94: Write(ZpX(), Y); break;
            case 0x8C: Write(Abs(), Y); break;
            // Transfers
            case 0xAA: X = A; SetNZ(X); break;
            case 0xA8: Y = A; SetNZ(Y); break;
            case 0x8A: A = X; SetNZ(A); break;
            case 0x98: A = Y; SetNZ(A); break;
            case 0xBA: X = S; SetNZ(X); break;
            case 0x9A: S = X; break;
            // Stack
            case 0x48: Push(A); break;
            case 0x68: A = Pop(); SetNZ(A); break;
            case 0x08: Push((byte)(P | 0x10)); break;
            case 0x28: P = Pop(); break;
            // ADC
            case 0x69: Adc(Imm()); break;
            case 0x65: Adc(Read(Zp())); break;
            case 0x75: Adc(Read(ZpX())); break;
            case 0x6D: Adc(Read(Abs())); break;
            case 0x7D: Adc(Read(AbsX())); break;
            case 0x79: Adc(Read(AbsY())); break;
            case 0x61: Adc(Read(IndX())); break;
            case 0x71: Adc(Read(IndY())); break;
            // SBC
            case 0xE9: Sbc(Imm()); break;
            case 0xE5: Sbc(Read(Zp())); break;
            case 0xF5: Sbc(Read(ZpX())); break;
            case 0xED: Sbc(Read(Abs())); break;
            case 0xFD: Sbc(Read(AbsX())); break;
            case 0xF9: Sbc(Read(AbsY())); break;
            case 0xE1: Sbc(Read(IndX())); break;
            case 0xF1: Sbc(Read(IndY())); break;
            // AND
            case 0x29: A &= Imm(); SetNZ(A); break;
            case 0x25: A &= Read(Zp()); SetNZ(A); break;
            case 0x35: A &= Read(ZpX()); SetNZ(A); break;
            case 0x2D: A &= Read(Abs()); SetNZ(A); break;
            case 0x3D: A &= Read(AbsX()); SetNZ(A); break;
            case 0x39: A &= Read(AbsY()); SetNZ(A); break;
            case 0x21: A &= Read(IndX()); SetNZ(A); break;
            case 0x31: A &= Read(IndY()); SetNZ(A); break;
            // ORA
            case 0x09: A |= Imm(); SetNZ(A); break;
            case 0x05: A |= Read(Zp()); SetNZ(A); break;
            case 0x15: A |= Read(ZpX()); SetNZ(A); break;
            case 0x0D: A |= Read(Abs()); SetNZ(A); break;
            case 0x1D: A |= Read(AbsX()); SetNZ(A); break;
            case 0x19: A |= Read(AbsY()); SetNZ(A); break;
            case 0x01: A |= Read(IndX()); SetNZ(A); break;
            case 0x11: A |= Read(IndY()); SetNZ(A); break;
            // EOR
            case 0x49: A ^= Imm(); SetNZ(A); break;
            case 0x45: A ^= Read(Zp()); SetNZ(A); break;
            case 0x55: A ^= Read(ZpX()); SetNZ(A); break;
            case 0x4D: A ^= Read(Abs()); SetNZ(A); break;
            case 0x5D: A ^= Read(AbsX()); SetNZ(A); break;
            case 0x59: A ^= Read(AbsY()); SetNZ(A); break;
            case 0x41: A ^= Read(IndX()); SetNZ(A); break;
            case 0x51: A ^= Read(IndY()); SetNZ(A); break;
            // CMP / CPX / CPY
            case 0xC9: Cmp(A, Imm()); break;
            case 0xC5: Cmp(A, Read(Zp())); break;
            case 0xD5: Cmp(A, Read(ZpX())); break;
            case 0xCD: Cmp(A, Read(Abs())); break;
            case 0xDD: Cmp(A, Read(AbsX())); break;
            case 0xD9: Cmp(A, Read(AbsY())); break;
            case 0xC1: Cmp(A, Read(IndX())); break;
            case 0xD1: Cmp(A, Read(IndY())); break;
            case 0xE0: Cmp(X, Imm()); break;
            case 0xE4: Cmp(X, Read(Zp())); break;
            case 0xEC: Cmp(X, Read(Abs())); break;
            case 0xC0: Cmp(Y, Imm()); break;
            case 0xC4: Cmp(Y, Read(Zp())); break;
            case 0xCC: Cmp(Y, Read(Abs())); break;
            // BIT
            case 0x24: Bit(Read(Zp())); break;
            case 0x2C: Bit(Read(Abs())); break;
            // INC / DEC memory
            case 0xE6: Rmw(Zp(), v => (byte)(v + 1)); break;
            case 0xF6: Rmw(ZpX(), v => (byte)(v + 1)); break;
            case 0xEE: Rmw(Abs(), v => (byte)(v + 1)); break;
            case 0xFE: Rmw(AbsX(), v => (byte)(v + 1)); break;
            case 0xC6: Rmw(Zp(), v => (byte)(v - 1)); break;
            case 0xD6: Rmw(ZpX(), v => (byte)(v - 1)); break;
            case 0xCE: Rmw(Abs(), v => (byte)(v - 1)); break;
            case 0xDE: Rmw(AbsX(), v => (byte)(v - 1)); break;
            case 0xE8: X++; SetNZ(X); break;
            case 0xCA: X--; SetNZ(X); break;
            case 0xC8: Y++; SetNZ(Y); break;
            case 0x88: Y--; SetNZ(Y); break;
            // Shifts
            case 0x0A: A = Asl(A); break;
            case 0x06: Rmw(Zp(), Asl); break;
            case 0x16: Rmw(ZpX(), Asl); break;
            case 0x0E: Rmw(Abs(), Asl); break;
            case 0x1E: Rmw(AbsX(), Asl); break;
            case 0x4A: A = Lsr(A); break;
            case 0x46: Rmw(Zp(), Lsr); break;
            case 0x56: Rmw(ZpX(), Lsr); break;
            case 0x4E: Rmw(Abs(), Lsr); break;
            case 0x5E: Rmw(AbsX(), Lsr); break;
            case 0x2A: A = Rol(A); break;
            case 0x26: Rmw(Zp(), Rol); break;
            case 0x36: Rmw(ZpX(), Rol); break;
            case 0x2E: Rmw(Abs(), Rol); break;
            case 0x3E: Rmw(AbsX(), Rol); break;
            case 0x6A: A = Ror(A); break;
            case 0x66: Rmw(Zp(), Ror); break;
            case 0x76: Rmw(ZpX(), Ror); break;
            case 0x6E: Rmw(Abs(), Ror); break;
            case 0x7E: Rmw(AbsX(), Ror); break;
            // Jumps
            case 0x4C: PC = Abs(); break;
            case 0x6C:
                {
                    ushort p = Abs();
                    // NMOS page-wrap bug
                    PC = (ushort)(Read(p) | (Read((ushort)((p & 0xFF00) | ((p + 1) & 0xFF))) << 8));
                    break;
                }
            case 0x20:
                {
                    ushort t = Abs();
                    ushort ret = (ushort)(PC - 1);
                    Push((byte)(ret >> 8)); Push((byte)ret);
                    PC = t;
                    break;
                }
            case 0x60: { byte lo = Pop(); byte hi = Pop(); PC = (ushort)(((hi << 8) | lo) + 1); break; }
            case 0x40: { P = Pop(); byte lo = Pop(); byte hi = Pop(); PC = (ushort)((hi << 8) | lo); break; }
            // Branches
            case 0x10: Branch(!N); break;
            case 0x30: Branch(N); break;
            case 0x50: Branch(!V); break;
            case 0x70: Branch(V); break;
            case 0x90: Branch(!C); break;
            case 0xB0: Branch(C); break;
            case 0xD0: Branch(!Z); break;
            case 0xF0: Branch(Z); break;
            // Flags
            case 0x18: C = false; break;
            case 0x38: C = true; break;
            case 0x58: I = false; break;
            case 0x78: I = true; break;
            case 0xB8: V = false; break;
            case 0xD8: D = false; break;
            case 0xF8: D = true; break;
            case 0xEA: break;
            case 0x00: throw new InvalidOperationException($"BRK at ${PC - 1:X4}");
            default: throw new InvalidOperationException($"Unsupported opcode ${op:X2} at ${PC - 1:X4}");
        }
        return cyc;
    }

    byte Imm() => Read(PC++);
    ushort Zp() => Read(PC++);
    ushort ZpX() => (byte)(Read(PC++) + X);
    ushort ZpY() => (byte)(Read(PC++) + Y);
    ushort Abs() { ushort a = Read16(PC); PC += 2; return a; }
    ushort AbsX() => (ushort)(Abs() + X);
    ushort AbsY() => (ushort)(Abs() + Y);
    ushort IndX() => Read16Zp((byte)(Read(PC++) + X));
    ushort IndY() => (ushort)(Read16Zp(Read(PC++)) + Y);

    void Branch(bool cond)
    {
        sbyte off = (sbyte)Read(PC++);
        if (cond) PC = (ushort)(PC + off);
    }

    void Rmw(ushort a, Func<byte, byte> f) { byte v = f(Read(a)); Write(a, v); SetNZ(v); }

    byte Asl(byte v) { C = (v & 0x80) != 0; v <<= 1; SetNZ(v); return v; }
    byte Lsr(byte v) { C = (v & 1) != 0; v >>= 1; SetNZ(v); return v; }
    byte Rol(byte v) { bool c = C; C = (v & 0x80) != 0; v = (byte)((v << 1) | (c ? 1 : 0)); SetNZ(v); return v; }
    byte Ror(byte v) { bool c = C; C = (v & 1) != 0; v = (byte)((v >> 1) | (c ? 0x80 : 0)); SetNZ(v); return v; }

    void Cmp(byte r, byte v) { int t = r - v; C = r >= v; SetNZ((byte)t); }
    void Bit(byte v) { Z = (A & v) == 0; N = (v & 0x80) != 0; V = (v & 0x40) != 0; }

    void Adc(byte v)
    {
        int c = C ? 1 : 0;
        if (!D)
        {
            int sum = A + v + c;
            V = (~(A ^ v) & (A ^ sum) & 0x80) != 0;
            C = sum > 0xFF;
            A = (byte)sum;
            SetNZ(A);
        }
        else
        {
            int lo = (A & 0x0F) + (v & 0x0F) + c;
            int hi = (A & 0xF0) + (v & 0xF0);
            Z = ((A + v + c) & 0xFF) == 0;
            if (lo > 9) { lo += 6; }
            if (lo > 0x0F) hi += 0x10;
            N = (hi & 0x80) != 0;
            V = (~(A ^ v) & (A ^ hi) & 0x80) != 0;
            if (hi > 0x90) hi += 0x60;
            C = hi > 0xFF;
            A = (byte)((hi & 0xF0) | (lo & 0x0F));
        }
    }

    void Sbc(byte v)
    {
        int c = C ? 0 : 1;
        int diff = A - v - c;
        if (!D)
        {
            V = ((A ^ v) & (A ^ diff) & 0x80) != 0;
            C = diff >= 0;
            A = (byte)diff;
            SetNZ(A);
        }
        else
        {
            int lo = (A & 0x0F) - (v & 0x0F) - c;
            int hi = (A & 0xF0) - (v & 0xF0);
            V = ((A ^ v) & (A ^ diff) & 0x80) != 0;
            if ((lo & 0x10) != 0) { lo -= 6; hi -= 0x10; }
            if ((hi & 0x100) != 0) hi -= 0x60;
            C = diff >= 0;
            SetNZ((byte)diff);
            A = (byte)((hi & 0xF0) | (lo & 0x0F));
        }
    }
}
