namespace DotXT;

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

    private Dictionary<int, int> _scheduled_interrupts = new Dictionary<int, int>();

    private bool _rep;
    private RepMode _rep_mode;
    private ushort _rep_addr;
    private byte _rep_opcode;

    private bool _intercept_int_flag;

    private bool _is_test;

    private bool _terminate_on_hlt;

    private readonly List<byte> floppy = new();

    private string tty_output = "";

    private int clock;

    public P8086(ref Bus b, string test, bool is_floppy, uint load_test_at, bool intercept_int_flag, bool terminate_on_hlt, ref List<Device> devices)
    {
        _b = b;

        _io = new IO(b, ref devices);

        // intercept also other ints besides keyboard/console access
        _intercept_int_flag = intercept_int_flag;

        if (_intercept_int_flag)
            Console.WriteLine("Intercept IRQ enabled");

        _terminate_on_hlt = terminate_on_hlt;

        if (test != "" && is_floppy == false)
        {
            _is_test = true;

            _cs = 0;
            _ip = 0x0800;

            uint addr = load_test_at == 0xffffffff ? 0 : load_test_at;

            Log.DoLog($"Load {test} at {addr:X6}");

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
        else if (test != "" && is_floppy == true)
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
        else
        {
            _cs = 0xf000;
            _ip = 0xfff0;
        }

        // bit 1 of the flags register is always 1
        // https://www.righto.com/2023/02/silicon-reverse-engineering-intel-8086.html
        _flags |= 2;
    }

    public void set_ip(ushort cs, ushort ip)
    {
        Log.DoLog($"Set CS/IP to {cs:X4}:{ip:X4}");

        _cs = cs;
        _ip = ip;
    }

    private bool InterceptInt(int nr)  // TODO rename
    {
        if (!_intercept_int_flag)
            return false;

        Log.DoLog($"INT {nr:X2} {_ah:X2}");

        if (nr == 0x10)
        {
            if (_ah == 0x0e)
            {
                // teletype output
                SetFlagC(false);

                Console.Write((char)_al);

                if (_al == 13 || _al == 10)
                {
                    Log.DoLog($"CONSOLE-STR: {tty_output}");

                    tty_output = "";
                }
                else
                {
                    Log.DoLog($"CONSOLE-CHR: {(char)_al}");
                }

                tty_output += (char)_al;

                if (_al == 13)
                    Console.WriteLine("");

                return true;
            }

#if DEBUG
            Console.WriteLine($"INT NR {nr:X2}, AH: {_ah:X2}");
#endif

            return false;
        }
        else if (nr == 0x16)
        {
            // keyboard access
#if DEBUG
            Console.WriteLine($"INT NR {nr:X2}, AH: {_ah:X2}");
#endif

            if (_ah == 0x00)
            {
                // Get keystroke
                ConsoleKeyInfo cki = Console.ReadKey(true);

// FIXME                _ah = 0x3b;  // F1 scan code
                _al = (byte)cki.KeyChar;  // F1 ascii char

                SetFlagC(false);
                return true;
            }
        }
        else if (nr == 0x29)
        {
            // fast console output
            SetFlagC(false);

            Console.Write((char)_al);

            return true;
        }

        if (nr == 0x12)
        {
            // return conventional memory size
            SetAX(640); // 640kB
            SetFlagC(false);
            return true;
        }
        else if (nr == 0x13 && floppy.Count > 0)
        {
#if DEBUG
            Console.WriteLine($"INT NR {nr:X2}, AH: {_ah:X2}");
#endif

            if (_ah == 0x00)
            {
                // reset disk system
                Log.DoLog("INT $13: reset disk system");

                SetFlagC(false);
                _ah = 0x00;  // no error

                _scheduled_interrupts[0x0e] = 50;

                return true;
            }
            else if (_ah == 0x02)
            {
                // read sector
                ushort bytes_per_sector = 512;
                byte sectors_per_track = 9;
                byte n_sides = 2;
                byte tracks_per_side = (byte)(floppy.Count / (bytes_per_sector * n_sides * sectors_per_track));

                int disk_offset = (_ch * n_sides + _dh) * sectors_per_track * bytes_per_sector + (_cl - 1) * bytes_per_sector;

                ushort _bx = GetBX();

                string base_str = $"INT $13, read sector(s): {_al} sectors, track {_ch}/{tracks_per_side}, sector {_cl}, head {_dh}, drive {_dl}, offset {disk_offset}/{floppy.Count} to ${_es:X4}:{_bx:X4}";

                if (disk_offset + bytes_per_sector <= floppy.Count)
                {
                    Log.DoLog(base_str);

                //    string s = "";

                    for(int i=0; i<bytes_per_sector * _al; i++)
                    {
                        WriteMemByte(_es, (ushort)(_bx + i), floppy[disk_offset + i]);
                //        s += $" {floppy[disk_offset + i]:X2}";
                    }
                //    Log.DoLog($"SECTOR: {s}");

                    SetFlagC(false);
                    _ah = 0x00;  // no error

                    return true;
                }

                Log.DoLog(base_str + " FAILED");
            }
            else if (_ah == 0x41)
            {
                // Check extensions present

                SetFlagC(true);
                _ah = 0x01;  // invalid command

                return true;
            }
        }
        else if (nr == 0x19)
        {
            // reboot (to bootloader)
            Log.DoLog("INT 19, Reboot");
            Console.WriteLine("REBOOT");
            System.Environment.Exit(1);
        }
        else if (nr == 0x2f)
        {
#if DEBUG
            Console.WriteLine($"INT NR {nr:X2}, AX: {_ah:X2}{_al:X2}");
#endif

            if (_ah == 0x0d)
            {
                // disk reset
                SetFlagC(false);
                _ah = 0x00;  // no error
                return true;
            }
        }
        else
        {
#if DEBUG
            Console.WriteLine($"INT NR {nr:X2}, AH: {_ah:X2}");
#endif
        }

        return false;
    }

    private void FixFlags()
    {
            _flags &= 0b0000111111010101;
            _flags |= 2;
    }

    private byte GetPcByte()
    {
        uint address = (uint)(_cs * 16 + _ip++) & MemMask;

        byte val = _b.ReadByte(address);

        // Log.DoLog($"{address:X} {val:X}");

        return val;
    }

    private ushort GetPcWord()
    {
        ushort v = GetPcByte();

        v |= (ushort)(GetPcByte() << 8);

        return v;
    }

    private ushort GetAX()
    {
        return (ushort)((_ah << 8) | _al);
    }

    private void SetAX(ushort v)
    {
        _ah = (byte)(v >> 8);
        _al = (byte)v;
    }

    private ushort GetBX()
    {
        return (ushort)((_bh << 8) | _bl);
    }

    private void SetBX(ushort v)
    {
        _bh = (byte)(v >> 8);
        _bl = (byte)v;
    }

    private ushort GetCX()
    {
        return (ushort)((_ch << 8) | _cl);
    }

    private void SetCX(ushort v)
    {
        _ch = (byte)(v >> 8);
        _cl = (byte)v;
    }

    private ushort GetDX()
    {
        return (ushort)((_dh << 8) | _dl);
    }

    private void SetDX(ushort v)
    {
        _dh = (byte)(v >> 8);
        _dl = (byte)v;
    }

    private void WriteMemByte(ushort segment, ushort offset, byte v)
    {
        uint a = (uint)(((segment << 4) + offset) & MemMask);

        // Log.DoLog($"WriteMemByte {segment:X4}:{offset:X4}: a:{a:X6}, v:{v:X2}");

       _b.WriteByte(a, v);
    }

    private void WriteMemWord(ushort segment, ushort offset, ushort v)
    {
        uint a1 = (uint)(((segment << 4) + offset) & MemMask);
        uint a2 = (uint)(((segment << 4) + ((offset + 1) & 0xffff)) & MemMask);

#if DEBUG
        Log.DoLog($"WriteMemWord {segment:X4}:{offset:X4}: a1:{a1:X6}/a2:{a2:X6}, v:{v:X4}");
#endif

       _b.WriteByte(a1, (byte)v);
       _b.WriteByte(a2, (byte)(v >> 8));
    }

    public byte ReadMemByte(ushort segment, ushort offset)
    {
        uint a = (uint)(((segment << 4) + offset) & MemMask);

        // Log.DoLog($"ReadMemByte {segment:X4}:{offset:X4}: {a:X6}");

        return _b.ReadByte(a);
    } 

    public ushort ReadMemWord(ushort segment, ushort offset)
    {
        uint a1 = (uint)(((segment << 4) + offset) & MemMask);
        uint a2 = (uint)(((segment << 4) + ((offset + 1) & 0xffff)) & MemMask);

        ushort v = (ushort)(_b.ReadByte(a1) | (_b.ReadByte(a2) << 8));

#if DEBUG
        Log.DoLog($"ReadMemWord {segment:X4}:{offset:X4}: {a1:X6}/{a2:X6}, value: {v:X4}");
#endif

        return v;
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

        Log.DoLog($"reg {reg} w {w} not supported for {nameof(GetRegister)}");

        return (0, "error");
    }

    private (ushort, string) GetSRegister(int reg)
    {
        if (reg == 0b000)
            return (_es, "ES");
        if (reg == 0b001)
            return (_cs, "CS");
        if (reg == 0b010)
            return (_ss, "SS");
        if (reg == 0b011)
            return (_ds, "DS");

        Log.DoLog($"reg {reg} not supported for {nameof(GetSRegister)}");

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
            Log.DoLog($"{nameof(GetDoubleRegisterMod00)} {reg} not implemented");
        }

        return (a, name, cycles);
    }

    // value, name, cycles
    private (ushort, string, int) GetDoubleRegisterMod01_02(int reg, bool word)
    {
        ushort a = 0;
        string name = "error";
        int cycles = 0;

        if (reg == 6)
        {
            a = _bp;
            name = "[BP]";
            cycles = 5;
        }
        else
        {
            (a, name, cycles) = GetDoubleRegisterMod00(reg);
        }

        short disp = word ? (short)GetPcWord() : (sbyte)GetPcByte();

        return ((ushort)(a + disp), name + $" disp {disp:X4}", cycles);
    }

    // value, name_of_source, segment_a_valid, segment/, address of value, number of cycles
    private (ushort, string, bool, ushort, ushort, int) GetRegisterMem(int reg, int mod, bool w)
    {
        if (mod == 0)
        {
            (ushort a, string name, int cycles) = GetDoubleRegisterMod00(reg);

            ushort segment = _segment_override_set ? _segment_override : _ds;

            if (_segment_override_set == false && (reg == 2 || reg == 3))  // BP uses SS
                segment = _ss;

            ushort v = w ? ReadMemWord(segment, a) : ReadMemByte(segment, a);

            cycles += 6;

            name += $" ({_segment_override_name}:${segment * 16 + a:X6} -> {v:X4})";

            return (v, name, true, segment, a, cycles);
        }

        if (mod == 1 || mod == 2)
        {
            bool word = mod == 2;

            (ushort a, string name, int cycles) = GetDoubleRegisterMod01_02(reg, word);

            ushort segment = _segment_override_set ? _segment_override : _ds;

            if (_segment_override_set == false && (reg == 2 || reg == 3))  // BP uses SS
                segment = _ss;

            ushort v = w ? ReadMemWord(segment, a) : ReadMemByte(segment, a);

            cycles += 6;

            name += $" ({_segment_override_name}:${segment * 16 + a:X6} -> {v:X4})";

            return (v, name, true, segment, a, cycles);
        }

        if (mod == 3)
        {
            (ushort v, string name) = GetRegister(reg, w);

            return (v, name, false, 0, 0, 0);
        }

        Log.DoLog($"reg {reg} mod {mod} w {w} not supported for {nameof(GetRegisterMem)}");

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

        Log.DoLog($"reg {reg} w {w} not supported for {nameof(PutRegister)} ({val:X})");

        return "error";
    }

    private string PutSRegister(int reg, ushort v)
    {
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

        Log.DoLog($"reg {reg} not supported for {nameof(PutSRegister)}");

        return "error";
    }

    // name, cycles
    private (string, int) PutRegisterMem(int reg, int mod, bool w, ushort val)
    {
        Log.DoLog($"PutRegisterMem {mod},{w}");

        if (mod == 0)
        {
            (ushort a, string name, int cycles) = GetDoubleRegisterMod00(reg);

            ushort segment = _segment_override_set ? _segment_override : _ds;

            if (_segment_override_set == false && (reg == 2 || reg == 3))  // BP uses SS
                segment = _ss;

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
            (ushort a, string name, int cycles) = GetDoubleRegisterMod01_02(reg, mod == 2);

            ushort segment = _segment_override_set ? _segment_override : _ds;

            if (_segment_override_set == false && (reg == 2 || reg == 3))  // BP uses SS
                segment = _ss;

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

        Log.DoLog($"reg {reg} mod {mod} w {w} value {val} not supported for {nameof(PutRegisterMem)}");

        return ("error", 0);
    }

    (string, int) UpdateRegisterMem(int reg, int mod, bool a_valid, ushort seg, ushort addr, bool word, ushort v)
    {
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
        Log.DoLog($"word {word}, r1 {r1}, r2 {r2}, result {result:X}, issub {issub}");
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
        SetFlagA(false);
        SetFlagP((byte)result);
    }

    public void push(ushort v)
    {
        _sp -= 2;

        // Log.DoLog($"push({v:X4}) write @ {_ss:X4}:{_sp:X4}");

        WriteMemWord(_ss, _sp, v);
    }

    public ushort pop()
    {
        ushort v = ReadMemWord(_ss, _sp);

        // Log.DoLog($"pop({v:X4}) read @ {_ss:X4}:{_sp:X4}");

        _sp += 2;

        return v;
    }

    void InvokeInterrupt(ushort instr_start, int interrupt_nr)
    {
        _segment_override_set = false;
        _segment_override_name = "";
        _rep = false;

        push(_flags);
        push(_cs);
        push(instr_start);

        uint addr = (uint)(interrupt_nr * 4);

        _ip = (ushort)(_b.ReadByte(addr + 0) + (_b.ReadByte(addr + 1) << 8));
        _cs = (ushort)(_b.ReadByte(addr + 2) + (_b.ReadByte(addr + 3) << 8));

#if DEBUG
        Log.DoLog($"----- ------ INT {interrupt_nr:X2} (int offset: {addr:X4}, addr: {_cs:X4}:{_ip:X4})");
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

    // cycle counts from https://zsmith.co/intel_i.php
    public bool Tick()
    {
        bool rc = true;

        int cycle_count = 0;  // cycles used for an instruction

        // check for interrupt
        if (GetFlagI() == true)
        {
            int enabled_interrupts = _io.GetCachedValue(0x0021) ^ 255;  // the xor is because they're inverted in the register

            foreach (var pair in _scheduled_interrupts)
            {
                // Log.DoLog($"Checking interrupt {pair.Key} ({pair.Value})");

                if (pair.Key >= 8 && pair.Key < 16)
                {
                    if ((enabled_interrupts & (1 << (pair.Key - 8))) == 0)
                        continue;
                }

                int new_count = _scheduled_interrupts[pair.Key] = pair.Value - 1;

                if (new_count == 0)
                {
                    InvokeInterrupt(_ip, pair.Key);

                    _scheduled_interrupts.Remove(pair.Key);

                    cycle_count += 4;  // TODO: guess (1 bus cycle)

                    break;
                }

                //Debug.Assert(new_count > 0);
            }
        }

#if DEBUG
        string flagStr = GetFlagsAsString();
#endif

        ushort instr_start = _ip;
        uint address = (uint)(_cs * 16 + _ip) & MemMask;
        byte opcode = GetPcByte();

        // ^ address must increase!
        if (_rep)
            opcode = _rep_opcode;  // TODO: redundant assignment? _ip should've pointed already at 'this' instruction

        // handle prefixes
        while (opcode is (0x26 or 0x2e or 0x36 or 0x3e or 0xf2 or 0xf3))
        {
            _rep_addr = _ip;

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
                cycle_count += 2;
                Log.DoLog($"set _rep_addr to {_rep_addr:X4}");
            }
            else
            {
                Log.DoLog($"------ {address:X6} prefix {opcode:X2} not implemented");
            }

            address = (uint)(_cs * 16 + _ip) & MemMask;
            byte next_opcode = GetPcByte();

            _rep_opcode = next_opcode;  // TODO: only allow for certain instructions

            if (opcode == 0xf2)
            {
                _rep_mode = RepMode.REPNZ;

                Log.DoLog($"REPNZ: {_cs:X4}:{_rep_addr:X4}");
            }
            else if (opcode == 0xf3)
            {
                if (next_opcode is (0xa6 or 0xa7 or 0xae or 0xaf))
                {
                    _rep_mode = RepMode.REPE_Z;
                    Log.DoLog($"REPZ: {_cs:X4}:{_rep_addr:X4}");
                }
                else
                {
                    _rep_mode = RepMode.REP;
                    Log.DoLog($"REP: {_cs:X4}:{_rep_addr:X4}");
                }
            }
            else
            {
                _segment_override_set = true;  // TODO: move up
                cycle_count += 2;
            }

            if (_segment_override_set)
                Log.DoLog($"segment override to {_segment_override_name}: {_ds:X4}, opcode(s): {opcode:X2} {HexDump(address, false):X2}");

            opcode = next_opcode;
        }

#if DEBUG
//        string mem = HexDump(address, false);
//        string stk = HexDump((uint)(_ss * 16 + _sp), true);

//        Log.DoLog($"{address:X6}: {mem}");
//        Log.DoLog($"{_ss * 16 + _sp:X6}: {stk}");

//        Log.DoLog($"repstate: {_rep} {_rep_mode} {_rep_addr:X4} {_rep_opcode:X2}");

        string prefixStr =
            $"{flagStr} {address:X6} {opcode:X2} AX:{_ah:X2}{_al:X2} BX:{_bh:X2}{_bl:X2} CX:{_ch:X2}{_cl:X2} DX:{_dh:X2}{_dl:X2} SP:{_sp:X4} BP:{_bp:X4} SI:{_si:X4} DI:{_di:X4} flags:{_flags:X4}, ES:{_es:X4}, CS:{_cs:X4}, SS:{_ss:X4}, DS:{_ds:X4} IP:{instr_start:X4} | ";
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
            Log.DoLog($"{prefixStr} {name} AL,${v:X2}");
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
            Log.DoLog($"{prefixStr} {name} AX,${v:X4}");
#endif
        }
        else if (opcode == 0x06)
        {
            // PUSH ES
            push(_es);

            cycle_count += 11;  // 15

#if DEBUG
            Log.DoLog($"{prefixStr} PUSH ES");
#endif
        }
        else if (opcode == 0x07)
        {
            // POP ES
            _es = pop();

            cycle_count += 8;

#if DEBUG
            Log.DoLog($"{prefixStr} POP ES");
#endif
        }
        else if (opcode == 0x0e)
        {
            // PUSH CS
            push(_cs);

            cycle_count += 11;  // 15

#if DEBUG
            Log.DoLog($"{prefixStr} PUSH CS");
#endif
        }
        else if (opcode == 0x16)
        {
            // PUSH SS
            push(_ss);

            cycle_count += 11;  // 15

#if DEBUG
            Log.DoLog($"{prefixStr} PUSH SS");
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
            Log.DoLog($"{prefixStr} SBB ${v:X4}");
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
            Log.DoLog($"{prefixStr} SBB ${v:X4}");
#endif
        }
        else if (opcode == 0x1e)
        {
            // PUSH DS
            push(_ds);

            cycle_count += 11;  // 15

#if DEBUG
            Log.DoLog($"{prefixStr} PUSH DS");
#endif
        }
        else if (opcode == 0x1f)
        {
            // POP DS
            _ds = pop();

            cycle_count += 8;

#if DEBUG
            Log.DoLog($"{prefixStr} POP DS");
#endif
        }
        else if (opcode == 0x27)
        {
            // DAA
            // from https://stackoverflow.com/questions/8119577/z80-daa-instruction/8119836
            int t = 0;

            t += GetFlagA() || (_al & 0x0f) > 9 ? 1 : 0;

            if (GetFlagC() || _al > 0x99)
            {
                t += 2;
                SetFlagC(true);
            }

            if (GetFlagS() && !GetFlagA())
                SetFlagA(false);
            else
            {
                if (GetFlagS() && GetFlagA())
                    SetFlagA((_al & 0x0F) < 6);
                else
                    SetFlagA((_al & 0x0F) >= 0x0A);
            }

            bool n = GetFlagS();
    
            if (t == 1)
                _al += (byte)(n ? 0xFA:0x06); // -6:6
            else if (t == 2)
                _al += (byte)(n ? 0xA0:0x60); // -0x60:0x60
            else if (t == 3)
                _al += (byte)(n ? 0x9A:0x66); // -0x66:0x66

            SetFlagS((_al & 0x80) == 0x80);
            SetFlagZ(_al != 0);
            SetFlagP(_al);

            cycle_count += 4;

#if DEBUG
            Log.DoLog($"{prefixStr} DAA");
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
            Log.DoLog($"{prefixStr} SUB ${v:X2}");
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
            Log.DoLog($"{prefixStr} SUB ${v:X4}");
#endif
        }
        else if (opcode == 0x58)
        {
            // POP AX
            SetAX(pop());

            cycle_count += 8;

#if DEBUG
            Log.DoLog($"{prefixStr} POP AX");
#endif
        }
        else if (opcode == 0x59)
        {
            // POP CX
            SetCX(pop());

            cycle_count += 8;

#if DEBUG
            Log.DoLog($"{prefixStr} POP CX");
#endif
        }
        else if (opcode == 0x5a)
        {
            // POP DX
            SetDX(pop());

            cycle_count += 8;

#if DEBUG
            Log.DoLog($"{prefixStr} POP DX");
#endif
        }
        else if (opcode == 0x5b)
        {
            // POP BX
            SetBX(pop());

            cycle_count += 8;

#if DEBUG
            Log.DoLog($"{prefixStr} POP BX");
#endif
        }
        else if (opcode == 0x5c)
        {
            // POP SP
            _sp = pop();

            cycle_count += 8;

#if DEBUG
            Log.DoLog($"{prefixStr} POP SP");
#endif
        }
        else if (opcode == 0x5d)
        {
            // POP BP
            _bp = pop();

            cycle_count += 8;

#if DEBUG
            Log.DoLog($"{prefixStr} POP BP");
#endif
        }
        else if (opcode == 0x5e)
        {
            // POP SI
            _si = pop();

            cycle_count += 8;

#if DEBUG
            Log.DoLog($"{prefixStr} POP SI");
#endif
        }
        else if (opcode == 0x5f)
        {
            // POP DI
            _di = pop();

            cycle_count += 8;

#if DEBUG
            Log.DoLog($"{prefixStr} POP DI");
#endif
        }
        else if (opcode == 0xa4)
        {
            // MOVSB
            ushort segment = _segment_override_set ? _segment_override : _ds;
            byte v = ReadMemByte(segment, _si);
            WriteMemByte(_es, _di, v);

#if DEBUG
            Log.DoLog($"{prefixStr} MOVSB ({v:X2} / {(v > 32 && v < 127 ? (char)v : ' ')}, {_rep}) {_segment_override_name} {segment * 16 + _si:X6} -> {_es * 16 + _di:X6}");
#endif

            if (GetFlagD())
            {
                _si--;
                _di--;
            }
            else
            {
                _si++;
                _di++;
            }

            cycle_count += 18;
        }
        else if (opcode == 0xa5)
        {
            // MOVSW
            WriteMemWord(_es, _di, ReadMemWord(_segment_override_set ? _segment_override : _ds, _si));

            if (GetFlagD())
            {
                _si -= 2;
                _di -= 2;
            }
            else
            {
                _si += 2;
                _di += 2;
            }

            cycle_count += 18;

#if DEBUG
            Log.DoLog($"{prefixStr} MOVSW");
#endif
        }
        else if (opcode == 0xa6)
        {
            // CMPSB
            byte v1 = ReadMemByte(_segment_override_set ? _segment_override : _ds, _si);
            byte v2 = ReadMemByte(_es, _di);

            int result = v1 - v2;

#if DEBUG
            if (result != 0)
            {
                string s1 = "";
                for(int i=0; i<11; i++)
                    s1 += (char)ReadMemByte(_ds, (ushort)(_si + i));

                string s2 = "";
                for(int i=0; i<11; i++)
                    s2 += (char)ReadMemByte(_es, (ushort)(_di + i));

                Log.DoLog($"{s1}/{s2}");
            }
#endif

            if (GetFlagD())
            {
                _si--;
                _di--;
            }
            else
            {
                _si++;
                _di++;
            }

            SetAddSubFlags(false, v1, v2, result, true, false);

            cycle_count += 22;

#if DEBUG
            Log.DoLog($"{prefixStr} CMPSB ({v1:X2}/{(v1 > 32 && v1 < 127 ? (char)v1 : ' ')}, {v2:X2}/{(v2 > 32 && v2 < 127 ? (char)v2 : ' ')})");
#endif
        }
        else if (opcode == 0xa7)
        {
            // CMPSW
            ushort v1 = ReadMemWord(_segment_override_set ? _segment_override : _ds, _si);
            ushort v2 = ReadMemWord(_es, _di);

            int result = v1 - v2;

            if (GetFlagD())
            {
                _si -= 2;
                _di -= 2;
            }
            else
            {
                _si += 2;
                _di += 2;
            }

            SetAddSubFlags(true, v1, v2, result, true, false);

            cycle_count += 22;

#if DEBUG
            Log.DoLog($"{prefixStr} CMPSW (${v1:X4},${v2:X4})");
#endif
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
            Log.DoLog($"{prefixStr} JCXZ {addr:X}");
#endif
        }
        else if (opcode == 0xe9)
        {
            // JMP np
            short offset = (short)GetPcWord();

            _ip = (ushort)(_ip + offset);

            cycle_count += 15;

#if DEBUG
            Log.DoLog($"{prefixStr} JMP {_ip:X} ({offset:X4})");
#endif
        }
        else if (opcode == 0x50)
        {
            // PUSH AX
            push(GetAX());

            cycle_count += 11;  // 15

#if DEBUG
            Log.DoLog($"{prefixStr} PUSH AX");
#endif
        }
        else if (opcode == 0x51)
        {
            // PUSH CX
            push(GetCX());

            cycle_count += 11;  // 15

#if DEBUG
            Log.DoLog($"{prefixStr} PUSH CX");
#endif
        }
        else if (opcode == 0x52)
        {
            // PUSH DX
            push(GetDX());

            cycle_count += 11;  // 15

#if DEBUG
            Log.DoLog($"{prefixStr} PUSH DX");
#endif
        }
        else if (opcode == 0x53)
        {
            // PUSH BX
            push(GetBX());

            cycle_count += 11;  // 15

#if DEBUG
            Log.DoLog($"{prefixStr} PUSH BX");
#endif
        }
        else if (opcode == 0x54)
        {
            // PUSH SP
            push(_sp);

            cycle_count += 11;  // 15

#if DEBUG
            Log.DoLog($"{prefixStr} PUSH SP");
#endif
        }
        else if (opcode == 0x55)
        {
            // PUSH BP
            push(_bp);

            cycle_count += 11;  // 15

#if DEBUG
            Log.DoLog($"{prefixStr} PUSH BP");
#endif
        }
        else if (opcode == 0x56)
        {
            // PUSH SI
            push(_si);

            cycle_count += 11;  // 15

#if DEBUG
            Log.DoLog($"{prefixStr} PUSH SI");
#endif
        }
        else if (opcode == 0x57)
        {
            // PUSH DI
            push(_di);

            cycle_count += 11;  // 15

#if DEBUG
            Log.DoLog($"{prefixStr} PUSH DI");
#endif
        }
        else if (opcode is (0x80 or 0x81 or 0x83))
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
                Log.DoLog($"{prefixStr} opcode {opcode:X2} not implemented");
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
                Log.DoLog($"{prefixStr} opcode {opcode:X2} function {function} not implemented");
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
            Log.DoLog($"{prefixStr} {iname} {name1},${r2:X2}");
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
            Log.DoLog($"{prefixStr} TEST {name1},{name2}");
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
            Log.DoLog($"{prefixStr} XCHG {name1},{name2}");
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
            Log.DoLog($"{prefixStr} POP {toName}");
#endif
        }
        else if (opcode == 0x90)
        {
            // NOP

            cycle_count += 3;

#if DEBUG
            Log.DoLog($"{prefixStr} NOP");
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
            Log.DoLog($"{prefixStr} XCHG AX,{name_other}");
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
            Log.DoLog($"{prefixStr} CBW");
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
            Log.DoLog($"{prefixStr} CDW");
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
            Log.DoLog($"{prefixStr} CALL ${_cs:X} ${_ip:X}: ${_cs * 16 + _ip:X}");
#endif
        }
        else if (opcode == 0x9c)
        {
            // PUSHF
            push(_flags);

            cycle_count += 10;  // 14

#if DEBUG
            Log.DoLog($"{prefixStr} PUSHF");
#endif
        }
        else if (opcode == 0x9d)
        {
            // POPF
            _flags = pop();

            cycle_count += 8;  // 12

            FixFlags();

#if DEBUG
            Log.DoLog($"{prefixStr} POPF");
#endif
        }
        else if (opcode == 0xac)
        {
            // LODSB
            _al = ReadMemByte(_segment_override_set ? _segment_override : _ds, _si);

            if (GetFlagD())
                _si--;
            else
                _si++;

            cycle_count += 5;

#if DEBUG
            Log.DoLog($"{prefixStr} LODSB");
#endif
        }
        else if (opcode == 0xad)
        {
            // LODSW
            SetAX(ReadMemWord(_segment_override_set ? _segment_override : _ds, _si));

            if (GetFlagD())
                _si -= 2;
            else
                _si += 2;

            cycle_count += 5;

#if DEBUG
            Log.DoLog($"{prefixStr} LODSW");
#endif
        }
        else if (opcode == 0xc2)
        {
            ushort nToRelease = GetPcWord();

            // RET
            _ip = pop();

            _sp += nToRelease;

            cycle_count += 16;

#if DEBUG
            Log.DoLog($"{prefixStr} RET ${nToRelease:X4}");
#endif
        }
        else if (opcode == 0xc3)
        {
            // RET
            _ip = pop();

            cycle_count += 16;

#if DEBUG
            Log.DoLog($"{prefixStr} RET");
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
            Log.DoLog($"{prefixStr} {name} {affected},{name_from}");
#endif
        }
        else if (opcode == 0xcd)
        {
            // INT 0x..
            byte @int = GetPcByte();

            uint addr = (uint)(@int * 4);

            if (InterceptInt(@int) == false)
            {
                push(_flags);
                push(_cs);
                push(_ip);

                _ip = (ushort)(_b.ReadByte(addr + 0) + (_b.ReadByte(addr + 1) << 8));
                _cs = (ushort)(_b.ReadByte(addr + 2) + (_b.ReadByte(addr + 3) << 8));
            }

            cycle_count += 51;  // 71

#if DEBUG
            Log.DoLog($"{prefixStr} INT {@int:X2} -> ${_cs * 16 + _ip:X6} (from {addr:X4})");
#endif
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
            Log.DoLog($"{prefixStr} IRET");
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
                    Log.DoLog($"{prefixStr} {name} {name2},{name1}");
#endif
                }
                else
                {
                    (string dummy, int put_cycles) = UpdateRegisterMem(reg2, mod, a_valid, seg, addr, word, (ushort)result);

                    cycle_count += put_cycles;

#if DEBUG
                    Log.DoLog($"{prefixStr} {name} {name1},{name2}");
#endif
                }
            }
            else
            {
#if DEBUG
                if (direction)
                    Log.DoLog($"{prefixStr} {name} {name2},{name1}");
                else
                    Log.DoLog($"{prefixStr} {name} {name1},{name2}");
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
                Log.DoLog($"{prefixStr} CMP AX,#${r2:X4}");
#endif
            }
            else if (opcode == 0x3c)
            {
                r1 = _al;
                r2 = GetPcByte();

                result = r1 - r2;

#if DEBUG
                Log.DoLog($"{prefixStr} CMP AL,#${r2:X2}");
#endif
            }
            else
            {
                Log.DoLog($"{prefixStr} opcode {opcode:X2} not implemented");
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
                SetFlagC(false);
            }
            else if (function == 3)
            {
                result = (ushort)(r2 ^ r1);
                name = "XOR";
            }
            else
            {
                Log.DoLog($"{prefixStr} opcode {opcode:X2} function {function} not implemented");
            }

            SetLogicFuncFlags(word, result);

            if (direction)
            {
                string affected = PutRegister(reg1, word, result);

#if DEBUG
                Log.DoLog($"{prefixStr} {name} {name1},{name2}");
#endif
            }
            else
            {
                (string affected, int put_cycles) = UpdateRegisterMem(reg2, mod, a_valid, seg, addr, word, result);

                cycle_count += put_cycles;

#if DEBUG
                Log.DoLog($"{prefixStr} {name} {name2},{name1}");
#endif
            }
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
                Log.DoLog($"{prefixStr} opcode {opcode:X2} function {function} not implemented");
            }

            SetLogicFuncFlags(word, word ? GetAX() : _al);

            SetFlagP(_al);

            cycle_count += 4;

