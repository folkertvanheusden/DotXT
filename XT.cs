namespace DotXT;

// TODO this needs a rewrite/clean-up

internal enum RepMode
{
    NotSet,
    REPE_Z,
    REPNZ,
    REP
}

struct State8086
{
    public byte ah { get; set; }
    public byte al { get; set; }
    public byte bh { get; set; }
    public byte bl { get; set; }
    public byte ch { get; set; }
    public byte cl { get; set; }
    public byte dh { get; set; }
    public byte dl { get; set; }

    public ushort si { get; set; }
    public ushort di { get; set; }
    public ushort bp { get; set; }
    public ushort sp { get; set; }

    public ushort ip { get; set; }

    public ushort cs { get; set; }
    public ushort ds { get; set; }
    public ushort es { get; set; }
    public ushort ss { get; set; }

    // replace by an Optional-type when available
    public ushort segment_override { get; set; }
    public bool segment_override_set { get; set; }

    public ushort flags { get; set; }

    public bool in_hlt { get; set; }
    public bool inhibit_interrupts { get; set; }  // for 1 instruction after loading segment registers

    public bool rep { get; set; }
    public bool rep_do_nothing { get; set; }
    public RepMode rep_mode { get; set; }
    public ushort rep_addr { get; set; }
    public byte rep_opcode { get; set; }

    public long clock { get; set; }

    public int crash_counter { get; set; }
};

internal class P8086
{
    private bool _terminate_on_off_the_rails = false;

    private const uint MemMask = 0x00ffffff;

    private Bus _b;
    private readonly IO _io;
    private List<Device> _devices;

    private List<uint> _breakpoints = new();
    private bool _ignore_breakpoints = false;
    private string _stop_reason = "";

    private State8086 _state = new();

    public State8086 GetState()
    {
        return _state;
    }

    public P8086(ref Bus b, ref List<Device> devices, bool run_IO)
    {
        _b = b;
        _devices = devices;
        _io = new IO(b, ref devices, !run_IO);
        _terminate_on_off_the_rails = run_IO;

        // bit 1 of the flags register is always 1
        // https://www.righto.com/2023/02/silicon-reverse-engineering-intel-8086.html
        _state.flags |= 2;
    }

    public string GetStopReason()
    {
        string rc = _stop_reason;
        _stop_reason = "";
        return rc;
    }

    public List<uint> GetBreakpoints()
    {
        return _breakpoints;
    }

    public void AddBreakpoint(uint a)
    {
        _breakpoints.Add(a);
    }

    public void DelBreakpoint(uint a)
    {
        _breakpoints.Remove(a);
    }

    public void ClearBreakpoints()
    {
        _breakpoints.Clear();
    }

    // is only once
    public void SetIgnoreBreakpoints()
    {
        _ignore_breakpoints = true;
    }

    public void Reset()
    {
        _state.cs = 0xf000;
        _state.ip = 0xfff0;
        _state.in_hlt = false;
        _state.segment_override_set = false;
        _state.rep = false;
    }

    public long GetClock()
    {
        return _state.clock;
    }

    public string SegmentAddr(ushort seg, ushort a)
    {
        return $"{seg:X04}:{a:X04}";
    }

    public void SetIP(ushort cs, ushort ip)
    {
        Log.DoLog($"Set CS/IP to {cs:X4}:{ip:X4}", LogLevel.DEBUG);

        _state.cs = cs;
        _state.ip = ip;
    }

    private void FixFlags()
    {
        _state.flags &= 0b1111111111010101;
        _state.flags |= 2;  // bit 1 is always set
        _state.flags |= 0xf000;  // upper 4 bits are always 1
    }

    private byte GetPcByte()
    {
        return ReadMemByte(_state.cs, _state.ip++);
    }

    private ushort GetPcWord()
    {
        ushort v = GetPcByte();
        v |= (ushort)(GetPcByte() << 8);
        return v;
    }

    public ushort GetAX()
    {
        return (ushort)((_state.ah << 8) | _state.al);
    }

    public void SetAX(ushort v)
    {
        _state.ah = (byte)(v >> 8);
        _state.al = (byte)v;
    }

    public ushort GetBX()
    {
        return (ushort)((_state.bh << 8) | _state.bl);
    }

    public void SetBX(ushort v)
    {
        _state.bh = (byte)(v >> 8);
        _state.bl = (byte)v;
    }

    public ushort GetCX()
    {
        return (ushort)((_state.ch << 8) | _state.cl);
    }

    public void SetCX(ushort v)
    {
        _state.ch = (byte)(v >> 8);
        _state.cl = (byte)v;
    }

    public ushort GetDX()
    {
        return (ushort)((_state.dh << 8) | _state.dl);
    }

    public void SetDX(ushort v)
    {
        _state.dh = (byte)(v >> 8);
        _state.dl = (byte)v;
    }

    public void SetSS(ushort v)
    {
        _state.ss = v;
    }

    public void SetCS(ushort v)
    {
        _state.cs = v;
    }

    public void SetDS(ushort v)
    {
        _state.ds = v;
    }

    public void SetES(ushort v)
    {
        _state.es = v;
    }

    public void SetSP(ushort v)
    {
        _state.sp = v;
    }

    public void SetBP(ushort v)
    {
        _state.bp = v;
    }

    public void SetSI(ushort v)
    {
        _state.si = v;
    }

    public void SetDI(ushort v)
    {
        _state.di = v;
    }

    public void SetIP(ushort v)
    {
        _state.ip = v;
    }

    public void SetFlags(ushort v)
    {
        _state.flags = v;
    }

    public ushort GetSS()
    {
        return _state.ss;
    }

    public ushort GetCS()
    {
        return _state.cs;
    }

    public ushort GetDS()
    {
        return _state.ds;
    }

    public ushort GetES()
    {
        return _state.es;
    }

    public ushort GetSP()
    {
        return _state.sp;
    }

    public ushort GetBP()
    {
        return _state.bp;
    }

    public ushort GetSI()
    {
        return _state.si;
    }

    public ushort GetDI()
    {
        return _state.di;
    }

    public ushort GetIP()
    {
        return _state.ip;
    }

    public ushort GetFlags()
    {
        return _state.flags;
    }

    public void SetZSPFlags(byte v)
    {
        SetFlagZ(v == 0);
        SetFlagS((v & 0x80) == 0x80);
        SetFlagP(v);
    }

    private void WriteMemByte(ushort segment, ushort offset, byte v)
    {
        uint a = (uint)(((segment << 4) + offset) & MemMask);
        _state.clock += _b.WriteByte(a, v);
    }

    private void WriteMemWord(ushort segment, ushort offset, ushort v)
    {
        WriteMemByte(segment, offset, (byte)v);
        WriteMemByte(segment, (ushort)(offset + 1), (byte)(v >> 8));
    }

    public byte ReadMemByte(ushort segment, ushort offset)
    {
        uint a = (uint)(((segment << 4) + offset) & MemMask);
        var rc = _b.ReadByte(a);
        _state.clock += rc.Item2;
        return rc.Item1;
    } 

    public ushort ReadMemWord(ushort segment, ushort offset)
    {
        return (ushort)(ReadMemByte(segment, offset) + (ReadMemByte(segment, (ushort)(offset + 1)) << 8));
    } 

    private ushort GetRegister(int reg, bool w)
    {
        if (w)
        {
            if (reg == 0)
                return GetAX();
            if (reg == 1)
                return GetCX();
            if (reg == 2)
                return GetDX();
            if (reg == 3)
                return GetBX();
            if (reg == 4)
                return _state.sp;
            if (reg == 5)
                return _state.bp;
            if (reg == 6)
                return _state.si;
            if (reg == 7)
                return _state.di;
        }
        else
        {
            if (reg == 0)
                return _state.al;
            if (reg == 1)
                return _state.cl;
            if (reg == 2)
                return _state.dl;
            if (reg == 3)
                return _state.bl;
            if (reg == 4)
                return _state.ah;
            if (reg == 5)
                return _state.ch;
            if (reg == 6)
                return _state.dh;
            if (reg == 7)
                return _state.bh;
        }

        Log.DoLog($"reg {reg} w {w} not supported for {nameof(GetRegister)}", LogLevel.WARNING);

        return 0;
    }

    private ushort GetSRegister(int reg)
    {
        reg &= 0b00000011;

        if (reg == 0b000)
            return _state.es;
        if (reg == 0b001)
            return _state.cs;
        if (reg == 0b010)
            return _state.ss;
        if (reg == 0b011)
            return _state.ds;

        Log.DoLog($"reg {reg} not supported for {nameof(GetSRegister)}", LogLevel.WARNING);

        return 0;
    }

    // value, cycles
    private (ushort, int) GetDoubleRegisterMod00(int reg)
    {
        ushort a = 0;
        int cycles = 0;

        if (reg == 0)
        {
            a = (ushort)(GetBX() + _state.si);
            cycles = 7;
        }
        else if (reg == 1)
        {
            a = (ushort)(GetBX() + _state.di);
            cycles = 8;
        }
        else if (reg == 2)
        {
            a = (ushort)(_state.bp + _state.si);
            cycles = 8;
        }
        else if (reg == 3)
        {
            a = (ushort)(_state.bp + _state.di);
            cycles = 7;
        }
        else if (reg == 4)
        {
            a = _state.si;
            cycles = 5;
        }
        else if (reg == 5)
        {
            a = _state.di;
            cycles = 5;
        }
        else if (reg == 6)
        {
            a = GetPcWord();
            cycles = 6;
        }
        else if (reg == 7)
        {
            a = GetBX();
            cycles = 5;
        }
        else
        {
            Log.DoLog($"{nameof(GetDoubleRegisterMod00)} {reg} not implemented", LogLevel.WARNING);
        }

        return (a, cycles);
    }

    // value, cycles
    private (ushort, int, bool, ushort) GetDoubleRegisterMod01_02(int reg, bool word)
    {
        ushort a = 0;
        int cycles = 0;
        bool override_segment = false;
        ushort new_segment = 0;

        if (reg == 6)
        {
            a = _state.bp;
            cycles = 5;
            override_segment = true;
            new_segment = _state.ss;
        }
        else
        {
            (a, cycles) = GetDoubleRegisterMod00(reg);
        }

        short disp = word ? (short)GetPcWord() : (sbyte)GetPcByte();

        return ((ushort)(a + disp), cycles, override_segment, new_segment);
    }

