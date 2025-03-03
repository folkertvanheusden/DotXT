namespace DotXT;

internal enum TMode
{
    NotSet,
    Binary,
    Blank
}

internal enum RepMode
{
    NotSet,
    REPE_Z,
    REPNZ,
    REP
}

internal class P8086
{
    private byte _ah, _al;
    private byte _bh, _bl;
    private byte _ch, _cl;
    private byte _dh, _dl;

    private ushort _si;
    private ushort _di;
    private ushort _bp;
    private ushort _sp;

    private ushort _ip;

    private ushort _cs;
    private ushort _ds;
    private ushort _es;
    private ushort _ss;

    // replace by an Optional-type when available
    private ushort _segment_override;
    private bool _segment_override_set;

    private ushort _flags;

    private bool _in_hlt = false;

    private int _crash_counter = 0;
    private bool _terminate_on_off_the_rails = false;

    private const uint MemMask = 0x00ffffff;

    private Bus _b;
    private readonly IO _io;

    private bool _rep;
    private bool _rep_do_nothing;
    private RepMode _rep_mode;
    private ushort _rep_addr;
    private byte _rep_opcode;

    private long _clock;
    private List<Device> _devices;

    private List<uint> _breakpoints = new();
    private bool _ignore_breakpoints = false;
    private string _stop_reason = "";

    public P8086(ref Bus b, string test, TMode t_mode, uint load_test_at, ref List<Device> devices, bool run_IO)
    {
        _b = b;
        _devices = devices;
        _io = new IO(b, ref devices, !run_IO);
        _terminate_on_off_the_rails = run_IO;

        if (test != "" && t_mode == TMode.Binary)
        {
            _cs = 0;
            _ip = 0x0800;

            uint addr = load_test_at == 0xffffffff ? 0 : load_test_at;

            Log.DoLog($"Load {test} at {addr:X6}", LogLevel.INFO);

            using(Stream source = File.Open(test, FileMode.Open))
            {
                byte[] buffer = new byte[512];

                for(;;)
                {
                    int n_read = source.Read(buffer, 0, 512);

                    if (n_read == 0)
                        break;

                    for(int i=0; i<n_read; i++)
                    {
                        _b.WriteByte(addr, buffer[i]);
                        addr++;
                    }
                }
            }
        }
        else if (t_mode != TMode.Blank)
        {
            _cs = 0xf000;
            _ip = 0xfff0;
        }

        // bit 1 of the flags register is always 1
        // https://www.righto.com/2023/02/silicon-reverse-engineering-intel-8086.html
        _flags |= 2;
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
        _cs = 0xf000;
        _ip = 0xfff0;
        _in_hlt = false;
    }

    public long GetClock()
    {
        return _clock;
    }

    public string SegmentAddr(ushort seg, ushort a)
    {
        return $"{seg:X04}:{a:X04}";
    }

    public void set_ip(ushort cs, ushort ip)
    {
        Log.DoLog($"Set CS/IP to {cs:X4}:{ip:X4}", LogLevel.DEBUG);

        _cs = cs;
        _ip = ip;
    }

    private void FixFlags()
    {
        _flags &= 0b1111111111010101;
        _flags |= 2;  // bit 1 is always set
        _flags |= 0xf000;  // upper 4 bits are always 1
    }

    private byte GetPcByte()
    {
        return ReadMemByte(_cs, _ip++);
    }

    private ushort GetPcWord()
    {
        ushort v = GetPcByte();
        v |= (ushort)(GetPcByte() << 8);
        return v;
    }

    public ushort GetAX()
    {
        return (ushort)((_ah << 8) | _al);
    }

    public void SetAX(ushort v)
    {
        _ah = (byte)(v >> 8);
        _al = (byte)v;
    }

    public ushort GetBX()
    {
        return (ushort)((_bh << 8) | _bl);
    }

    public void SetBX(ushort v)
    {
        _bh = (byte)(v >> 8);
        _bl = (byte)v;
    }

    public ushort GetCX()
    {
        return (ushort)((_ch << 8) | _cl);
    }

    public void SetCX(ushort v)
    {
        _ch = (byte)(v >> 8);
        _cl = (byte)v;
    }

    public ushort GetDX()
    {
        return (ushort)((_dh << 8) | _dl);
    }

    public void SetDX(ushort v)
    {
        _dh = (byte)(v >> 8);
        _dl = (byte)v;
    }

    public void SetSS(ushort v)
    {
        _ss = v;
    }

    public void SetCS(ushort v)
    {
        _cs = v;
    }

    public void SetDS(ushort v)
    {
        _ds = v;
    }

    public void SetES(ushort v)
    {
        _es = v;
    }

    public void SetSP(ushort v)
    {
        _sp = v;
    }

    public void SetBP(ushort v)
    {
        _bp = v;
    }

    public void SetSI(ushort v)
    {
        _si = v;
    }

    public void SetDI(ushort v)
    {
        _di = v;
    }

    public void SetIP(ushort v)
    {
        _ip = v;
    }

    public void SetFlags(ushort v)
    {
        _flags = v;
    }

    public ushort GetSS()
    {
        return _ss;
    }

    public ushort GetCS()
    {
        return _cs;
    }

    public ushort GetDS()
    {
        return _ds;
    }

    public ushort GetES()
    {
        return _es;
    }

    public ushort GetSP()
    {
        return _sp;
    }

    public ushort GetBP()
    {
        return _bp;
    }

    public ushort GetSI()
    {
        return _si;
    }

    public ushort GetDI()
    {
        return _di;
    }

    public ushort GetIP()
    {
        return _ip;
    }

    public ushort GetFlags()
    {
        return _flags;
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
        _clock += _b.WriteByte(a, v);
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
        _clock += rc.Item2;
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
                return _sp;
            if (reg == 5)
                return _bp;
            if (reg == 6)
                return _si;
            if (reg == 7)
                return _di;
        }
        else
        {
            if (reg == 0)
                return _al;
            if (reg == 1)
                return _cl;
            if (reg == 2)
                return _dl;
            if (reg == 3)
                return _bl;
            if (reg == 4)
                return _ah;
            if (reg == 5)
                return _ch;
            if (reg == 6)
                return _dh;
            if (reg == 7)
                return _bh;
        }

        Log.DoLog($"reg {reg} w {w} not supported for {nameof(GetRegister)}", LogLevel.WARNING);

