namespace DotXT;

// TODO this needs a rewrite/clean-up

class P8086Disassembler
{
    private State8086 _state;
    private const uint MemMask = 0x00ffffff;
    private Bus _b;
    
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

    public ushort GetAX()
    {
        return (ushort)((_state.ah << 8) | _state.al);
    }

    public ushort GetBX()
    {
        return (ushort)((_state.bh << 8) | _state.bl);
    }

    public ushort GetCX()
    {
        return (ushort)((_state.ch << 8) | _state.cl);
    }

    public ushort GetDX()
    {
        return (ushort)((_state.dh << 8) | _state.dl);
    }

    private byte GetByte(ref int instr_len, ref List<byte> bytes)
    {
        byte b = ReadMemByte(_state.cs, _state.ip);
        bytes.Add(b);
        _state.ip++;
        instr_len++;
        return b;
    }

    private ushort GetWord(ref int instr_len, ref List<byte> bytes)
    {
        byte low = GetByte(ref instr_len, ref bytes);
        byte high = GetByte(ref instr_len, ref bytes);
        return (ushort)(low + (high << 8));
    }

    // value, name, meta
    private (ushort, string, string) GetDoubleRegisterMod00(int reg, ref int instr_len, ref List<byte> bytes)
    {
        ushort a = 0;
        string name = "error";
        string meta = "";

        if (reg == 0)
        {
            a = (ushort)(GetBX() + _state.si);
            name = "[BX+SI]";
        }
        else if (reg == 1)
        {
            a = (ushort)(GetBX() + _state.di);
            name = "[BX+DI]";
        }
        else if (reg == 2)
        {
            a = (ushort)(_state.bp + _state.si);
            name = "[BP+SI]";
        }
        else if (reg == 3)
        {
            a = (ushort)(_state.bp + _state.di);
            name = "[BP+DI]";
        }
        else if (reg == 4)
        {
            a = _state.si;
            name = "[SI]";
        }
        else if (reg == 5)
        {
            a = _state.di;
            name = "[DI]";
        }
        else if (reg == 6)
        {
            a = GetWord(ref instr_len, ref bytes);
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
    private (ushort, string, string, bool, ushort) GetDoubleRegisterMod01_02(int reg, bool word, ref int instr_len, ref List<byte> bytes)
    {
        ushort a = 0;
        string name = "error";
        bool override_segment = false;
        ushort new_segment = 0;
        string meta = "";

        if (reg == 6)
        {
            a = _state.bp;
            name = "[BP]";
            override_segment = true;
            new_segment = _state.ss;
        }
        else
        {
            (a, name, meta) = GetDoubleRegisterMod00(reg, ref instr_len, ref bytes);
        }

        short disp = word ? (short)GetWord(ref instr_len, ref bytes) : (sbyte)GetByte(ref instr_len, ref bytes);

        return ((ushort)(a + disp), name, $"disp {disp:X4} " + meta, override_segment, new_segment);
    }

    // value, name_of_source, segment_a_valid, segment/, address of value, meta
    private (ushort, string, bool, ushort, ushort, string) GetRegisterMem(int reg, int mod, bool w, ref int instr_len, ref List<byte> bytes)
    {
        string meta = "";

        if (mod == 0)
        {
            (ushort a, string name, meta) = GetDoubleRegisterMod00(reg, ref instr_len, ref bytes);

            ushort segment = _state.segment_override_set ? _state.segment_override : _state.ds;
            if (_state.segment_override_set == false && (reg == 2 || reg == 3))  // BP uses SS
            {
                segment = _state.ss;
                meta = $"BP SS-override ${_state.ss:X4}";
            }

            ushort v = w ? ReadMemWord(segment, a) : ReadMemByte(segment, a);

            return (v, name, true, segment, a, meta);
        }

        if (mod == 1 || mod == 2)
        {
            bool word = mod == 2;

            (ushort a, string name, meta, bool override_segment, ushort new_segment) = GetDoubleRegisterMod01_02(reg, word, ref instr_len, ref bytes);

            ushort segment = _state.segment_override_set ? _state.segment_override : _state.ds;
            if (_state.segment_override_set == false && override_segment)
            {
                segment = new_segment;
                meta += $"BP SS-override ${_state.ss:X4} [2]";
            }
            if (_state.segment_override_set == false && (reg == 2 || reg == 3))  // BP uses SS
            {
                segment = _state.ss;
                meta += $"BP SS-override ${_state.ss:X4} [3]";
            }

            ushort v = w ? ReadMemWord(segment, a) : ReadMemByte(segment, a);

            return (v, name, true, segment, a, meta);
        }

        if (mod == 3)
        {
            (ushort v, string name) = GetRegister(reg, w);
            return (v, name, false, 0, 0, "");
        }

        return (0, "error", false, 0, 0, $"reg {reg} mod {mod} w {w} not supported for {nameof(GetRegisterMem)}");
    }

    private string PutRegister(int reg, bool w, ushort val)
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

    private string PutSRegister(int reg, ushort v)
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
                return (_state.sp, "SP");
            if (reg == 5)
                return (_state.bp, "BP");
            if (reg == 6)
                return (_state.si, "SI");
            if (reg == 7)
                return (_state.di, "DI");
        }
        else
        {
            if (reg == 0)
                return (_state.al, "AL");
            if (reg == 1)
                return (_state.cl, "CL");
            if (reg == 2)
                return (_state.dl, "DL");
            if (reg == 3)
                return (_state.bl, "BL");
            if (reg == 4)
                return (_state.ah, "AH");
            if (reg == 5)
                return (_state.ch, "CH");
            if (reg == 6)
                return (_state.dh, "DH");
            if (reg == 7)
                return (_state.bh, "BH");
        }

        return (0, "error");
    }