    // value, segment_a_valid, segment/, address of value, number of cycles
    private (ushort, bool, ushort, ushort, int) GetRegisterMem(int reg, int mod, bool w)
    {
        if (mod == 0)
        {
            (ushort a, int cycles) = GetDoubleRegisterMod00(reg);

            ushort segment = _state.segment_override_set ? _state.segment_override : _state.ds;

            if (_state.segment_override_set == false && (reg == 2 || reg == 3)) {  // BP uses SS
                segment = _state.ss;
            }

            ushort v = w ? ReadMemWord(segment, a) : ReadMemByte(segment, a);

            cycles += 6;

            return (v, true, segment, a, cycles);
        }

        if (mod == 1 || mod == 2)
        {
            bool word = mod == 2;

            (ushort a, int cycles, bool override_segment, ushort new_segment) = GetDoubleRegisterMod01_02(reg, word);

            ushort segment = _state.segment_override_set ? _state.segment_override : _state.ds;

            if (_state.segment_override_set == false && override_segment)
                segment = new_segment;

            if (_state.segment_override_set == false && (reg == 2 || reg == 3))  // BP uses SS
                segment = _state.ss;

            ushort v = w ? ReadMemWord(segment, a) : ReadMemByte(segment, a);

            cycles += 6;

            return (v, true, segment, a, cycles);
        }

        if (mod == 3)
        {
            ushort v = GetRegister(reg, w);

            return (v, false, 0, 0, 0);
        }

        Log.DoLog($"reg {reg} mod {mod} w {w} not supported for {nameof(GetRegisterMem)}", LogLevel.WARNING);

        return (0, false, 0, 0, 0);
    }

    private void PutRegister(int reg, bool w, ushort val)
    {
        if (reg == 0)
        {
            if (w)
                SetAX(val);
            else
                _state.al = (byte)val;
        }
        else if (reg == 1)
        {
            if (w)
                SetCX(val);
            else
                _state.cl = (byte)val;
        }
        else if (reg == 2)
        {
            if (w)
                SetDX(val);
            else
                _state.dl = (byte)val;
        }
        else if (reg == 3)
        {
            if (w)
                SetBX(val);
            else
                _state.bl = (byte)val;
        }
        else if (reg == 4)
        {
            if (w)
                _state.sp = val;
            else
                _state.ah = (byte)val;
        }
        else if (reg == 5)
        {
            if (w)
                _state.bp = val;
            else
                _state.ch = (byte)val;
        }
        else if (reg == 6)
        {
            if (w)
                _state.si = val;
            else
                _state.dh = (byte)val;
        }
        else if (reg == 7)
        {
            if (w)
                _state.di = val;
            else
                _state.bh = (byte)val;
        }
        else
        {
            Log.DoLog($"reg {reg} w {w} not supported for {nameof(PutRegister)} ({val:X})", LogLevel.WARNING);
        }
    }

    private void PutSRegister(int reg, ushort v)
    {
        reg &= 0b00000011;

        if (reg == 0b000)
            _state.es = v;
        else if (reg == 0b001)
            _state.cs = v;
        else if (reg == 0b010)
            _state.ss = v;
        else if (reg == 0b011)
            _state.ds = v;
        else
            Log.DoLog($"reg {reg} not supported for {nameof(PutSRegister)}", LogLevel.WARNING);
    }

    // cycles
    private int PutRegisterMem(int reg, int mod, bool w, ushort val)
    {
        if (mod == 0)
        {
            (ushort a, int cycles) = GetDoubleRegisterMod00(reg);

            ushort segment = _state.segment_override_set ? _state.segment_override : _state.ds;

            if (_state.segment_override_set == false && (reg == 2 || reg == 3))  // BP uses SS
                segment = _state.ss;

            if (w)
                WriteMemWord(segment, a, val);
            else
                WriteMemByte(segment, a, (byte)val);

            cycles += 4;

            return cycles;
        }

        if (mod == 1 || mod == 2)
        {
            (ushort a, int cycles, bool override_segment, ushort new_segment) = GetDoubleRegisterMod01_02(reg, mod == 2);

            ushort segment = _state.segment_override_set ? _state.segment_override : _state.ds;

            if (_state.segment_override_set == false && override_segment)
                segment = new_segment;

            if (_state.segment_override_set == false && (reg == 2 || reg == 3))  // BP uses SS
                segment = _state.ss;

            if (w)
                WriteMemWord(segment, a, val);
            else
                WriteMemByte(segment, a, (byte)val);

            cycles += 4;

            return cycles;
        }

        if (mod == 3)
        {
            PutRegister(reg, w, val);
            return 0;  // TODO
        }

        Log.DoLog($"reg {reg} mod {mod} w {w} value {val} not supported for {nameof(PutRegisterMem)}", LogLevel.WARNING);

        return 0;
    }

    int UpdateRegisterMem(int reg, int mod, bool a_valid, ushort seg, ushort addr, bool word, ushort v)
    {
        if (a_valid)
        {
            if (word)
                WriteMemWord(seg, addr, v);
            else
                WriteMemByte(seg, addr, (byte)v);
            return 4;
        }

        return PutRegisterMem(reg, mod, word, v);
    }

    private void ClearFlagBit(int bit)
    {
        _state.flags &= (ushort)(ushort.MaxValue ^ (1 << bit));
    }

    private void SetFlagBit(int bit)
    {
        _state.flags |= (ushort)(1 << bit);
    }

    private void SetFlag(int bit, bool state)
    {
        if (state)
            SetFlagBit(bit);
        else
            ClearFlagBit(bit);
    }

    private bool GetFlag(int bit)
    {
        return (_state.flags & (1 << bit)) != 0;
    }

    private void SetFlagC(bool state)
    {
        SetFlag(0, state);
    }

    private bool GetFlagC()
    {
        return GetFlag(0);
    }

    private void SetFlagP(byte v)
    {
        int count = 0;

        while (v != 0)
        {
            count++;

            v &= (byte)(v - 1);
        }

        SetFlag(2, (count & 1) == 0);
    }

    private bool GetFlagP()
    {
        return GetFlag(2);
    }

    private void SetFlagA(bool state)
    {
        SetFlag(4, state);
    }

    private bool GetFlagA()
    {
        return GetFlag(4);
    }

    private void SetFlagZ(bool state)
    {
        SetFlag(6, state);
    }

    private bool GetFlagZ()
    {
        return GetFlag(6);
    }

    private void SetFlagS(bool state)
    {
        SetFlag(7, state);
    }

    private bool GetFlagS()
    {
        return GetFlag(7);
    }

    private void SetFlagT(bool state)
    {
        SetFlag(8, state);
    }

    private bool GetFlagT()
    {
        return GetFlag(8);
    }

    private void SetFlagI(bool state)
    {
        SetFlag(9, state);
    }

    private bool GetFlagI()
    {
        return GetFlag(9);
    }

    private void SetFlagD(bool state)
    {
        SetFlag(10, state);
    }

    private bool GetFlagD()
    {
        return GetFlag(10);
    }

    private void SetFlagO(bool state)
    {
        SetFlag(11, state);
    }

    private bool GetFlagO()
    {
        return GetFlag(11);
    }

    public string GetFlagsAsString()
    {
        string @out = String.Empty;

        @out += GetFlagO() ? "o" : "-";
        @out += GetFlagI() ? "I" : "-";
        @out += GetFlagT() ? "T" : "-";
        @out += GetFlagS() ? "s" : "-";
        @out += GetFlagZ() ? "z" : "-";
        @out += GetFlagA() ? "a" : "-";
        @out += GetFlagP() ? "p" : "-";
        @out += GetFlagC() ? "c" : "-";

        return @out;
    }

    private void SetAddSubFlags(bool word, ushort r1, ushort r2, int result, bool issub, bool flag_c)
    {
        ushort in_reg_result = word ? (ushort)result : (byte)result;

        uint u_result = (uint)result;

        ushort mask = (ushort)(word ? 0x8000 : 0x80);

        ushort temp_r2 = (ushort)(issub ? (r2 - (flag_c ? 1 : 0)) : (r2 + (flag_c ? 1 : 0)));

        bool before_sign = (r1 & mask) == mask;
        bool value_sign = (r2 & mask) == mask;
        bool after_sign = (u_result & mask) == mask;
        SetFlagO(after_sign != before_sign && ((before_sign != value_sign && issub) || (before_sign == value_sign && issub == false)));

        SetFlagC(word ? u_result >= 0x10000 : u_result >= 0x100);

        SetFlagS((in_reg_result & mask) != 0);

        SetFlagZ(in_reg_result == 0);

        if (issub)
            SetFlagA((((r1 & 0x0f) - (r2 & 0x0f) - (flag_c ? 1 : 0)) & 0x10) > 0);
        else
            SetFlagA((((r1 & 0x0f) + (r2 & 0x0f) + (flag_c ? 1 : 0)) & 0x10) > 0);

        SetFlagP((byte)result);
    }

    private void SetLogicFuncFlags(bool word, ushort result)
    {
        SetFlagO(false);
        SetFlagS((word ? result & 0x8000 : result & 0x80) != 0);
        SetFlagZ(word ? result == 0 : (result & 0xff) == 0);
        SetFlagP((byte)result);

        SetFlagA(false);  // undefined

        SetFlagC(false);
    }

    public void push(ushort v)
    {
        _state.sp -= 2;

        // Log.DoLog($"push({v:X4}) write @ {_state.ss:X4}:{_state.sp:X4}", true);

        WriteMemWord(_state.ss, _state.sp, v);
    }

    public ushort pop()
    {
        ushort v = ReadMemWord(_state.ss, _state.sp);

        // Log.DoLog($"pop({v:X4}) read @ {_state.ss:X4}:{_state.sp:X4}", true);

        _state.sp += 2;

        return v;
    }

    void InvokeInterrupt(ushort instr_start, int interrupt_nr, bool pic)
    {
        _state.segment_override_set = false;

        if (pic)
        {
            _io.GetPIC().SetIRQBeingServiced(interrupt_nr);
            interrupt_nr += _io.GetPIC().GetInterruptOffset();
        }

        push(_state.flags);
        push(_state.cs);
        if (_state.rep)
        {
            push(_state.rep_addr);
            _state.rep = false;
        }
        else
        {
            push(instr_start);
        }

        SetFlagI(false);
        SetFlagT(false);

        ushort addr = (ushort)(interrupt_nr * 4);

        _state.ip = ReadMemWord(0, addr);
        _state.cs = ReadMemWord(0, (ushort)(addr + 2));
    }