        return 0;
    }

    private ushort GetSRegister(int reg)
    {
        reg &= 0b00000011;

        if (reg == 0b000)
            return _es;
        if (reg == 0b001)
            return _cs;
        if (reg == 0b010)
            return _ss;
        if (reg == 0b011)
            return _ds;

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
            a = (ushort)(GetBX() + _si);
            cycles = 7;
        }
        else if (reg == 1)
        {
            a = (ushort)(GetBX() + _di);
            cycles = 8;
        }
        else if (reg == 2)
        {
            a = (ushort)(_bp + _si);
            cycles = 8;
        }
        else if (reg == 3)
        {
            a = (ushort)(_bp + _di);
            cycles = 7;
        }
        else if (reg == 4)
        {
            a = _si;
            cycles = 5;
        }
        else if (reg == 5)
        {
            a = _di;
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
            a = _bp;
            cycles = 5;
            override_segment = true;
            new_segment = _ss;
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

            ushort segment = _segment_override_set ? _segment_override : _ds;

            if (_segment_override_set == false && (reg == 2 || reg == 3)) {  // BP uses SS
                segment = _ss;
            }

            ushort v = w ? ReadMemWord(segment, a) : ReadMemByte(segment, a);

            cycles += 6;

            return (v, true, segment, a, cycles);
        }

        if (mod == 1 || mod == 2)
        {
            bool word = mod == 2;

            (ushort a, int cycles, bool override_segment, ushort new_segment) = GetDoubleRegisterMod01_02(reg, word);

            ushort segment = _segment_override_set ? _segment_override : _ds;

            if (_segment_override_set == false && override_segment)
                segment = new_segment;

            if (_segment_override_set == false && (reg == 2 || reg == 3))  // BP uses SS
                segment = _ss;

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
                _al = (byte)val;
        }
        else if (reg == 1)
        {
            if (w)
                SetCX(val);
            else
                _cl = (byte)val;
        }
        else if (reg == 2)
        {
            if (w)
                SetDX(val);
            else
                _dl = (byte)val;
        }
        else if (reg == 3)
        {
            if (w)
                SetBX(val);
            else
                _bl = (byte)val;
        }
        else if (reg == 4)
        {
            if (w)
                _sp = val;
            else
                _ah = (byte)val;
        }
        else if (reg == 5)
        {
            if (w)
                _bp = val;
            else
                _ch = (byte)val;
        }
        else if (reg == 6)
        {
            if (w)
                _si = val;
            else
                _dh = (byte)val;
        }
        else if (reg == 7)
        {
            if (w)
                _di = val;
            else
                _bh = (byte)val;
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
            _es = v;
        else if (reg == 0b001)
            _cs = v;
        else if (reg == 0b010)
            _ss = v;
        else if (reg == 0b011)
            _ds = v;
        else
            Log.DoLog($"reg {reg} not supported for {nameof(PutSRegister)}", LogLevel.WARNING);
    }

    // cycles
    private int PutRegisterMem(int reg, int mod, bool w, ushort val)
    {
        if (mod == 0)
        {
            (ushort a, int cycles) = GetDoubleRegisterMod00(reg);

            ushort segment = _segment_override_set ? _segment_override : _ds;

            if (_segment_override_set == false && (reg == 2 || reg == 3))  // BP uses SS
                segment = _ss;

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

            ushort segment = _segment_override_set ? _segment_override : _ds;

            if (_segment_override_set == false && override_segment)
                segment = new_segment;

            if (_segment_override_set == false && (reg == 2 || reg == 3))  // BP uses SS
                segment = _ss;

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
        _flags &= (ushort)(ushort.MaxValue ^ (1 << bit));
    }

    private void SetFlagBit(int bit)
    {
        _flags |= (ushort)(1 << bit);
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
        return (_flags & (1 << bit)) != 0;
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

    // TODO class/struct or enum Flags (with [Flags]) and ToString()
    public string GetFlagsAsString()
    {
        string @out = String.Empty;

        @out += GetFlagO() ? "o" : "-";
        @out += GetFlagI() ? "I" : "-";
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
        _sp -= 2;

        // Log.DoLog($"push({v:X4}) write @ {_ss:X4}:{_sp:X4}", true);

        WriteMemWord(_ss, _sp, v);
    }

    public ushort pop()
    {
        ushort v = ReadMemWord(_ss, _sp);

        // Log.DoLog($"pop({v:X4}) read @ {_ss:X4}:{_sp:X4}", true);

        _sp += 2;

        return v;
    }

    void InvokeInterrupt(ushort instr_start, int interrupt_nr, bool pic)
    {
        _segment_override_set = false;

        if (pic)
        {
            _io.GetPIC().SetIRQBeingServiced(interrupt_nr);
            interrupt_nr += _io.GetPIC().GetInterruptOffset();
        }

        push(_flags);
        push(_cs);
        if (_rep)
        {
            push(_rep_addr);
            _rep = false;
        }
        else
        {
            push(instr_start);
        }

        SetFlagI(false);

        ushort addr = (ushort)(interrupt_nr * 4);

        _ip = ReadMemWord(0, addr);
        _cs = ReadMemWord(0, (ushort)(addr + 2));
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
        return _rep;
    }

    public bool PrefixMustRun()
    {
        bool rc = true;

        if (_rep)
        {
            ushort cx = GetCX();

            if (_rep_do_nothing)
            {
                _rep = false;
                rc = false;
            }
            else
            {
                cx--;
                SetCX(cx);

                if (cx == 0)
                {
                    _rep = false;
                }
                else if (_rep_mode == RepMode.REPE_Z)
                {
                }
                else if (_rep_mode == RepMode.REPNZ)
                {
                }
                else if (_rep_mode == RepMode.REP)
                {
                }
                else
                {
                    Log.DoLog($"unknown _rep_mode {_rep_mode}", LogLevel.WARNING);
                    _rep = false;
                    rc = false;
                }
            }
        }

        _rep_do_nothing = false;

        return rc;
    }

    public void PrefixEnd(byte opcode)
    {
        if (opcode is (0xa4 or 0xa5 or 0xa6 or 0xa7 or 0xaa or 0xab or 0xac or 0xad or 0xae or 0xaf))
        {
            if (_rep_mode == RepMode.REPE_Z)
            {
                // REPE/REPZ
                if (GetFlagZ() != true)
                {
                    _rep = false;
                }
            }
            else if (_rep_mode == RepMode.REPNZ)
            {
                // REPNZ
                if (GetFlagZ() != false)
                {
                    _rep = false;
                }
            }
        }
        else
        {
            _rep = false;
        }

        if (_rep == false)
        {
            _segment_override_set = false;
        }

        if (_rep)
            _ip = _rep_addr;
    }

    public void ResetCrashCounter()
    {
        _crash_counter = 0;
    }

    public bool IsInHlt()
    {
        return _in_hlt;
    }

    // cycle counts from https://zsmith.co/intel_i.php
    public bool Tick()
    {
        int cycle_count = 0;  // cycles used for an instruction

        Log.SetMeta(_clock, _cs, _ip);

        // check for interrupt
        if (GetFlagI() == true)
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

                    _in_hlt = false;
                    InvokeInterrupt(_ip, irq, true);
                    cycle_count += 60;

                    break;
                }
            }
        }

        if (_in_hlt)
        {
            cycle_count += 2;
            _clock += cycle_count;  // time needs to progress for timers etc
            _io.Tick(cycle_count, _clock);
            return true;
        }

        ushort instr_start = _ip;
        uint address = (uint)(_cs * 16 + _ip) & MemMask;
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
                    return false;
                }
            }
        }

        // handle prefixes
        while (opcode is (0x26 or 0x2e or 0x36 or 0x3e or 0xf2 or 0xf3))
        {
            if (opcode == 0x26)
                _segment_override = _es;
            else if (opcode == 0x2e)
                _segment_override = _cs;
            else if (opcode == 0x36)
                _segment_override = _ss;
            else if (opcode == 0x3e)
                _segment_override = _ds;
            else if (opcode is (0xf2 or 0xf3))
            {
                _rep = true;
                _rep_mode = RepMode.NotSet;
                cycle_count += 3;

                _rep_do_nothing = GetCX() == 0;
            }
            else
            {
                Log.DoLog($"prefix {opcode:X2} not implemented", LogLevel.WARNING);
            }

            address = (uint)(_cs * 16 + _ip) & MemMask;
            byte next_opcode = GetPcByte();

            _rep_opcode = next_opcode;  // TODO: only allow for certain instructions

            if (opcode == 0xf2)
            {
                _rep_addr = instr_start;
                if (next_opcode is (0xa6 or 0xa7 or 0xae or 0xaf))
                {
                    _rep_mode = RepMode.REPNZ;
                    //Log.DoLog($"REPNZ: {_cs:X4}:{_rep_addr:X4}", true);
                }
                else
                {
                    _rep_mode = RepMode.REP;
                }
            }
            else if (opcode == 0xf3)
            {
                _rep_addr = instr_start;
                if (next_opcode is (0xa6 or 0xa7 or 0xae or 0xaf))
                {
                    _rep_mode = RepMode.REPE_Z;
                    //Log.DoLog($"REPZ: {_cs:X4}:{_rep_addr:X4}", true);
                }
                else
                {
                    _rep_mode = RepMode.REP;
                    //Log.DoLog($"REP: {_cs:X4}:{_rep_addr:X4}", true);
                }
            }
            else
            {
                _segment_override_set = true;  // TODO: move up
                cycle_count += 2;
            }

            opcode = next_opcode;
        }

        if (opcode == 0x00)
        {
            if (_terminate_on_off_the_rails == true && ++_crash_counter >= 5)
            {
                _stop_reason = $"Terminating because of {_crash_counter}x 0x00 opcode ({address:X06})";
                Log.DoLog(_stop_reason, LogLevel.WARNING);
                return false;
            }
        }
        else
        {
            _crash_counter = 0;
        }

        // main instruction handling
        if (opcode == 0x04 || opcode == 0x14)
        {
            // ADD AL,xx
            byte v = GetPcByte();

            bool flag_c = GetFlagC();
            bool use_flag_c = false;

            int result = _al + v;

            if (opcode == 0x14)
            {
                if (flag_c)
                    result++;

                use_flag_c = true;
            }

            cycle_count += 3;

            SetAddSubFlags(false, _al, v, result, false, use_flag_c ? flag_c : false);

            _al = (byte)result;
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
            push(_es);

            cycle_count += 11;  // 15
        }
        else if (opcode == 0x07)
        {
            // POP ES
            _es = pop();

            cycle_count += 8;
        }
        else if (opcode == 0x0e)
        {
            // PUSH CS
            push(_cs);

            cycle_count += 11;  // 15
        }
        else if (opcode == 0x0f)
        {
            // POP CS
            _cs = pop();

            cycle_count += 8;
        }
        else if (opcode == 0x16)
        {
            // PUSH SS
            push(_ss);

            cycle_count += 11;  // 15
        }
        else if (opcode == 0x17)
        {
            // POP SS
            _ss = pop();

            cycle_count += 11;  // 15
        }
        else if (opcode == 0x1c)
        {
            // SBB AL,ib
            byte v = GetPcByte();

            bool flag_c = GetFlagC();

            int result = _al - v;

            if (flag_c)
                result--;

            SetAddSubFlags(false, _al, v, result, true, flag_c);

            _al = (byte)result;

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
            push(_ds);

            cycle_count += 11;  // 15
        }
        else if (opcode == 0x1f)
        {
            // POP DS
            _ds = pop();

            cycle_count += 8;
        }
        else if (opcode == 0x27)
        {
            // DAA
            // https://www.felixcloutier.com/x86/daa
            byte old_al = _al;
            bool old_af = GetFlagA();
            bool old_cf = GetFlagC();

            SetFlagC(false);

            if (((_al & 0x0f) > 9) || GetFlagA() == true)
            {
                bool add_carry = _al + 6 > 255;

                _al += 6;

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
                _al += 0x60;
                SetFlagC(true);
            }
            else
            {
                SetFlagC(false);
            }

            SetZSPFlags(_al);

            cycle_count += 4;
        }
        else if (opcode == 0x2c)
        {
            // SUB AL,ib
            byte v = GetPcByte();

            int result = _al - v;

            SetAddSubFlags(false, _al, v, result, true, false);

            _al = (byte)result;

            cycle_count += 3;
        }
        else if (opcode == 0x2f)
        {
            // DAS
            byte old_al = _al;
            bool old_af = GetFlagA();
            bool old_cf = GetFlagC();

            SetFlagC(false);

            if ((_al & 0x0f) > 9 || GetFlagA() == true)
            {
                _al -= 6;

                SetFlagA(true);
            }
            else
            {
                SetFlagA(false);
            }

            byte upper_nibble_check = (byte)(old_af ? 0x9f : 0x99);

            if (old_al > upper_nibble_check || old_cf)
            {
                _al -= 0x60;
                SetFlagC(true);
            }

            SetZSPFlags(_al);

            cycle_count += 4;
        }
        else if (opcode == 0x37)
        {
            if ((_al & 0x0f) > 9 || GetFlagA())
            {
                _ah += 1;

                _al += 6;

                SetFlagA(true);
                SetFlagC(true);
            }
            else
            {
                SetFlagA(false);
                SetFlagC(false);
            }

            _al &= 0x0f;

            cycle_count += 8;
        }
        else if (opcode == 0x3f)
        {
            if ((_al & 0x0f) > 9 || GetFlagA())
            {
                _al -= 6;
                _ah -= 1;

                SetFlagA(true);
                SetFlagC(true);
            }
            else
            {
                SetFlagA(false);
                SetFlagC(false);
            }

            _al &= 0x0f;

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
            _sp = pop();

            cycle_count += 8;
        }
        else if (opcode == 0x5d)
        {
            // POP BP
            _bp = pop();

            cycle_count += 8;
        }
        else if (opcode == 0x5e)
        {
            // POP SI
            _si = pop();

            cycle_count += 8;
        }
        else if (opcode == 0x5f)
        {
            // POP DI
            _di = pop();

            cycle_count += 8;
        }
        else if (opcode == 0xa4)
        {
            if (PrefixMustRun())
            {
                // MOVSB
                ushort segment = _segment_override_set ? _segment_override : _ds;
                byte v = ReadMemByte(segment, _si);
                WriteMemByte(_es, _di, v);

                _si += (ushort)(GetFlagD() ? -1 : 1);
                _di += (ushort)(GetFlagD() ? -1 : 1);

                cycle_count += 18;
            }
        }
        else if (opcode == 0xa5)
        {
            if (PrefixMustRun())
            {
                // MOVSW
                WriteMemWord(_es, _di, ReadMemWord(_segment_override_set ? _segment_override : _ds, _si));

                _si += (ushort)(GetFlagD() ? -2 : 2);
                _di += (ushort)(GetFlagD() ? -2 : 2);

                cycle_count += 18;
            }
        }
        else if (opcode == 0xa6)
        {
            if (PrefixMustRun())
            {
                // CMPSB
                byte v1 = ReadMemByte(_segment_override_set ? _segment_override : _ds, _si);
                byte v2 = ReadMemByte(_es, _di);

                int result = v1 - v2;

                _si += (ushort)(GetFlagD() ? -1 : 1);
                _di += (ushort)(GetFlagD() ? -1 : 1);

                SetAddSubFlags(false, v1, v2, result, true, false);

                cycle_count += 22;
            }
        }
        else if (opcode == 0xa7)
        {
            if (PrefixMustRun())
            {
                // CMPSW
                ushort v1 = ReadMemWord(_segment_override_set ? _segment_override : _ds, _si);
                ushort v2 = ReadMemWord(_es, _di);

                int result = v1 - v2;

                _si += (ushort)(GetFlagD() ? -2 : 2);
                _di += (ushort)(GetFlagD() ? -2 : 2);

                SetAddSubFlags(true, v1, v2, result, true, false);

                cycle_count += 22;
            }
        }
        else if (opcode == 0xe3)
        {
            // JCXZ np
            sbyte offset = (sbyte)GetPcByte();

            ushort addr = (ushort)(_ip + offset);

            if (GetCX() == 0)
            {
                _ip = addr;
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

            _ip = (ushort)(_ip + offset);

            cycle_count += 15;
        }
        else if (opcode == 0x50)
        {
            // PUSH AX
            push(GetAX());

            cycle_count += 11;  // 15
        }
        else if (opcode == 0x51)
        {
            // PUSH CX
            push(GetCX());

            cycle_count += 11;  // 15
        }
        else if (opcode == 0x52)
        {
            // PUSH DX
            push(GetDX());

            cycle_count += 11;  // 15
        }
        else if (opcode == 0x53)
        {
            // PUSH BX
            push(GetBX());

            cycle_count += 11;  // 15
        }
        else if (opcode == 0x54)
        {
            // PUSH SP
            // special case, see:
            // https://c9x.me/x86/html/file_module_x86_id_269.html
            _sp -= 2;
            WriteMemWord(_ss, _sp, _sp);

            cycle_count += 11;  // 15
        }
        else if (opcode == 0x55)
        {
            // PUSH BP
            push(_bp);

            cycle_count += 11;  // 15
        }
        else if (opcode == 0x56)
        {
            // PUSH SI
            push(_si);

            cycle_count += 11;  // 15
        }
        else if (opcode == 0x57)
        {
            // PUSH DI
            push(_di);

            cycle_count += 11;  // 15
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
            ushort new_value = _al;

            if ((_al & 128) == 128)
                new_value |= 0xff00;

            SetAX(new_value);

            cycle_count += 2;
        }
        else if (opcode == 0x99)
        {
            // CWD
            if ((_ah & 128) == 128)
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

            push(_cs);
            push(_ip);

            _ip = temp_ip;
            _cs = temp_cs;

            cycle_count += 37;
        }
        else if (opcode == 0x9c)
        {
            // PUSHF
            push(_flags);

            cycle_count += 10;  // 14
        }
        else if (opcode == 0x9d)
        {
            // POPF
            _flags = pop();

            cycle_count += 8;  // 12

            FixFlags();
        }
        else if (opcode == 0xac)
        {
            if (PrefixMustRun())
            {
                // LODSB
                _al = ReadMemByte(_segment_override_set ? _segment_override : _ds, _si);

                _si += (ushort)(GetFlagD() ? -1 : 1);

                cycle_count += 5;
            }
        }
        else if (opcode == 0xad)
        {
            if (PrefixMustRun())
            {
                // LODSW
                SetAX(ReadMemWord(_segment_override_set ? _segment_override : _ds, _si));

                _si += (ushort)(GetFlagD() ? -2 : 2);

                cycle_count += 5;
            }
        }
        else if (opcode == 0xc2 || opcode == 0xc0)
        {
            ushort nToRelease = GetPcWord();

            // RET
            _ip = pop();
            _sp += nToRelease;

            cycle_count += 16;
        }
        else if (opcode == 0xc3 || opcode == 0xc1)
        {
            // RET
            _ip = pop();

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
                _es = ReadMemWord(seg, (ushort)(addr + 2));
            else
                _ds = ReadMemWord(seg, (ushort)(addr + 2));

            PutRegister(reg, true, val);

            cycle_count += 7 + get_cycles;
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

                push(_flags);
                push(_cs);
                if (_rep)
                {
                    push(_rep_addr);
                    Log.DoLog($"INT from rep {_rep_addr:X04}", LogLevel.TRACE);
                }
                else
                {
                    push(_ip);
                }

                SetFlagI(false);

                _ip = ReadMemWord(0, addr);
                _cs = ReadMemWord(0, (ushort)(addr + 2));

                cycle_count += 51;  // 71  TODO
            }
        }
        else if (opcode == 0xcf)
        {
            // IRET
            _ip = pop();
            _cs = pop();
            _flags = pop();
            FixFlags();

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
                    bool override_to_ss = a_valid && word && _segment_override_set == false &&
                        (
                         ((reg2 == 2 || reg2 == 3) && mod == 0)
                        );

                    if (override_to_ss)
                        seg = _ss;

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
                r1 = _al;
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

            cycle_count += get_cycles;
            cycle_count += 3;

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
                _al |= bLow;

                if (word)
                    _ah |= bHigh;
            }
            else if (function == 2)
            {
                _al &= bLow;

                if (word)
                    _ah &= bHigh;

                SetFlagC(false);
            }
            else if (function == 3)
            {
                _al ^= bLow;

                if (word)
                    _ah ^= bHigh;
            }
            else
            {
                Log.DoLog($"opcode {opcode:X2} function {function} not implemented", LogLevel.WARNING);
            }

            SetLogicFuncFlags(word, word ? GetAX() : _al);

            SetFlagP(_al);

            cycle_count += 4;
        }
        else if (opcode == 0xe8)
        {
            // CALL
            short a = (short)GetPcWord();
            push(_ip);
            _ip = (ushort)(a + _ip);

            cycle_count += 16;
        }
        else if (opcode == 0xea)
        {
            // JMP far ptr
            ushort temp_ip = GetPcWord();
            ushort temp_cs = GetPcWord();

            _ip = temp_ip;
            _cs = temp_cs;

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
                bool negate = _rep_mode == RepMode.REP && _rep;
                _rep = false;

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
                    int result = _al * r1;
                    if (negate)
                        result = -result;
                    SetAX((ushort)result);

                    bool flag = _ah != 0;
                    SetFlagC(flag);
                    SetFlagO(flag);

                    cycle_count += 70;
                }
            }
            else if (function == 5)
            {
                bool negate = _rep_mode == RepMode.REP && _rep;
                _rep = false;

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
                    int result = (sbyte)_al * (short)(sbyte)r1;
                    if (negate)
                        result = -result;
                    SetAX((ushort)result);

                    SetFlagS((_ah & 128) == 128);
                    bool flag = (short)(sbyte)_al != (short)result;
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
                        SetZSPFlags(_ah);
                        SetFlagA(false);
                        InvokeInterrupt(_ip, 0x00, false);  // divide by zero or divisor too small
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
                        SetZSPFlags(_ah);
                        SetFlagA(false);
                        InvokeInterrupt(_ip, 0x00, false);  // divide by zero or divisor too small
                    }
                    else
                    {
                        _al = (byte)(ax / r1);
                        _ah = (byte)(ax % r1);
                    }
                }
            }
            else if (function == 7)
            {
                bool negate = _rep_mode == RepMode.REP && _rep;
                _rep = false;

                SetFlagC(false);
                SetFlagO(false);

                // IDIV
                if (word) {
                    int dx_ax = (GetDX() << 16) | GetAX();
                    int r1s = (int)(short)r1;

                    if (r1s == 0 || dx_ax / r1s > 0x7fffffff || dx_ax / r1s < -0x80000000)
                    {
                        SetZSPFlags(_ah);
                        SetFlagA(false);
                        InvokeInterrupt(_ip, 0x00, false);  // divide by zero or divisor too small
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
                        SetZSPFlags(_ah);
                        SetFlagA(false);
                        InvokeInterrupt(_ip, 0x00, false);  // divide by zero or divisor too small
                    }
                    else
                    {
                        if (negate)
                            _al = (byte)-(ax / r1s);
                        else
                            _al = (byte)(ax / r1s);
                        _ah = (byte)(ax % r1s);
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

            _al = ReadMemByte(_segment_override_set ? _segment_override : _ds, a);

            cycle_count += 2;
        }
        else if (opcode == 0xa1)
        {
            // MOV AX,[...]
            ushort a = GetPcWord();

            SetAX(ReadMemWord(_segment_override_set ? _segment_override : _ds, a));

            cycle_count += 2;
        }
        else if (opcode == 0xa2)
        {
            // MOV [...],AL
            ushort a = GetPcWord();

            WriteMemByte(_segment_override_set ? _segment_override : _ds, a, _al);

            cycle_count += 2;
        }
        else if (opcode == 0xa3)
        {
            // MOV [...],AX
            ushort a = GetPcWord();

            WriteMemWord(_segment_override_set ? _segment_override : _ds, a, GetAX());

            cycle_count += 2;
        }
        else if (opcode == 0xa8)
        {
            // TEST AL,..
            byte v = GetPcByte();

            byte result = (byte)(_al & v);

            SetLogicFuncFlags(false, result);

            SetFlagC(false);

            cycle_count += 3;
        }
        else if (opcode == 0xa9)
        {
            // TEST AX,..
            ushort v = GetPcWord();

            ushort result = (ushort)(GetAX() & v);

            SetLogicFuncFlags(true, result);

            SetFlagC(false);

            cycle_count += 3;
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
                word = true;

            cycle_count += 2;

            // Log.Disassemble($"{opcode:X}|{o1:X} mode {mode}, reg {reg}, rm {rm}, dir {dir}, word {word}, sreg {sreg}");

            // 88: rm < r (byte) 00  false,byte
            // 89: rm < r (word) 01  false,word  <--
            // 8a: r < rm (byte) 10  true, byte
            // 8b: r < rm (word) 11  true, word

            // 89|E6 mode 3, reg 4, rm 6, dir False, word True, sreg False

            if (dir)
            {
                // to 'REG' from 'rm'
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

            cycle_count += 3;
        }
        else if (opcode == 0x8d)
        {
            // LEA
            byte o1 = GetPcByte();
            int mod = o1 >> 6;
            int reg = (o1 >> 3) & 7;
            int rm = o1 & 7;

            cycle_count += 3;

            // might introduce problems when the dereference of *addr reads from i/o even
            // when it is not required
            (ushort val, bool a_valid, ushort seg, ushort addr, int get_cycles) = GetRegisterMem(rm, mod, true);

            cycle_count += get_cycles;

            PutRegister(reg, true, addr);
        }
        else if (opcode == 0x9e)
        {
            // SAHF
            ushort keep = (ushort)(_flags & 0b1111111100101010);
            ushort add = (ushort)(_ah & 0b11010101);

            _flags = (ushort)(keep | add);

            FixFlags();

            cycle_count += 4;
        }
        else if (opcode == 0x9f)
        {
            // LAHF
            _ah = (byte)_flags;

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
                WriteMemByte(_es, _di, _al);

                _di += (ushort)(GetFlagD() ? -1 : 1);

                cycle_count += 11;
            }
        }
        else if (opcode == 0xab)
        {
            if (PrefixMustRun())
            {
                // STOSW
                WriteMemWord(_es, _di, GetAX());

                _di += (ushort)(GetFlagD() ? -2 : 2);

                cycle_count += 11;
            }
        }
        else if (opcode == 0xae)
        {
            if (PrefixMustRun())
            {
                // SCASB
                byte v = ReadMemByte(_es, _di);
                int result = _al - v;
                SetAddSubFlags(false, _al, v, result, true, false);

                _di += (ushort)(GetFlagD() ? -1 : 1);

                cycle_count += 15;
            }
        }
        else if (opcode == 0xaf)
        {
            if (PrefixMustRun())
            {
                // SCASW
                ushort ax = GetAX();
                ushort v = ReadMemWord(_es, _di);
                int result = ax - v;
                SetAddSubFlags(true, ax, v, result, true, false);

                _di += (ushort)(GetFlagD() ? -2 : 2);

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

            _ip = pop();
            _cs = pop();

            if (opcode == 0xca || opcode == 0xc8)
            {
                _sp += nToRelease;
                cycle_count += 16;
            }
            else
            {
                cycle_count += 26;
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
                count = _cl;

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
                    if (_cl != 0)
                    {
                        SetFlagC(false);
                        SetFlagA(false);
                        SetFlagZ(false);
                        SetFlagO(false);
                        SetFlagP(0xff);
                        SetFlagS(true);

                        v1 = (ushort)(word ? 0xffff : 0xff);
                    }
                }
                else
                {
                    SetFlagC(false);
                    SetFlagA(false);
                    SetFlagZ(false);
                    SetFlagO(false);
                    SetFlagP(0xff);
                    SetFlagS(true);

                    v1 = (ushort)(word ? 0xffff : 0xff);
                }

                // TODO cycle_count
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
                _ah = (byte)(_al / b2);
                _al %= b2;

                SetZSPFlags(_al);
            }
            else
            {
                SetZSPFlags(0);

                SetFlagO(false);
                SetFlagA(false);
                SetFlagC(false);

                InvokeInterrupt(_ip, 0x00, false);
            }

            cycle_count += 83;
        }
        else if (opcode == 0xd5)
        {
            // AAD
            byte b2 = GetPcByte();

            _al = (byte)(_al + _ah * b2);
            _ah = 0;

            SetZSPFlags(_al);

            cycle_count += 60;
        }
        else if (opcode == 0xd6)
        {
            // SALC
            if (GetFlagC())
                _al = 0xff;
            else
                _al = 0x00;

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

            ushort newAddress = (ushort)(_ip + (sbyte)to);

            if (state)
            {
                _ip = newAddress;
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
            byte old_al = _al;

            _al = ReadMemByte(_segment_override_set ? _segment_override : _ds, (ushort)(GetBX() + _al));

            cycle_count += 11;
        }
        else if (opcode == 0xe0 || opcode == 0xe1 || opcode == 0xe2)
        {
            // LOOP
            byte to = GetPcByte();

            ushort cx = GetCX();
            cx--;
            SetCX(cx);

            ushort newAddresses = (ushort)(_ip + (sbyte)to);

            cycle_count += 4;

            if (opcode == 0xe2)
            {
                if (cx > 0)
                {
                    _ip = newAddresses;
                    cycle_count += 4;
                }
            }
            else if (opcode == 0xe1)
            {
                if (cx > 0 && GetFlagZ() == true)
                {
                    _ip = newAddresses;
                    cycle_count += 4;
                }
            }
            else if (opcode == 0xe0)
            {
                if (cx > 0 && GetFlagZ() == false)
                {
                    _ip = newAddresses;
                    cycle_count += 4;
                }
            }
            else
            {
                Log.DoLog($" opcode {opcode:X2} not implemented", LogLevel.WARNING);
            }
        }
        else if (opcode == 0xe4)
        {
            // IN AL,ib
            byte @from = GetPcByte();

            (ushort val, bool i) = _io.In(@from);
            _al = (byte)val;

            cycle_count += 10;  // or 14
        }
        else if (opcode == 0xe5)
        {
            // IN AX,ib
            byte @from = GetPcByte();

            (ushort val, bool i) = _io.In(@from);
            SetAX(val);

            cycle_count += 10;  // or 14
        }
        else if (opcode == 0xe6)
        {
            // OUT
            byte to = GetPcByte();
            _io.Out(@to, _al);

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
            _al = (byte)val;

            cycle_count += 8;  // or 12
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
            _io.Out(GetDX(), _al);

            cycle_count += 8;  // or 12
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
            _ip = (ushort)(_ip + (sbyte)to);
            cycle_count += 15;
        }
        else if (opcode == 0xf4)
        {
            // HLT
            _in_hlt = true;
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
                push(_ip);

                _rep = false;
                _ip = v;

                cycle_count += 16;
            }
            else if (function == 3)
            {
                // CALL FAR
                push(_cs);
                push(_ip);

                _ip = v;
                _cs = ReadMemWord(seg, (ushort)(addr + 2));

                cycle_count += 37;
            }
            else if (function == 4)
            {
                // JMP NEAR
                _ip = v;

                cycle_count += 18;
            }
            else if (function == 5)
            {
                // JMP
                _cs = ReadMemWord(seg, (ushort)(addr + 2));
                _ip = ReadMemWord(seg, addr);

                cycle_count += 15;
            }
            else if (function == 6)
            {
                // PUSH rmw
                if (reg == 4 && mod == 3 && word == true)  // PUSH SP
                {
                    v -= 2;
                    WriteMemWord(_ss, v, v);
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

        _clock += cycle_count;

        // tick I/O
        _io.Tick(cycle_count, _clock);

        return true;
    }

    // Disassembly code

    public byte DisassembleGetByte(ref ushort d_cs, ref ushort d_ip, ref int instr_len, ref List<byte> bytes)
    {
        byte opcode = ReadMemByte(d_cs, d_ip);
        bytes.Add(opcode);
        d_ip++;
        instr_len++;
        return opcode;
    }

    public ushort DisassembleGetWord(ref ushort d_cs, ref ushort d_ip, ref int instr_len, ref List<byte> bytes)
    {
        byte low = ReadMemByte(d_cs, d_ip);
        bytes.Add(low);
        byte high = ReadMemByte(d_cs, d_ip);
        bytes.Add(high);
        return (ushort)(low + (high << 8));
    }

    // value, name, meta
    private (ushort, string, string) DisassemblyGetDoubleRegisterMod00(int reg, ref ushort d_cs, ref ushort d_ip, ref int instr_len, ref List<byte> bytes)
    {
        ushort a = 0;
        string name = "error";
        string meta = "";

        if (reg == 0)
        {
            a = (ushort)(GetBX() + _si);
            name = "[BX+SI]";
        }
        else if (reg == 1)
        {
            a = (ushort)(GetBX() + _di);
            name = "[BX+DI]";
        }
        else if (reg == 2)
        {
            a = (ushort)(_bp + _si);
            name = "[BP+SI]";
        }
        else if (reg == 3)
        {
            a = (ushort)(_bp + _di);
            name = "[BP+DI]";
        }
        else if (reg == 4)
        {
            a = _si;
            name = "[SI]";
        }
        else if (reg == 5)
        {
            a = _di;
            name = "[DI]";
        }
        else if (reg == 6)
        {
            a = DisassembleGetWord(ref d_cs, ref d_ip, ref instr_len, ref bytes);
            name = $"[${a:X4}]";
        }
        else if (reg == 7)
        {
            a = GetBX();
            name = "[BX]";
        }
        else
        {
            meta = $"{nameof(GetDoubleRegisterMod00)} {reg} not implemented";
        }

        return (a, name, meta);
    }

    // value, name, meta, cycles
    private (ushort, string, string, bool, ushort) DisassemblyGetDoubleRegisterMod01_02(int reg, bool word, ref ushort d_cs, ref ushort d_ip, ref int instr_len, ref List<byte> bytes)
    {
        ushort a = 0;
        string name = "error";
        bool override_segment = false;
        ushort new_segment = 0;
        string meta = "";

        if (reg == 6)
        {
            a = _bp;
            name = "[BP]";
            override_segment = true;
            new_segment = _ss;
        }
        else
        {
            (a, name, meta) = DisassemblyGetDoubleRegisterMod00(reg, ref d_cs, ref d_ip, ref instr_len, ref bytes);
        }

        short disp = word ? (short)DisassembleGetWord(ref d_cs, ref d_ip, ref instr_len, ref bytes) : (sbyte)DisassembleGetByte(ref d_cs, ref d_ip, ref instr_len, ref bytes);

        return ((ushort)(a + disp), name, $"disp {disp:X4} " + meta, override_segment, new_segment);
    }

    // value, name_of_source, segment_a_valid, segment/, address of value, meta
    private (ushort, string, bool, ushort, ushort, string) DisassemblyGetRegisterMem(int reg, int mod, bool w, ref ushort d_cs, ref ushort d_ip, ref int instr_len, ref List<byte> bytes)
    {
        string meta = "";

        if (mod == 0)
        {
            (ushort a, string name, meta) = DisassemblyGetDoubleRegisterMod00(reg, ref d_cs, ref d_ip, ref instr_len, ref bytes);

            ushort segment = _segment_override_set ? _segment_override : _ds;
            if (_segment_override_set == false && (reg == 2 || reg == 3))  // BP uses SS
            {
                segment = _ss;
                meta = $"BP SS-override ${_ss:X4}";
            }

            ushort v = w ? ReadMemWord(segment, a) : ReadMemByte(segment, a);

            return (v, name, true, segment, a, meta);
        }

        if (mod == 1 || mod == 2)
        {
            bool word = mod == 2;

            (ushort a, string name, meta, bool override_segment, ushort new_segment) = DisassemblyGetDoubleRegisterMod01_02(reg, word, ref d_cs, ref d_ip, ref instr_len, ref bytes);

            ushort segment = _segment_override_set ? _segment_override : _ds;
            if (_segment_override_set == false && override_segment)
            {
                segment = new_segment;
                meta += $"BP SS-override ${_ss:X4} [2]";
            }
            if (_segment_override_set == false && (reg == 2 || reg == 3))  // BP uses SS
            {
                segment = _ss;
                meta += $"BP SS-override ${_ss:X4} [3]";
            }

            ushort v = w ? ReadMemWord(segment, a) : ReadMemByte(segment, a);

            return (v, name, true, segment, a, meta);
        }

        if (mod == 3)
        {
            (ushort v, string name) = DisassemblyGetRegister(reg, w);
            return (v, name, false, 0, 0, "");
        }

        return (0, "error", false, 0, 0, $"reg {reg} mod {mod} w {w} not supported for {nameof(DisassemblyGetRegisterMem)}");
    }

    private string DisassemblyPutRegister(int reg, bool w, ushort val)
    {
        if (reg == 0)
        {
            if (w)
                return "AX";
            return "AL";
        }

        if (reg == 1)
        {
            if (w)
                return "CX";
            return "CL";
        }

        if (reg == 2)
        {
            if (w)
                return "DX";
            return "DL";
        }

        if (reg == 3)
        {
            if (w)
                return "BX";
            return "BL";
        }

        if (reg == 4)
        {
            if (w)
                return "SP";
            return "AH";
        }

        if (reg == 5)
        {
            if (w)
                return "BP";
            return "CH";
        }

        if (reg == 6)
        {
            if (w)
                return "SI";
            return "DH";
        }

        if (reg == 7)
        {
            if (w)
                return "DI";
            return "BH";
        }

        return "error";
    }

    private string DisassemblyPutSRegister(int reg, ushort v)
    {
        reg &= 0b00000011;

        if (reg == 0b000)
            return "ES";

        if (reg == 0b001)
            return "CS";

        if (reg == 0b010)
            return "SS";

        if (reg == 0b011)
            return "DS";

        return "error";
    }

    private (ushort, string) DisassemblyGetRegister(int reg, bool w)
    {
        if (w)
        {
            if (reg == 0)
                return (GetAX(), "AX");
            if (reg == 1)
                return (GetCX(), "CX");
            if (reg == 2)
                return (GetDX(), "DX");
            if (reg == 3)
                return (GetBX(), "BX");
            if (reg == 4)
                return (_sp, "SP");
            if (reg == 5)
                return (_bp, "BP");
            if (reg == 6)
                return (_si, "SI");
            if (reg == 7)
                return (_di, "DI");
        }
        else
        {
            if (reg == 0)
                return (_al, "AL");
            if (reg == 1)
                return (_cl, "CL");
            if (reg == 2)
                return (_dl, "DL");
            if (reg == 3)
                return (_bl, "BL");
            if (reg == 4)
                return (_ah, "AH");
            if (reg == 5)
                return (_ch, "CH");
            if (reg == 6)
                return (_dh, "DH");
            if (reg == 7)
                return (_bh, "BH");
        }

        return (0, "error");
    }

    private (string, int) DisassemblyPutRegisterMem(int reg, int mod, bool w, ushort val, ref ushort d_cs, ref ushort d_ip, ref int instr_len, ref List<byte> bytes)
    {
        if (mod == 0)
        {
            (ushort a, string name, string meta) = DisassemblyGetDoubleRegisterMod00(reg, ref d_cs, ref d_ip, ref instr_len, ref bytes);

            ushort segment = _segment_override_set ? _segment_override : _ds;

            if (_segment_override_set == false && (reg == 2 || reg == 3)) {  // BP uses SS
                segment = _ss;
                meta = $"BP SS-override ${_ss:X4}";
            }

            return (name, 0);
        }

        if (mod == 1 || mod == 2)
        {
            (ushort a, string name, string meta, bool override_segment, ushort new_segment) = DisassemblyGetDoubleRegisterMod01_02(reg, mod == 2, ref d_cs, ref d_ip, ref instr_len, ref bytes);

            ushort segment = _segment_override_set ? _segment_override : _ds;

            if (_segment_override_set == false && override_segment)
            {
                segment = new_segment;
                meta = $"BP SS-override ${_ss:X4} [5]";
            }

            if (_segment_override_set == false && (reg == 2 || reg == 3))  // BP uses SS
            {
                segment = _ss;
                meta = $"BP SS-override ${_ss:X4} [6]";
            }

            return (name, 0);
        }

        if (mod == 3)
            return (DisassemblyPutRegister(reg, w, val), 0);

        return ("error", 0);
    }

    (string, int) DisassemblyUpdateRegisterMem(int reg, int mod, bool a_valid, ushort seg, ushort addr, bool word, ushort v, ref ushort d_cs, ref ushort d_ip, ref int instr_len, ref List<byte> bytes)
    {
        if (a_valid)
            return ($"[{addr:X4}]", 4);

        return DisassemblyPutRegisterMem(reg, mod, word, v, ref d_cs, ref d_ip, ref instr_len, ref bytes);
    }

    private (ushort, string) DisassemblyGetSRegister(int reg)
    {
        reg &= 0b00000011;
 
        if (reg == 0b000)
            return (_es, "ES");
        if (reg == 0b001)
            return (_cs, "CS");  // TODO use d_cs from Disassemble invocation?
        if (reg == 0b010)
            return (_ss, "SS");
        if (reg == 0b011)
            return (_ds, "DS");

        Log.DoLog($"reg {reg} not supported for {nameof(GetSRegister)}", LogLevel.WARNING);

        return (0, "error");
    }

    // instruction length, instruction string, additional info, hex-string
    public Tuple<int, string, string, string> Disassemble(ushort d_cs, ushort d_ip)
    {
        int instr_len = 0;
        List<byte> bytes = new();
        byte opcode = DisassembleGetByte(ref d_cs, ref d_ip, ref instr_len, ref bytes);

        string meta = "";
        string prefix = "";
        string instr = "";

        // handle prefixes
        while (opcode is (0x26 or 0x2e or 0x36 or 0x3e or 0xf2 or 0xf3))
        {
            if (opcode == 0x26)
                prefix = "ES ";
            else if (opcode == 0x2e)
                prefix = "CS ";
            else if (opcode == 0x36)
                prefix = "SS ";
            else if (opcode  == 0x3e)
                prefix = "DS ";
            else if (opcode is (0xf2 or 0xf3))
            {
            }
            else
            {
                return new Tuple<int, string, string, string>(1, "?", $"prefix {opcode:X2} not implemented", "" );
            }

            byte next_opcode = DisassembleGetByte(ref d_cs, ref d_ip, ref instr_len, ref bytes);

            if (opcode == 0xf2)
            {
                if (next_opcode is (0xa6 or 0xa7 or 0xae or 0xaf))
                    prefix += "REPNZ ";
                else
                    prefix += "REP ";
            }
            else if (opcode == 0xf3)
            {
                if (next_opcode is (0xa6 or 0xa7 or 0xae or 0xaf))
                    prefix += "REPE/Z ";
                else
                    prefix += "REP ";
            }

            opcode = next_opcode;
        }

        // main instruction handling
        if (opcode == 0x04 || opcode == 0x14)
        {
            // ADD AL,xx
            byte v = DisassembleGetByte(ref d_cs, ref d_ip, ref instr_len, ref bytes);
            instr = $" ADD AL,#{v:X2}";
        }
        else if (opcode == 0x05 || opcode == 0x15)
        {
            // ADD AX,xxxx
            ushort v = DisassembleGetWord(ref d_cs, ref d_ip, ref instr_len, ref bytes);

            if (opcode == 0x05)
                instr = $" ADD AX,${v:X4}";
            else
                instr = $" ADC AX,${v:X4}";
        }
        else if (opcode == 0x06)
        {
            instr = "PUSH ES";
        }
        else if (opcode == 0x07)
        {
            instr = "POP ES";
        }
        else if (opcode == 0x0e)
        {
            instr = "PUSH CS";
        }
        else if (opcode == 0x0f)
        {
            instr = "POP CS";
        }
        else if (opcode == 0x16)
        {
            instr = "PUSH SS";
        }
        else if (opcode == 0x17)
        {
            instr = "POP SS";
        }
        else if (opcode == 0x1c)
        {
            byte v = DisassembleGetByte(ref d_cs, ref d_ip, ref instr_len, ref bytes);
            instr = $"SBB AL,${v:X2}";
        }
        else if (opcode == 0x1d)
        {
            ushort v = DisassembleGetWord(ref d_cs, ref d_ip, ref instr_len, ref bytes);
            instr = $"SBB AX,${v:X4}";
        }
        else if (opcode == 0x1e)
        {
            instr = "PUSH DS";
        }
        else if (opcode == 0x1f)
        {
            instr = "POP DS";
        }
        else if (opcode == 0x27)
        {
            instr = "DAA";
        }
        else if (opcode == 0x2c)
        {
            byte v = DisassembleGetByte(ref d_cs, ref d_ip, ref instr_len, ref bytes);
            instr = $" SUB AL,${v:X2}";
        }
        else if (opcode == 0x2f)
        {
            instr = "DAS";
        }
        else if (opcode == 0x37)
        {
            instr = "AAA";
        }
        else if (opcode == 0x3f)
        {
            instr = "AAS";
        }
        else if (opcode == 0x2d)
        {
            ushort v = DisassembleGetWord(ref d_cs, ref d_ip, ref instr_len, ref bytes);
            instr = $"SUB AX,${v:X4}";
        }
        else if (opcode == 0x58)
        {
            instr = "POP AX";
        }
        else if (opcode == 0x59)
        {
            instr = "POP CX";
        }
        else if (opcode == 0x5a)
        {
            instr = "POP DX";
        }
        else if (opcode == 0x5b)
        {
            instr = "POP BX";
        }
        else if (opcode == 0x5c)
        {
            instr = "POP SP";
        }
        else if (opcode == 0x5d)
        {
            instr = "POP BP";
        }
        else if (opcode == 0x5e)
        {
            instr = "POP SI";
        }
        else if (opcode == 0x5f)
        {
            instr = "POP DI";
        }
        else if (opcode == 0xa4)
        {
            instr = "MOVSB";
        }
        else if (opcode == 0xa5)
        {
            instr = "MOVSW";
        }
        else if (opcode == 0xa6)
        {
            instr = "CMPSB";
        }
        else if (opcode == 0xa7)
        {
            instr = "CMPSW";
        }
        else if (opcode == 0xe3)
        {
            // JCXZ np
            byte offset = DisassembleGetByte(ref d_cs, ref d_ip, ref instr_len, ref bytes);
            instr = "JCXZ ${offset:X02}";
        }
        else if (opcode == 0xe9)
        {
            short offset = (short)DisassembleGetWord(ref d_cs, ref d_ip, ref instr_len, ref bytes);
            ushort word = (ushort)(d_ip + offset);
            instr = $"JMP {d_ip:X}";
            meta = $"{offset:X4}";
        }
        else if (opcode == 0x50)
        {
            instr = "PUSH AX";
        }
        else if (opcode == 0x51)
        {
            instr = "PUSH CX";
        }
        else if (opcode == 0x52)
        {
            instr = "PUSH DX";
        }
        else if (opcode == 0x53)
        {
            instr = "PUSH BX";
        }
        else if (opcode == 0x54)
        {
            instr = "PUSH SP";
        }
        else if (opcode == 0x55)
        {
            instr = "PUSH BP";
        }
        else if (opcode == 0x56)
        {
            instr = "PUSH SI";
        }
        else if (opcode == 0x57)
        {
            instr = "PUSH DI";
        }
        else if (opcode is (0x80 or 0x81 or 0x82 or 0x83))
        {
            byte o1 = DisassembleGetByte(ref d_cs, ref d_ip, ref instr_len, ref bytes);

            int mod = o1 >> 6;
            int reg = o1 & 7;
            int function = (o1 >> 3) & 7;

            ushort r1 = 0;
            string name1 = "error";
            bool a_valid = false;
            ushort seg = 0;
            ushort addr = 0;
            ushort r2 = 0;

            if (opcode == 0x80)
            {
                (r1, name1, a_valid, seg, addr, meta) = DisassemblyGetRegisterMem(reg, mod, false, ref d_cs, ref d_ip, ref instr_len, ref bytes);

                r2 = DisassembleGetByte(ref d_cs, ref d_ip, ref instr_len, ref bytes);
            }
            else if (opcode == 0x81)
            {
                (r1, name1, a_valid, seg, addr, meta) = DisassemblyGetRegisterMem(reg, mod, true, ref d_cs, ref d_ip, ref instr_len, ref bytes);

                r2 = DisassembleGetWord(ref d_cs, ref d_ip, ref instr_len, ref bytes);
            }
            else if (opcode == 0x82)
            {
                (r1, name1, a_valid, seg, addr, meta) = DisassemblyGetRegisterMem(reg, mod, false, ref d_cs, ref d_ip, ref instr_len, ref bytes);

                r2 = DisassembleGetByte(ref d_cs, ref d_ip, ref instr_len, ref bytes);
            }
            else if (opcode == 0x83)
            {
                (r1, name1, a_valid, seg, addr, meta) = DisassemblyGetRegisterMem(reg, mod, true, ref d_cs, ref d_ip, ref instr_len, ref bytes);

                r2 = DisassembleGetByte(ref d_cs, ref d_ip, ref instr_len, ref bytes);
                if ((r2 & 128) == 128)
                    r2 |= 0xff00;
            }
            else
            {
                meta = $"opcode {opcode:X2} not implemented";
            }

            string iname = "error";

            if (function == 0)
                iname = "ADD";
            else if (function == 1)
                iname = "OR";
            else if (function == 2)
                iname = "ADC";
            else if (function == 3)
                iname = "SBC";
            else if (function == 4)
                iname = "AND";
            else if (function == 5)
                iname = "SUB";
            else if (function == 6)
                iname = "XOR";
            else if (function == 7)
                iname = "CMP";
            else
            {
                iname = "?";
                meta = $"opcode {opcode:X2} function {function} not implemented";
            }

            instr = $"{iname} {name1},${r2:X2}";
        }
        else if (opcode == 0x84 || opcode == 0x85)
        {
            // TEST ...,...
            bool word = (opcode & 1) == 1;
            byte o1 = DisassembleGetByte(ref d_cs, ref d_ip, ref instr_len, ref bytes);

            int mod = o1 >> 6;
            int reg1 = (o1 >> 3) & 7;
            int reg2 = o1 & 7;

            (ushort r1, string name1, bool a_valid, ushort seg, ushort addr, meta) = DisassemblyGetRegisterMem(reg2, mod, word, ref d_cs, ref d_ip, ref instr_len, ref bytes);
            (ushort r2, string name2) = DisassemblyGetRegister(reg1, word);

            instr = $"TEST {name1},{name2}";
        }
        else if (opcode == 0x86 || opcode == 0x87)
        {
            // XCHG
            bool word = (opcode & 1) == 1;
            byte o1 = DisassembleGetByte(ref d_cs, ref d_ip, ref instr_len, ref bytes);

            int mod = o1 >> 6;
            int reg1 = (o1 >> 3) & 7;
            int reg2 = o1 & 7;

            (ushort r1, string name1, bool a_valid, ushort seg, ushort addr, meta) = DisassemblyGetRegisterMem(reg2, mod, word, ref d_cs, ref d_ip, ref instr_len, ref bytes);
            (ushort r2, string name2) = DisassemblyGetRegister(reg1, word);

            instr = $"XCHG {name1},{name2}";
        }
        else if (opcode == 0x8f)
        {
            // POP rmw
            byte o1 = DisassembleGetByte(ref d_cs, ref d_ip, ref instr_len, ref bytes);
            int mod = o1 >> 6;
            int reg2 = o1 & 7;
            (string toName, int put_cycles) = DisassemblyPutRegisterMem(reg2, mod, true, ReadMemWord(_ss, _sp), ref d_cs, ref d_ip, ref instr_len, ref bytes);
            instr = $"POP {toName}";
        }
        else if (opcode == 0x90)
        {
            instr = "NOP";
        }
        else if (opcode >= 0x91 && opcode <= 0x97)
        {
            // XCHG AX,...
            int reg_nr = opcode - 0x90;
            (ushort v, string name_other) = DisassemblyGetRegister(reg_nr, true);
            instr = $"XCHG AX,{name_other}";
        }
        else if (opcode == 0x98)
        {
            instr = $"CBW";
        }
        else if (opcode == 0x99)
        {
            instr = $"CDW";
        }
        else if (opcode == 0x9a)
        {
            // CALL far ptr
            ushort temp_ip = DisassembleGetWord(ref d_cs, ref d_ip, ref instr_len, ref bytes);
            ushort temp_cs = DisassembleGetWord(ref d_cs, ref d_ip, ref instr_len, ref bytes);

            instr = $"CALL ${temp_cs:X} ${temp_ip:X}";
            meta = $"${temp_cs * 16 + temp_ip:X}";
        }
        else if (opcode == 0x9c)
        {
            instr = "PUSHF";
        }
        else if (opcode == 0x9d)
        {
            instr = "POPF";
        }
        else if (opcode == 0xac)
        {
            instr = "LODSB";
        }
        else if (opcode == 0xad)
        {
            instr = "LODSW";
        }
        else if (opcode == 0xc2 || opcode == 0xc0)
        {
            ushort nToRelease = DisassembleGetWord(ref d_cs, ref d_ip, ref instr_len, ref bytes);
            instr = $"RET ${nToRelease:X4}";
        }
        else if (opcode == 0xc3 || opcode == 0xc1)
        {
            instr = "RET";
        }
        else if (opcode == 0xc4 || opcode == 0xc5)
        {
            // LES (c4) / LDS (c5)
            byte o1 = DisassembleGetByte(ref d_cs, ref d_ip, ref instr_len, ref bytes);
            int mod = o1 >> 6;
            int reg = (o1 >> 3) & 7;
            int rm = o1 & 7;

            (ushort val, string name_from, bool a_valid, ushort seg, ushort addr, meta) = DisassemblyGetRegisterMem(rm, mod, true, ref d_cs, ref d_ip, ref instr_len, ref bytes);

            string name;
            if (opcode == 0xc4)
                name = "LES";
            else
                name = "LDS";

            string affected = DisassemblyPutRegister(reg, true, val);
            instr = $"{name} {affected},{name_from}";
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
                    @int = DisassembleGetByte(ref d_cs, ref d_ip, ref instr_len, ref bytes);

                ushort addr = (ushort)(@int * 4);

                if (opcode == 0xce)
                {
                    instr = $"INTO {@int:X2}";
                    meta = $"{SegmentAddr(d_cs, d_ip)} (from {addr:X4})";
                }
                else 
                {
                    instr = $"INT {@int:X2}";
                    meta = $"{SegmentAddr(d_cs, d_ip)} (from {addr:X4})";
                }
            }
        }
        else if (opcode == 0xcf)
        {
            instr = "IRET";
        }
        else if ((opcode >= 0x00 && opcode <= 0x03) || (opcode >= 0x10 && opcode <= 0x13) || (opcode >= 0x28 && opcode <= 0x2b) || (opcode >= 0x18 && opcode <= 0x1b) || (opcode >= 0x38 && opcode <= 0x3b))
        {
            bool word = (opcode & 1) == 1;
            bool direction = (opcode & 2) == 2;
            byte o1 = DisassembleGetByte(ref d_cs, ref d_ip, ref instr_len, ref bytes);

            int mod = o1 >> 6;
            int reg1 = (o1 >> 3) & 7;
            int reg2 = o1 & 7;

            (ushort r1, string name1, bool a_valid, ushort seg, ushort addr, meta) = DisassemblyGetRegisterMem(reg2, mod, word, ref d_cs, ref d_ip, ref instr_len, ref bytes);
            (ushort r2, string name2) = DisassemblyGetRegister(reg1, word);

            string name = "error";
            int result = 0;
            bool apply = true;

            if (opcode <= 0x03)
                name = "ADD";
            else if (opcode >= 0x10 && opcode <= 0x13)
                name = "ADC";
            else if (opcode >= 0x38 && opcode <= 0x3b)
            {
                apply = false;
                name = "CMP";
            }
            else if (opcode >= 0x28 && opcode <= 0x2b)
                name = "SUB";
            else  // 0x18...0x1b
            {
                name = "SBB";
            }


            // 0x38...0x3b are CMP
            if (apply)
            {
                (string dummy, int put_cycles) = DisassemblyUpdateRegisterMem(reg2, mod, a_valid, seg, addr, word, (ushort)result, ref d_cs, ref d_ip, ref instr_len, ref bytes);
                instr = $"{name} {name1},{name2}";
            }
            else
            {
                if (direction)
                    instr = $"{name} {name2},{name1}";
                else
                    instr = $"{name} {name1},{name2}";
            }
        }
        else if (opcode == 0x3c || opcode == 0x3d)
        {
            // CMP
            bool word = (opcode & 1) == 1;

            ushort r1 = 0;
            ushort r2 = 0;

            if (opcode == 0x3d)
            {
                r1 = GetAX();
                r2 = DisassembleGetWord(ref d_cs, ref d_ip, ref instr_len, ref bytes);
                instr = $"CMP AX,#${r2:X4}";
            }
            else if (opcode == 0x3c)
            {
                r1 = _al;
                r2 = DisassembleGetByte(ref d_cs, ref d_ip, ref instr_len, ref bytes);
                instr = $"CMP AL,#${r2:X2}";
            }
            else
            {
                meta = $"opcode {opcode:X2} not implemented";
            }
        }
        else if (opcode is >= 0x30 and <= 0x33 || opcode is >= 0x20 and <= 0x23 || opcode is >= 0x08 and <= 0x0b)
        {
            bool word = (opcode & 1) == 1;
            bool direction = (opcode & 2) == 2;
            byte o1 = DisassembleGetByte(ref d_cs, ref d_ip, ref instr_len, ref bytes);

            int mod = o1 >> 6;
            int reg1 = (o1 >> 3) & 7;
            int reg2 = o1 & 7;

            (ushort r1, string name1, bool a_valid, ushort seg, ushort addr, meta) = DisassemblyGetRegisterMem(reg2, mod, word, ref d_cs, ref d_ip, ref instr_len, ref bytes);
            (ushort r2, string name2) = DisassemblyGetRegister(reg1, word);

            string name = "error";
            int function = opcode >> 4;

            if (function == 0)
                name = "OR";
            else if (function == 2)
                name = "AND";
            else if (function == 3)
                name = "XOR";
            else
                meta = "opcode {opcode:X2} function {function} not implemented";

            instr = $"{name} {name1},{name2}";
        }
        else if (opcode is (0x34 or 0x35 or 0x24 or 0x25 or 0x0c or 0x0d))
        {
            bool word = (opcode & 1) == 1;

            byte bLow = DisassembleGetByte(ref d_cs, ref d_ip, ref instr_len, ref bytes);
            byte bHigh = word ? DisassembleGetByte(ref d_cs, ref d_ip, ref instr_len, ref bytes) : (byte)0;

            string tgt_name = word ? "AX" : "AL";
            string name = "error";

            int function = opcode >> 4;

            if (function == 0)
                name = "OR";
            else if (function == 2)
                name = "AND";
            else if (function == 3)
                name = "XOR";
            else
                meta = "opcode {opcode:X2} function {function} not implemented";

            instr = $"{name} {tgt_name},${bHigh:X2}{bLow:X2}";
        }
        else if (opcode == 0xe8)
        {
            short a = (short)DisassembleGetWord(ref d_cs, ref d_ip, ref instr_len, ref bytes);
            ushort temp_ip = (ushort)(a + d_ip);
            instr = $"CALL {a:X4}";
            meta = $"{SegmentAddr(d_cs, temp_ip)}";
        }
        else if (opcode == 0xea)
        {
            // JMP far ptr
            ushort temp_ip = DisassembleGetWord(ref d_cs, ref d_ip, ref instr_len, ref bytes);
            ushort temp_cs = DisassembleGetWord(ref d_cs, ref d_ip, ref instr_len, ref bytes);
            instr = $"JMP ${temp_cs:X04}:${temp_ip:X04}";
            meta = $"{SegmentAddr(temp_cs, temp_ip)}";
        }
        else if (opcode == 0xf6 || opcode == 0xf7)
        {
            // TEST and others
            bool word = (opcode & 1) == 1;

            byte o1 = DisassembleGetByte(ref d_cs, ref d_ip, ref instr_len, ref bytes);
            int mod = o1 >> 6;
            int reg1 = o1 & 7;

            (ushort r1, string name1, bool a_valid, ushort seg, ushort addr, meta) = DisassemblyGetRegisterMem(reg1, mod, word, ref d_cs, ref d_ip, ref instr_len, ref bytes);

            string name2 = "";
            string name = "error";

            int function = (o1 >> 3) & 7;
            if (function == 0 || function == 1)
                name = "TEST";
            else if (function == 2)
                name = "NOT";
            else if (function == 3)
                name = "NEG";
            else if (function == 4)
            {
                name = "MUL";
                if (word)
                {
                    name2 = name1;
                    name1 = "DX:AX";
                }
            }
            else if (function == 5)
            {
                name = "IMUL";
                if (word)
                {
                    name2 = name1;
                    name1 = "DX:AX";
                }
            }
            else if (function == 6)
                name = "DIV";
            else if (function == 7)
                name = "IDIV";
            else
            {
                meta = $"opcode {opcode:X2} o1 {o1:X2} function {function} not implemented";
            }

            if (name2 != "")
                instr = $"{name} {name1},{name2}";
            else
                instr = $"{name} {name1}";
            if (meta == "")
                meta = $"word: {word}";
        }
        else if (opcode == 0xfa)
            instr = "CLI";
        else if ((opcode & 0xf0) == 0xb0)
        {
            int reg = opcode & 0x07;
            bool word = (opcode & 0x08) == 0x08;

            ushort v = DisassembleGetByte(ref d_cs, ref d_ip, ref instr_len, ref bytes);
            if (word)
                v |= (ushort)(DisassembleGetByte(ref d_cs, ref d_ip, ref instr_len, ref bytes) << 8);

            string name = DisassemblyPutRegister(reg, word, v);
            instr = $"MOV {name},${v:X}";
        }
        else if (opcode == 0xa0)
        {
            ushort a = DisassembleGetWord(ref d_cs, ref d_ip, ref instr_len, ref bytes);
            instr = $"MOV AL,[${a:X4}]";
        }
        else if (opcode == 0xa1)
        {
            // MOV AX,[...]
            ushort a = DisassembleGetWord(ref d_cs, ref d_ip, ref instr_len, ref bytes);
            instr = $"MOV AX,[${a:X4}]";
        }
        else if (opcode == 0xa2)
        {
            // MOV [...],AL
            ushort a = DisassembleGetWord(ref d_cs, ref d_ip, ref instr_len, ref bytes);
            instr = $"MOV [${a:X4}],AL";
        }
        else if (opcode == 0xa3)
        {
            ushort a = DisassembleGetWord(ref d_cs, ref d_ip, ref instr_len, ref bytes);
            instr = $"MOV [${a:X4}],AX";
        }
        else if (opcode == 0xa8)
        {
            // TEST AL,..
            byte v = DisassembleGetByte(ref d_cs, ref d_ip, ref instr_len, ref bytes);
            instr = $"TEST AL,${v:X2}";
        }
        else if (opcode == 0xa9)
        {
            // TEST AX,..
            ushort v = DisassembleGetWord(ref d_cs, ref d_ip, ref instr_len, ref bytes);
            instr = $"TEST AX,${v:X4}";
        }
        else if (opcode is (0x88 or 0x89 or 0x8a or 0x8b or 0x8e or 0x8c))
        {
            bool dir = (opcode & 2) == 2; // direction
            bool word = (opcode & 1) == 1; // b/w

            byte o1 = DisassembleGetByte(ref d_cs, ref d_ip, ref instr_len, ref bytes);
            int mode = o1 >> 6;
            int reg = (o1 >> 3) & 7;
            int rm = o1 & 7;

            bool sreg = opcode == 0x8e || opcode == 0x8c;
            if (sreg)
                word = true;

            if (dir)
            {
                // to 'REG' from 'rm'
                (ushort v, string fromName, bool a_valid, ushort seg, ushort addr, meta) = DisassemblyGetRegisterMem(rm, mode, word, ref d_cs, ref d_ip, ref instr_len, ref bytes);
                string toName;
                if (sreg)
                    toName = DisassemblyPutSRegister(reg, v);
                else
                    toName = DisassemblyPutRegister(reg, word, v);

                instr = $"MOV {toName},{fromName}";
            }
            else
            {
                // from 'REG' to 'rm'
                ushort v;
                string fromName;
                if (sreg)
                    (v, fromName) = DisassemblyGetSRegister(reg);
                else
                    (v, fromName) = DisassemblyGetRegister(reg, word);

                (string toName, int put_cycles) = DisassemblyPutRegisterMem(rm, mode, word, v, ref d_cs, ref d_ip, ref instr_len, ref bytes);
                instr = $"MOV {toName},{fromName}";
                meta = $"{v:X4}";
            }
        }
        else if (opcode == 0x8d)
        {
            // LEA
            byte o1 = DisassembleGetByte(ref d_cs, ref d_ip, ref instr_len, ref bytes);
            int mod = o1 >> 6;
            int reg = (o1 >> 3) & 7;
            int rm = o1 & 7;

            // might introduce problems when the dereference of *addr reads from i/o even
            // when it is not required
            (ushort val, string name_from, bool a_valid, ushort seg, ushort addr, meta) = DisassemblyGetRegisterMem(rm, mod, true, ref d_cs, ref d_ip, ref instr_len, ref bytes);
            string name_to = DisassemblyPutRegister(reg, true, addr);
            instr = $"LEA {name_to},{name_from}";
        }
        else if (opcode == 0x9e)
        {
            instr = "SAHF";
        }
        else if (opcode == 0x9f)
        {
            instr = "LAHF";
        }
        else if (opcode is >= 0x40 and <= 0x4f)
        {
            int reg = (opcode - 0x40) & 7;
            (ushort v, string name) = DisassemblyGetRegister(reg, true);
            bool isDec = opcode >= 0x48;

            if (isDec)
                instr = $"DEC {name}";
            else
                instr = $"INC {name}";
        }
        else if (opcode == 0xaa)
        {
            instr = "STOSB";
        }
        else if (opcode == 0xab)
        {
            instr = "STOSW";
        }
        else if (opcode == 0xae)
        {
            instr = "SCASB";
        }
        else if (opcode == 0xaf)
        {
            instr = "SCASW";
        }
        else if (opcode == 0xc6 || opcode == 0xc7)
        {
            // MOV
            bool word = (opcode & 1) == 1;
            byte o1 = DisassembleGetByte(ref d_cs, ref d_ip, ref instr_len, ref bytes);

            int mod = o1 >> 6;
            int mreg = o1 & 7;

            (ushort dummy, string name, bool a_valid, ushort seg, ushort addr, meta) = DisassemblyGetRegisterMem(mreg, mod, word, ref d_cs, ref d_ip, ref instr_len, ref bytes);

            if (word)
            {
                ushort v = DisassembleGetWord(ref d_cs, ref d_ip, ref instr_len, ref bytes);
                instr = $"MOV word {name},${v:X4}";
            }
            else
            {
                // the value follows
                byte v = DisassembleGetByte(ref d_cs, ref d_ip, ref instr_len, ref bytes);
                instr = $"MOV byte {name},${v:X2}";
            }
        }
        else if (opcode >= 0xc8 && opcode <= 0xcb)
        {
            // RETF n / RETF
            if (opcode == 0xca || opcode == 0xc8)
            {
                ushort nToRelease = DisassembleGetWord(ref d_cs, ref d_ip, ref instr_len, ref bytes);
                instr = $"RETF ${nToRelease:X4}";
            }
            else
            {
                instr = $"RETF";
            }
        }
        else if ((opcode & 0xfc) == 0xd0)
        {
            bool word = (opcode & 1) == 1;
            byte o1 = DisassembleGetByte(ref d_cs, ref d_ip, ref instr_len, ref bytes);

            int mod = o1 >> 6;
            int reg1 = o1 & 7;
            (ushort v1, string vName, bool a_valid, ushort seg, ushort addr, meta) = DisassemblyGetRegisterMem(reg1, mod, word, ref d_cs, ref d_ip, ref instr_len, ref bytes);

            string countName = "1";
            if ((opcode & 2) == 2)
                countName = "CL";

            bool count_1_of = opcode is (0xd0 or 0xd1 or 0xd2 or 0xd3);
            bool oldSign = (word ? v1 & 0x8000 : v1 & 0x80) != 0;
            int mode = (o1 >> 3) & 7;

            ushort check_bit = (ushort)(word ? 32768 : 128);
            ushort check_bit2 = (ushort)(word ? 16384 : 64);

            if (mode == 0)
                instr = $"ROL {vName},{countName}";
            else if (mode == 1)
                instr = $"ROR {vName},{countName}";
            else if (mode == 2)
                instr = $"RCL {vName},{countName}";
            else if (mode == 3)
                instr = $"RCR {vName},{countName}";
            else if (mode == 4)
                instr = $"SAL {vName},{countName}";
            else if (mode == 5)
                instr = $"SHR {vName},{countName}";
            else if (mode == 6)
            {
                if (opcode >= 0xd2)
                    instr = $"SETMOC";
                else
                    instr = $"SETMO";
            }
            else if (mode == 7)
                instr = $"SAR {vName},{countName}";
            else
            {
                meta = $"RCR/SHR/{opcode:X2} mode {mode} not implemented";
            }
        }
        else if (opcode == 0xd4)
        {
            instr = "AAM";
        }
        else if (opcode == 0xd5)
        {
            instr = "AAD";
        }
        else if (opcode == 0xd6)
        {
            instr = "SALC";
        }
        else if (opcode == 0x9b)
        {
            instr = "FWAIT";
            meta = "ignored";
        }
        else if (opcode >= 0xd8 && opcode <= 0xdf)
        {
            byte o1 = DisassembleGetByte(ref d_cs, ref d_ip, ref instr_len, ref bytes);
            int mod = o1 >> 6;
            int reg1 = o1 & 7;
            (ushort v1, string vName, bool a_valid, ushort seg, ushort addr, meta) = DisassemblyGetRegisterMem(reg1, mod, false, ref d_cs, ref d_ip, ref instr_len, ref bytes);
            meta = $"FPU ({opcode:X02} {o1:X02}) - ignored";
        }
        else if ((opcode & 0xf0) == 0x70 || (opcode & 0xf0) == 0x60)
        {
            // J..., 0x70/0x60
            byte to = DisassembleGetByte(ref d_cs, ref d_ip, ref instr_len, ref bytes);

            string name = String.Empty;
            if (opcode == 0x70 || opcode == 0x60)
                name = "JO";
            else if (opcode == 0x71 || opcode == 0x61)
                name = "JNO";
            else if (opcode == 0x72 || opcode == 0x62)
                name = "JC/JB";
            else if (opcode == 0x73 || opcode == 0x63)
                name = "JNC";
            else if (opcode == 0x74 || opcode == 0x64)
                name = "JE/JZ";
            else if (opcode == 0x75 || opcode == 0x65)
                name = "JNE/JNZ";
            else if (opcode == 0x76 || opcode == 0x66)
                name = "JBE/JNA";
            else if (opcode == 0x77 || opcode == 0x67)
                name = "JA/JNBE";
            else if (opcode == 0x78 || opcode == 0x68)
                name = "JS";
            else if (opcode == 0x79 || opcode == 0x69)
                name = "JNS";
            else if (opcode == 0x7a || opcode == 0x6a)
                name = "JNP/JPO";
            else if (opcode == 0x7b || opcode == 0x6b)
                name = "JNP/JPO";
            else if (opcode == 0x7c || opcode == 0x6c)
                name = "JNGE";
            else if (opcode == 0x7d || opcode == 0x6d)
                name = "JNL";
            else if (opcode == 0x7e || opcode == 0x6e)
                name = "JLE";
            else if (opcode == 0x7f || opcode == 0x6f)
                name = "JNLE";
            else
                meta = "opcode {opcode:x2} not implemented";

            ushort newAddress = (ushort)(d_ip + (sbyte)to);

            instr = $"{name} {to}";
            meta = $"{d_cs:X4}:{newAddress:X4} -> {SegmentAddr(d_cs, newAddress)}";
        }
        else if (opcode == 0xd7)
        {
            instr = "XLATB";
        }
        else if (opcode == 0xe0 || opcode == 0xe1 || opcode == 0xe2)
        {
            // LOOP
            byte to = DisassembleGetByte(ref d_cs, ref d_ip, ref instr_len, ref bytes);
            string name = "?";
            ushort newAddresses = (ushort)(d_ip + (sbyte)to);

            if (opcode == 0xe2)
                name = "LOOP";
            else if (opcode == 0xe1)
                name = "LOOPZ";
            else if (opcode == 0xe0)
                name = "LOOPNZ";
            else
                instr = $"opcode {opcode:X2} not implemented";

            meta = $"{newAddresses:X4}";
            instr = name;
        }
        else if (opcode == 0xe4)
        {
            // IN AL,ib
            byte @from = DisassembleGetByte(ref d_cs, ref d_ip, ref instr_len, ref bytes);
            instr = $"IN AL,${from:X2}";
        }
        else if (opcode == 0xe5)
        {
            byte @from = DisassembleGetByte(ref d_cs, ref d_ip, ref instr_len, ref bytes);
            instr = $"IN AX,${from:X2}";
        }
        else if (opcode == 0xe6)
        {
            byte @to = DisassembleGetByte(ref d_cs, ref d_ip, ref instr_len, ref bytes);
            instr = $"OUT ${to:X2},AL";
        }
        else if (opcode == 0xe7)
        {
            byte @to = DisassembleGetByte(ref d_cs, ref d_ip, ref instr_len, ref bytes);
            instr = $"OUT ${to:X2},AX";
        }
        else if (opcode == 0xec)
        {
            instr = "IN AL,DX";
        }
        else if (opcode == 0xed)
        {
            instr = "IN AX,DX";
        }
        else if (opcode == 0xee)
        {
            instr = "OUT DX,AL";
        }
        else if (opcode == 0xef)
        {
            instr = "OUT DX,AX";
        }
        else if (opcode == 0xeb)
        {
            // JMP
            sbyte to = (sbyte)DisassembleGetByte(ref d_cs, ref d_ip, ref instr_len, ref bytes);
            instr = $"JP ${to:X2}";
            meta = $"{d_cs * 16 + d_ip + to:X6} {d_cs:X04}:{d_ip:X04}";
        }
        else if (opcode == 0xf4)
        {
            instr = "HLT";
        }
        else if (opcode == 0xf5)
        {
            instr = "CMC";
        }
        else if (opcode == 0xf8)
        {
            instr = "CLC";
        }
        else if (opcode == 0xf9)
        {
            instr = "STC";
        }
        else if (opcode == 0xfb)
        {
            instr = "STI";
        }
        else if (opcode == 0xfc)
        {
            instr = "CLD";
        }
        else if (opcode == 0xfd)
        {
            instr = "STD";
        }
        else if (opcode == 0xfe || opcode == 0xff)
        {
            bool word = (opcode & 1) == 1;
            byte o1 = DisassembleGetByte(ref d_cs, ref d_ip, ref instr_len, ref bytes);
            int mod = o1 >> 6;
            int reg = o1 & 7;
            int function = (o1 >> 3) & 7;

            (ushort v, string name, bool a_valid, ushort seg, ushort addr, meta) = DisassemblyGetRegisterMem(reg, mod, word, ref d_cs, ref d_ip, ref instr_len, ref bytes);

            if (function == 0)
                instr = $"INC {name}";
            else if (function == 1)
                instr = $"DEC {name}";
            else if (function == 2)
            {
                instr = $"CALL {name}";
                meta = $"${v:X4} -> {SegmentAddr(d_cs, d_ip)}";
            }
            else if (function == 3)
            {
                ushort temp_cs = ReadMemWord(seg, (ushort)(addr + 2));
                instr = $"CALL {name}";
                meta = "${v:X4} -> {SegmentAddr(temp_cs, v)})";
            }
            else if (function == 4)
            {
                instr = $"JMP {name}";
                meta = $"{d_cs * 16 + v:X6}";
            }
            else if (function == 5)
            {
                // JMP
                ushort temp_cs = ReadMemWord(seg, (ushort)(addr + 2));
                ushort temp_ip = ReadMemWord(seg, addr);
                instr = $"JMP {temp_cs:X4}:{temp_ip:X4}";
            }
            else if (function == 6)
            {
                instr = $"PUSH ${v:X4}";
            }
            else
            {
                meta = "opcode {opcode:X2} function {function} not implemented";
            }
        }
        else
        {
            instr = "?";
            meta = $"opcode {opcode:x} not implemented";
        }

        string hex_string = "";
        foreach(var v in bytes)
        {
            if (hex_string != "")
                hex_string += " ";
            hex_string += $"{v:X02}";
        }

        return new Tuple<int, string, string, string>(instr_len, prefix + instr, meta, hex_string);
    }
}