    private (string, int) PutRegisterMem(int reg, int mod, bool w, ushort val, ref int instr_len, ref List<byte> bytes)
    {
        if (mod == 0)
        {
            (ushort a, string name, string meta) = GetDoubleRegisterMod00(reg, ref instr_len, ref bytes);

            ushort segment = _state.segment_override_set ? _state.segment_override : _state.ds;

            if (_state.segment_override_set == false && (reg == 2 || reg == 3)) {  // BP uses SS
                segment = _state.ss;
                meta = $"BP SS-override ${_state.ss:X4}";
            }

            return (name, 0);
        }

        if (mod == 1 || mod == 2)
        {
            (ushort a, string name, string meta, bool override_segment, ushort new_segment) = GetDoubleRegisterMod01_02(reg, mod == 2, ref instr_len, ref bytes);

            ushort segment = _state.segment_override_set ? _state.segment_override : _state.ds;

            if (_state.segment_override_set == false && override_segment)
            {
                segment = new_segment;
                meta = $"BP SS-override ${_state.ss:X4} [5]";
            }

            if (_state.segment_override_set == false && (reg == 2 || reg == 3))  // BP uses SS
            {
                segment = _state.ss;
                meta = $"BP SS-override ${_state.ss:X4} [6]";
            }

            return (name, 0);
        }

        if (mod == 3)
            return (PutRegister(reg, w, val), 0);

        return ("error", 0);
    }

    (string, int) DisassemblyUpdateRegisterMem(int reg, int mod, bool a_valid, ushort seg, ushort addr, bool word, ushort v, ref int instr_len, ref List<byte> bytes)
    {
        if (a_valid)
            return ($"[{addr:X4}]", 4);

        return PutRegisterMem(reg, mod, word, v, ref instr_len, ref bytes);
    }

    private (ushort, string) GetSRegister(int reg)
    {
        reg &= 0b00000011;
 
        if (reg == 0b000)
            return (_state.es, "ES");
        if (reg == 0b001)
            return (_state.cs, "CS");  // TODO use _state.cs from Disassemble invocation?
        if (reg == 0b010)
            return (_state.ss, "SS");
        if (reg == 0b011)
            return (_state.ds, "DS");

        Log.DoLog($"reg {reg} not supported for {nameof(GetSRegister)}", LogLevel.WARNING);

        return (0, "error");
    }

    public ushort GetFlags()
    {
        return _state.flags;
    }

    private bool GetFlag(int bit)
    {
        return (_state.flags & (1 << bit)) != 0;
    }

    private bool GetFlagC()
    {
        return GetFlag(0);
    }

    private bool GetFlagP()
    {
        return GetFlag(2);
    }

    private bool GetFlagA()
    {
        return GetFlag(4);
    }

