namespace DotXT;

internal enum TMode
{
    NotSet,
        Floppy,
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
    private string _segment_override_name = "";

    private ushort _flags;

    private const uint MemMask = 0x00ffffff;

    private Bus _b;

    private readonly IO _io;

    // TODO: make it into a Device, tick_count so that the Device tells which source
    // triggerd the timer so that it can be reset. dus; tick_count = 0, vraag device
    // of die int nog gezet is: device heeft een vlag per source (bijv. timer x van
    // de 8255) die gezet wordt, bij een reconfigure van die 8255-timer wordt dan
    // die vlag gereset
    private bool _scheduled_interrupts = false;

    private bool _rep;
    private bool _rep_do_nothing;
    private RepMode _rep_mode;
    private ushort _rep_addr;
    private byte _rep_opcode;

    private bool _is_test;

    private bool _terminate_on_hlt;

    private readonly List<byte> floppy = new();

    private string tty_output = "";

    private int clock;

    private List<Device> _devices;

    public P8086(ref Bus b, string test, TMode t_mode, uint load_test_at, bool terminate_on_hlt, ref List<Device> devices, bool run_IO)
    {
        _b = b;
        _devices = devices;
        _io = new IO(b, ref devices, !run_IO);

        _terminate_on_hlt = terminate_on_hlt;

        if (test != "" && t_mode == TMode.Binary)
        {
            _is_test = true;

            _cs = 0;
            _ip = 0x0800;

            uint addr = load_test_at == 0xffffffff ? 0 : load_test_at;

            Log.DoLog($"Load {test} at {addr:X6}", true);

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
        else if (test != "" && t_mode == TMode.Floppy)
        {
            _cs = 0;
            _ip = 0x7c00;

            using (var stream = File.Open(test, FileMode.Open))
            {
                byte[] buffer = new byte[512];

                for(;;)
                {
                    int n_read = stream.Read(buffer, 0, 512);

                    if (n_read == 0)
                        break;

                    if (n_read != 512)
                        Console.WriteLine($"Short read from floppy image: {n_read}");

                    for(int i=0; i<512; i++)
                        floppy.Add(buffer[i]);
                }
            }

            for(int i=0; i<512; i++)
                _b.WriteByte((ushort)(_ip + i), floppy[i]);
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

    public string SegmentAddr(ushort seg, ushort a)
    {
        return $"{seg:X04}:{a:X04}";
    }

    public void set_ip(ushort cs, ushort ip)
    {
        Log.DoLog($"Set CS/IP to {cs:X4}:{ip:X4}", true);

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
        uint address = (uint)(_cs * 16 + _ip++) & MemMask;

        return _b.ReadByte(address);
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

    private void WriteMemByte(ushort segment, ushort offset, byte v)
    {
        uint a = (uint)(((segment << 4) + offset) & MemMask);

        _b.WriteByte(a, v);
    }

    private void WriteMemWord(ushort segment, ushort offset, ushort v)
    {
        uint a1 = (uint)(((segment << 4) + offset) & MemMask);
        uint a2 = (uint)(((segment << 4) + ((offset + 1) & 0xffff)) & MemMask);

        _b.WriteByte(a1, (byte)v);
        _b.WriteByte(a2, (byte)(v >> 8));
    }

    public byte ReadMemByte(ushort segment, ushort offset)
    {
        uint a = (uint)(((segment << 4) + offset) & MemMask);

        return _b.ReadByte(a);
    } 

    public ushort ReadMemWord(ushort segment, ushort offset)
    {
        uint a1 = (uint)(((segment << 4) + offset) & MemMask);
        uint a2 = (uint)(((segment << 4) + ((offset + 1) & 0xffff)) & MemMask);

        return (ushort)(_b.ReadByte(a1) | (_b.ReadByte(a2) << 8));
    } 

    private (ushort, string) GetRegister(int reg, bool w)
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

        Log.DoLog($"reg {reg} w {w} not supported for {nameof(GetRegister)}", true);

        return (0, "error");
    }

    private (ushort, string) GetSRegister(int reg)
    {
        reg &= 0b00000011;

        if (reg == 0b000)
            return (_es, "ES");
        if (reg == 0b001)
            return (_cs, "CS");
        if (reg == 0b010)
            return (_ss, "SS");
        if (reg == 0b011)
            return (_ds, "DS");

        Log.DoLog($"reg {reg} not supported for {nameof(GetSRegister)}", true);

        return (0, "error");
    }

    // value, name, cycles
    private (ushort, string, int) GetDoubleRegisterMod00(int reg)
    {
        ushort a = 0;
        string name = "error";
        int cycles = 0;

        if (reg == 0)
        {
            a = (ushort)(GetBX() + _si);
            name = "[BX+SI]";
            cycles = 7;
        }
        else if (reg == 1)
        {
            a = (ushort)(GetBX() + _di);
            name = "[BX+DI]";
            cycles = 8;
        }
        else if (reg == 2)
        {
            a = (ushort)(_bp + _si);
            name = "[BP+SI]";
            cycles = 8;
        }
        else if (reg == 3)
        {
            a = (ushort)(_bp + _di);
            name = "[BP+DI]";
            cycles = 7;
        }
        else if (reg == 4)
        {
            a = _si;
            name = "[SI]";
            cycles = 5;
        }
        else if (reg == 5)
        {
            a = _di;
            name = "[DI]";
            cycles = 5;
        }
        else if (reg == 6)
        {
            a = GetPcWord();
            name = $"[${a:X4}]";
            cycles = 6;
        }
        else if (reg == 7)
        {
            a = GetBX();
            name = "[BX]";
            cycles = 5;
        }
        else
        {
            Log.DoLog($"{nameof(GetDoubleRegisterMod00)} {reg} not implemented", true);
        }

        return (a, name, cycles);
    }

    // value, name, cycles
    private (ushort, string, int, bool, ushort) GetDoubleRegisterMod01_02(int reg, bool word)
    {
        ushort a = 0;
        string name = "error";
        int cycles = 0;
        bool override_segment = false;
        ushort new_segment = 0;

        if (reg == 6)
        {
            a = _bp;
            name = "[BP]";
            cycles = 5;
            override_segment = true;
            new_segment = _ss;
        }
        else
        {
            (a, name, cycles) = GetDoubleRegisterMod00(reg);
        }

        short disp = word ? (short)GetPcWord() : (sbyte)GetPcByte();

        return ((ushort)(a + disp), name + $" disp {disp:X4}", cycles, override_segment, new_segment);
    }

    // value, name_of_source, segment_a_valid, segment/, address of value, number of cycles
    private (ushort, string, bool, ushort, ushort, int) GetRegisterMem(int reg, int mod, bool w)
    {
        if (mod == 0)
        {
            (ushort a, string name, int cycles) = GetDoubleRegisterMod00(reg);

            ushort segment = _segment_override_set ? _segment_override : _ds;

            if (_segment_override_set == false && (reg == 2 || reg == 3)) {  // BP uses SS
                segment = _ss;
#if DEBUG
                Log.DoLog($"BP SS-override ${_ss:X4} [1]", true);
#endif
            }

            ushort v = w ? ReadMemWord(segment, a) : ReadMemByte(segment, a);

            cycles += 6;

            name += $" ({_segment_override_name}:${SegmentAddr(segment, a)} -> {v:X4})";

            return (v, name, true, segment, a, cycles);
        }

        if (mod == 1 || mod == 2)
        {
            bool word = mod == 2;

            (ushort a, string name, int cycles, bool override_segment, ushort new_segment) = GetDoubleRegisterMod01_02(reg, word);

            ushort segment = _segment_override_set ? _segment_override : _ds;

            if (_segment_override_set == false && override_segment)
            {
                segment = new_segment;
#if DEBUG
                Log.DoLog($"BP SS-override ${_ss:X4} [2]", true);
#endif
            }

            if (_segment_override_set == false && (reg == 2 || reg == 3))  // BP uses SS
            {
                segment = _ss;
#if DEBUG
                Log.DoLog($"BP SS-override ${_ss:X4} [3]", true);
#endif
            }

            ushort v = w ? ReadMemWord(segment, a) : ReadMemByte(segment, a);

            cycles += 6;

            name += $" ({_segment_override_name}:${SegmentAddr(segment, a)} -> {v:X4})";

            return (v, name, true, segment, a, cycles);
        }

        if (mod == 3)
        {
            (ushort v, string name) = GetRegister(reg, w);

            return (v, name, false, 0, 0, 0);
        }

        Log.DoLog($"reg {reg} mod {mod} w {w} not supported for {nameof(GetRegisterMem)}", true);

        return (0, "error", false, 0, 0, 0);
    }

    private string PutRegister(int reg, bool w, ushort val)
    {
        if (reg == 0)
        {
            if (w)
            {
                SetAX(val);

                return "AX";
            }

            _al = (byte)val;

            return "AL";
        }

        if (reg == 1)
        {
            if (w)
            {
                SetCX(val);

                return "CX";
            }

            _cl = (byte)val;

            return "CL";
        }

        if (reg == 2)
        {
            if (w)
            {
                SetDX(val);

                return "DX";
            }

            _dl = (byte)val;

            return "DL";
        }

        if (reg == 3)
        {
            if (w)
            {
                SetBX(val);

                return "BX";
            }

            _bl = (byte)val;

            return "BL";
        }

        if (reg == 4)
        {
            if (w)
            {
                _sp = val;

                return "SP";
            }

            _ah = (byte)val;

            return "AH";
        }

        if (reg == 5)
        {
            if (w)
            {
                _bp = val;

                return "BP";
            }

            _ch = (byte)val;

            return "CH";
        }

        if (reg == 6)
        {
            if (w)
            {
                _si = val;

                return "SI";
            }

            _dh = (byte)val;

            return "DH";
        }

        if (reg == 7)
        {
            if (w)
            {
                _di = val;

                return "DI";
            }

            _bh = (byte)val;

            return "BH";
        }

        Log.DoLog($"reg {reg} w {w} not supported for {nameof(PutRegister)} ({val:X})", true);

        return "error";
    }

    private string PutSRegister(int reg, ushort v)
    {
        reg &= 0b00000011;

        if (reg == 0b000)
        {
            _es = v;
            return "ES";
        }

        if (reg == 0b001)
        {
            _cs = v;
            return "CS";
        }

        if (reg == 0b010)
        {
            _ss = v;
            return "SS";
        }

        if (reg == 0b011)
        {
            _ds = v;
            return "DS";
        }

        Log.DoLog($"reg {reg} not supported for {nameof(PutSRegister)}", true);

        return "error";
    }

    // name, cycles
    private (string, int) PutRegisterMem(int reg, int mod, bool w, ushort val)
    {
        //        Log.DoLog($"PutRegisterMem {mod},{w}", true);

        if (mod == 0)
        {
            (ushort a, string name, int cycles) = GetDoubleRegisterMod00(reg);

            ushort segment = _segment_override_set ? _segment_override : _ds;

            if (_segment_override_set == false && (reg == 2 || reg == 3)) {  // BP uses SS
                segment = _ss;
#if DEBUG
                Log.DoLog($"BP SS-override ${_ss:X4} [4]", true);
#endif
            }

            name += $" ({_segment_override_name}:${segment * 16 + a:X6})";

            if (w)
                WriteMemWord(segment, a, val);
            else
                WriteMemByte(segment, a, (byte)val);

            cycles += 4;

            return (name, cycles);
        }

        if (mod == 1 || mod == 2)
        {
            (ushort a, string name, int cycles, bool override_segment, ushort new_segment) = GetDoubleRegisterMod01_02(reg, mod == 2);

            ushort segment = _segment_override_set ? _segment_override : _ds;

            if (_segment_override_set == false && override_segment)
            {
                segment = new_segment;
#if DEBUG
                Log.DoLog($"BP SS-override ${_ss:X4} [5]", true);
#endif
            }

            if (_segment_override_set == false && (reg == 2 || reg == 3))  // BP uses SS
            {
                segment = _ss;
#if DEBUG
                Log.DoLog($"BP SS-override ${_ss:X4} [6]", true);
#endif
            }

#if DEBUG
            name += $" ({_segment_override_name}:${segment * 16 + a:X6})";
#endif

            if (w)
                WriteMemWord(segment, a, val);
            else
                WriteMemByte(segment, a, (byte)val);

            cycles += 4;

            return (name, cycles);
        }

        if (mod == 3)
            return (PutRegister(reg, w, val), 0);

        Log.DoLog($"reg {reg} mod {mod} w {w} value {val} not supported for {nameof(PutRegisterMem)}", true);

        return ("error", 0);
    }

    (string, int) UpdateRegisterMem(int reg, int mod, bool a_valid, ushort seg, ushort addr, bool word, ushort v)
    {
        Log.DoLog($"UpdateRegisterMem {reg} {mod} {a_valid} {seg:X4}:{addr:X4} -> {seg * 16 + addr}/{seg * 16 + addr:X4} {word} {v}", true);

        if (a_valid)
        {
            if (word)
                WriteMemWord(seg, addr, v);
            else
                WriteMemByte(seg, addr, (byte)v);

            return ($"[{addr:X4}]", 4);
        }
        else
        {
            return PutRegisterMem(reg, mod, word, v);
        }
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
    private string GetFlagsAsString()
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
#if DEBUG
        // Log.DoLog($"word {word}, r1 {r1}, r2 {r2}, result {result:X}, issub {issub}", true);
#endif

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
        _segment_override_name = "";

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

        uint addr = (uint)(interrupt_nr * 4);

        _ip = (ushort)(_b.ReadByte(addr + 0) + (_b.ReadByte(addr + 1) << 8));
        _cs = (ushort)(_b.ReadByte(addr + 2) + (_b.ReadByte(addr + 3) << 8));

#if DEBUG
        Log.DoLog($"----- ------ INT {interrupt_nr:X2} (int offset: {addr:X4}, addr: {_cs:X4}:{_ip:X4}, PIC: {pic})", true);
#endif
    }

    public string HexDump(uint addr, bool word)
    {
        string s = "";

        if (word)
        {
            for(uint o=0; o<32; o += 2)
                s += $" {_b.ReadByte((addr + o) & 0xfffff) + (_b.ReadByte((addr + o + 1) & 0xfffff) << 8):X4}";
        }
        else
        {
            for(uint o=0; o<32; o++)
                s += $" {_b.ReadByte((addr + o) & 0xfffff):X2}";
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

    public ushort GetRegisterByName(string name)
    {
        if (name == "si")
            return _si;

        if (name == "cs")
            return _cs;

        return 0xffff;
    }

    public void RunScript(string script)
    {
        string[] lines = script.Split(';');

        int line_nr = 1;

        foreach (var line in lines)
        {
            string[] tokens = line.Split(' ');

            if (tokens[0] == "print*")
            {
                string[] registers = tokens[1].Split(',');

                Log.DoLog($"{line_nr} {tokens[0]}: {GetTerminatedString(GetRegisterByName(registers[0]), GetRegisterByName(registers[1]), '\n')}", true);
            }
            else
            {
                Log.DoLog($"Script token {tokens[0]} (line {line_nr}) not understood", true);
                break;
            }

            line_nr++;
        }
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
                    Log.DoLog($"unknown _rep_mode {_rep_mode}", true);
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
            _segment_override_name = "";
        }

        if (_rep)
            _ip = _rep_addr;
    }

    // cycle counts from https://zsmith.co/intel_i.php
    public bool Tick()
    {
        bool rc = true;

        int cycle_count = 0;  // cycles used for an instruction

        // check for interrupt
        if (GetFlagI() == true) // TODO && _scheduled_interrupts)
        {
            int irq = _io.GetPIC().GetPendingInterrupt();
            if (irq != 255)
            {
                Log.DoLog($"Scanning for IRQ {irq}", true);

                foreach (var device in _devices)
                {
                    if (device.GetIRQNumber() != irq)
                        continue;

                    Log.DoLog($"{device.GetName()} triggers IRQ {irq}", true);

                    InvokeInterrupt(_ip, irq, true);
                    cycle_count += 60;

                    break;
                }
            }
        }

#if DEBUG
        string flagStr = GetFlagsAsString();
#endif

        ushort instr_start = _ip;
        uint address = (uint)(_cs * 16 + _ip) & MemMask;
        Log.SetAddress(_cs, _ip);
        byte opcode = GetPcByte();

        // handle prefixes
        while (opcode is (0x26 or 0x2e or 0x36 or 0x3e or 0xf2 or 0xf3))
        {
            if (opcode == 0x26)
            {
                _segment_override = _es;
                _segment_override_name = "ES";
            }
            else if (opcode == 0x2e)
            {
                _segment_override = _cs;
                _segment_override_name = "CS";
            }
            else if (opcode == 0x36)
            {
                _segment_override = _ss;
                _segment_override_name = "SS";
            }
            else if (opcode == 0x3e)
            {
                _segment_override = _ds;
                _segment_override_name = "DS";
            }
            else if (opcode is (0xf2 or 0xf3))
            {
                _rep = true;
                _rep_mode = RepMode.NotSet;
                cycle_count += 3;

                _rep_do_nothing = GetCX() == 0;
            }
            else
            {
                Log.DoLog($"------ prefix {opcode:X2} not implemented", true);
            }

            address = (uint)(_cs * 16 + _ip) & MemMask;
            Log.SetAddress(_cs, _ip);
            byte next_opcode = GetPcByte();

            _rep_opcode = next_opcode;  // TODO: only allow for certain instructions

            if (opcode == 0xf2)
            {
                _rep_addr = instr_start;
                if (next_opcode is (0xa6 or 0xa7 or 0xae or 0xaf))
                {
                    _rep_mode = RepMode.REPNZ;
                    Log.DoLog($"REPNZ: {_cs:X4}:{_rep_addr:X4}", true);
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
                    Log.DoLog($"REPZ: {_cs:X4}:{_rep_addr:X4}", true);
                }
                else
                {
                    _rep_mode = RepMode.REP;
                    Log.DoLog($"REP: {_cs:X4}:{_rep_addr:X4}", true);
                }
            }
            else
            {
                _segment_override_set = true;  // TODO: move up
                cycle_count += 2;
            }

            if (_segment_override_set)
                Log.DoLog($"segment override to {_segment_override_name}: {_segment_override:X4}, opcode(s): {opcode:X2} {HexDump(address, false):X2}", true);

            if (_rep)
                Log.DoLog($"repetition mode {_rep_mode}, addr {_rep_addr:X4}, instr start {instr_start:X4}", true);

            opcode = next_opcode;
        }

#if DEBUG
        string annotation = _b.GetAnnotation(address);
        if (annotation != null)
            Log.DoLog($"; Annotation: {annotation}", true);

        string script = _b.GetScript(address);
        if (script != null)
            RunScript(script);

        if (_rep)
            Log.DoLog($"repstate: {_rep} {_rep_mode} {_rep_addr:X4} {_rep_opcode:X2}", true);

        string prefixStr =
            $"{flagStr} {opcode:X2} AX:{_ah:X2}{_al:X2} BX:{_bh:X2}{_bl:X2} CX:{_ch:X2}{_cl:X2} DX:{_dh:X2}{_dl:X2} SP:{_sp:X4} BP:{_bp:X4} SI:{_si:X4} DI:{_di:X4} flags:{_flags:X4} ES:{_es:X4} CS:{_cs:X4} SS:{_ss:X4} DS:{_ds:X4} IP:{instr_start:X4} | ";
#else
        string prefixStr = "";
#endif

        // main instruction handling
        if (opcode == 0x04 || opcode == 0x14)
        {
            // ADD AL,xx
            byte v = GetPcByte();

            string name = "ADD";

            bool flag_c = GetFlagC();
            bool use_flag_c = false;

            int result = _al + v;

            if (opcode == 0x14)
            {
                if (flag_c)
                    result++;

                use_flag_c = true;
                name = "ADC";
            }

            cycle_count += 3;

            SetAddSubFlags(false, _al, v, result, false, use_flag_c ? flag_c : false);

            _al = (byte)result;

#if DEBUG
            Log.Disassemble(prefixStr, $" {name} AL,${v:X2}");
#endif
        }
        else if (opcode == 0x05 || opcode == 0x15)
        {
            // ADD AX,xxxx
            ushort v = GetPcWord();

            string name = "ADD";

            bool flag_c = GetFlagC();
            bool use_flag_c = false;

            ushort before = GetAX();

            int result = before + v;

            if (opcode == 0x15)
            {
                if (flag_c)
                    result++;

                use_flag_c = true;
                name = "ADC";
            }

            SetAddSubFlags(true, before, v, result, false, use_flag_c ? flag_c : false);

            SetAX((ushort)result);

            cycle_count += 3;

#if DEBUG
            Log.Disassemble(prefixStr, $" {name} AX,${v:X4}");
#endif
        }
        else if (opcode == 0x06)
        {
            // PUSH ES
            push(_es);

            cycle_count += 11;  // 15

#if DEBUG
            Log.Disassemble(prefixStr, $" PUSH ES");
#endif
        }
        else if (opcode == 0x07)
        {
            // POP ES
            _es = pop();

            cycle_count += 8;

#if DEBUG
            Log.Disassemble(prefixStr, $" POP ES");
#endif
        }
        else if (opcode == 0x0e)
        {
            // PUSH CS
            push(_cs);

            cycle_count += 11;  // 15

#if DEBUG
            Log.Disassemble(prefixStr, $" PUSH CS");
#endif
        }
        else if (opcode == 0x0f)
        {
            // POP CS
            _cs = pop();

            cycle_count += 8;

#if DEBUG
            Log.Disassemble(prefixStr, $" POP CS");
#endif
        }
        else if (opcode == 0x16)
        {
            // PUSH SS
            push(_ss);

            cycle_count += 11;  // 15

#if DEBUG
            Log.Disassemble(prefixStr, $" PUSH SS");
#endif
        }
        else if (opcode == 0x17)
        {
            // POP SS
            _ss = pop();

            cycle_count += 11;  // 15

#if DEBUG
            Log.Disassemble(prefixStr, $" POP SS");
#endif
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

#if DEBUG
            Log.Disassemble(prefixStr, $" SBB ${v:X4}");
#endif
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

#if DEBUG
            Log.Disassemble(prefixStr, $" SBB ${v:X4}");
#endif
        }
        else if (opcode == 0x1e)
        {
            // PUSH DS
            push(_ds);

            cycle_count += 11;  // 15

#if DEBUG
            Log.Disassemble(prefixStr, $" PUSH DS");
#endif
        }
        else if (opcode == 0x1f)
        {
            // POP DS
            _ds = pop();

            cycle_count += 8;

#if DEBUG
            Log.Disassemble(prefixStr, $" POP DS");
#endif
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

            SetFlagS((_al & 0x80) == 0x80);
            SetFlagZ(_al == 0);
            SetFlagP(_al);

            cycle_count += 4;

#if DEBUG
            Log.Disassemble(prefixStr, $" DAA");
#endif
        }
        else if (opcode == 0x2c)
        {
            // SUB AL,ib
            byte v = GetPcByte();

            int result = _al - v;

            SetAddSubFlags(false, _al, v, result, true, false);

            _al = (byte)result;

            cycle_count += 3;

#if DEBUG
            Log.Disassemble(prefixStr, $" SUB ${v:X2}");
#endif
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

            SetFlagS((_al & 0x80) == 0x80);
            SetFlagZ(_al == 0);
            SetFlagP(_al);

            cycle_count += 4;

#if DEBUG
            Log.Disassemble(prefixStr, $" DAS");
#endif
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

            cycle_count += 4;  // FIXME
#if DEBUG
            Log.Disassemble(prefixStr, $" AAA");
#endif
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

            cycle_count += 4;  // FIXME
#if DEBUG
            Log.Disassemble(prefixStr, $" AAS");
#endif
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

#if DEBUG
            Log.Disassemble(prefixStr, $" SUB ${v:X4}");
#endif
        }
        else if (opcode == 0x58)
        {
            // POP AX
            SetAX(pop());

            cycle_count += 8;

#if DEBUG
            Log.Disassemble(prefixStr, $" POP AX");
#endif
        }
        else if (opcode == 0x59)
        {
            // POP CX
            SetCX(pop());

            cycle_count += 8;

#if DEBUG
            Log.Disassemble(prefixStr, $" POP CX");
#endif
        }
        else if (opcode == 0x5a)
        {
            // POP DX
            SetDX(pop());

            cycle_count += 8;

#if DEBUG
            Log.Disassemble(prefixStr, $" POP DX");
#endif
        }
        else if (opcode == 0x5b)
        {
            // POP BX
            SetBX(pop());

            cycle_count += 8;

#if DEBUG
            Log.Disassemble(prefixStr, $" POP BX");
#endif
        }
        else if (opcode == 0x5c)
        {
            // POP SP
            _sp = pop();

            cycle_count += 8;

#if DEBUG
            Log.Disassemble(prefixStr, $" POP SP");
#endif
        }
        else if (opcode == 0x5d)
        {
            // POP BP
            _bp = pop();

            cycle_count += 8;

#if DEBUG
            Log.Disassemble(prefixStr, $" POP BP");
#endif
        }
        else if (opcode == 0x5e)
        {
            // POP SI
            _si = pop();

            cycle_count += 8;

#if DEBUG
            Log.Disassemble(prefixStr, $" POP SI");
#endif
        }
        else if (opcode == 0x5f)
        {
            // POP DI
            _di = pop();

            cycle_count += 8;

#if DEBUG
            Log.Disassemble(prefixStr, $" POP DI");
#endif
        }
        else if (opcode == 0xa4)
        {
            if (PrefixMustRun())
            {
                // MOVSB
                ushort segment = _segment_override_set ? _segment_override : _ds;
                byte v = ReadMemByte(segment, _si);
                WriteMemByte(_es, _di, v);

#if DEBUG
                Log.Disassemble(prefixStr, $" MOVSB ({v:X2} / {(v > 32 && v < 127 ? (char)v : ' ')}, {_rep}) {_segment_override_set}: {_segment_override_name} {SegmentAddr(segment, _si)} -> {SegmentAddr(_es, _di)}");
#endif

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

#if DEBUG
                Log.Disassemble(prefixStr, $" MOVSW");
#endif
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

#if DEBUG
                Log.Disassemble(prefixStr, $" CMPSB ({v1:X2}/{(v1 > 32 && v1 < 127 ? (char)v1 : ' ')}, {v2:X2}/{(v2 > 32 && v2 < 127 ? (char)v2 : ' ')}) {GetCX()}");
#endif
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

#if DEBUG
                Log.Disassemble(prefixStr, $" CMPSW (${v1:X4},${v2:X4})");
#endif
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

#if DEBUG
            Log.Disassemble(prefixStr, $" JCXZ {addr:X}");
#endif
        }
        else if (opcode == 0xe9)
        {
            // JMP np
            short offset = (short)GetPcWord();

            _ip = (ushort)(_ip + offset);

            cycle_count += 15;

#if DEBUG
            Log.Disassemble(prefixStr, $" JMP {_ip:X} ({offset:X4})");
#endif
        }
        else if (opcode == 0x50)
        {
            // PUSH AX
            push(GetAX());

            cycle_count += 11;  // 15

#if DEBUG
            Log.Disassemble(prefixStr, $" PUSH AX");
#endif
        }
        else if (opcode == 0x51)
        {
            // PUSH CX
            push(GetCX());

            cycle_count += 11;  // 15

#if DEBUG
            Log.Disassemble(prefixStr, $" PUSH CX");
#endif
        }
        else if (opcode == 0x52)
        {
            // PUSH DX
            push(GetDX());

            cycle_count += 11;  // 15

#if DEBUG
            Log.Disassemble(prefixStr, $" PUSH DX");
#endif
        }
        else if (opcode == 0x53)
        {
            // PUSH BX
            push(GetBX());

            cycle_count += 11;  // 15

#if DEBUG
            Log.Disassemble(prefixStr, $" PUSH BX");
#endif
        }
        else if (opcode == 0x54)
        {
            // PUSH SP
            // special case, see:
            // https://c9x.me/x86/html/file_module_x86_id_269.html
            _sp -= 2;
            WriteMemWord(_ss, _sp, _sp);

            cycle_count += 11;  // 15

#if DEBUG
            Log.Disassemble(prefixStr, $" PUSH SP");
#endif
        }
        else if (opcode == 0x55)
        {
            // PUSH BP
            push(_bp);

            cycle_count += 11;  // 15

#if DEBUG
            Log.Disassemble(prefixStr, $" PUSH BP");
#endif
        }
        else if (opcode == 0x56)
        {
            // PUSH SI
            push(_si);

            cycle_count += 11;  // 15

#if DEBUG
            Log.Disassemble(prefixStr, $" PUSH SI");
#endif
        }
        else if (opcode == 0x57)
        {
            // PUSH DI
            push(_di);

            cycle_count += 11;  // 15

#if DEBUG
            Log.Disassemble(prefixStr, $" PUSH DI");
#endif
        }
        else if (opcode is (0x80 or 0x81 or 0x82 or 0x83))
        {
            // CMP and others
            byte o1 = GetPcByte();

            int mod = o1 >> 6;
            int reg = o1 & 7;

            int function = (o1 >> 3) & 7;

            ushort r1 = 0;
            string name1 = "error";
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
                (r1, name1, a_valid, seg, addr, cycles) = GetRegisterMem(reg, mod, false);

                r2 = GetPcByte();
            }
            else if (opcode == 0x81)
            {
                (r1, name1, a_valid, seg, addr, cycles) = GetRegisterMem(reg, mod, true);

                r2 = GetPcWord();

                word = true;
            }
            else if (opcode == 0x82)
            {
                (r1, name1, a_valid, seg, addr, cycles) = GetRegisterMem(reg, mod, false);

                r2 = GetPcByte();
            }
            else if (opcode == 0x83)
            {
                (r1, name1, a_valid, seg, addr, cycles) = GetRegisterMem(reg, mod, true);

                r2 = GetPcByte();

                if ((r2 & 128) == 128)
                    r2 |= 0xff00;

                word = true;
            }
            else
            {
                Log.DoLog($"{prefixStr} opcode {opcode:X2} not implemented", true);
            }

            string iname = "error";
            bool apply = true;
            bool use_flag_c = false;

            if (function == 0)
            {
                result = r1 + r2;
                iname = "ADD";
            }
            else if (function == 1)
            {
                result = r1 | r2;
                is_logic = true;
                iname = "OR";
            }
            else if (function == 2)
            {
                result = r1 + r2 + (GetFlagC() ? 1 : 0);
                use_flag_c = true;
                iname = "ADC";
            }
            else if (function == 3)
            {
                result = r1 - r2 - (GetFlagC() ? 1 : 0);
                is_sub = true;
                use_flag_c = true;
                iname = "SBB";
            }
            else if (function == 4)
            {
                result = r1 & r2;
                is_logic = true;
                iname = "AND";
                SetFlagC(false);
            }
            else if (function == 5)
            {
                result = r1 - r2;
                is_sub = true;
                iname = "SUB";
            }
            else if (function == 6)
            {
                result = r1 ^ r2;
                is_logic = true;
                iname = "XOR";
            }
            else if (function == 7)
            {
                result = r1 - r2;
                is_sub = true;
                apply = false;
                iname = "CMP";
            }
            else
            {
                Log.DoLog($"{prefixStr} opcode {opcode:X2} function {function} not implemented", true);
            }

            if (is_logic)
                SetLogicFuncFlags(word, (ushort)result);
            else
                SetAddSubFlags(word, r1, r2, result, is_sub, use_flag_c ? GetFlagC() : false);

            if (apply)
            {
                (string dummy, int put_cycles) = UpdateRegisterMem(reg, mod, a_valid, seg, addr, word, (ushort)result);

                cycles += put_cycles;
            }

            cycle_count += 3 + cycles;

#if DEBUG
            Log.Disassemble(prefixStr, $" {iname} {name1},${r2:X2}");
#endif
        }
        else if (opcode == 0x84 || opcode == 0x85)
        {
            // TEST ...,...
            bool word = (opcode & 1) == 1;
            byte o1 = GetPcByte();

            int mod = o1 >> 6;
            int reg1 = (o1 >> 3) & 7;
            int reg2 = o1 & 7;

            (ushort r1, string name1, bool a_valid, ushort seg, ushort addr, int cycles) = GetRegisterMem(reg2, mod, word);
            (ushort r2, string name2) = GetRegister(reg1, word);

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

#if DEBUG
            Log.Disassemble(prefixStr, $" TEST {name1},{name2}");
#endif
        }
        else if (opcode == 0x86 || opcode == 0x87)
        {
            // XCHG
            bool word = (opcode & 1) == 1;
            byte o1 = GetPcByte();

            int mod = o1 >> 6;
            int reg1 = (o1 >> 3) & 7;
            int reg2 = o1 & 7;

            (ushort r1, string name1, bool a_valid, ushort seg, ushort addr, int get_cycles) = GetRegisterMem(reg2, mod, word);
            (ushort r2, string name2) = GetRegister(reg1, word);

            (string dummy, int put_cycles) = UpdateRegisterMem(reg2, mod, a_valid, seg, addr, word, r2);

            PutRegister(reg1, word, r1);

            cycle_count += 3 + get_cycles + put_cycles;

#if DEBUG
            Log.Disassemble(prefixStr, $" XCHG {name1},{name2}");
#endif
        }
        else if (opcode == 0x8f)
        {
            // POP rmw
            byte o1 = GetPcByte();

            int mod = o1 >> 6;
            int reg2 = o1 & 7;

            (string toName, int put_cycles) = PutRegisterMem(reg2, mod, true, pop());

            cycle_count += put_cycles;

            cycle_count += 17;

#if DEBUG
            Log.Disassemble(prefixStr, $" POP {toName}");
#endif
        }
        else if (opcode == 0x90)
        {
            // NOP

            cycle_count += 3;

#if DEBUG
            Log.Disassemble(prefixStr, $" NOP");
#endif
        }
        else if (opcode >= 0x91 && opcode <= 0x97)
        {
            // XCHG AX,...
            int reg_nr = opcode - 0x90;

            (ushort v, string name_other) = GetRegister(reg_nr, true);

            ushort old_ax = GetAX();
            SetAX(v);

            PutRegister(reg_nr, true, old_ax);

            cycle_count += 3;

#if DEBUG
            Log.Disassemble(prefixStr, $" XCHG AX,{name_other}");
#endif
        }
        else if (opcode == 0x98)
        {
            // CBW
            ushort new_value = _al;

            if ((_al & 128) == 128)
                new_value |= 0xff00;

            SetAX(new_value);

            cycle_count += 2;

#if DEBUG
            Log.Disassemble(prefixStr, $" CBW");
#endif
        }
        else if (opcode == 0x99)
        {
            // CWD
            if ((_ah & 128) == 128)
                SetDX(0xffff);
            else
                SetDX(0);

            cycle_count += 5;

#if DEBUG
            Log.Disassemble(prefixStr, $" CDW");
#endif
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

#if DEBUG
            Log.Disassemble(prefixStr, $" CALL ${_cs:X} ${_ip:X}: ${_cs * 16 + _ip:X}");
#endif
        }
        else if (opcode == 0x9c)
        {
            // PUSHF
            push(_flags);

            cycle_count += 10;  // 14

#if DEBUG
            Log.Disassemble(prefixStr, $" PUSHF");
#endif
        }
        else if (opcode == 0x9d)
        {
            // POPF
            _flags = pop();

            cycle_count += 8;  // 12

            FixFlags();

#if DEBUG
            Log.Disassemble(prefixStr, $" POPF");
#endif
        }
        else if (opcode == 0xac)
        {
            if (PrefixMustRun())
            {
                // LODSB
                _al = ReadMemByte(_segment_override_set ? _segment_override : _ds, _si);

                _si += (ushort)(GetFlagD() ? -1 : 1);

                cycle_count += 5;

#if DEBUG
                Log.Disassemble(prefixStr, $" LODSB");
#endif
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

#if DEBUG
                Log.Disassemble(prefixStr, $" LODSW");
#endif
            }
        }
        else if (opcode == 0xc2 || opcode == 0xc0)
        {
            ushort nToRelease = GetPcWord();

            // RET
            _ip = pop();

            _sp += nToRelease;

            cycle_count += 16;

#if DEBUG
            Log.Disassemble(prefixStr, $" RET ${nToRelease:X4}");
#endif
        }
        else if (opcode == 0xc3 || opcode == 0xc1)
        {
            // RET
            _ip = pop();

            cycle_count += 16;

#if DEBUG
            Log.Disassemble(prefixStr, $" RET");
#endif
        }
        else if (opcode == 0xc4 || opcode == 0xc5)
        {
            // LES (c4) / LDS (c5)
            byte o1 = GetPcByte();
            int mod = o1 >> 6;
            int reg = (o1 >> 3) & 7;
            int rm = o1 & 7;

            (ushort val, string name_from, bool a_valid, ushort seg, ushort addr, int get_cycles) = GetRegisterMem(rm, mod, true);

            string name;

            if (opcode == 0xc4)
            {
                _es = ReadMemWord(seg, (ushort)(addr + 2));
                name = "LES";
            }
            else
            {
                _ds = ReadMemWord(seg, (ushort)(addr + 2));
                name = "LDS";
            }

            string affected = PutRegister(reg, true, val);

            cycle_count += 7 + get_cycles;

#if DEBUG
            Log.Disassemble(prefixStr, $" {name} {affected},{name_from}");
#endif
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

                uint addr = (uint)(@int * 4);

                push(_flags);
                push(_cs);
                if (_rep)
                {
                    push(_rep_addr);
                    Log.DoLog($"INT from rep {_rep_addr:X04}", true);
                }
                else
                {
                    push(_ip);
                }

                SetFlagI(false);

                _ip = (ushort)(_b.ReadByte(addr + 0) + (_b.ReadByte(addr + 1) << 8));
                _cs = (ushort)(_b.ReadByte(addr + 2) + (_b.ReadByte(addr + 3) << 8));

                cycle_count += 51;  // 71  TODO

#if DEBUG
                if (opcode == 0xce)
                    Log.Disassemble(prefixStr, $" INTO {@int:X2} -> {SegmentAddr(_cs, _ip)} (from {addr:X4})");
                else
                    Log.Disassemble(prefixStr, $" INT {@int:X2} -> {SegmentAddr(_cs, _ip)} (from {addr:X4})");
#endif
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

#if DEBUG
            Log.Disassemble(prefixStr, $" IRET");
#endif
        }
        else if ((opcode >= 0x00 && opcode <= 0x03) || (opcode >= 0x10 && opcode <= 0x13) || (opcode >= 0x28 && opcode <= 0x2b) || (opcode >= 0x18 && opcode <= 0x1b) || (opcode >= 0x38 && opcode <= 0x3b))
        {
            bool word = (opcode & 1) == 1;
            bool direction = (opcode & 2) == 2;
            byte o1 = GetPcByte();

            int mod = o1 >> 6;
            int reg1 = (o1 >> 3) & 7;
            int reg2 = o1 & 7;

            (ushort r1, string name1, bool a_valid, ushort seg, ushort addr, int get_cycles) = GetRegisterMem(reg2, mod, word);
            (ushort r2, string name2) = GetRegister(reg1, word);

            cycle_count += get_cycles;

            string name = "error";
            int result = 0;
            bool is_sub = false;
            bool apply = true;
            bool use_flag_c = false;

            if (opcode <= 0x03)
            {
                result = r1 + r2;

                cycle_count += 4;

                name = "ADD";
            }
            else if (opcode >= 0x10 && opcode <= 0x13)
            {
                use_flag_c = true;

                result = r1 + r2 + (GetFlagC() ? 1 : 0);

                cycle_count += 4;

                name = "ADC";
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
                    name = "CMP";
                }
                else if (opcode >= 0x28 && opcode <= 0x2b)
                {
                    name = "SUB";
                }
                else  // 0x18...0x1b
                {
                    use_flag_c = true;

                    result -= (GetFlagC() ? 1 : 0);

                    name = "SBB";
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

#if DEBUG
                    Log.Disassemble(prefixStr, $" {name} {name2},{name1}");
#endif
                }
                else
                {
                    bool override_to_ss = a_valid && word && _segment_override_set == false &&
                        (
                         ((reg2 == 2 || reg2 == 3) && mod == 0)
                        );

                    if (override_to_ss)
                    {
                        seg = _ss;
#if DEBUG
                        Log.DoLog($"BP SS-override ${_ss:X4} [7]", true);
#endif
                    }

                    (string dummy, int put_cycles) = UpdateRegisterMem(reg2, mod, a_valid, seg, addr, word, (ushort)result);

                    cycle_count += put_cycles;

#if DEBUG
                    Log.Disassemble(prefixStr, $" {name} {name1},{name2}");
#endif
                }
            }
            else
            {
#if DEBUG
                if (direction)
                    Log.Disassemble(prefixStr, $" {name} {name2},{name1}");
                else
                    Log.Disassemble(prefixStr, $" {name} {name1},{name2}");
#endif
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

#if DEBUG
                Log.Disassemble(prefixStr, $" CMP AX,#${r2:X4}");
#endif
            }
            else if (opcode == 0x3c)
            {
                r1 = _al;
                r2 = GetPcByte();

                result = r1 - r2;

#if DEBUG
                Log.Disassemble(prefixStr, $" CMP AL,#${r2:X2}");
#endif
            }
            else
            {
                Log.DoLog($"{prefixStr} opcode {opcode:X2} not implemented", true);
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

            (ushort r1, string name1, bool a_valid, ushort seg, ushort addr, int get_cycles) = GetRegisterMem(reg2, mod, word);
            (ushort r2, string name2) = GetRegister(reg1, word);

            cycle_count += get_cycles;

            string name = "error";

            ushort result = 0;

            int function = opcode >> 4;

            cycle_count += 3;

            if (function == 0)
            {
                result = (ushort)(r1 | r2);
                name = "OR";
            }
            else if (function == 2)
            {
                result = (ushort)(r2 & r1);
                name = "AND";
            }
            else if (function == 3)
            {
                result = (ushort)(r2 ^ r1);
                name = "XOR";
            }
            else
            {
                Log.DoLog($"{prefixStr} opcode {opcode:X2} function {function} not implemented", true);
            }

            SetLogicFuncFlags(word, result);

            if (direction)
            {
                string affected = PutRegister(reg1, word, result);
            }
            else
            {
                (string affected, int put_cycles) = UpdateRegisterMem(reg2, mod, a_valid, seg, addr, word, result);

                cycle_count += put_cycles;
            }

#if DEBUG
            Log.Disassemble(prefixStr, $" {name} {name1},{name2}");
#endif
        }
        else if (opcode is (0x34 or 0x35 or 0x24 or 0x25 or 0x0c or 0x0d))
        {
            bool word = (opcode & 1) == 1;

            byte bLow = GetPcByte();
            byte bHigh = word ? GetPcByte() : (byte)0;

            string tgt_name = word ? "AX" : "AL";
            string name = "error";

            int function = opcode >> 4;

            if (function == 0)
            {
                _al |= bLow;

                if (word)
                    _ah |= bHigh;

                name = "OR";
            }
            else if (function == 2)
            {
                _al &= bLow;

                if (word)
                    _ah &= bHigh;

                name = "AND";

                SetFlagC(false);
            }
            else if (function == 3)
            {
                _al ^= bLow;

                if (word)
                    _ah ^= bHigh;

                name = "XOR";
            }
            else
            {
                Log.DoLog($"{prefixStr} opcode {opcode:X2} function {function} not implemented", true);
            }

            SetLogicFuncFlags(word, word ? GetAX() : _al);

            SetFlagP(_al);

            cycle_count += 4;

#if DEBUG
            Log.Disassemble(prefixStr, $" {name} {tgt_name},${bHigh:X2}{bLow:X2}");
#endif
        }
        else if (opcode == 0xe8)
        {
            // CALL
            short a = (short)GetPcWord();

            push(_ip);

            _ip = (ushort)(a + _ip);

            cycle_count += 16;

#if DEBUG
            Log.Disassemble(prefixStr, $" CALL {a:X4} (${_ip:X4} -> {SegmentAddr(_cs, _ip)})");
#endif
        }
        else if (opcode == 0xea)
        {
            // JMP far ptr
            ushort temp_ip = GetPcWord();
            ushort temp_cs = GetPcWord();

            _ip = temp_ip;
            _cs = temp_cs;

            cycle_count += 15;

#if DEBUG
            Log.Disassemble(prefixStr, $" JMP ${_cs:X} ${_ip:X}: {SegmentAddr(_cs, _ip)}");
#endif
        }
        else if (opcode == 0xf6 || opcode == 0xf7)
        {
            // TEST and others
            bool word = (opcode & 1) == 1;

            byte o1 = GetPcByte();
            int mod = o1 >> 6;
            int reg1 = o1 & 7;

            (ushort r1, string name1, bool a_valid, ushort seg, ushort addr, int get_cycles) = GetRegisterMem(reg1, mod, word);
            cycle_count += get_cycles;

            string name2 = "";
            string cmd_name = "error";

            int function = (o1 >> 3) & 7;
            if (function == 0 || function == 1)
            {
                // TEST
                if (word) {
                    ushort r2 = GetPcWord();
                    name2 = $"{r2:X4}";

                    ushort result = (ushort)(r1 & r2);
                    SetLogicFuncFlags(true, result);

                    SetFlagC(false);

                    cmd_name = "TEST";
                }
                else {
                    byte r2 = GetPcByte();
                    name2 = $",{r2:X2}";

                    ushort result = (ushort)(r1 & r2);
                    SetLogicFuncFlags(word, result);

                    SetFlagC(false);
                }

                cmd_name = "TEST";
            }
            else if (function == 2)
            {
                // NOT
                (string dummy, int put_cycles) = UpdateRegisterMem(reg1, mod, a_valid, seg, addr, word, (ushort)~r1);

                cycle_count += put_cycles;

                cmd_name = "NOT";
            }
            else if (function == 3)
            {
                // NEG
                int result = (ushort)-r1;

                cmd_name = "NEG";

                SetAddSubFlags(word, 0, r1, -r1, true, false);
                SetFlagC(r1 != 0);

                (string dummy, int put_cycles) = UpdateRegisterMem(reg1, mod, a_valid, seg, addr, word, (ushort)result);

                cycle_count += put_cycles;
            }
            else if (function == 4)
            {
                // MUL
                if (word) {
                    ushort ax = GetAX();
                    int resulti = ax * r1;

                    uint dx_ax = (uint)resulti;
                    SetAX((ushort)dx_ax);
                    SetDX((ushort)(dx_ax >> 16));

                    bool flag = GetDX() != 0;
                    SetFlagC(flag);
                    SetFlagO(flag);

                    name2 = name1;
                    name1 = "DX:AX";
                }
                else {
                    int result = _al * r1;
                    SetAX((ushort)result);

                    bool flag = _ah != 0;
                    SetFlagC(flag);
                    SetFlagO(flag);
                }

                cmd_name = "MUL";
            }
            else if (function == 5)
            {
                // IMUL
                if (word) {
                    short ax = (short)GetAX();
                    int resulti = ax * (short)r1;

                    uint dx_ax = (uint)resulti;
                    SetAX((ushort)dx_ax);
                    SetDX((ushort)(dx_ax >> 16));

                    bool flag = (int)(short)GetAX() != resulti;
                    SetFlagC(flag);
                    SetFlagO(flag);

                    name2 = name1;
                    name1 = "DX:AX";
                }
                else {
                    int result = (sbyte)_al * (short)(sbyte)r1;
                    SetAX((ushort)result);

                    SetFlagS((_ah & 128) == 128);
                    bool flag = (short)(sbyte)_al != (short)result;
                    SetFlagC(flag);
                    SetFlagO(flag);
                }

                cmd_name = "IMUL";
            }
            else if (function == 6)
            {
                // DIV
                if (word) {
                    uint dx_ax = (uint)((GetDX() << 16) | GetAX());

                    if (r1 == 0 || dx_ax / r1 >= 0x10000)
                        InvokeInterrupt(_ip, 0x00, false);  // divide by zero or divisor too small
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
                        SetFlagP(0);
                        SetFlagS((ax & 0x8000) != 0);
                        InvokeInterrupt(_ip, 0x00, false);  // divide by zero or divisor too small
                    }
                    else
                    {
                        _al = (byte)(ax / r1);
                        _ah = (byte)(ax % r1);
                        SetFlagP(0);
                        SetFlagS((_ah ^ 0x80) != 0);
                    }
                }

                cmd_name = "DIV";
            }
            else if (function == 7)
            {
                // IDIV
                if (word) {
                    int dx_ax = (GetDX() << 16) | GetAX();
                    int r1s = (int)(short)r1;

                    if (r1s == 0 || dx_ax / r1s > 0x7fff || dx_ax / r1s < -0x8000)
                        InvokeInterrupt(_ip, 0x00, false);  // divide by zero or divisor too small
                    else
                    {
                        SetAX((ushort)(dx_ax / r1s));
                        SetDX((ushort)(dx_ax % r1s));
                    }
                }
                else {
                    short ax = (short)GetAX();
                    short r1s = (short)r1;

                    if (r1s == 0 || ax / r1s > 0x7f || ax / r1s < -0x80)
                    {
                        InvokeInterrupt(_ip, 0x00, false);  // divide by zero or divisor too small
                    }
                    else
                    {
                        _al = (byte)(ax / r1s);
                        _ah = (byte)(ax % r1s);
                        SetFlagP(0);
                    }
                }

                cmd_name = "IDIV";
            }
            else
            {
                Log.DoLog($"{prefixStr} opcode {opcode:X2} o1 {o1:X2} function {function} not implemented", true);
            }

            cycle_count += 4;

#if DEBUG
            if (name2 != "")
                Log.Disassemble(prefixStr, $" {cmd_name} {name1},{name2} word:{word}");
            else
                Log.Disassemble(prefixStr, $" {cmd_name} {name1} word:{word}");
#endif
        }
        else if (opcode == 0xfa)
        {
            // CLI
            SetFlagI(false); // IF

            cycle_count += 2;

#if DEBUG
            Log.Disassemble(prefixStr, $" CLI");
#endif
        }
        else if ((opcode & 0xf0) == 0xb0)
        {
            // MOV reg,ib
            int reg = opcode & 0x07;

            bool word = (opcode & 0x08) == 0x08;

            ushort v = GetPcByte();

            if (word)
                v |= (ushort)(GetPcByte() << 8);

            string name = PutRegister(reg, word, v);

            cycle_count += 2;

#if DEBUG
            Log.Disassemble(prefixStr, $" MOV {name},${v:X}");
#endif
        }
        else if (opcode == 0xa0)
        {
            // MOV AL,[...]
            ushort a = GetPcWord();

            _al = ReadMemByte(_segment_override_set ? _segment_override : _ds, a);

            cycle_count += 2;

#if DEBUG
            Log.Disassemble(prefixStr, $" MOV AL,[${a:X4}]");
#endif
        }
        else if (opcode == 0xa1)
        {
            // MOV AX,[...]
            ushort a = GetPcWord();

            SetAX(ReadMemWord(_segment_override_set ? _segment_override : _ds, a));

            cycle_count += 2;

#if DEBUG
            Log.Disassemble(prefixStr, $" MOV AX,[${a:X4}]");
#endif
        }
        else if (opcode == 0xa2)
        {
            // MOV [...],AL
            ushort a = GetPcWord();

            WriteMemByte(_segment_override_set ? _segment_override : _ds, a, _al);

            cycle_count += 2;

#if DEBUG
            Log.Disassemble(prefixStr, $" MOV [${a:X4}],AL");
#endif
        }
        else if (opcode == 0xa3)
        {
            // MOV [...],AX
            ushort a = GetPcWord();

            WriteMemWord(_segment_override_set ? _segment_override : _ds, a, GetAX());

            cycle_count += 2;

#if DEBUG
            Log.Disassemble(prefixStr, $" MOV [${a:X4}],AX");
#endif
        }
        else if (opcode == 0xa8)
        {
            // TEST AL,..
            byte v = GetPcByte();

            byte result = (byte)(_al & v);

            SetLogicFuncFlags(false, result);

            SetFlagC(false);

            cycle_count += 3;

#if DEBUG
            Log.Disassemble(prefixStr, $" TEST AL,${v:X2}");
#endif
        }
        else if (opcode == 0xa9)
        {
            // TEST AX,..
            ushort v = GetPcWord();

            ushort result = (ushort)(GetAX() & v);

            SetLogicFuncFlags(true, result);

            SetFlagC(false);

            cycle_count += 3;

#if DEBUG
            Log.Disassemble(prefixStr, $" TEST AX,${v:X4}");
#endif
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
                (ushort v, string fromName, bool a_valid, ushort seg, ushort addr, int get_cycles) = GetRegisterMem(rm, mode, word);

                cycle_count += get_cycles;

                string toName;

                if (sreg)
                    toName = PutSRegister(reg, v);
                else
                    toName = PutRegister(reg, word, v);

#if DEBUG
                Log.Disassemble(prefixStr, $" MOV {toName},{fromName}");
#endif
            }
            else
            {
                // from 'REG' to 'rm'
                ushort v;
                string fromName;

                if (sreg)
                    (v, fromName) = GetSRegister(reg);
                else
                    (v, fromName) = GetRegister(reg, word);

                (string toName, int put_cycles) = PutRegisterMem(rm, mode, word, v);

                cycle_count += put_cycles;

#if DEBUG
                Log.Disassemble(prefixStr, $" MOV {toName},{fromName} ({v:X4})");
#endif
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
            (ushort val, string name_from, bool a_valid, ushort seg, ushort addr, int get_cycles) = GetRegisterMem(rm, mod, true);

            cycle_count += get_cycles;

            string name_to = PutRegister(reg, true, addr);

#if DEBUG
            Log.Disassemble(prefixStr, $" LEA {name_to},{name_from}");
#endif
        }
        else if (opcode == 0x9e)
        {
            // SAHF
            ushort keep = (ushort)(_flags & 0b1111111100101010);
            ushort add = (ushort)(_ah & 0b11010101);

            _flags = (ushort)(keep | add);

            FixFlags();

            cycle_count += 4;

#if DEBUG
            Log.Disassemble(prefixStr, $" SAHF (set to {GetFlagsAsString()})");
#endif
        }
        else if (opcode == 0x9f)
        {
            // LAHF
            _ah = (byte)_flags;

            cycle_count += 2;

#if DEBUG
            Log.Disassemble(prefixStr, $" LAHF");
#endif
        }
        else if (opcode is >= 0x40 and <= 0x4f)
        {
            // INC/DECw
            int reg = (opcode - 0x40) & 7;

            (ushort v, string name) = GetRegister(reg, true);

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

#if DEBUG
            if (isDec)
                Log.Disassemble(prefixStr, $" DEC {name}");
            else
                Log.Disassemble(prefixStr, $" INC {name}");
#endif
        }
        else if (opcode == 0xaa)
        {
            if (PrefixMustRun())
            {
                // STOSB
                WriteMemByte(_es, _di, _al);

                _di += (ushort)(GetFlagD() ? -1 : 1);

                cycle_count += 11;

#if DEBUG
                Log.Disassemble(prefixStr, $" STOSB");
#endif
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

#if DEBUG
                Log.Disassemble(prefixStr, $" STOSW");
#endif
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

#if DEBUG
                Log.Disassemble(prefixStr, $" SCASB");
#endif
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

#if DEBUG
                Log.Disassemble(prefixStr, $" SCASW");
#endif
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
            (ushort dummy, string name, bool a_valid, ushort seg, ushort addr, int get_cycles) = GetRegisterMem(mreg, mod, word);

            cycle_count += get_cycles;

            if (word)
            {
                // the value follows
                ushort v = GetPcWord();

                (string dummy2, int put_cycles) = UpdateRegisterMem(mreg, mod, a_valid, seg, addr, word, v);

                cycle_count += put_cycles;

#if DEBUG
                Log.Disassemble(prefixStr, $" MOV word {name},${v:X4}");
#endif
            }
            else
            {
                // the value follows
                byte v = GetPcByte();

                (string dummy2, int put_cycles) = UpdateRegisterMem(mreg, mod, a_valid, seg, addr, word, v);

                cycle_count += put_cycles;

#if DEBUG
                Log.Disassemble(prefixStr, $" MOV byte {name},${v:X2}");
#endif
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

#if DEBUG
                Log.Disassemble(prefixStr, $" RETF ${nToRelease:X4}");
#endif
            }
#if DEBUG
            else
            {
                Log.Disassemble(prefixStr, $" RETF");

                cycle_count += 26;
            }
#endif
        }
        else if ((opcode & 0xfc) == 0xd0)
        {
            bool word = (opcode & 1) == 1;
            byte o1 = GetPcByte();

            int mod = o1 >> 6;
            int reg1 = o1 & 7;

            (ushort v1, string vName, bool a_valid, ushort seg, ushort addr, int get_cycles) = GetRegisterMem(reg1, mod, word);

            cycle_count += get_cycles;

            int count = 1;
            string countName = "1";
            int count_mask = 0x1f;

            if ((opcode & 2) == 2)
            {
                count = _cl;
                countName = "CL";
            }

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

#if DEBUG
                Log.Disassemble(prefixStr, $" ROL {vName},{countName}");
#endif
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

#if DEBUG
                Log.Disassemble(prefixStr, $" ROR {vName},{countName}");
#endif
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

#if DEBUG
                Log.Disassemble(prefixStr, $" RCL {vName},{countName}");
#endif
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

#if DEBUG
                Log.Disassemble(prefixStr, $" RCR {vName},{countName}");
#endif
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

                cycle_count += 2;

#if DEBUG
                Log.Disassemble(prefixStr, $" SAL {vName},{countName}");
#endif
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

                cycle_count += 2;

#if DEBUG
                Log.Disassemble(prefixStr, $" SHR {vName},{countName}");
#endif
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

#if DEBUG
                    Log.Disassemble(prefixStr, $" SETMOC");
#endif
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

#if DEBUG
                    Log.Disassemble(prefixStr, $" SETMO");
#endif
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

#if DEBUG
                Log.Disassemble(prefixStr, $" SAR {vName},{countName}");
#endif
            }
            else
            {
                Log.DoLog($"{prefixStr} RCR/SHR/{opcode:X2} mode {mode} not implemented", true);
            }

            if (!word)
                v1 &= 0xff;

            if (set_flags)
            {
                SetFlagS((word ? v1 & 0x8000 : v1 & 0x80) != 0);
                SetFlagZ(v1 == 0);
                SetFlagP((byte)v1);
            }

            (string dummy, int put_cycles) = UpdateRegisterMem(reg1, mod, a_valid, seg, addr, word, v1);

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

                SetFlagS((_al & 128) == 128);
                SetFlagZ(_al == 0);
                SetFlagP(_al);
            }
            else
            {
                SetFlagS(false);
                SetFlagZ(true);
                SetFlagP(0);

                SetFlagO(false);
                SetFlagA(false);
                SetFlagC(false);

                InvokeInterrupt(_ip, 0x00, false);
            }

            cycle_count += 2;  // TODO

#if DEBUG
            Log.Disassemble(prefixStr, $" AAM");
#endif
        }
        else if (opcode == 0xd5)
        {
            // AAD
            byte b2 = GetPcByte();

            _al = (byte)(_al + _ah * b2);
            _ah = 0;

            SetFlagS((_al & 128) == 128);
            SetFlagZ(_al == 0);
            SetFlagP(_al);

            cycle_count += 2;  // TODO

#if DEBUG
            Log.Disassemble(prefixStr, $" AAD");
#endif
        }
        else if (opcode == 0xd6)
        {
            // SALC
            if (GetFlagC())
                _al = 0xff;
            else
                _al = 0x00;

            cycle_count += 2;  // TODO

#if DEBUG
            Log.Disassemble(prefixStr, $" SALC");
#endif
        }
        else if (opcode == 0xdb || opcode==0xdd)
        {
            byte o1 = GetPcByte();
            int mod = o1 >> 6;
            int reg1 = o1 & 7;
            (ushort v1, string vName, bool a_valid, ushort seg, ushort addr, int get_cycles) = GetRegisterMem(reg1, mod, false);
            cycle_count += get_cycles;

            cycle_count += 2;  // TODO

#if DEBUG
            Log.DoLog($"{prefixStr} FPU - ignored", true);
#endif
        }
        else if ((opcode & 0xf0) == 0x70 || (opcode & 0xf0) == 0x60)
        {
            // J..., 0x70/0x60
            byte to = GetPcByte();

            bool state = false;
            string name = String.Empty;

            if (opcode == 0x70 || opcode == 0x60)
            {
                state = GetFlagO();
                name = "JO";
            }
            else if (opcode == 0x71 || opcode == 0x61)
            {
                state = GetFlagO() == false;
                name = "JNO";
            }
            else if (opcode == 0x72 || opcode == 0x62)
            {
                state = GetFlagC();
                name = "JC/JB";
            }
            else if (opcode == 0x73 || opcode == 0x63)
            {
                state = GetFlagC() == false;
                name = "JNC";
            }
            else if (opcode == 0x74 || opcode == 0x64)
            {
                state = GetFlagZ();
                name = "JE/JZ";
            }
            else if (opcode == 0x75 || opcode == 0x65)
            {
                state = GetFlagZ() == false;
                name = "JNE/JNZ";
            }
            else if (opcode == 0x76 || opcode == 0x66)
            {
                state = GetFlagC() || GetFlagZ();
                name = "JBE/JNA";
            }
            else if (opcode == 0x77 || opcode == 0x67)
            {
                state = GetFlagC() == false && GetFlagZ() == false;
                name = "JA/JNBE";
            }
            else if (opcode == 0x78 || opcode == 0x68)
            {
                state = GetFlagS();
                name = "JS";
            }
            else if (opcode == 0x79 || opcode == 0x69)
            {
                state = GetFlagS() == false;
                name = "JNS";
            }
            else if (opcode == 0x7a || opcode == 0x6a)
            {
                state = GetFlagP();
                name = "JNP/JPO";
            }
            else if (opcode == 0x7b || opcode == 0x6b)
            {
                state = GetFlagP() == false;
                name = "JNP/JPO";
            }
            else if (opcode == 0x7c || opcode == 0x6c)
            {
                state = GetFlagS() != GetFlagO();
                name = "JNGE";
            }
            else if (opcode == 0x7d || opcode == 0x6d)
            {
                state = GetFlagS() == GetFlagO();
                name = "JNL";
            }
            else if (opcode == 0x7e || opcode == 0x6e)
            {
                state = GetFlagZ() == true || GetFlagS() != GetFlagO();
                name = "JLE";
            }
            else if (opcode == 0x7f || opcode == 0x6f)
            {
                state = GetFlagZ() == false && GetFlagS() == GetFlagO();
                name = "JNLE";
            }
            else
            {
                Log.DoLog($"{prefixStr} opcode {opcode:x2} not implemented", true);
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

#if DEBUG
            Log.Disassemble(prefixStr, $" {name} {to} ({_cs:X4}:{newAddress:X4} -> {SegmentAddr(_cs, newAddress)})");
#endif
        }
        else if (opcode == 0xd7)
        {
            // XLATB
            byte old_al = _al;

            _al = ReadMemByte(_segment_override_set ? _segment_override : _ds, (ushort)(GetBX() + _al));

            cycle_count += 11;

#if DEBUG
            Log.Disassemble(prefixStr, $" XLATB ({_ds:X4}:{GetBX():X4} + {old_al:X2})");
#endif
        }
        else if (opcode == 0xe0 || opcode == 0xe1 || opcode == 0xe2)
        {
            // LOOP
            byte to = GetPcByte();

            ushort cx = GetCX();
            cx--;
            SetCX(cx);

            string name = "?";
            ushort newAddresses = (ushort)(_ip + (sbyte)to);

            cycle_count += 4;

            if (opcode == 0xe2)
            {
                if (cx > 0)
                {
                    _ip = newAddresses;
                    cycle_count += 4;
                }
                else
                {
#if DEBUG
                    Log.DoLog("LOOP end", true);
#endif
                }

                name = "LOOP";
            }
            else if (opcode == 0xe1)
            {
                if (cx > 0 && GetFlagZ() == true)
                {
                    _ip = newAddresses;
                    cycle_count += 4;
                }

                name = "LOOPZ";
            }
            else if (opcode == 0xe0)
            {
                if (cx > 0 && GetFlagZ() == false)
                {
                    _ip = newAddresses;
                    cycle_count += 4;
                }

                name = "LOOPNZ";
            }
#if DEBUG
            else
            {
                Log.Disassemble(prefixStr, $" opcode {opcode:X2} not implemented");
            }
#endif

#if DEBUG
            Log.Disassemble(prefixStr, $" {name} {to} ({newAddresses:X4})");
#endif
        }
        else if (opcode == 0xe4)
        {
            // IN AL,ib
            byte @from = GetPcByte();

            (ushort val, bool i) = _io.In(@from);
            _al = (byte)val;

            _scheduled_interrupts |= i;

            cycle_count += 10;  // or 14

#if DEBUG
            Log.Disassemble(prefixStr, $" IN AL,${from:X2}");
#endif
        }
        else if (opcode == 0xe5)
        {
            // IN AX,ib
            byte @from = GetPcByte();

            (ushort val, bool i) = _io.In(@from);
            SetAX(val);

            _scheduled_interrupts |= i;
            cycle_count += 10;  // or 14

#if DEBUG
            Log.Disassemble(prefixStr, $" IN AX,${from:X2}");
#endif
        }
        else if (opcode == 0xe6)
        {
            // OUT
            byte to = GetPcByte();
            _scheduled_interrupts |= _io.Out(@to, _al);

            cycle_count += 10;  // max 14

#if DEBUG
            Log.Disassemble(prefixStr, $" OUT ${to:X2},AL");
#endif
        }
        else if (opcode == 0xe7)
        {
            // OUT
            byte to = GetPcByte();
            _scheduled_interrupts |= _io.Out(@to, GetAX());

            cycle_count += 10;  // max 14

#if DEBUG
            Log.Disassemble(prefixStr, $" OUT ${to:X2},AX");
#endif
        }
        else if (opcode == 0xec)
        {
            // IN AL,DX
            (ushort val, bool i) = _io.In(GetDX());
            _al = (byte)val;

            _scheduled_interrupts |= i;

            cycle_count += 8;  // or 12

#if DEBUG
            Log.Disassemble(prefixStr, $" IN AL,DX");
#endif
        }
        else if (opcode == 0xed)
        {
            // IN AX,DX
            (ushort val, bool i) = _io.In(GetDX());
            SetAX(val);
            _scheduled_interrupts |= i;

            cycle_count += 12;

#if DEBUG
            Log.Disassemble(prefixStr, $" IN AX,DX");
#endif
        }
        else if (opcode == 0xee)
        {
            // OUT
            _scheduled_interrupts |= _io.Out(GetDX(), _al);

            cycle_count += 8;  // or 12

#if DEBUG
            Log.Disassemble(prefixStr, $" OUT DX,AL");
#endif
        }
        else if (opcode == 0xef)
        {
            // OUT
            _scheduled_interrupts |= _io.Out(GetDX(), GetAX());

            cycle_count += 8;  // or 12 TODO

#if DEBUG
            Log.Disassemble(prefixStr, $" OUT DX,AX");
#endif
        }
        else if (opcode == 0xeb)
        {
            // JMP
            byte to = GetPcByte();

            _ip = (ushort)(_ip + (sbyte)to);

            cycle_count += 15;

#if DEBUG
            Log.Disassemble(prefixStr, $" JP ${_ip:X4} ({_cs * 16 + _ip:X6})");
#endif
        }
        else if (opcode == 0xf4)
        {
            // HLT
            _ip--;

#if DEBUG
            Log.Disassemble(prefixStr, $" HLT");
#endif

            if (_terminate_on_hlt)
            {
                Log.EmitDisassembly();

                if (_is_test)
                    System.Environment.Exit(_si == 0xa5ee ? 123 : 0);

                System.Environment.Exit(0);
            }

            rc = false;
        }
        else if (opcode == 0xf5)
        {
            // CMC
            SetFlagC(! GetFlagC());

            cycle_count += 2;

#if DEBUG
            Log.Disassemble(prefixStr, $" CMC");
#endif
        }
        else if (opcode == 0xf8)
        {
            // CLC
            SetFlagC(false);

            cycle_count += 2;

#if DEBUG
            Log.Disassemble(prefixStr, $" CLC");
#endif
        }
        else if (opcode == 0xf9)
        {
            // STC
            SetFlagC(true);

            cycle_count += 2;

#if DEBUG
            Log.Disassemble(prefixStr, $" STC");
#endif
        }
        else if (opcode == 0xfb)
        {
            // STI
            SetFlagI(true); // IF

            cycle_count += 2;

            _scheduled_interrupts = true;  // TODO temp?

#if DEBUG
            Log.Disassemble(prefixStr, $" STI");
#endif
        }
        else if (opcode == 0xfc)
        {
            // CLD
            SetFlagD(false);

            cycle_count += 2;

#if DEBUG
            Log.Disassemble(prefixStr, $" CLD");
#endif
        }
        else if (opcode == 0xfd)
        {
            // STD
            SetFlagD(true);

            cycle_count += 2;

#if DEBUG
            Log.Disassemble(prefixStr, $" STD");
#endif
        }
        else if (opcode == 0xfe || opcode == 0xff)
        {
            // DEC and others
            bool word = (opcode & 1) == 1;

            byte o1 = GetPcByte();

            int mod = o1 >> 6;
            int reg = o1 & 7;

            int function = (o1 >> 3) & 7;

            // Log.Disassemble($"mod {mod} reg {reg} function {function}");

            (ushort v, string name, bool a_valid, ushort seg, ushort addr, int get_cycles) = GetRegisterMem(reg, mod, word);

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

#if DEBUG
                Log.Disassemble(prefixStr, $" INC {name}");
#endif
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

#if DEBUG
                Log.Disassemble(prefixStr, $" DEC {name}");
#endif
            }
            else if (function == 2)
            {
                // CALL
                push(_ip);

                _rep = false;
                _ip = v;

                cycle_count += 16;

#if DEBUG
                Log.Disassemble(prefixStr, $" CALL {name} (${_ip:X4} -> {SegmentAddr(_cs, _ip)})");
#endif
            }
            else if (function == 3)
            {
                // CALL FAR
                push(_cs);
                push(_ip);

                Log.DoLog($"v: {v:X4}, addr: {addr:X4}, word@addr+0: {ReadMemWord(seg, (ushort)(addr + 0)):X4}, word@addr+2: {ReadMemWord(seg, (ushort)(addr + 2)):X4}", true);

                _ip = v;
                _cs = ReadMemWord(seg, (ushort)(addr + 2));

                cycle_count += 37;

#if DEBUG
                Log.Disassemble(prefixStr, $" CALL {name} (${_ip:X4} -> {SegmentAddr(_cs, _ip)})");
#endif
            }
            else if (function == 4)
            {
                // JMP NEAR
                _ip = v;

                cycle_count += 18;

#if DEBUG
                Log.Disassemble(prefixStr, $" JMP {name} ({_cs * 16 + _ip:X6})");
#endif
            }
            else if (function == 5)
            {
                // JMP
                _cs = ReadMemWord(seg, (ushort)(addr + 2));
                _ip = ReadMemWord(seg, addr);

                cycle_count += 18;  // TODO

#if DEBUG
                Log.Disassemble(prefixStr, $" JMP {_cs:X4}:{_ip:X4}");
#endif
            }
            else if (function == 6)
            {
                // PUSH rmw
                if (reg == 4 && mod == 3 && word == true)
                {
                    _sp -= 2;
                    WriteMemWord(seg, _sp, _sp);
                }
                else
                {
                    push(v);
                }

                cycle_count += 16;
#if DEBUG
                Log.Disassemble(prefixStr, $" PUSH ${v:X4}");
#endif
            }
            else
            {
                Log.DoLog($"{prefixStr} opcode {opcode:X2} function {function} not implemented", true);
            }

            if (!word)
                v &= 0xff;

            (string dummy, int put_cycles) = UpdateRegisterMem(reg, mod, a_valid, seg, addr, word, v);

            cycle_count += put_cycles;
        }
        else
        {
            Log.DoLog($"{prefixStr} opcode {opcode:x} not implemented", true);
        }

        PrefixEnd(opcode);

        if (cycle_count == 0)
            cycle_count = 1;  // TODO workaround

        // tick I/O
        _scheduled_interrupts |= _io.Tick(cycle_count, clock);

        clock += cycle_count;

        return rc;
    }
}