#if DEBUG
            Log.DoLog($"{prefixStr} {name} {tgt_name},${bHigh:X2}{bLow:X2}");
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
            Log.DoLog($"{prefixStr} CALL {a:X4} (${_ip:X4} -> ${_cs * 16 + _ip:X6})");
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
            Log.DoLog($"{prefixStr} JMP ${_cs:X} ${_ip:X}: ${_cs * 16 + _ip:X}");
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

            if (function == 0)
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
            else if (function == 6)
            {
                // DIV
                if (word) {
                    uint dx_ax = (uint)((GetDX() << 16) | GetAX());

                    if (r1 == 0 || dx_ax / r1 >= 0x10000)
                        InvokeInterrupt(instr_start, r1 == 0 ? 0x00 : 0x10);  // divide by zero or divisor too small
                    else
                    {
                        SetAX((ushort)(dx_ax / r1));
                        SetDX((ushort)(dx_ax % r1));
                    }
                }
                else {
                    ushort ax = GetAX();

                    if (r1 == 0 || ax / r1 > 0x100)
                        InvokeInterrupt(instr_start, r1 == 0 ? 0x00 : 0x10);  // divide by zero or divisor too small
                    else
                    {
                        _al = (byte)(ax / r1);
                        _ah = (byte)(ax % r1);
                    }
                }

                cmd_name = "DIV";
            }
            else
            {
                Log.DoLog($"{prefixStr} opcode {opcode:X2} o1 {o1:X2} function {function} not implemented");
            }

            cycle_count += 4;