    private bool GetFlagZ()
    {
        return GetFlag(6);
    }

    private bool GetFlagS()
    {
        return GetFlag(7);
    }

    private bool GetFlagT()
    {
        return GetFlag(8);
    }

    private bool GetFlagI()
    {
        return GetFlag(9);
    }

    private bool GetFlagD()
    {
        return GetFlag(10);
    }

    private bool GetFlagO()
    {
        return GetFlag(11);
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

    private string SegmentAddr(ushort seg, ushort a)
    {
        return $"{seg:X04}:{a:X04}";
    }

    private string GetFlagsAsString()
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

    public P8086Disassembler(Bus b)
    {
        _b = b;
    }

    public void SetCPUState(in State8086 state)
    {
        _state = state;
    }

    public string GetRegisters()
    {
        return  $"{GetFlagsAsString()} AX:{GetAX():X4} BX:{GetBX():X4} CX:{GetCX():X4} DX:{GetDX():X4} SP:{GetSP():X4} BP:{GetBP():X4} SI:{GetSI():X4} DI:{GetDI():X4} flags:{GetFlags():X4} ES:{GetES():X4} CS:{_state.cs:X4} SS:{GetSS():X4} DS:{GetDS():X4} IP:{_state.ip:X4}";
    }

    // instruction length, instruction string, additional info, hex-string
    public Tuple<int, string, string, string> Disassemble()
    {
        int instr_len = 0;
        List<byte> bytes = new();
        byte opcode = GetByte(ref instr_len, ref bytes);

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

            byte next_opcode = GetByte(ref instr_len, ref bytes);

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
            byte v = GetByte(ref instr_len, ref bytes);
            instr = $"ADD AL,#{v:X2}";
        }
        else if (opcode == 0x05 || opcode == 0x15)
        {
            // ADD AX,xxxx
            ushort v = GetWord(ref instr_len, ref bytes);

            if (opcode == 0x05)
                instr = $"ADD AX,${v:X4}";
            else
                instr = $"ADC AX,${v:X4}";
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
            byte v = GetByte(ref instr_len, ref bytes);
            instr = $"SBB AL,${v:X2}";
        }
        else if (opcode == 0x1d)
        {
            ushort v = GetWord(ref instr_len, ref bytes);
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
            byte v = GetByte(ref instr_len, ref bytes);
            instr = $"SUB AL,${v:X2}";
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
            ushort v = GetWord(ref instr_len, ref bytes);
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
            byte offset = GetByte(ref instr_len, ref bytes);
            instr = "JCXZ ${offset:X02}";
        }
        else if (opcode == 0xe9)
        {
            short offset = (short)GetWord(ref instr_len, ref bytes);
            ushort word = (ushort)(_state.ip + offset);
            instr = $"JMP {_state.ip:X}";
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
            byte o1 = GetByte(ref instr_len, ref bytes);

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
                (r1, name1, a_valid, seg, addr, meta) = GetRegisterMem(reg, mod, false, ref instr_len, ref bytes);

                r2 = GetByte(ref instr_len, ref bytes);
            }
            else if (opcode == 0x81)
            {
                (r1, name1, a_valid, seg, addr, meta) = GetRegisterMem(reg, mod, true, ref instr_len, ref bytes);

                r2 = GetWord(ref instr_len, ref bytes);
            }
            else if (opcode == 0x82)
            {
                (r1, name1, a_valid, seg, addr, meta) = GetRegisterMem(reg, mod, false, ref instr_len, ref bytes);

                r2 = GetByte(ref instr_len, ref bytes);
            }
            else if (opcode == 0x83)
            {
                (r1, name1, a_valid, seg, addr, meta) = GetRegisterMem(reg, mod, true, ref instr_len, ref bytes);

                r2 = GetByte(ref instr_len, ref bytes);
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
            byte o1 = GetByte(ref instr_len, ref bytes);

            int mod = o1 >> 6;
            int reg1 = (o1 >> 3) & 7;
            int reg2 = o1 & 7;

            (ushort r1, string name1, bool a_valid, ushort seg, ushort addr, meta) = GetRegisterMem(reg2, mod, word, ref instr_len, ref bytes);
            (ushort r2, string name2) = GetRegister(reg1, word);

            instr = $"TEST {name1},{name2}";
        }
        else if (opcode == 0x86 || opcode == 0x87)
        {
            // XCHG
            bool word = (opcode & 1) == 1;
            byte o1 = GetByte(ref instr_len, ref bytes);

            int mod = o1 >> 6;
            int reg1 = (o1 >> 3) & 7;
            int reg2 = o1 & 7;

            (ushort r1, string name1, bool a_valid, ushort seg, ushort addr, meta) = GetRegisterMem(reg2, mod, word, ref instr_len, ref bytes);
            (ushort r2, string name2) = GetRegister(reg1, word);

            instr = $"XCHG {name1},{name2}";
        }
        else if (opcode == 0x8f)
        {
            // POP rmw
            byte o1 = GetByte(ref instr_len, ref bytes);
            int mod = o1 >> 6;
            int reg2 = o1 & 7;
            (string toName, int put_cycles) = PutRegisterMem(reg2, mod, true, ReadMemWord(_state.ss, _state.sp), ref instr_len, ref bytes);
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
            (ushort v, string name_other) = GetRegister(reg_nr, true);
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
            ushort temp_ip = GetWord(ref instr_len, ref bytes);
            ushort temp_cs = GetWord(ref instr_len, ref bytes);

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
            ushort nToRelease = GetWord(ref instr_len, ref bytes);
            instr = $"RET ${nToRelease:X4}";
        }
        else if (opcode == 0xc3 || opcode == 0xc1)
        {
            instr = "RET";
        }
        else if (opcode == 0xc4 || opcode == 0xc5)
        {
            // LES (c4) / LDS (c5)
            byte o1 = GetByte(ref instr_len, ref bytes);
            int mod = o1 >> 6;
            int reg = (o1 >> 3) & 7;
            int rm = o1 & 7;

            (ushort val, string name_from, bool a_valid, ushort seg, ushort addr, meta) = GetRegisterMem(rm, mod, true, ref instr_len, ref bytes);

            string name;
            if (opcode == 0xc4)
                name = "LES";
            else
                name = "LDS";

            string affected = PutRegister(reg, true, val);
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
                    @int = GetByte(ref instr_len, ref bytes);

                ushort addr = (ushort)(@int * 4);

                if (opcode == 0xce)
                {
                    instr = $"INTO {@int:X2}";
                    meta = $"{SegmentAddr(_state.cs, _state.ip)} (from {addr:X4})";
                }
                else 
                {
                    instr = $"INT {@int:X2}";
                    meta = $"{SegmentAddr(_state.cs, _state.ip)} (from {addr:X4})";
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
            byte o1 = GetByte(ref instr_len, ref bytes);

            int mod = o1 >> 6;
            int reg1 = (o1 >> 3) & 7;
            int reg2 = o1 & 7;

            (ushort r1, string name1, bool a_valid, ushort seg, ushort addr, meta) = GetRegisterMem(reg2, mod, word, ref instr_len, ref bytes);
            (ushort r2, string name2) = GetRegister(reg1, word);

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
                (string dummy, int put_cycles) = DisassemblyUpdateRegisterMem(reg2, mod, a_valid, seg, addr, word, (ushort)result, ref instr_len, ref bytes);
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
                r2 = GetWord(ref instr_len, ref bytes);
                instr = $"CMP AX,#${r2:X4}";
            }
            else if (opcode == 0x3c)
            {
                r1 = _state.al;
                r2 = GetByte(ref instr_len, ref bytes);
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
            byte o1 = GetByte(ref instr_len, ref bytes);

            int mod = o1 >> 6;
            int reg1 = (o1 >> 3) & 7;
            int reg2 = o1 & 7;

            (ushort r1, string name1, bool a_valid, ushort seg, ushort addr, meta) = GetRegisterMem(reg2, mod, word, ref instr_len, ref bytes);
            (ushort r2, string name2) = GetRegister(reg1, word);

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

            byte bLow = GetByte(ref instr_len, ref bytes);
            byte bHigh = word ? GetByte(ref instr_len, ref bytes) : (byte)0;

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
            short a = (short)GetWord(ref instr_len, ref bytes);
            ushort temp_ip = (ushort)(a + _state.ip);
            instr = $"CALL {a:X4}";
            meta = $"{SegmentAddr(_state.cs, temp_ip)}";
        }
        else if (opcode == 0xea)
        {
            // JMP far ptr
            ushort temp_ip = GetWord(ref instr_len, ref bytes);
            ushort temp_cs = GetWord(ref instr_len, ref bytes);
            instr = $"JMP {SegmentAddr(temp_cs, temp_ip)}";
        }
        else if (opcode == 0xf6 || opcode == 0xf7)
        {
            // TEST and others
            bool word = (opcode & 1) == 1;

            byte o1 = GetByte(ref instr_len, ref bytes);
            int mod = o1 >> 6;
            int reg1 = o1 & 7;

            (ushort r1, string name1, bool a_valid, ushort seg, ushort addr, meta) = GetRegisterMem(reg1, mod, word, ref instr_len, ref bytes);

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
            else if (function == 6 || function == 7)
            {
                name = function == 6 ? "DIV" : "IDIV";
                if (word)
                    meta = $"DX:AX ({GetDX():X04}:{GetAX():X04} / {r1:X04})";
                else
                    meta = $"AX ({GetAX():X04} / {r1:X04})";
            }
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

            ushort v = GetByte(ref instr_len, ref bytes);
            if (word)
                v |= (ushort)(GetByte(ref instr_len, ref bytes) << 8);

            string name = PutRegister(reg, word, v);
            instr = $"MOV {name},${v:X}";
        }
        else if (opcode == 0xa0)
        {
            ushort a = GetWord(ref instr_len, ref bytes);
            instr = $"MOV AL,[${a:X4}]";
        }
        else if (opcode == 0xa1)
        {
            // MOV AX,[...]
            ushort a = GetWord(ref instr_len, ref bytes);
            instr = $"MOV AX,[${a:X4}]";
        }
        else if (opcode == 0xa2)
        {
            // MOV [...],AL
            ushort a = GetWord(ref instr_len, ref bytes);
            instr = $"MOV [${a:X4}],AL";
        }
        else if (opcode == 0xa3)
        {
            ushort a = GetWord(ref instr_len, ref bytes);
            instr = $"MOV [${a:X4}],AX";
        }
        else if (opcode == 0xa8)
        {
            // TEST AL,..
            byte v = GetByte(ref instr_len, ref bytes);
            instr = $"TEST AL,${v:X2}";
        }
        else if (opcode == 0xa9)
        {
            // TEST AX,..
            ushort v = GetWord(ref instr_len, ref bytes);
            instr = $"TEST AX,${v:X4}";
        }
        else if (opcode is (0x88 or 0x89 or 0x8a or 0x8b or 0x8e or 0x8c))
        {
            bool dir = (opcode & 2) == 2; // direction
            bool word = (opcode & 1) == 1; // b/w

            byte o1 = GetByte(ref instr_len, ref bytes);
            int mode = o1 >> 6;
            int reg = (o1 >> 3) & 7;
            int rm = o1 & 7;

            bool sreg = opcode == 0x8e || opcode == 0x8c;
            if (sreg)
                word = true;

            if (dir)
            {
                // to 'REG' from 'rm'
                (ushort v, string fromName, bool a_valid, ushort seg, ushort addr, meta) = GetRegisterMem(rm, mode, word, ref instr_len, ref bytes);
                string toName;
                if (sreg)
                    toName = PutSRegister(reg, v);
                else
                    toName = PutRegister(reg, word, v);

                instr = $"MOV {toName},{fromName}";
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

                (string toName, int put_cycles) = PutRegisterMem(rm, mode, word, v, ref instr_len, ref bytes);
                instr = $"MOV {toName},{fromName}";
                meta = $"{v:X4}";
            }
        }
        else if (opcode == 0x8d)
        {
            // LEA
            byte o1 = GetByte(ref instr_len, ref bytes);
            int mod = o1 >> 6;
            int reg = (o1 >> 3) & 7;
            int rm = o1 & 7;

            // might introduce problems when the dereference of *addr reads from i/o even
            // when it is not required
            (ushort val, string name_from, bool a_valid, ushort seg, ushort addr, meta) = GetRegisterMem(rm, mod, true, ref instr_len, ref bytes);
            string name_to = PutRegister(reg, true, addr);
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
            (ushort v, string name) = GetRegister(reg, true);
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
            byte o1 = GetByte(ref instr_len, ref bytes);

            int mod = o1 >> 6;
            int mreg = o1 & 7;

            (ushort dummy, string name, bool a_valid, ushort seg, ushort addr, meta) = GetRegisterMem(mreg, mod, word, ref instr_len, ref bytes);

            if (word)
            {
                ushort v = GetWord(ref instr_len, ref bytes);
                instr = $"MOV word {name},${v:X4}";
            }
            else
            {
                // the value follows
                byte v = GetByte(ref instr_len, ref bytes);
                instr = $"MOV byte {name},${v:X2}";
            }
        }
        else if (opcode >= 0xc8 && opcode <= 0xcb)
        {
            // RETF n / RETF
            if (opcode == 0xca || opcode == 0xc8)
            {
                ushort nToRelease = GetWord(ref instr_len, ref bytes);
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
            byte o1 = GetByte(ref instr_len, ref bytes);

            int mod = o1 >> 6;
            int reg1 = o1 & 7;
            (ushort v1, string vName, bool a_valid, ushort seg, ushort addr, meta) = GetRegisterMem(reg1, mod, word, ref instr_len, ref bytes);

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
            byte o1 = GetByte(ref instr_len, ref bytes);
            int mod = o1 >> 6;
            int reg1 = o1 & 7;
            (ushort v1, string vName, bool a_valid, ushort seg, ushort addr, meta) = GetRegisterMem(reg1, mod, false, ref instr_len, ref bytes);
            meta = $"FPU ({opcode:X02} {o1:X02}) - ignored";
        }
        else if ((opcode & 0xf0) == 0x70 || (opcode & 0xf0) == 0x60)
        {
            // J..., 0x70/0x60
            byte to = GetByte(ref instr_len, ref bytes);

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

            ushort newAddress = (ushort)(_state.ip + (sbyte)to);

            instr = $"{name} {to}";
            meta = $"{_state.cs:X4}:{newAddress:X4} -> {SegmentAddr(_state.cs, newAddress)}";
        }
        else if (opcode == 0xd7)
        {
            instr = "XLATB";
        }
        else if (opcode == 0xe0 || opcode == 0xe1 || opcode == 0xe2)
        {
            // LOOP
            byte to = GetByte(ref instr_len, ref bytes);
            string name = "?";
            ushort newAddresses = (ushort)(_state.ip + (sbyte)to);

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
            byte @from = GetByte(ref instr_len, ref bytes);
            instr = $"IN AL,${from:X2}";
        }
        else if (opcode == 0xe5)
        {
            byte @from = GetByte(ref instr_len, ref bytes);
            instr = $"IN AX,${from:X2}";
        }
        else if (opcode == 0xe6)
        {
            byte @to = GetByte(ref instr_len, ref bytes);
            instr = $"OUT ${to:X2},AL";
        }
        else if (opcode == 0xe7)
        {
            byte @to = GetByte(ref instr_len, ref bytes);
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
            sbyte to = (sbyte)GetByte(ref instr_len, ref bytes);
            instr = $"JP ${to:X2}";
            meta = $"{_state.cs * 16 + _state.ip + to:X6} {_state.cs:X04}:{_state.ip:X04}";
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
            byte o1 = GetByte(ref instr_len, ref bytes);
            int mod = o1 >> 6;
            int reg = o1 & 7;
            int function = (o1 >> 3) & 7;

            (ushort v, string name, bool a_valid, ushort seg, ushort addr, meta) = GetRegisterMem(reg, mod, word, ref instr_len, ref bytes);

            meta += $"function {function} ";

            if (function == 0)
                instr = $"INC {name}";
            else if (function == 1)
                instr = $"DEC {name}";
            else if (function == 2)
            {
                instr = $"CALL {name}";
                meta += $"${v:X4} -> {SegmentAddr(_state.cs, _state.ip)}";
            }
            else if (function == 3)
            {
                ushort temp_cs = ReadMemWord(seg, (ushort)(addr + 2));
                instr = $"CALL {name}";
                meta += $"{v:X4} -> {SegmentAddr(temp_cs, v)})";
            }
            else if (function == 4)
            {
                instr = $"JMP {name}";
                meta += $"{_state.cs * 16 + v:X6}";
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