    public string HexDump(uint addr)
    {
        string s = "";
        for(uint o=0; o<16; o++)
        {
            var rc = _b.ReadByte((addr + o) & 0xfffff);
            s += $" {rc.Item1:X2}";
        }
        return s;
    }

    public string CharDump(uint addr)
    {
        string s = "";
        for(uint o=0; o<16; o++)
        {
            var rc = _b.ReadByte((addr + o) & 0xfffff);
            byte b = rc.Item1;
            if (b >= 33 && b < 127)
                s += $" {(char)b}";
            else
                s += " .";
        }
        return s;
    }

    public string GetTerminatedString(ushort segment, ushort p, char terminator)
    {
        string out_ = "";

        for(;;)
        {
            byte byte_ = ReadMemByte(segment, p);

            if (byte_ == terminator)
                break;

            out_ += (char)byte_;

            p++;

            if (p == 0)  // stop at end of segment
                break;
        }

        return out_;
    }

    public bool IsProcessingRep()
    {
        return _state.rep;
    }

    public bool PrefixMustRun()
    {
        bool rc = true;

        if (_state.rep)
        {
            ushort cx = GetCX();

            if (_state.rep_do_nothing)
            {
                _state.rep = false;
                rc = false;
            }
            else
            {
                cx--;
                SetCX(cx);

                if (cx == 0)
                {
                    _state.rep = false;
                }
                else if (_state.rep_mode == RepMode.REPE_Z)
                {
                }
                else if (_state.rep_mode == RepMode.REPNZ)
                {
                }
                else if (_state.rep_mode == RepMode.REP)
                {
                }
                else
                {
                    Log.DoLog($"unknown _state.rep_mode {_state.rep_mode}", LogLevel.WARNING);
                    _state.rep = false;
                    rc = false;
                }
            }
        }

        _state.rep_do_nothing = false;

        return rc;
    }

    public void PrefixEnd(byte opcode)
    {
        if (opcode is (0xa4 or 0xa5 or 0xa6 or 0xa7 or 0xaa or 0xab or 0xac or 0xad or 0xae or 0xaf))
        {
            if (_state.rep_mode == RepMode.REPE_Z)
            {
                // REPE/REPZ
                if (GetFlagZ() != true)
                {
                    _state.rep = false;
                }
            }
            else if (_state.rep_mode == RepMode.REPNZ)
            {
                // REPNZ
                if (GetFlagZ() != false)
                {
                    _state.rep = false;
                }
            }
        }
        else
        {
            _state.rep = false;
        }

        if (_state.rep == false)
        {
            _state.segment_override_set = false;
        }

        if (_state.rep)
            _state.ip = _state.rep_addr;
    }

    public void ResetCrashCounter()
    {
        _state.crash_counter = 0;
    }

    public bool IsInHlt()
    {
        return _state.in_hlt;
    }