#if DEBUG
            Log.DoLog($"{prefixStr} {cmd_name} {name1}{name2}");
#endif
        }
        else if (opcode == 0xfa)
        {
            // CLI
            SetFlagI(false); // IF

            cycle_count += 2;

#if DEBUG
            Log.DoLog($"{prefixStr} CLI");
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
            Log.DoLog($"{prefixStr} MOV {name},${v:X}");
#endif
        }
        else if (opcode == 0xa0)
        {
            // MOV AL,[...]
            ushort a = GetPcWord();

            _al = ReadMemByte(_segment_override_set ? _segment_override : _ds, a);

            cycle_count += 2;

#if DEBUG
            Log.DoLog($"{prefixStr} MOV AL,[${a:X4}]");
#endif
        }
        else if (opcode == 0xa1)
        {
            // MOV AX,[...]
            ushort a = GetPcWord();

            SetAX(ReadMemWord(_segment_override_set ? _segment_override : _ds, a));

            cycle_count += 2;

#if DEBUG
            Log.DoLog($"{prefixStr} MOV AX,[${a:X4}]");
#endif
        }
        else if (opcode == 0xa2)
        {
            // MOV [...],AL
            ushort a = GetPcWord();

            WriteMemByte(_segment_override_set ? _segment_override : _ds, a, _al);

            cycle_count += 2;

#if DEBUG
            Log.DoLog($"{prefixStr} MOV [${a:X4}],AL");
#endif
        }
        else if (opcode == 0xa3)
        {
            // MOV [...],AX
            ushort a = GetPcWord();

            WriteMemWord(_segment_override_set ? _segment_override : _ds, a, GetAX());

            cycle_count += 2;

#if DEBUG
            Log.DoLog($"{prefixStr} MOV [${a:X4}],AX");
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
            Log.DoLog($"{prefixStr} TEST AL,${v:X2}");
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
            Log.DoLog($"{prefixStr} TEST AX,${v:X4}");
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

            // Log.DoLog($"{opcode:X}|{o1:X} mode {mode}, reg {reg}, rm {rm}, dir {dir}, word {word}, sreg {sreg}");

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
                Log.DoLog($"{prefixStr} MOV {toName},{fromName}");
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
                Log.DoLog($"{prefixStr} MOV {toName},{fromName} ({v:X4})");
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
            Log.DoLog($"{prefixStr} LEA {name_to},{name_from}");
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
            Log.DoLog($"{prefixStr} SAHF (set to {GetFlagsAsString()})");
#endif
        }
        else if (opcode == 0x9f)
        {
            // LAHF
            _ah = (byte)_flags;

            cycle_count += 2;

#if DEBUG
            Log.DoLog($"{prefixStr} LAHF");
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
                Log.DoLog($"{prefixStr} DEC {name}");
            else
                Log.DoLog($"{prefixStr} INC {name}");
#endif
        }
        else if (opcode == 0xaa)
        {
            // STOSB
            WriteMemByte(_es, _di, _al);

            _di += (ushort)(GetFlagD() ? -1 : 1);

            cycle_count += 11;

#if DEBUG
            Log.DoLog($"{prefixStr} STOSB");
#endif
        }
        else if (opcode == 0xab)
        {
            // STOSW
            WriteMemWord(_es, _di, GetAX());

            _di += (ushort)(GetFlagD() ? -2 : 2);

            cycle_count += 11;

#if DEBUG
            Log.DoLog($"{prefixStr} STOSW");
#endif
        }
        else if (opcode == 0xae)
        {
            // SCASB
            byte v = ReadMemByte(_es, _di);

            int result = _al - v;

            SetAddSubFlags(false, _al, v, result, true, false);

            _di += (ushort)(GetFlagD() ? -1 : 1);

            cycle_count += 15;

#if DEBUG
            Log.DoLog($"{prefixStr} SCASB");
#endif
        }
        else if (opcode == 0xaf)
        {
            // SCASW
            ushort ax = GetAX();
            ushort v = ReadMemWord(_es, _di);

            int result = ax - v;

            SetAddSubFlags(true, ax, v, result, true, false);

            _di += (ushort)(GetFlagD() ? -2 : 2);

            cycle_count += 15;

#if DEBUG
            Log.DoLog($"{prefixStr} SCASW");
#endif
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
                Log.DoLog($"{prefixStr} MOV word {name},${v:X4}");
#endif
            }
            else
            {
                // the value follows
                byte v = GetPcByte();

                (string dummy2, int put_cycles) = UpdateRegisterMem(mreg, mod, a_valid, seg, addr, word, v);

                cycle_count += put_cycles;

#if DEBUG
                Log.DoLog($"{prefixStr} MOV byte {name},${v:X2}");
#endif
            }
        }
        else if (opcode == 0xca || opcode == 0xcb)
        {
            // RETF n / RETF
            ushort nToRelease = opcode == 0xca ? GetPcWord() : (ushort)0;

            _ip = pop();
            _cs = pop();

            if (opcode == 0xca)
            {
                _sp += nToRelease;

                cycle_count += 16;

#if DEBUG
                Log.DoLog($"{prefixStr} RETF ${nToRelease:X4}");
#endif
            }
#if DEBUG
            else
            {
                Log.DoLog($"{prefixStr} RETF");

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

            if ((opcode & 2) == 2)
            {
                count = _cl;
                countName = "CL";
            }

            // which one is correct? dosbox and cpu_test from 'riapyx' use the 2nd definition
            //bool count_1_of = opcode is (0xd0 or 0xd1);
            bool count_1_of = count == 1;

            count &= 31;  // masked to 5 bits
            // only since 286?
            // count %= (word ? 17 : 9);  // from documentation ( https://www.felixcloutier.com/x86/rcl:rcr:rol:ror )

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
                Log.DoLog($"{prefixStr} ROL {vName},{countName}");
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
                Log.DoLog($"{prefixStr} ROR {vName},{countName}");
#endif
            }
            else if (mode == 2)
            {
                // RCL
                for (int i = 0; i < count; i++)
                {
                    bool newCarry = (v1 & check_bit) == check_bit;
                    v1 <<= 1;

                    bool oldCarry = GetFlagC();

                    if (oldCarry)
                        v1 |= 1;

                    SetFlagC(newCarry);
                }

                if (count_1_of)
                    SetFlagO(GetFlagC() ^ ((v1 & check_bit) == check_bit));

                cycle_count += 2;

#if DEBUG
                Log.DoLog($"{prefixStr} RCL {vName},{countName}");
#endif
            }
            else if (mode == 3)
            {
                // RCR
                for (int i = 0; i < count; i++)
                {
                    bool newCarry = (v1 & 1) == 1;
                    v1 >>= 1;

                    bool oldCarry = GetFlagC();

                    if (oldCarry)
                        v1 |= (ushort)(word ? 0x8000 : 0x80);

                    SetFlagC(newCarry);
                }

                if (count_1_of)
                    SetFlagO(((v1 & check_bit) == check_bit) ^ ((v1 & check_bit2) == check_bit2));

                cycle_count += 2;

#if DEBUG
                Log.DoLog($"{prefixStr} RCR {vName},{countName}");
#endif
            }
            else if (mode == 4)
            {
                ushort prev_v1 = v1;

                // SAL/SHL
                for (int i = 0; i < count; i++)
                {
                    bool newCarry = (v1 & check_bit) == check_bit;

                    v1 <<= 1;

                    SetFlagC(newCarry);
                }

                if (count_1_of)
                {
                    bool b7 = (prev_v1 & check_bit) != 0;
                    bool b6 = (prev_v1 & check_bit2) != 0;

#if DEBUG
                    Log.DoLog($"b6: {b6}, b7: {b7}: flagO: {b7 != b6}");
#endif

                    SetFlagO(b7 != b6);
                }
                else
                {
                    SetFlagO(false);  // undefined!
                }

                set_flags = count != 0;

                cycle_count += 2;

#if DEBUG
                Log.DoLog($"{prefixStr} SAL {vName},{countName}");
#endif
            }
            else if (mode == 5)
            {
                // SHR
                if (count_1_of)
                    SetFlagO((v1 & check_bit) == check_bit);

                for (int i = 0; i < count; i++)
                {
                    bool newCarry = (v1 & 1) == 1;

                    v1 >>= 1;

                    SetFlagC(newCarry);
                }

                set_flags = count != 0;

                cycle_count += 2;

#if DEBUG
                Log.DoLog($"{prefixStr} SHR {vName},{countName}");
#endif
            }
            else if (mode == 7)
            {
                // SAR
                ushort mask = (ushort)((v1 & check_bit) != 0 ? check_bit : 0);

                for (int i = 0; i < count; i++)
                {
                    bool newCarry = (v1 & 0x01) == 0x01;

                    v1 >>= 1;

                    v1 |= mask;

                    SetFlagC(newCarry);
                }

                SetFlagO(false);

                set_flags = count != 0;

                cycle_count += 2;

#if DEBUG
                Log.DoLog($"{prefixStr} SAR {vName},{countName}");
#endif
            }
            else
            {
                Log.DoLog($"{prefixStr} RCR/SHR/{opcode:X2} mode {mode} not implemented");
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
        else if ((opcode & 0xf0) == 0b01110000)
        {
            // J..., 0x70
            byte to = GetPcByte();

            bool state = false;
            string name = String.Empty;

            if (opcode == 0x70)
            {
                state = GetFlagO();
                name = "JO";
            }
            else if (opcode == 0x71)
            {
                state = GetFlagO() == false;
                name = "JNO";
            }
            else if (opcode == 0x72)
            {
                state = GetFlagC();
                name = "JC";
            }
            else if (opcode == 0x73)
            {
                state = GetFlagC() == false;
                name = "JNC";
            }
            else if (opcode == 0x74)
            {
                state = GetFlagZ();
                name = "JE/JZ";
            }
            else if (opcode == 0x75)
            {
                state = GetFlagZ() == false;
                name = "JNE/JNZ";
            }
            else if (opcode == 0x76)
            {
                state = GetFlagC() || GetFlagZ();
                name = "JBE/JNA";
            }
            else if (opcode == 0x77)
            {
                state = GetFlagC() == false && GetFlagZ() == false;
                name = "JA/JNBE";
            }
            else if (opcode == 0x78)
            {
                state = GetFlagS();
                name = "JS";
            }
            else if (opcode == 0x79)
            {
                state = GetFlagS() == false;
                name = "JNS";
            }
            else if (opcode == 0x7a)
            {
                state = GetFlagP();
                name = "JNP/JPO";
            }
            else if (opcode == 0x7b)
            {
                state = GetFlagP() == false;
                name = "JNP/JPO";
            }
            else if (opcode == 0x7c)
            {
                state = GetFlagS() != GetFlagO();
                name = "JNGE";
            }
            else if (opcode == 0x7d)
            {
                state = GetFlagS() == GetFlagO();
                name = "JNL";
            }
            else if (opcode == 0x7e)
            {
                state = GetFlagZ() == true || GetFlagS() != GetFlagO();
                name = "JLE";
            }
            else if (opcode == 0x7f)
            {
                state = GetFlagZ() == false && GetFlagS() == GetFlagO();
                name = "JNLE";
            }
            else
            {
                Log.DoLog($"{prefixStr} opcode {opcode:x2} not implemented");
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
            Log.DoLog($"{prefixStr} {name} {to} ({_cs:X4}:{newAddress:X4} -> {_cs * 16 + newAddress:X6})");
#endif
        }
        else if (opcode == 0xd7)
        {
            // XLATB
            byte old_al = _al;

            _al = ReadMemByte(_segment_override_set ? _segment_override : _ds, (ushort)(GetBX() + _al));

            cycle_count += 11;

#if DEBUG
            Log.DoLog($"{prefixStr} XLATB ({_ds:X4}:{GetBX():X4} + {old_al:X2})");
#endif
        }
        else if (opcode == 0xe0)
        {
            // LOOPNZ
            byte to = GetPcByte();

            ushort cx = GetCX();

            cx--;

            SetCX(cx);

            ushort newAddresses = (ushort)(_ip + (sbyte)to);

            if (cx > 0 && GetFlagZ() == false)
            {
                _ip = newAddresses;
                cycle_count += 8;
            }
            else
            {
                cycle_count += 4;
            }

#if DEBUG
            Log.DoLog($"{prefixStr} LOOPNZ {to} ({newAddresses:X4} -> {_ip:X4})");
#endif
        }
        else if (opcode == 0xe1 || opcode == 0xe2)
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
#if DEBUG
            else
            {
                Log.DoLog($"{prefixStr} opcode {opcode:X2} not implemented");
            }
#endif

#if DEBUG
            Log.DoLog($"{prefixStr} {name} {to} ({newAddresses:X4})");
#endif
        }
        else if (opcode == 0xe4)
        {
            // IN AL,ib
            byte @from = GetPcByte();

            _al = _io.In(_scheduled_interrupts, @from);

            cycle_count += 10;  // or 14

#if DEBUG
            Log.DoLog($"{prefixStr} IN AL,${from:X2}");
#endif
        }
        else if (opcode == 0xe5)
        {
            // IN AX,ib
            byte @from = GetPcByte();

            SetAX(_io.In(_scheduled_interrupts, @from));

            cycle_count += 10;  // or 14

#if DEBUG
            Log.DoLog($"{prefixStr} IN AX,${from:X2}");
#endif
        }
        else if (opcode == 0xe6)
        {
            // OUT
            byte to = GetPcByte();

            _io.Out(_scheduled_interrupts, @to, _al);

            cycle_count += 10;  // max 14

#if DEBUG
            Log.DoLog($"{prefixStr} OUT ${to:X2},AL");
#endif
        }
        else if (opcode == 0xec)
        {
            // IN AL,DX
            _al = _io.In(_scheduled_interrupts, GetDX());

            cycle_count += 8;  // or 12

#if DEBUG
            Log.DoLog($"{prefixStr} IN AL,DX");
#endif
        }
        else if (opcode == 0xee)
        {
            // OUT
            _io.Out(_scheduled_interrupts, GetDX(), _al);

            cycle_count += 8;  // or 12

#if DEBUG
            Log.DoLog($"{prefixStr} OUT DX,AL");
#endif
        }
        else if (opcode == 0xeb)
        {
            // JMP
            byte to = GetPcByte();

            _ip = (ushort)(_ip + (sbyte)to);

            cycle_count += 15;

#if DEBUG
            Log.DoLog($"{prefixStr} JP ${_ip:X4} ({_cs * 16 + _ip:X6})");
#endif
        }
        else if (opcode == 0xf4)
        {
            // HLT
            _ip--;

#if DEBUG
            Log.DoLog($"{prefixStr} HLT");
#endif

            Console.WriteLine($"{address:X6} HLT");

            if (_terminate_on_hlt)
            {
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
            Log.DoLog($"{prefixStr} CMC");
#endif
        }
        else if (opcode == 0xf8)
        {
            // CLC
            SetFlagC(false);

            cycle_count += 2;

#if DEBUG
            Log.DoLog($"{prefixStr} CLC");
#endif
        }
        else if (opcode == 0xf9)
        {
            // STC
            SetFlagC(true);

            cycle_count += 2;

#if DEBUG
            Log.DoLog($"{prefixStr} STC");
#endif
        }
        else if (opcode == 0xfb)
        {
            // STI
            SetFlagI(true); // IF

            cycle_count += 2;

#if DEBUG
            Log.DoLog($"{prefixStr} STI");
#endif
        }
        else if (opcode == 0xfc)
        {
            // CLD
            SetFlagD(false);

            cycle_count += 2;

#if DEBUG
            Log.DoLog($"{prefixStr} CLD");
#endif
        }
        else if (opcode == 0xfd)
        {
            // STD
            SetFlagD(true);

            cycle_count += 2;

#if DEBUG
            Log.DoLog($"{prefixStr} STD");
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

            Log.DoLog($"mod {mod} reg {reg} function {function}");

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
                Log.DoLog($"{prefixStr} INC {name}");
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
                Log.DoLog($"{prefixStr} DEC {name}");
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
                Log.DoLog($"{prefixStr} CALL {name} (${_ip:X4} -> ${_cs * 16 + _ip:X6})");
#endif
            }
            else if (function == 3)
            {
                // CALL FAR
                push(_cs);
                push(_ip);

                Log.DoLog($"v: {v:X4}, addr: {addr:X4}, word@addr+0: {ReadMemWord(seg, (ushort)(addr + 0)):X4}, word@addr+2: {ReadMemWord(seg, (ushort)(addr + 2)):X4}");

                _ip = v;
                _cs = ReadMemWord(seg, (ushort)(addr + 2));

                cycle_count += 37;

#if DEBUG
                Log.DoLog($"{prefixStr} CALL {name} (${_ip:X4} -> ${_cs * 16 + _ip:X6})");
#endif
            }
            else if (function == 4)
            {
                // JMP NEAR
                _ip = v;

                cycle_count += 18;

#if DEBUG
                Log.DoLog($"{prefixStr} JMP {name} ({_cs * 16 + _ip:X6})");
#endif
            }
            else if (function == 5)
            {
                // JMP
                _cs = ReadMemWord(seg, (ushort)(addr + 2));
                _ip = ReadMemWord(seg, addr);

                cycle_count += 18;  // TODO

#if DEBUG
                Log.DoLog($"{prefixStr} JMP {_cs:X4}:{_ip:X4}");
#endif
            }
            else if (function == 6)
            {
                // PUSH rmw
                push(v);

                cycle_count += 16;
#if DEBUG
                Log.DoLog($"{prefixStr} PUSH ${v:X4}");
#endif
            }
            else
            {
                Log.DoLog($"{prefixStr} opcode {opcode:X2} function {function} not implemented");
            }

            if (!word)
                v &= 0xff;

            (string dummy, int put_cycles) = UpdateRegisterMem(reg, mod, a_valid, seg, addr, word, v);

            cycle_count += put_cycles;
        }
        else
        {
            Log.DoLog($"{prefixStr} opcode {opcode:x} not implemented");
        }

        if (_rep)
        {
            ushort cx = GetCX();
            cx--;
            SetCX(cx);

            if (_rep_mode == RepMode.REPE_Z)
            {
                // REPE/REPZ
                if (cx > 0 && GetFlagZ() == true)
                    _ip = _rep_addr;
                else
                    _rep = false;
            }
            else if (_rep_mode == RepMode.REPNZ)
            {
                // REPNZ
                if (cx > 0 && GetFlagZ() == false)
                    _ip = _rep_addr;
                else
                    _rep = false;
            }
            else if (_rep_mode == RepMode.REP)
            {
                if (cx > 0)
                    _ip = _rep_addr;
                else
                    _rep = false;
            }
            else
            {
                Log.DoLog($"{prefixStr} unknown _rep_mode {(int)_rep_mode}");
                _rep = false;
            }

            if (_rep == false)
            {
                _segment_override_set = false;
                _segment_override_name = "";
            }
        }
        else
        {
            _segment_override_set = false;
            _segment_override_name = "";
        }

        if (cycle_count == 0)
            cycle_count = 1;  // TODO workaround

        // tick I/O
        _io.Tick(_scheduled_interrupts, cycle_count, clock);

        clock += cycle_count;

        return rc;
    }
}