    // cycle counts from https://zsmith.co/intel_i.php
    public int Tick()
    {
        int cycle_count = 0;  // cycles used for an instruction
        bool back_from_trace = false;

        Log.SetMeta(_state.clock, _state.cs, _state.ip);

        // check for interrupt
        if (GetFlagI() == true && _state.inhibit_interrupts == false)
        {
            int irq = _io.GetPIC().GetPendingInterrupt();
            if (irq != 255)
            {
                if (irq != 0)
                    Log.DoLog($"Scanning for IRQ {irq}", LogLevel.TRACE);

                foreach (var device in _devices)
                {
                    if (device.GetIRQNumber() != irq)
                        continue;

                    Log.DoLog($"{device.GetName()} triggers IRQ {irq}", LogLevel.TRACE);

                    _state.in_hlt = false;
                    InvokeInterrupt(_state.ip, irq, true);
                    cycle_count += 60;
                    _state.clock += cycle_count;

                    return cycle_count;
                }
            }
        }

        _state.inhibit_interrupts = false;

        // T-flag produces an interrupt after each instruction
        if (_state.in_hlt)
        {
            cycle_count += 2;
            _state.clock += cycle_count;  // time needs to progress for timers etc
            _io.Tick(cycle_count, _state.clock);
            return cycle_count;
        }

        ushort instr_start = _state.ip;
        uint address = (uint)(_state.cs * 16 + _state.ip) & MemMask;
        byte opcode = GetPcByte();

        if (_ignore_breakpoints)
            _ignore_breakpoints = false;
        else
        {
            foreach(uint check_address in _breakpoints)
            {
                if (check_address == instr_start)
                {
                    _stop_reason = $"Breakpoint reached at address {check_address:X06}";
                    Log.DoLog(_stop_reason, LogLevel.INFO);
                    return -1;
                }
            }
        }

        // handle prefixes
        while (opcode is (0x26 or 0x2e or 0x36 or 0x3e or 0xf2 or 0xf3))
        {
            if (opcode == 0x26)
                _state.segment_override = _state.es;
            else if (opcode == 0x2e)
                _state.segment_override = _state.cs;
            else if (opcode == 0x36)
                _state.segment_override = _state.ss;
            else if (opcode == 0x3e)
                _state.segment_override = _state.ds;
            else if (opcode is (0xf2 or 0xf3))
            {
                _state.rep = true;
                _state.rep_mode = RepMode.NotSet;
                cycle_count += 9;

                _state.rep_do_nothing = GetCX() == 0;
            }
            else
            {
                Log.DoLog($"prefix {opcode:X2} not implemented", LogLevel.WARNING);
            }

            address = (uint)(_state.cs * 16 + _state.ip) & MemMask;
            byte next_opcode = GetPcByte();

            _state.rep_opcode = next_opcode;  // TODO: only allow for certain instructions

            if (opcode == 0xf2)
            {
                _state.rep_addr = instr_start;
                if (next_opcode is (0xa6 or 0xa7 or 0xae or 0xaf))
                {
                    _state.rep_mode = RepMode.REPNZ;
                    //Log.DoLog($"REPNZ: {_state.cs:X4}:{_state.rep_addr:X4}", true);
                }
                else
                {
                    _state.rep_mode = RepMode.REP;
                }
            }
            else if (opcode == 0xf3)
            {
                _state.rep_addr = instr_start;
                if (next_opcode is (0xa6 or 0xa7 or 0xae or 0xaf))
                {
                    _state.rep_mode = RepMode.REPE_Z;
                    //Log.DoLog($"REPZ: {_state.cs:X4}:{_state.rep_addr:X4}", true);
                }
                else
                {
                    _state.rep_mode = RepMode.REP;
                    //Log.DoLog($"REP: {_state.cs:X4}:{_state.rep_addr:X4}", true);
                }
            }
            else
            {
                _state.segment_override_set = true;  // TODO: move up
                cycle_count += 2;
            }

            opcode = next_opcode;
        }

        if (opcode == 0x00)
        {
            if (_terminate_on_off_the_rails == true && ++_state.crash_counter >= 5)
            {
                _stop_reason = $"Terminating because of {_state.crash_counter}x 0x00 opcode ({address:X06})";
                Log.DoLog(_stop_reason, LogLevel.WARNING);
                return -1;
            }
        }
        else
        {
            _state.crash_counter = 0;
        }

        // main instruction handling
        if (opcode == 0x04 || opcode == 0x14)
        {
            // ADD AL,xx
            byte v = GetPcByte();

            bool flag_c = GetFlagC();
            bool use_flag_c = false;

            int result = _state.al + v;

            if (opcode == 0x14)
            {
                if (flag_c)
                    result++;

                use_flag_c = true;
            }

            cycle_count += 3;

            SetAddSubFlags(false, _state.al, v, result, false, use_flag_c ? flag_c : false);

            _state.al = (byte)result;
        }
        else if (opcode == 0x05 || opcode == 0x15)
        {
            // ADD AX,xxxx
            ushort v = GetPcWord();

            bool flag_c = GetFlagC();
            bool use_flag_c = false;

            ushort before = GetAX();

            int result = before + v;

            if (opcode == 0x15)
            {
                if (flag_c)
                    result++;

                use_flag_c = true;
            }

            SetAddSubFlags(true, before, v, result, false, use_flag_c ? flag_c : false);

            SetAX((ushort)result);

            cycle_count += 3;
        }
        else if (opcode == 0x06)
        {
            // PUSH ES
            push(_state.es);

            cycle_count += 15;
        }
        else if (opcode == 0x07)
        {
            // POP ES
            _state.es = pop();
            _state.inhibit_interrupts = true;

            cycle_count += 12;
        }
        else if (opcode == 0x0e)
        {
            // PUSH CS
            push(_state.cs);

            cycle_count += 15;
        }
        else if (opcode == 0x0f)
        {
            // POP CS
            _state.cs = pop();
            _state.inhibit_interrupts = true;

            cycle_count += 12;
        }
        else if (opcode == 0x16)
        {
            // PUSH SS
            push(_state.ss);

            cycle_count += 15;
        }
        else if (opcode == 0x17)
        {
            // POP SS
            _state.ss = pop();
            _state.inhibit_interrupts = true;

            cycle_count += 12;
        }
        else if (opcode == 0x1c)
        {
            // SBB AL,ib
            byte v = GetPcByte();

            bool flag_c = GetFlagC();

            int result = _state.al - v;

            if (flag_c)
                result--;

            SetAddSubFlags(false, _state.al, v, result, true, flag_c);

            _state.al = (byte)result;

            cycle_count += 3;
        }
        else if (opcode == 0x1d)
        {
            // SBB AX,iw
            ushort v = GetPcWord();

            ushort AX = GetAX();

            bool flag_c = GetFlagC();

            int result = AX - v;

            if (flag_c)
                result--;

            SetAddSubFlags(true, AX, v, result, true, flag_c);

            SetAX((ushort)result);

            cycle_count += 3;
        }
        else if (opcode == 0x1e)
        {
            // PUSH DS
            push(_state.ds);

            cycle_count += 11;  // 15
        }
        else if (opcode == 0x1f)
        {
            // POP DS
            _state.ds = pop();
            _state.inhibit_interrupts = true;

            cycle_count += 8;
        }
        else if (opcode == 0x27)
        {
            // DAA
            // https://www.felixcloutier.com/x86/daa
            byte old_al = _state.al;
            bool old_af = GetFlagA();
            bool old_cf = GetFlagC();

            SetFlagC(false);

            if (((_state.al & 0x0f) > 9) || GetFlagA() == true)
            {
                bool add_carry = _state.al + 6 > 255;

                _state.al += 6;

                SetFlagC(old_cf || add_carry);

                SetFlagA(true);
            }
            else
            {
                SetFlagA(false);
            }

            byte upper_nibble_check = (byte)(old_af ? 0x9f : 0x99);

            if (old_al > upper_nibble_check || old_cf)
            {
                _state.al += 0x60;
                SetFlagC(true);
            }
            else
            {
                SetFlagC(false);
            }

            SetZSPFlags(_state.al);

            cycle_count += 4;
        }
        else if (opcode == 0x2c)
        {
            // SUB AL,ib
            byte v = GetPcByte();

            int result = _state.al - v;

            SetAddSubFlags(false, _state.al, v, result, true, false);

            _state.al = (byte)result;

            cycle_count += 3;
        }
        else if (opcode == 0x2f)
        {
            // DAS
            byte old_al = _state.al;
            bool old_af = GetFlagA();
            bool old_cf = GetFlagC();

            SetFlagC(false);

            if ((_state.al & 0x0f) > 9 || GetFlagA() == true)
            {
                _state.al -= 6;

                SetFlagA(true);
            }
            else
            {
                SetFlagA(false);
            }

            byte upper_nibble_check = (byte)(old_af ? 0x9f : 0x99);

            if (old_al > upper_nibble_check || old_cf)
            {
                _state.al -= 0x60;
                SetFlagC(true);
            }

            SetZSPFlags(_state.al);

            cycle_count += 4;
        }
        else if (opcode == 0x37)
        {
            if ((_state.al & 0x0f) > 9 || GetFlagA())
            {
                _state.ah += 1;

                _state.al += 6;

                SetFlagA(true);
                SetFlagC(true);
            }
            else
            {
                SetFlagA(false);
                SetFlagC(false);
            }

            _state.al &= 0x0f;

            cycle_count += 8;
        }
        else if (opcode == 0x3f)
        {
            if ((_state.al & 0x0f) > 9 || GetFlagA())
            {
                _state.al -= 6;
                _state.ah -= 1;

                SetFlagA(true);
                SetFlagC(true);
            }
            else
            {
                SetFlagA(false);
                SetFlagC(false);
            }

            _state.al &= 0x0f;

            cycle_count += 8;
        }
        else if (opcode == 0x2d)
        {
            // SUB AX,iw
            ushort v = GetPcWord();

            ushort before = GetAX();

            int result = before - v;

            SetAddSubFlags(true, before, v, result, true, false);

            SetAX((ushort)result);

            cycle_count += 3;
        }
        else if (opcode == 0x58)
        {
            // POP AX
            SetAX(pop());

            cycle_count += 8;
        }
        else if (opcode == 0x59)
        {
            // POP CX
            SetCX(pop());

            cycle_count += 8;
        }
        else if (opcode == 0x5a)
        {
            // POP DX
            SetDX(pop());

            cycle_count += 8;
        }
        else if (opcode == 0x5b)
        {
            // POP BX
            SetBX(pop());

            cycle_count += 8;
        }
        else if (opcode == 0x5c)
        {
            // POP SP
            _state.sp = pop();

            cycle_count += 8;
        }
        else if (opcode == 0x5d)
        {
            // POP BP
            _state.bp = pop();

            cycle_count += 8;
        }
        else if (opcode == 0x5e)
        {
            // POP SI
            _state.si = pop();

            cycle_count += 8;
        }
        else if (opcode == 0x5f)
        {
            // POP DI
            _state.di = pop();

            cycle_count += 8;
        }
        else if (opcode == 0xa4)
        {
            if (PrefixMustRun())
            {
                // MOVSB
                ushort segment = _state.segment_override_set ? _state.segment_override : _state.ds;
                byte v = ReadMemByte(segment, _state.si);
                WriteMemByte(_state.es, _state.di, v);

                _state.si += (ushort)(GetFlagD() ? -1 : 1);
                _state.di += (ushort)(GetFlagD() ? -1 : 1);

                cycle_count += 18;
            }
        }
        else if (opcode == 0xa5)
        {
            if (PrefixMustRun())
            {
                // MOVSW
                WriteMemWord(_state.es, _state.di, ReadMemWord(_state.segment_override_set ? _state.segment_override : _state.ds, _state.si));

                _state.si += (ushort)(GetFlagD() ? -2 : 2);
                _state.di += (ushort)(GetFlagD() ? -2 : 2);

                cycle_count += 26;
            }
        }
        else if (opcode == 0xa6)
        {
            if (PrefixMustRun())
            {
                // CMPSB
                byte v1 = ReadMemByte(_state.segment_override_set ? _state.segment_override : _state.ds, _state.si);
                byte v2 = ReadMemByte(_state.es, _state.di);

                int result = v1 - v2;

                _state.si += (ushort)(GetFlagD() ? -1 : 1);
                _state.di += (ushort)(GetFlagD() ? -1 : 1);

                SetAddSubFlags(false, v1, v2, result, true, false);

                cycle_count += 30;
            }
        }
        else if (opcode == 0xa7)
        {
            if (PrefixMustRun())
            {
                // CMPSW
                ushort v1 = ReadMemWord(_state.segment_override_set ? _state.segment_override : _state.ds, _state.si);
                ushort v2 = ReadMemWord(_state.es, _state.di);

                int result = v1 - v2;

                _state.si += (ushort)(GetFlagD() ? -2 : 2);
                _state.di += (ushort)(GetFlagD() ? -2 : 2);

                SetAddSubFlags(true, v1, v2, result, true, false);

                cycle_count += 30;
            }
        }
        else if (opcode == 0xe3)
        {
            // JCXZ np
            sbyte offset = (sbyte)GetPcByte();

            ushort addr = (ushort)(_state.ip + offset);

            if (GetCX() == 0)
            {
                _state.ip = addr;
                cycle_count += 18;
            }
            else
            {
                cycle_count += 6;
            }
        }
        else if (opcode == 0xe9)
        {
            // JMP np
            short offset = (short)GetPcWord();

            _state.ip = (ushort)(_state.ip + offset);

            cycle_count += 15;
        }
        else if (opcode == 0x50)
        {
            // PUSH AX
            push(GetAX());

            cycle_count += 15;
        }
        else if (opcode == 0x51)
        {
            // PUSH CX
            push(GetCX());

            cycle_count += 15;
        }
        else if (opcode == 0x52)
        {
            // PUSH DX
            push(GetDX());

            cycle_count += 15;
        }
        else if (opcode == 0x53)
        {
            // PUSH BX
            push(GetBX());

            cycle_count += 15;
        }
        else if (opcode == 0x54)
        {
            // PUSH SP
            // special case, see:
            // https://c9x.me/x86/html/file_module_x86_id_269.html
            _state.sp -= 2;
            WriteMemWord(_state.ss, _state.sp, _state.sp);

            cycle_count += 15;
        }
        else if (opcode == 0x55)
        {
            // PUSH BP
            push(_state.bp);

            cycle_count += 15;
        }
        else if (opcode == 0x56)
        {
            // PUSH SI
            push(_state.si);

            cycle_count += 15;
        }
        else if (opcode == 0x57)
        {
            // PUSH DI
            push(_state.di);

            cycle_count += 15;
        }
        else if (opcode is (0x80 or 0x81 or 0x82 or 0x83))
        {
            // CMP and others
            byte o1 = GetPcByte();

            int mod = o1 >> 6;
            int reg = o1 & 7;

            int function = (o1 >> 3) & 7;

            ushort r1 = 0;
            bool a_valid = false;
            ushort seg = 0;
            ushort addr = 0;

            ushort r2 = 0;

            bool word = false;

            bool is_logic = false;
            bool is_sub = false;

            int result = 0;

            int cycles = 0;

            if (opcode == 0x80)
            {
                (r1, a_valid, seg, addr, cycles) = GetRegisterMem(reg, mod, false);

                r2 = GetPcByte();
            }
            else if (opcode == 0x81)
            {
                (r1, a_valid, seg, addr, cycles) = GetRegisterMem(reg, mod, true);

                r2 = GetPcWord();

                word = true;
            }
            else if (opcode == 0x82)
            {
                (r1, a_valid, seg, addr, cycles) = GetRegisterMem(reg, mod, false);

                r2 = GetPcByte();
            }
            else if (opcode == 0x83)
            {
                (r1, a_valid, seg, addr, cycles) = GetRegisterMem(reg, mod, true);

                r2 = GetPcByte();

                if ((r2 & 128) == 128)
                    r2 |= 0xff00;

                word = true;
            }
            else
            {
                Log.DoLog($"opcode {opcode:X2} not implemented", LogLevel.WARNING);
            }

            bool apply = true;
            bool use_flag_c = false;

            if (function == 0)
            {
                result = r1 + r2;
            }
            else if (function == 1)
            {
                result = r1 | r2;
                is_logic = true;
            }
            else if (function == 2)
            {
                result = r1 + r2 + (GetFlagC() ? 1 : 0);
                use_flag_c = true;
            }
            else if (function == 3)
            {
                result = r1 - r2 - (GetFlagC() ? 1 : 0);
                is_sub = true;
                use_flag_c = true;
            }
            else if (function == 4)
            {
                result = r1 & r2;
                is_logic = true;
                SetFlagC(false);
            }
            else if (function == 5)
            {
                result = r1 - r2;
                is_sub = true;
            }
            else if (function == 6)
            {
                result = r1 ^ r2;
                is_logic = true;
            }
            else if (function == 7)
            {
                result = r1 - r2;
                is_sub = true;
                apply = false;
            }
            else
            {
                Log.DoLog($"opcode {opcode:X2} function {function} not implemented", LogLevel.WARNING);
            }

            if (is_logic)
                SetLogicFuncFlags(word, (ushort)result);
            else
                SetAddSubFlags(word, r1, r2, result, is_sub, use_flag_c ? GetFlagC() : false);

            if (apply)
            {
                int put_cycles = UpdateRegisterMem(reg, mod, a_valid, seg, addr, word, (ushort)result);

                cycles += put_cycles;
            }

            cycle_count += 3 + cycles;
        }
        else if (opcode == 0x84 || opcode == 0x85)
        {
            // TEST ...,...
            bool word = (opcode & 1) == 1;
            byte o1 = GetPcByte();

            int mod = o1 >> 6;
            int reg1 = (o1 >> 3) & 7;
            int reg2 = o1 & 7;

            (ushort r1, bool a_valid, ushort seg, ushort addr, int cycles) = GetRegisterMem(reg2, mod, word);
            ushort r2 = GetRegister(reg1, word);

            if (word)
            {
                ushort result = (ushort)(r1 & r2);
                SetLogicFuncFlags(true, result);
            }
            else
            {
                byte result = (byte)(r1 & r2);
                SetLogicFuncFlags(false, result);
            }

            SetFlagC(false);

            cycle_count += 3 + cycles;
        }
        else if (opcode == 0x86 || opcode == 0x87)
        {
            // XCHG
            bool word = (opcode & 1) == 1;
            byte o1 = GetPcByte();

            int mod = o1 >> 6;
            int reg1 = (o1 >> 3) & 7;
            int reg2 = o1 & 7;

            (ushort r1, bool a_valid, ushort seg, ushort addr, int get_cycles) = GetRegisterMem(reg2, mod, word);
            ushort r2 = GetRegister(reg1, word);

            int put_cycles = UpdateRegisterMem(reg2, mod, a_valid, seg, addr, word, r2);

            PutRegister(reg1, word, r1);

            cycle_count += 3 + get_cycles + put_cycles;
        }
        else if (opcode == 0x8f)
        {
            // POP rmw
            byte o1 = GetPcByte();

            int mod = o1 >> 6;
            int reg2 = o1 & 7;

            int put_cycles = PutRegisterMem(reg2, mod, true, pop());

            cycle_count += put_cycles;

            cycle_count += 17;
        }
        else if (opcode == 0x90)
        {
            // NOP

            cycle_count += 3;
        }
        else if (opcode >= 0x91 && opcode <= 0x97)
        {
            // XCHG AX,...
            int reg_nr = opcode - 0x90;

            ushort v = GetRegister(reg_nr, true);

            ushort old_ax = GetAX();
            SetAX(v);

            PutRegister(reg_nr, true, old_ax);

            cycle_count += 3;
        }
        else if (opcode == 0x98)
        {
            // CBW
            ushort new_value = _state.al;

            if ((_state.al & 128) == 128)
                new_value |= 0xff00;

            SetAX(new_value);

            cycle_count += 2;
        }
        else if (opcode == 0x99)
        {
            // CWD
            if ((_state.ah & 128) == 128)
                SetDX(0xffff);
            else
                SetDX(0);

            cycle_count += 5;
        }
        else if (opcode == 0x9a)
        {
            // CALL far ptr
            ushort temp_ip = GetPcWord();
            ushort temp_cs = GetPcWord();

            push(_state.cs);
            push(_state.ip);

            _state.ip = temp_ip;
            _state.cs = temp_cs;

            cycle_count += 37;
        }
        else if (opcode == 0x9c)
        {
            // PUSHF
            push(_state.flags);

            cycle_count += 14;
        }
        else if (opcode == 0x9d)
        {
            bool before = GetFlagT();

            // POPF
            _state.flags = pop();

            if (GetFlagT() && before == false)
                back_from_trace = true;

            cycle_count += 12;

            FixFlags();
        }
        else if (opcode == 0xac)
        {
            if (PrefixMustRun())
            {
                // LODSB
                _state.al = ReadMemByte(_state.segment_override_set ? _state.segment_override : _state.ds, _state.si);

                _state.si += (ushort)(GetFlagD() ? -1 : 1);

                cycle_count += 5;
            }
        }
        else if (opcode == 0xad)
        {
            if (PrefixMustRun())
            {
                // LODSW
                SetAX(ReadMemWord(_state.segment_override_set ? _state.segment_override : _state.ds, _state.si));

                _state.si += (ushort)(GetFlagD() ? -2 : 2);

                cycle_count += 5;
            }
        }
        else if (opcode == 0xc2 || opcode == 0xc0)
        {
            ushort nToRelease = GetPcWord();

            // RET
            _state.ip = pop();
            _state.sp += nToRelease;

            cycle_count += 16;
        }
        else if (opcode == 0xc3 || opcode == 0xc1)
        {
            // RET
            _state.ip = pop();

            cycle_count += 16;
        }
        else if (opcode == 0xc4 || opcode == 0xc5)
        {
            // LES (c4) / LDS (c5)
            byte o1 = GetPcByte();
            int mod = o1 >> 6;
            int reg = (o1 >> 3) & 7;
            int rm = o1 & 7;

            (ushort val, bool a_valid, ushort seg, ushort addr, int get_cycles) = GetRegisterMem(rm, mod, true);

            if (opcode == 0xc4)
                _state.es = ReadMemWord(seg, (ushort)(addr + 2));
            else
                _state.ds = ReadMemWord(seg, (ushort)(addr + 2));

            PutRegister(reg, true, val);

            cycle_count += 24 + get_cycles;
        }
        else if (opcode == 0xcc || opcode == 0xcd || opcode == 0xce)
        {
            // INT 0x..
            if (opcode != 0xce || GetFlagO())
            {
                byte @int = 0;

                if (opcode == 0xcc)
                    @int = 3;
                else if (opcode == 0xce)
                    @int = 4;
                else
                    @int = GetPcByte();

                ushort addr = (ushort)(@int * 4);

                push(_state.flags);
                push(_state.cs);
                if (_state.rep)
                {
                    push(_state.rep_addr);
                    Log.DoLog($"INT from rep {_state.rep_addr:X04}", LogLevel.TRACE);
                }
                else
                {
                    push(_state.ip);
                }

                SetFlagI(false);

                _state.ip = ReadMemWord(0, addr);
                _state.cs = ReadMemWord(0, (ushort)(addr + 2));

                cycle_count += 51;  // 71  TODO
            }
        }
        else if (opcode == 0xcf)
        {
            // IRET
            bool before = GetFlagT();

            _state.ip = pop();
            _state.cs = pop();
            _state.flags = pop();
            FixFlags();

            if (GetFlagT() && before == false)
                back_from_trace = true;

            cycle_count += 32;  // 44
        }
        else if ((opcode >= 0x00 && opcode <= 0x03) || (opcode >= 0x10 && opcode <= 0x13) || (opcode >= 0x28 && opcode <= 0x2b) || (opcode >= 0x18 && opcode <= 0x1b) || (opcode >= 0x38 && opcode <= 0x3b))
        {
            bool word = (opcode & 1) == 1;
            bool direction = (opcode & 2) == 2;
            byte o1 = GetPcByte();

            int mod = o1 >> 6;
            int reg1 = (o1 >> 3) & 7;
            int reg2 = o1 & 7;

            (ushort r1, bool a_valid, ushort seg, ushort addr, int get_cycles) = GetRegisterMem(reg2, mod, word);
            ushort r2 = GetRegister(reg1, word);

            cycle_count += get_cycles;

            int result = 0;
            bool is_sub = false;
            bool apply = true;
            bool use_flag_c = false;

            if (opcode <= 0x03)
            {
                result = r1 + r2;

                cycle_count += 4;
            }
            else if (opcode >= 0x10 && opcode <= 0x13)
            {
                use_flag_c = true;

                result = r1 + r2 + (GetFlagC() ? 1 : 0);

                cycle_count += 4;
            }
            else
            {
                if (direction)
                    result = r2 - r1;
                else
                    result = r1 - r2;

                is_sub = true;

                if (opcode >= 0x38 && opcode <= 0x3b)
                {
                    apply = false;
                }
                else if (opcode >= 0x28 && opcode <= 0x2b)
                {
                }
                else  // 0x18...0x1b
                {
                    use_flag_c = true;

                    result -= (GetFlagC() ? 1 : 0);
                }

                cycle_count += 4;
            }

            if (direction)
                SetAddSubFlags(word, r2, r1, result, is_sub, use_flag_c ? GetFlagC() : false);
            else
                SetAddSubFlags(word, r1, r2, result, is_sub, use_flag_c ? GetFlagC() : false);

            // 0x38...0x3b are CMP
            if (apply)
            {
                if (direction)
                {
                    PutRegister(reg1, word, (ushort)result);
                }
                else
                {
                    bool override_to_ss = a_valid && word && _state.segment_override_set == false &&
                        (
                         ((reg2 == 2 || reg2 == 3) && mod == 0)
                        );

                    if (override_to_ss)
                        seg = _state.ss;

                    int put_cycles = UpdateRegisterMem(reg2, mod, a_valid, seg, addr, word, (ushort)result);
                    cycle_count += put_cycles;
                }
            }
        }
        else if (opcode == 0x3c || opcode == 0x3d)
        {
            // CMP
            bool word = (opcode & 1) == 1;

            int result = 0;

            ushort r1 = 0;
            ushort r2 = 0;

            cycle_count += 4;

            if (opcode == 0x3d)
            {
                r1 = GetAX();
                r2 = GetPcWord();

                result = r1 - r2;
            }
            else if (opcode == 0x3c)
            {
                r1 = _state.al;
                r2 = GetPcByte();

                result = r1 - r2;
            }
            else
            {
                Log.DoLog($"opcode {opcode:X2} not implemented", LogLevel.WARNING);
            }

            SetAddSubFlags(word, r1, r2, result, true, false);
        }
        else if (opcode is >= 0x30 and <= 0x33 || opcode is >= 0x20 and <= 0x23 || opcode is >= 0x08 and <= 0x0b)
        {
            bool word = (opcode & 1) == 1;
            bool direction = (opcode & 2) == 2;
            byte o1 = GetPcByte();

            int mod = o1 >> 6;
            int reg1 = (o1 >> 3) & 7;
            int reg2 = o1 & 7;

            (ushort r1, bool a_valid, ushort seg, ushort addr, int get_cycles) = GetRegisterMem(reg2, mod, word);
            ushort r2 = GetRegister(reg1, word);

            cycle_count += get_cycles + 3;

            ushort result = 0;

            int function = opcode >> 4;
            if (function == 0)
            {
                result = (ushort)(r1 | r2);
            }
            else if (function == 2)
            {
                result = (ushort)(r2 & r1);
            }
            else if (function == 3)
            {
                result = (ushort)(r2 ^ r1);
            }
            else
            {
                Log.DoLog($"opcode {opcode:X2} function {function} not implemented", LogLevel.WARNING);
            }

            SetLogicFuncFlags(word, result);

            if (direction)
            {
                PutRegister(reg1, word, result);
            }
            else
            {
                int put_cycles = UpdateRegisterMem(reg2, mod, a_valid, seg, addr, word, result);
                cycle_count += put_cycles;
            }
        }
        else if (opcode is (0x34 or 0x35 or 0x24 or 0x25 or 0x0c or 0x0d))
        {
            bool word = (opcode & 1) == 1;

            byte bLow = GetPcByte();
            byte bHigh = word ? GetPcByte() : (byte)0;

            int function = opcode >> 4;

            if (function == 0)
            {
                _state.al |= bLow;

                if (word)
                    _state.ah |= bHigh;
            }
            else if (function == 2)
            {
                _state.al &= bLow;

                if (word)
                    _state.ah &= bHigh;

                SetFlagC(false);
            }
            else if (function == 3)
            {
                _state.al ^= bLow;

                if (word)
                    _state.ah ^= bHigh;
            }
            else
            {
                Log.DoLog($"opcode {opcode:X2} function {function} not implemented", LogLevel.WARNING);
            }

            SetLogicFuncFlags(word, word ? GetAX() : _state.al);

            SetFlagP(_state.al);

            cycle_count += 4;
        }
        else if (opcode == 0xe8)
        {
            // CALL
            short a = (short)GetPcWord();
            push(_state.ip);
            _state.ip = (ushort)(a + _state.ip);

            cycle_count += 16;
        }
        else if (opcode == 0xea)
        {
            // JMP far ptr
            ushort temp_ip = GetPcWord();
            ushort temp_cs = GetPcWord();

            _state.ip = temp_ip;
            _state.cs = temp_cs;

            cycle_count += 15;
        }
        else if (opcode == 0xf6 || opcode == 0xf7)
        {
            // TEST and others
            bool word = (opcode & 1) == 1;

            byte o1 = GetPcByte();
            int mod = o1 >> 6;
            int reg1 = o1 & 7;

            (ushort r1, bool a_valid, ushort seg, ushort addr, int get_cycles) = GetRegisterMem(reg1, mod, word);
            cycle_count += get_cycles;

            int function = (o1 >> 3) & 7;
            if (function == 0 || function == 1)
            {
                // TEST
                if (word) {
                    ushort r2 = GetPcWord();

                    ushort result = (ushort)(r1 & r2);
                    SetLogicFuncFlags(true, result);

                    SetFlagC(false);
                }
                else {
                    byte r2 = GetPcByte();
                    ushort result = (ushort)(r1 & r2);
                    SetLogicFuncFlags(word, result);

                    SetFlagC(false);
                }
            }
            else if (function == 2)
            {
                // NOT
                int put_cycles = UpdateRegisterMem(reg1, mod, a_valid, seg, addr, word, (ushort)~r1);
                cycle_count += put_cycles;
            }
            else if (function == 3)
            {
                // NEG
                int result = (ushort)-r1;

                SetAddSubFlags(word, 0, r1, -r1, true, false);
                SetFlagC(r1 != 0);

                int put_cycles = UpdateRegisterMem(reg1, mod, a_valid, seg, addr, word, (ushort)result);
                cycle_count += put_cycles;
            }
            else if (function == 4)
            {
                bool negate = _state.rep_mode == RepMode.REP && _state.rep;
                _state.rep = false;

                // MUL
                if (word) {
                    ushort ax = GetAX();
                    int resulti = ax * r1;

                    uint dx_ax = (uint)resulti;
                    if (negate)
                        dx_ax = (uint)-dx_ax;
                    SetAX((ushort)dx_ax);
                    SetDX((ushort)(dx_ax >> 16));

                    bool flag = GetDX() != 0;
                    SetFlagC(flag);
                    SetFlagO(flag);

                    cycle_count += 118;
                }
                else {
                    int result = _state.al * r1;
                    if (negate)
                        result = -result;
                    SetAX((ushort)result);

                    bool flag = _state.ah != 0;
                    SetFlagC(flag);
                    SetFlagO(flag);

                    cycle_count += 70;
                }
            }
            else if (function == 5)
            {
                bool negate = _state.rep_mode == RepMode.REP && _state.rep;
                _state.rep = false;

                // IMUL
                if (word) {
                    short ax = (short)GetAX();
                    int resulti = ax * (short)r1;

                    uint dx_ax = (uint)resulti;
                    if (negate)
                        dx_ax = (uint)-dx_ax;
                    SetAX((ushort)dx_ax);
                    SetDX((ushort)(dx_ax >> 16));

                    bool flag = (int)(short)GetAX() != resulti;
                    SetFlagC(flag);
                    SetFlagO(flag);

                    cycle_count += 128;
                }
                else {
                    int result = (sbyte)_state.al * (short)(sbyte)r1;
                    if (negate)
                        result = -result;
                    SetAX((ushort)result);

                    SetFlagS((_state.ah & 128) == 128);
                    bool flag = (short)(sbyte)_state.al != (short)result;
                    SetFlagC(flag);
                    SetFlagO(flag);

                    cycle_count += 80;
                }
            }
            else if (function == 6)
            {
                SetFlagC(false);
                SetFlagO(false);

                // DIV
                if (word) {
                    uint dx_ax = (uint)((GetDX() << 16) | GetAX());

                    if (r1 == 0 || dx_ax / r1 >= 0x10000)
                    {
                        SetZSPFlags(_state.ah);
                        SetFlagA(false);
                        InvokeInterrupt(_state.ip, 0x00, false);  // divide by zero or divisor too small
                    }
                    else
                    {
                        SetAX((ushort)(dx_ax / r1));
                        SetDX((ushort)(dx_ax % r1));
                    }
                }
                else {
                    ushort ax = GetAX();

                    if (r1 == 0 || ax / r1 >= 0x100)
                    {
                        SetZSPFlags(_state.ah);
                        SetFlagA(false);
                        InvokeInterrupt(_state.ip, 0x00, false);  // divide by zero or divisor too small
                    }
                    else
                    {
                        _state.al = (byte)(ax / r1);
                        _state.ah = (byte)(ax % r1);
                    }
                }
            }
            else if (function == 7)
            {
                bool negate = _state.rep_mode == RepMode.REP && _state.rep;
                _state.rep = false;

                SetFlagC(false);
                SetFlagO(false);

                // IDIV
                if (word) {
                    int dx_ax = (GetDX() << 16) | GetAX();
                    int r1s = (int)(short)r1;

                    if (r1s == 0 || dx_ax / r1s > 0x7fffffff || dx_ax / r1s < -0x80000000)
                    {
                        SetZSPFlags(_state.ah);
                        SetFlagA(false);
                        InvokeInterrupt(_state.ip, 0x00, false);  // divide by zero or divisor too small
                    }
                    else
                    {
                        if (negate)
                            SetAX((ushort)-(dx_ax / r1s));
                        else
                            SetAX((ushort)(dx_ax / r1s));
                        SetDX((ushort)(dx_ax % r1s));
                    }
                }
                else {
                    short ax = (short)GetAX();
                    short r1s = (short)(sbyte)r1;

                    if (r1s == 0 || ax / r1s > 0x7fff || ax / r1s < -0x8000)
                    {
                        SetZSPFlags(_state.ah);
                        SetFlagA(false);
                        InvokeInterrupt(_state.ip, 0x00, false);  // divide by zero or divisor too small
                    }
                    else
                    {
                        if (negate)
                            _state.al = (byte)-(ax / r1s);
                        else
                            _state.al = (byte)(ax / r1s);
                        _state.ah = (byte)(ax % r1s);
                    }
                }
            }
            else
            {
                Log.DoLog($"opcode {opcode:X2} o1 {o1:X2} function {function} not implemented", LogLevel.WARNING);
            }

            cycle_count += 4;
        }
        else if (opcode == 0xfa)
        {
            // CLI
            SetFlagI(false); // IF

            cycle_count += 2;
        }
        else if ((opcode & 0xf0) == 0xb0)
        {
            // MOV reg,ib
            int reg = opcode & 0x07;

            bool word = (opcode & 0x08) == 0x08;

            ushort v = GetPcByte();

            if (word)
                v |= (ushort)(GetPcByte() << 8);

            PutRegister(reg, word, v);

            cycle_count += 2;
        }
        else if (opcode == 0xa0)
        {
            // MOV AL,[...]
            ushort a = GetPcWord();

            _state.al = ReadMemByte(_state.segment_override_set ? _state.segment_override : _state.ds, a);

            cycle_count += 12;
        }
        else if (opcode == 0xa1)
        {
            // MOV AX,[...]
            ushort a = GetPcWord();

            SetAX(ReadMemWord(_state.segment_override_set ? _state.segment_override : _state.ds, a));

            cycle_count += 12;
        }
        else if (opcode == 0xa2)
        {
            // MOV [...],AL
            ushort a = GetPcWord();

            WriteMemByte(_state.segment_override_set ? _state.segment_override : _state.ds, a, _state.al);

            cycle_count += 13;
        }
        else if (opcode == 0xa3)
        {
            // MOV [...],AX
            ushort a = GetPcWord();

            WriteMemWord(_state.segment_override_set ? _state.segment_override : _state.ds, a, GetAX());

            cycle_count += 13;
        }
        else if (opcode == 0xa8)
        {
            // TEST AL,..
            byte v = GetPcByte();
            byte result = (byte)(_state.al & v);
            SetLogicFuncFlags(false, result);
            SetFlagC(false);

            cycle_count += 5;
        }
        else if (opcode == 0xa9)
        {
            // TEST AX,..
            ushort v = GetPcWord();
            ushort result = (ushort)(GetAX() & v);
            SetLogicFuncFlags(true, result);
            SetFlagC(false);

            cycle_count += 5;
        }
        else if (opcode is (0x88 or 0x89 or 0x8a or 0x8b or 0x8e or 0x8c))
        {
            bool dir = (opcode & 2) == 2; // direction
            bool word = (opcode & 1) == 1; // b/w

            byte o1 = GetPcByte();
            int mode = o1 >> 6;
            int reg = (o1 >> 3) & 7;
            int rm = o1 & 7;

            bool sreg = opcode == 0x8e || opcode == 0x8c;
            if (sreg)
            {
                word = true;
                _state.inhibit_interrupts = opcode == 0x8e;
            }

            cycle_count += 13;

            // 88: rm < r (byte) 00  false,byte
            // 89: rm < r (word) 01  false,word  <--
            // 8a: r < rm (byte) 10  true, byte
            // 8b: r < rm (word) 11  true, word

            // 89|E6 mode 3, reg 4, rm 6, dir False, word True, sreg False

            if (dir)
            {
                // to 'rm' from 'REG'
                (ushort v, bool a_valid, ushort seg, ushort addr, int get_cycles) = GetRegisterMem(rm, mode, word);
                cycle_count += get_cycles;

                if (sreg)
                    PutSRegister(reg, v);
                else
                    PutRegister(reg, word, v);
            }
            else
            {
                // from 'REG' to 'rm'
                ushort v = 0;
                if (sreg)
                    v = GetSRegister(reg);
                else
                    v = GetRegister(reg, word);

                int put_cycles = PutRegisterMem(rm, mode, word, v);
                cycle_count += put_cycles;
            }
        }
        else if (opcode == 0x8d)
        {
            // LEA
            byte o1 = GetPcByte();
            int mod = o1 >> 6;
            int reg = (o1 >> 3) & 7;
            int rm = o1 & 7;

            (ushort val, bool a_valid, ushort seg, ushort addr, int get_cycles) = GetRegisterMem(rm, mod, true);
            cycle_count += get_cycles + 3;

            PutRegister(reg, true, addr);
        }
        else if (opcode == 0x9e)
        {
            // SAHF
            ushort keep = (ushort)(_state.flags & 0b1111111100101010);
            ushort add = (ushort)(_state.ah & 0b11010101);

            _state.flags = (ushort)(keep | add);

            FixFlags();

            cycle_count += 4;
        }
        else if (opcode == 0x9f)
        {
            // LAHF
            _state.ah = (byte)_state.flags;

            cycle_count += 2;
        }
        else if (opcode is >= 0x40 and <= 0x4f)
        {
            // INC/DECw
            int reg = (opcode - 0x40) & 7;

            ushort v = GetRegister(reg, true);

            bool isDec = opcode >= 0x48;

            if (isDec)
                v--;
            else
                v++;

            if (isDec)
            {
                SetFlagO(v == 0x7fff);
                SetFlagA((v & 15) == 15);
            }
            else
            {
                SetFlagO(v == 0x8000);
                SetFlagA((v & 15) == 0);
            }

            SetFlagS((v & 0x8000) == 0x8000);
            SetFlagZ(v == 0);
            SetFlagP((byte)v);

            PutRegister(reg, true, v);

            cycle_count += 3;
        }
        else if (opcode == 0xaa)
        {
            if (PrefixMustRun())
            {
                // STOSB
                WriteMemByte(_state.es, _state.di, _state.al);

                _state.di += (ushort)(GetFlagD() ? -1 : 1);

                cycle_count += 11;
            }
        }
        else if (opcode == 0xab)
        {
            if (PrefixMustRun())
            {
                // STOSW
                WriteMemWord(_state.es, _state.di, GetAX());

                _state.di += (ushort)(GetFlagD() ? -2 : 2);

                cycle_count += 11;
            }
        }
        else if (opcode == 0xae)
        {
            if (PrefixMustRun())
            {
                // SCASB
                byte v = ReadMemByte(_state.es, _state.di);
                int result = _state.al - v;
                SetAddSubFlags(false, _state.al, v, result, true, false);

                _state.di += (ushort)(GetFlagD() ? -1 : 1);

                cycle_count += 15;
            }
        }
        else if (opcode == 0xaf)
        {
            if (PrefixMustRun())
            {
                // SCASW
                ushort ax = GetAX();
                ushort v = ReadMemWord(_state.es, _state.di);
                int result = ax - v;
                SetAddSubFlags(true, ax, v, result, true, false);

                _state.di += (ushort)(GetFlagD() ? -2 : 2);

                cycle_count += 15;
            }
        }
        else if (opcode == 0xc6 || opcode == 0xc7)
        {
            // MOV
            bool word = (opcode & 1) == 1;

            byte o1 = GetPcByte();

            int mod = o1 >> 6;

            int mreg = o1 & 7;

            cycle_count += 2;  // base (correct?)

            // get address to write to ('seg, addr')
            (ushort dummy, bool a_valid, ushort seg, ushort addr, int get_cycles) = GetRegisterMem(mreg, mod, word);
            cycle_count += get_cycles;

            if (word)
            {
                // the value follows
                ushort v = GetPcWord();
                int put_cycles = UpdateRegisterMem(mreg, mod, a_valid, seg, addr, word, v);
                cycle_count += put_cycles;
            }
            else
            {
                // the value follows
                byte v = GetPcByte();
                int put_cycles = UpdateRegisterMem(mreg, mod, a_valid, seg, addr, word, v);
                cycle_count += put_cycles;
            }
        }
        else if (opcode >= 0xc8 && opcode <= 0xcb)
        {
            // RETF n / RETF
            ushort nToRelease = (opcode == 0xca || opcode == 0xc8) ? GetPcWord() : (ushort)0;

            _state.ip = pop();
            _state.cs = pop();

            if (opcode == 0xca || opcode == 0xc8)
            {
                _state.sp += nToRelease;
                cycle_count += opcode == 0xca ? 33 : 24;
            }
            else
            {
                cycle_count += opcode == 0xcb ? 34 : 20;
            }
        }
        else if ((opcode & 0xfc) == 0xd0)
        {
            bool word = (opcode & 1) == 1;
            byte o1 = GetPcByte();

            int mod = o1 >> 6;
            int reg1 = o1 & 7;

            (ushort v1, bool a_valid, ushort seg, ushort addr, int get_cycles) = GetRegisterMem(reg1, mod, word);
            cycle_count += get_cycles;

            int count = 1;
            if ((opcode & 2) == 2)
                count = _state.cl;

            bool count_1_of = opcode is (0xd0 or 0xd1 or 0xd2 or 0xd3);

            bool oldSign = (word ? v1 & 0x8000 : v1 & 0x80) != 0;

            bool set_flags = false;

            int mode = (o1 >> 3) & 7;

            ushort check_bit = (ushort)(word ? 32768 : 128);
            ushort check_bit2 = (ushort)(word ? 16384 : 64);

            if (mode == 0)
            {
                // ROL
                for (int i = 0; i < count; i++)
                {
                    bool b7 = (v1 & check_bit) == check_bit;

                    SetFlagC(b7);

                    v1 <<= 1;

                    if (b7)
                        v1 |= 1;
                }

                if (count_1_of)
                    SetFlagO(GetFlagC() ^ ((v1 & check_bit) == check_bit));

                cycle_count += 2;
            }
            else if (mode == 1)
            {
                // ROR
                for (int i = 0; i < count; i++)
                {
                    bool b0 = (v1 & 1) == 1;

                    SetFlagC(b0);

                    v1 >>= 1;

                    if (b0)
                        v1 |= check_bit;
                }

                if (count_1_of)
                    SetFlagO(((v1 & check_bit) == check_bit) ^ ((v1 & check_bit2) == check_bit2));

                cycle_count += 2;
            }
            else if (mode == 2)
            {
                // RCL
                for (int i = 0; i < count; i++)
                {
                    bool new_carry = (v1 & check_bit) == check_bit;
                    v1 <<= 1;

                    bool oldCarry = GetFlagC();

                    if (oldCarry)
                        v1 |= 1;

                    SetFlagC(new_carry);
                }

                if (count_1_of)
                    SetFlagO(GetFlagC() ^ ((v1 & check_bit) == check_bit));

                cycle_count += 2;
            }
            else if (mode == 3)
            {
                // RCR
                for (int i = 0; i < count; i++)
                {
                    bool new_carry = (v1 & 1) == 1;
                    v1 >>= 1;

                    bool oldCarry = GetFlagC();

                    if (oldCarry)
                        v1 |= (ushort)(word ? 0x8000 : 0x80);

                    SetFlagC(new_carry);
                }

                if (count_1_of)
                    SetFlagO(((v1 & check_bit) == check_bit) ^ ((v1 & check_bit2) == check_bit2));

                cycle_count += 2;
            }
            else if (mode == 4)
            {
                ushort prev_v1 = v1;

                // SAL/SHL
                for (int i = 0; i < count; i++)
                {
                    bool new_carry = (v1 & check_bit) == check_bit;
                    v1 <<= 1;
                    SetFlagC(new_carry);
                }

                set_flags = count != 0;
                if (set_flags)
                {
                    SetFlagO(((v1 & check_bit) == check_bit) ^ GetFlagC());
                }

                cycle_count += count * 4;
            }
            else if (mode == 5)
            {
                ushort org_v1 = v1;

                // SHR
                for (int i = 0; i < count; i++)
                {
                    bool new_carry = (v1 & 1) == 1;
                    v1 >>= 1;
                    SetFlagC(new_carry);
                }

                set_flags = count != 0;

                if (count == 1)
                    SetFlagO((org_v1 & check_bit) != 0);
                else
                    SetFlagO(false);

                cycle_count += count * 4;
            }
            else if (mode == 6)
            {
                if (opcode >= 0xd2)
                {
                    // SETMOC
                    if (_state.cl != 0)
                    {
                        SetFlagC(false);
                        SetFlagA(false);
                        SetFlagZ(false);
                        SetFlagO(false);
                        SetFlagP(0xff);
                        SetFlagS(true);

                        v1 = (ushort)(word ? 0xffff : 0xff);

                        cycle_count += word ? 5 : 4;
                    }
                }
                else
                {
                    // SETMO
                    SetFlagC(false);
                    SetFlagA(false);
                    SetFlagZ(false);
                    SetFlagO(false);
                    SetFlagP(0xff);
                    SetFlagS(true);

                    v1 = (ushort)(word ? 0xffff : 0xff);

                    cycle_count += word ? 3 : 2;
                }
            }
            else if (mode == 7)
            {
                // SAR
                ushort mask = (ushort)((v1 & check_bit) != 0 ? check_bit : 0);

                for (int i = 0; i < count; i++)
                {
                    bool new_carry = (v1 & 0x01) == 0x01;
                    v1 >>= 1;
                    v1 |= mask;
                    SetFlagC(new_carry);
                }

                set_flags = count != 0;
                if (set_flags)
                    SetFlagO(false);

                cycle_count += 2;
            }
            else
            {
                Log.DoLog($"RCR/SHR/{opcode:X2} mode {mode} not implemented", LogLevel.WARNING);
            }

            if (!word)
                v1 &= 0xff;

            if (set_flags)
            {
                SetFlagS((word ? v1 & 0x8000 : v1 & 0x80) != 0);
                SetFlagZ(v1 == 0);
                SetFlagP((byte)v1);
            }

            int put_cycles = UpdateRegisterMem(reg1, mod, a_valid, seg, addr, word, v1);
            cycle_count += put_cycles;
        }
        else if (opcode == 0xd4)
        {
            // AAM
            byte b2 = GetPcByte();

            if (b2 != 0)
            {
                _state.ah = (byte)(_state.al / b2);
                _state.al %= b2;

                SetZSPFlags(_state.al);
            }
            else
            {
                SetZSPFlags(0);

                SetFlagO(false);
                SetFlagA(false);
                SetFlagC(false);

                InvokeInterrupt(_state.ip, 0x00, false);
            }

            cycle_count += 83;
        }
        else if (opcode == 0xd5)
        {
            // AAD
            byte b2 = GetPcByte();

            _state.al = (byte)(_state.al + _state.ah * b2);
            _state.ah = 0;

            SetZSPFlags(_state.al);

            cycle_count += 60;
        }
        else if (opcode == 0xd6)
        {
            // SALC
            if (GetFlagC())
                _state.al = 0xff;
            else
                _state.al = 0x00;

            cycle_count += 2;  // TODO
        }
        else if (opcode == 0x9b)
        {
            // FWAIT
            cycle_count += 2;  // TODO
        }
        else if (opcode >= 0xd8 && opcode <= 0xdf)
        {
            // FPU
            byte o1 = GetPcByte();
            int mod = o1 >> 6;
            int reg1 = o1 & 7;
            (ushort v1, bool a_valid, ushort seg, ushort addr, int get_cycles) = GetRegisterMem(reg1, mod, false);
            cycle_count += get_cycles;

            cycle_count += 2;  // TODO
        }
        else if ((opcode & 0xf0) == 0x70 || (opcode & 0xf0) == 0x60)
        {
            // J..., 0x70/0x60
            byte to = GetPcByte();

            bool state = false;

            if (opcode == 0x70 || opcode == 0x60)
            {
                state = GetFlagO();
            }
            else if (opcode == 0x71 || opcode == 0x61)
            {
                state = GetFlagO() == false;
            }
            else if (opcode == 0x72 || opcode == 0x62)
            {
                state = GetFlagC();
            }
            else if (opcode == 0x73 || opcode == 0x63)
            {
                state = GetFlagC() == false;
            }
            else if (opcode == 0x74 || opcode == 0x64)
            {
                state = GetFlagZ();
            }
            else if (opcode == 0x75 || opcode == 0x65)
            {
                state = GetFlagZ() == false;
            }
            else if (opcode == 0x76 || opcode == 0x66)
            {
                state = GetFlagC() || GetFlagZ();
            }
            else if (opcode == 0x77 || opcode == 0x67)
            {
                state = GetFlagC() == false && GetFlagZ() == false;
            }
            else if (opcode == 0x78 || opcode == 0x68)
            {
                state = GetFlagS();
            }
            else if (opcode == 0x79 || opcode == 0x69)
            {
                state = GetFlagS() == false;
            }
            else if (opcode == 0x7a || opcode == 0x6a)
            {
                state = GetFlagP();
            }
            else if (opcode == 0x7b || opcode == 0x6b)
            {
                state = GetFlagP() == false;
            }
            else if (opcode == 0x7c || opcode == 0x6c)
            {
                state = GetFlagS() != GetFlagO();
            }
            else if (opcode == 0x7d || opcode == 0x6d)
            {
                state = GetFlagS() == GetFlagO();
            }
            else if (opcode == 0x7e || opcode == 0x6e)
            {
                state = GetFlagZ() == true || GetFlagS() != GetFlagO();
            }
            else if (opcode == 0x7f || opcode == 0x6f)
            {
                state = GetFlagZ() == false && GetFlagS() == GetFlagO();
            }
            else
            {
                Log.DoLog($"opcode {opcode:x2} not implemented", LogLevel.WARNING);
            }

            ushort newAddress = (ushort)(_state.ip + (sbyte)to);

            if (state)
            {
                _state.ip = newAddress;
                cycle_count += 16;
            }
            else
            {
                cycle_count += 4;
            }
        }
        else if (opcode == 0xd7)
        {
            // XLATB
            byte old_al = _state.al;

            _state.al = ReadMemByte(_state.segment_override_set ? _state.segment_override : _state.ds, (ushort)(GetBX() + _state.al));

            cycle_count += 11;
        }
        else if (opcode == 0xe0 || opcode == 0xe1 || opcode == 0xe2)
        {
            // LOOP
            byte to = GetPcByte();

            ushort cx = GetCX();
            cx--;
            SetCX(cx);

            ushort newAddresses = (ushort)(_state.ip + (sbyte)to);

            cycle_count += 4;

            if (opcode == 0xe2)
            {
                if (cx > 0)
                {
                    _state.ip = newAddresses;
                    cycle_count += 4;
                }
            }
            else if (opcode == 0xe1)
            {
                if (cx > 0 && GetFlagZ() == true)
                {
                    _state.ip = newAddresses;
                    cycle_count += 4;
                }
            }
            else if (opcode == 0xe0)
            {
                if (cx > 0 && GetFlagZ() == false)
                {
                    _state.ip = newAddresses;
                    cycle_count += 4;
                }
            }
            else
            {
                Log.DoLog($"opcode {opcode:X2} not implemented", LogLevel.WARNING);
            }
        }
        else if (opcode == 0xe4)
        {
            // IN AL,ib
            byte @from = GetPcByte();

            (ushort val, bool i) = _io.In(@from);
            _state.al = (byte)val;

            cycle_count += 14;
        }
        else if (opcode == 0xe5)
        {
            // IN AX,ib
            byte @from = GetPcByte();

            (ushort val, bool i) = _io.In(@from);
            SetAX(val);

            cycle_count += 14;
        }
        else if (opcode == 0xe6)
        {
            // OUT
            byte to = GetPcByte();
            _io.Out(@to, _state.al);

            cycle_count += 10;  // max 14
        }
        else if (opcode == 0xe7)
        {
            // OUT
            byte to = GetPcByte();
            _io.Out(@to, GetAX());

            cycle_count += 10;  // max 14
        }
        else if (opcode == 0xec)
        {
            // IN AL,DX
            (ushort val, bool i) = _io.In(GetDX());
            _state.al = (byte)val;

            cycle_count += 12;
        }
        else if (opcode == 0xed)
        {
            // IN AX,DX
            (ushort val, bool i) = _io.In(GetDX());
            SetAX(val);

            cycle_count += 12;
        }
        else if (opcode == 0xee)
        {
            // OUT
            _io.Out(GetDX(), _state.al);

            cycle_count += 12;
        }
        else if (opcode == 0xef)
        {
            // OUT
            _io.Out(GetDX(), GetAX());

            cycle_count += 12;
        }
        else if (opcode == 0xeb)
        {
            // JMP
            byte to = GetPcByte();
            _state.ip = (ushort)(_state.ip + (sbyte)to);
            cycle_count += 15;
        }
        else if (opcode == 0xf4)
        {
            // HLT
            _state.in_hlt = true;
            cycle_count += 2;
        }
        else if (opcode == 0xf5)
        {
            // CMC
            SetFlagC(! GetFlagC());

            cycle_count += 2;
        }
        else if (opcode == 0xf8)
        {
            // CLC
            SetFlagC(false);

            cycle_count += 2;
        }
        else if (opcode == 0xf9)
        {
            // STC
            SetFlagC(true);

            cycle_count += 2;
        }
        else if (opcode == 0xfb)
        {
            // STI
            SetFlagI(true); // IF
            _state.inhibit_interrupts = true;

            cycle_count += 2;
        }
        else if (opcode == 0xfc)
        {
            // CLD
            SetFlagD(false);

            cycle_count += 2;
        }
        else if (opcode == 0xfd)
        {
            // STD
            SetFlagD(true);

            cycle_count += 2;
        }
        else if (opcode == 0xfe || opcode == 0xff)
        {
            // DEC and others
            bool word = (opcode & 1) == 1;
            byte o1 = GetPcByte();
            int mod = o1 >> 6;
            int reg = o1 & 7;
            int function = (o1 >> 3) & 7;

            // Log.DoLog($"mod {mod} reg {reg} word {word} function {function}", true);

            (ushort v, bool a_valid, ushort seg, ushort addr, int get_cycles) = GetRegisterMem(reg, mod, word);
            cycle_count += get_cycles;

            if (function == 0)
            {
                // INC
                v++;

                cycle_count += 3;

                SetFlagO(word ? v == 0x8000 : v == 0x80);
                SetFlagA((v & 15) == 0);

                SetFlagS(word ? (v & 0x8000) == 0x8000 : (v & 0x80) == 0x80);
                SetFlagZ(word ? v == 0 : (v & 0xff) == 0);
                SetFlagP((byte)v);
            }
            else if (function == 1)
            {
                // DEC
                v--;

                cycle_count += 3;

                SetFlagO(word ? v == 0x7fff : v == 0x7f);
                SetFlagA((v & 15) == 15);

                SetFlagS(word ? (v & 0x8000) == 0x8000 : (v & 0x80) == 0x80);
                SetFlagZ(word ? v == 0 : (v & 0xff) == 0);
                SetFlagP((byte)v);
            }
            else if (function == 2)
            {
                // CALL
                push(_state.ip);

                _state.rep = false;
                _state.ip = v;

                cycle_count += 16;
            }
            else if (function == 3)
            {
                // CALL FAR
                push(_state.cs);
                push(_state.ip);

                _state.ip = v;
                _state.cs = ReadMemWord(seg, (ushort)(addr + 2));

                cycle_count += 37;
            }
            else if (function == 4)
            {
                // JMP NEAR
                _state.ip = v;

                cycle_count += 18;
            }
            else if (function == 5)
            {
                // JMP
                _state.cs = ReadMemWord(seg, (ushort)(addr + 2));
                _state.ip = ReadMemWord(seg, addr);

                cycle_count += 15;
            }
            else if (function == 6)
            {
                // PUSH rmw
                if (reg == 4 && mod == 3 && word == true)  // PUSH SP
                {
                    v -= 2;
                    WriteMemWord(_state.ss, v, v);
                }
                else
                {
                    push(v);
                }

                cycle_count += 16;
            }
            else
            {
                Log.DoLog($"opcode {opcode:X2} function {function} not implemented", LogLevel.WARNING);
            }

            if (!word)
                v &= 0xff;

            int put_cycles = UpdateRegisterMem(reg, mod, a_valid, seg, addr, word, v);
            cycle_count += put_cycles;
        }
        else
        {
            Log.DoLog($"opcode {opcode:x} not implemented", LogLevel.WARNING);
        }

        PrefixEnd(opcode);

        if (cycle_count == 0)
        {
            Log.DoLog($"cyclecount not set for {opcode:X02}", LogLevel.WARNING);
            cycle_count = 1;  // TODO workaround
        }

        _state.clock += cycle_count;

        // tick I/O
        _io.Tick(cycle_count, _state.clock);

        if (GetFlagT() && back_from_trace == false && _state.inhibit_interrupts == false)
            InvokeInterrupt(_state.ip, 1, false);

        return cycle_count;
    }
}
