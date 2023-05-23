namespace DotXT;

internal class Log
{
    private static string logfile = "logfile.txt";

    public static void SetLogFile(string file)
    {
        logfile = file;
    }

    public static void DoLog(string what)
    {
        File.AppendAllText(logfile, what + Environment.NewLine);
    }
}

internal class Memory
{
    private readonly byte[] _m;

    public Memory(uint size)
    {
        _m = new byte[size];
    }

    public byte ReadByte(uint address)
    {
        if (address >= _m.Length)
            return 0xee;

        return _m[address];
    }

    public void WriteByte(uint address, byte v)
    {
        if (address < _m.Length)
            _m[address] = v;
    }
}

internal class Rom
{
    private readonly byte[] _contents;

    public Rom(string filename)
    {
        _contents = File.ReadAllBytes(filename);
    }

    public byte ReadByte(uint address)
    {
        return _contents[address];
    }
}

internal class Bus
{
    private Memory _m;

    private readonly Rom _bios = new("roms/BIOS_5160_16AUG82_U18_5000026.BIN");
    private readonly Rom _basic = new("roms/BIOS_5160_16AUG82_U19_5000027.BIN");

    public Bus(uint size)
    {
        _m = new Memory(size);
    }

    public byte ReadByte(uint address)
    {
        if (address is >= 0x000f8000 and <= 0x000fffff)
            return _bios.ReadByte(address - 0x000f8000);

        if (address is >= 0x000f0000 and <= 0x000f1fff)
            return _basic.ReadByte(address - 0x000f0000);

        return _m.ReadByte(address);
    }

    public void WriteByte(uint address, byte v)
    {
        _m.WriteByte(address, v);
    }
}

internal struct Timer
{
    public ushort counter    { get; set; }
    public int    mode       { get; set; }
    public bool   in_setup   { get; set; }
    public int    latch_type { get; set; }
    public int    latch_n    { get; set; }
    public bool   is_running { get; set; }
}

internal class i8253
{
    Timer [] _timers = new Timer[3];

    // using a static seed to make it behave
    // the same every invocation (until threads
    // and timers are introduced)
    private Random _random = new Random(1);

    public i8253()
    {
        for(int i=0; i<_timers.Length; i++)
            _timers[i] = new Timer();
    }

    public void latch_counter(int nr, byte v)
    {
        Log.DoLog($"OUT 8253: latch_counter {nr} to {v}");

        if (_timers[nr].latch_n > 0)
        {
            if (_timers[nr].latch_n == 2)
            {
                _timers[nr].counter &= 0xff00;
                _timers[nr].counter |= v;
            }
            else if (_timers[nr].latch_n == 1 && _timers[nr].latch_type == 3)
            {
                _timers[nr].counter &= 0xff00;
                _timers[nr].counter |= v;
            }
            else if (_timers[nr].latch_type == 1)
            {
                _timers[nr].counter = v;
            }
            else if (_timers[nr].latch_type == 2)
            {
                _timers[nr].counter = (ushort)(v << 8);
            }

            _timers[nr].latch_n--;

            if (_timers[nr].latch_n == 0)
            {
                _timers[nr].is_running = true;
                _timers[nr].in_setup   = false;
            }
        }
    }

    public byte get_counter(int nr)
    {
        Log.DoLog($"OUT 8253: get_counter {nr}");

        return (byte)_timers[nr].counter;
    }

    public void command(byte v)
    {
        int counter = v >> 6;
        int latch   = (v >> 4) & 3;
        int mode    = (v >> 1) & 7;
        int type    = v & 1;

        Log.DoLog($"OUT 8253: command counter {counter}, latch {latch}, mode {mode}, type {type}");

        _timers[counter].mode       = mode;
        _timers[counter].in_setup   = true;
        _timers[counter].latch_type = latch;
        _timers[counter].is_running = false;

        _timers[counter].counter = 0;

        if (_timers[counter].latch_type == 1 || _timers[counter].latch_type == 2)
            _timers[counter].latch_n = 1;
        else if (_timers[counter].latch_type == 3)
            _timers[counter].latch_n = 2;
    }

    public bool Tick()
    {
        // this trickery is to (hopefully) trigger code that expects
        // some kind of cycle-count versus interrupt-count locking
        if (_random.Next(2) == 1)
            _timers[1].counter--;  // RAM refresh

        _timers[0].counter--;  // counter
       
        if (_timers[0].counter == 0 && _timers[0].mode == 0 && _timers[0].is_running == true)
        {
            _timers[0].is_running = false;

            // interrupt
            return true;
        }

        _timers[2].counter--;  // speaker

        return false;
    }
}

internal class IO
{
    private i8253 _i8253 = new i8253();

    private Dictionary <ushort, byte> values = new Dictionary <ushort, byte>();

    public byte In(Dictionary <int, int> scheduled_interrupts, ushort addr)
    {
        if (addr == 0x0008)  // DMA status register
            return 0x0f;  // 'transfer complete'

        if (addr == 0x0040)
            return _i8253.get_counter(0);

        if (addr == 0x0041)
            return _i8253.get_counter(1);

        if (addr == 0x0042)
            return _i8253.get_counter(2);

        if (addr == 0x0062)  // PPI (XT only)
            return 0x03;  // ~(LOOP IN POST, COPROCESSOR INSTALLED)

        if (addr == 0x0210)  // verify expansion bus data
            return 0xa5;

        if (addr == 0x03f4)  // diskette controller main status register
            return 0x80;

        if (addr == 0x03f5)  // diskette command/data register 0 (ST0)
            return 0b00100000;  // seek completed

        Log.DoLog($"IN: I/O port {addr:X4} not implemented");

        if (values.ContainsKey(addr))
            return values[addr];

        return 0;
    }

    public (int, int) Tick()
    {
        if (_i8253.Tick())
            return (0x08, 10);

        return (-1, -1);
    }

    public void Out(Dictionary <int, int> scheduled_interrupts, ushort addr, byte value)
    {
        // TODO

        if (addr == 0x0040)
            _i8253.latch_counter(0, value);

        else if (addr == 0x0041)
            _i8253.latch_counter(1, value);

        else if (addr == 0x0042)
            _i8253.latch_counter(2, value);

        else if (addr == 0x0043)
            _i8253.command(value);

        else if (addr == 0x0322)
        {
            Log.DoLog($"OUT: I/O port {addr:X4} ({value:X2}) generate controller select pulse");

            if (scheduled_interrupts.ContainsKey(0x0d) == false)
                scheduled_interrupts[0x0d] = 31;  // generate (XT disk-)controller select pulse (IRQ 5)
        }
        else if (addr == 0x03f2)
        {
            Log.DoLog($"OUT: I/O port {addr:X4} ({value:X2}) FDC enable");

            scheduled_interrupts[0x0e] = 10;  // FDC enable (controller reset) (IRQ 6)
        }
        else
        {
            Log.DoLog($"OUT: I/O port {addr:X4} ({value:X2}) not implemented");
        }

        values[addr] = value;
    }
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
    private ushort segment_override;
    private bool segment_override_set;

    private ushort _flags;

    private const uint MemMask = 0x00ffffff;

    private readonly Bus _b;

    private readonly IO _io = new();

    private Dictionary<int, int> _scheduled_interrupts = new Dictionary<int, int>();

    private bool _rep;
    private RepMode _rep_mode;
    private ushort _rep_addr;
    private byte _rep_opcode;

    private bool _is_test;

    private readonly List<byte> floppy = new();

    private string tty_output = "";

    public P8086(string test, bool is_floppy)
    {
        if (test != "" && is_floppy == false)
        {
            _is_test = true;

            _b = new Bus(1024 * 1024);

            _cs = 0;
            _ip = 0x0800;

            uint addr = 0;

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
                        _b.WriteByte((uint)addr, buffer[i]);
                        addr++;
                    }
                }
            }

        }
        else if (test != "" && is_floppy == true)
        {
            _b = new Bus(1024 * 1024);

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
            _b = new Bus(64 * 1024);

            _cs = 0xf000;
            _ip = 0xfff0;
        }
    }

    private bool intercept_int(int nr)
    {
        if (nr == 0x10)
        {
            if (_ah == 00)
            {
                // set resolution
                SetFlagC(false);
                return true;
            }

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

            Console.WriteLine($"INT NR {nr:X2}, AH: {_ah:X2}");
        }
        else if (nr == 0x12)
        {
            // return conventional memory size
            SetAX(640); // 640kB
            SetFlagC(false);
            return true;
        }
        else if (nr == 0x13 && floppy.Count > 0)
        {
            Console.WriteLine($"INT NR {nr:X2}, AH: {_ah:X2}");

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
        else if (nr == 0x16)
        {
            // keyboard access
            Console.WriteLine($"INT NR {nr:X2}, AH: {_ah:X2}");

            SetFlagC(true);
            _ah = 0x01;  // invalid command
            return true;
        }
        else if (nr == 0x19)
        {
            // reboot (to bootloader)
            Log.DoLog("Reboot");
            Console.WriteLine("REBOOT");
            System.Environment.Exit(1);
        }
        else
        {
            Console.WriteLine($"INT NR {nr:X2}, AH: {_ah:X2}");
        }

        return false;
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

        Log.DoLog($"WriteMemWord {segment:X4}:{offset:X4}: a1:{a1:X6}/a2:{a2:X6}, v:{v:X4}");

       _b.WriteByte(a1, (byte)v);
       _b.WriteByte(a2, (byte)(v >> 8));
    }

    private byte ReadMemByte(ushort segment, ushort offset)
    {
        uint a = (uint)(((segment << 4) + offset) & MemMask);

        // Log.DoLog($"ReadMemByte {segment:X4}:{offset:X4}: {a:X6}");

        return _b.ReadByte(a);
    } 

    private ushort ReadMemWord(ushort segment, ushort offset)
    {
        uint a1 = (uint)(((segment << 4) + offset) & MemMask);
        uint a2 = (uint)(((segment << 4) + ((offset + 1) & 0xffff)) & MemMask);

        ushort v = (ushort)(_b.ReadByte(a1) | (_b.ReadByte(a2) << 8));

        Log.DoLog($"ReadMemWord {segment:X4}:{offset:X4}: {a1:X6}/{a2:X6}, value: {v:X4}");

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

    private (ushort, string) GetDoubleRegisterMod00(int reg)
    {
        ushort a = 0;
        string name = "error";

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
            a = GetPcWord();

            name = $"[${a:X4}]";
        }
        else if (reg == 7)
        {
            a = GetBX();
            name = "[BX]";
        }
        else
        {
            Log.DoLog($"{nameof(GetDoubleRegisterMod00)} {reg} not implemented");
        }

        return (a, name);
    }

    private (ushort, string) GetDoubleRegisterMod01_02(int reg, bool word)
    {
        ushort a = 0;
        string name = "error";

        if (reg == 6)
        {
            a = _bp;
            name = "[BP]";
        }
        else
        {
            (a, name) = GetDoubleRegisterMod00(reg);
        }

        short disp = word ? (short)GetPcWord() : (sbyte)GetPcByte();

        return ((ushort)(a + disp), name + $" disp {disp:X4}");
    }

    private (ushort, string, bool, ushort, ushort) GetRegisterMem(int reg, int mod, bool w)
    {
        if (mod == 0)
        {
            (ushort a, string name) = GetDoubleRegisterMod00(reg);

            ushort segment = segment_override_set ? segment_override : _ds;

            ushort v = w ? ReadMemWord(segment, a) : ReadMemByte(segment, a);

            name += $" (${segment * 16 + a:X6} -> {v:X4})";

            return (v, name, true, segment, a);
        }

        if (mod == 1 || mod == 2)
        {
            bool word = mod == 2;

            (ushort a, string name) = GetDoubleRegisterMod01_02(reg, word);

            ushort segment = segment_override_set ? segment_override : _ds;

            ushort v = w ? ReadMemWord(segment, a) : ReadMemByte(segment, a);

            name += $" (${segment * 16 + a:X6} -> {v:X4})";

            return (v, name, true, segment, a);
        }

        if (mod == 3)
        {
            (ushort v, string name) = GetRegister(reg, w);

            return (v, name, false, 0, 0);
        }

        Log.DoLog($"reg {reg} mod {mod} w {w} not supported for {nameof(GetRegisterMem)}");

        return (0, "error", false, 0, 0);
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

    private string PutRegisterMem(int reg, int mod, bool w, ushort val)
    {
        if (mod == 0)
        {
            (ushort a, string name) = GetDoubleRegisterMod00(reg);

            ushort segment = segment_override_set ? segment_override : _ds;

            name += $" (${segment * 16 + a:X6})";

            WriteMemWord(segment, a, val);

            return name;
        }

        if (mod == 1 || mod == 2)
        {
            Log.DoLog($"mod = {mod}, word {w}, val {val:X4}");
            (ushort a, string name) = GetDoubleRegisterMod01_02(reg, mod == 2);

            ushort segment = segment_override_set ? segment_override : _ds;

            name += $" (${segment * 16 + a:X6})";

            if (w)
                WriteMemWord(segment, a, val);
            else
                WriteMemByte(segment, a, (byte)val);

            return name;
        }

        if (mod == 3)
            return PutRegister(reg, w, val);

        Log.DoLog($"reg {reg} mod {mod} w {w} value {val} not supported for {nameof(PutRegisterMem)}");

        return "error";
    }

    string UpdateRegisterMem(int reg, int mod, bool a_valid, ushort seg, ushort addr, bool word, ushort v)
    {
        if (a_valid)
        {
            if (word)
                WriteMemWord(seg, addr, v);
            else
                WriteMemByte(seg, addr, (byte)v);

            return $"[addr:X4]";
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
        @out += GetFlagS() ? "s" : "-";
        @out += GetFlagZ() ? "z" : "-";
        @out += GetFlagA() ? "a" : "-";
        @out += GetFlagP() ? "p" : "-";
        @out += GetFlagC() ? "c" : "-";

        return @out;
    }

    private void SetAddSubFlags(bool word, ushort r1, ushort r2, int result, bool issub, bool flag_c)
    {
        Log.DoLog($"word {word}, r1 {r1}, r2 {r2}, result {result:X}, issub {issub}");

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

    void invoke_interrupt(int interrupt_nr)
    {
        push(_flags);
        push(_cs);
        push(_ip);

        uint addr = (uint)(interrupt_nr * 4);

        _ip = (ushort)(_b.ReadByte(addr + 0) + (_b.ReadByte(addr + 1) << 8));
        _cs = (ushort)(_b.ReadByte(addr + 2) + (_b.ReadByte(addr + 3) << 8));

        Log.DoLog($"----- ------ INT {interrupt_nr:X2}");
    }

    private void HexDump(uint addr)
    {
        string s = "";

        for(uint o=0; o<16; o++)
            s += $" {_b.ReadByte(addr + o):X2}";

        Log.DoLog($"{addr:X6}: {s}");
    }

    public void Tick()
    {
        string flagStr = GetFlagsAsString();

        // tick I/O, check for interrupt
        (int interrupt_countdown_nr, int interrupt_countdown) = _io.Tick();

        if (interrupt_countdown_nr != -1)
            _scheduled_interrupts[interrupt_countdown_nr] = interrupt_countdown;

        if (GetFlag(9) == true)
        {
            foreach (var pair in _scheduled_interrupts)
            {
                int new_count = _scheduled_interrupts[pair.Key] = pair.Value - 1;

                if (new_count == 0)
                {
                    invoke_interrupt(pair.Key);

                    _scheduled_interrupts.Remove(pair.Key);

                    break;
                }
            }
        }

        uint address = (uint)(_cs * 16 + _ip) & MemMask;
        byte opcode = _rep ? _rep_opcode : GetPcByte();

        // handle prefixes
        if (opcode is (0x26 or 0x2e or 0x36 or 0x3e or 0xf2 or 0xf3))
        {
            if (opcode == 0x26)
            {
                segment_override = _es;
                Log.DoLog($"segment override to ES: {_es:X4}");
            }
            else if (opcode == 0x2e)
            {
                segment_override = _cs;
                Log.DoLog($"segment override to CS: {_cs:X4}");
            }
            else if (opcode == 0x36)
            {
                segment_override = _ss;
                Log.DoLog($"segment override to SS: {_ss:X4}");
            }
            else if (opcode == 0x3e)
            {
                segment_override = _ds;
                Log.DoLog($"segment override to DS: {_ds:X4}");
            }
            else if (opcode is (0xf2 or 0xf3))
            {
                _rep = true;
                _rep_mode = RepMode.NotSet;
                _rep_addr = _ip;
            }
            else
            {
                Log.DoLog($"------ {address:X6} prefix {opcode:X2} not implemented");
            }

            address = (uint)(_cs * 16 + _ip) & MemMask;
            byte next_opcode = GetPcByte();

            _rep_opcode = next_opcode;

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
                segment_override_set = true;
            }

            opcode = next_opcode;
        }

        HexDump(address);

        string prefixStr =
            $"{flagStr} {address:X6} {opcode:X2} AX:{_ah:X2}{_al:X2} BX:{_bh:X2}{_bl:X2} CX:{_ch:X2}{_cl:X2} DX:{_dh:X2}{_dl:X2} SP:{_sp:X4} BP:{_bp:X4} SI:{_si:X4} DI:{_di:X4} flags:{_flags:X4}, ES:{_es:X4}, CS:{_cs:X4}, SS:{_ss:X4}, DS:{_ds:X4} | ";

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

            SetAddSubFlags(false, _al, v, result, false, use_flag_c ? flag_c : false);

            _al = (byte)result;

            Log.DoLog($"{prefixStr} {name} AL,${v:X2}");
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

            Log.DoLog($"{prefixStr} {name} AX,${v:X4}");
        }
        else if (opcode == 0x06)
        {
            // PUSH ES
            push(_es);

            Log.DoLog($"{prefixStr} PUSH ES");
        }
        else if (opcode == 0x07)
        {
            // POP ES
            _es = pop();

            Log.DoLog($"{prefixStr} POP ES");
        }
        else if (opcode == 0x0e)
        {
            // PUSH CS
            push(_cs);

            Log.DoLog($"{prefixStr} PUSH CS");
        }
        else if (opcode == 0x16)
        {
            // PUSH SS
            push(_ss);

            Log.DoLog($"{prefixStr} PUSH SS");
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

            Log.DoLog($"{prefixStr} SBB ${v:X4}");
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

            Log.DoLog($"{prefixStr} SBB ${v:X4}");
        }
        else if (opcode == 0x1e)
        {
            // PUSH DS
            push(_ds);

            Log.DoLog($"{prefixStr} PUSH DS");
        }
        else if (opcode == 0x1f)
        {
            // POP DS
            _ds = pop();

            Log.DoLog($"{prefixStr} POP DS");
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

            Log.DoLog($"{prefixStr} DAA");
        }
        else if (opcode == 0x2c)
        {
            // SUB AL,ib
            byte v = GetPcByte();

            int result = _al - v;

            SetAddSubFlags(false, _al, v, result, true, false);

            _al = (byte)result;

            Log.DoLog($"{prefixStr} SUB ${v:X2}");
        }
        else if (opcode == 0x2d)
        {
            // SUB AX,iw
            ushort v = GetPcWord();

            ushort before = GetAX();

            int result = before - v;

            SetAddSubFlags(true, before, v, result, true, false);

            SetAX((ushort)result);

            Log.DoLog($"{prefixStr} SUB ${v:X4}");
        }
        else if (opcode == 0x58)
        {
            // POP AX
            SetAX(pop());

            Log.DoLog($"{prefixStr} POP AX");
        }
        else if (opcode == 0x59)
        {
            // POP CX
            SetCX(pop());

            Log.DoLog($"{prefixStr} POP CX");
        }
        else if (opcode == 0x5a)
        {
            // POP DX
            SetDX(pop());

            Log.DoLog($"{prefixStr} POP DX");
        }
        else if (opcode == 0x5b)
        {
            // POP BX
            SetBX(pop());

            Log.DoLog($"{prefixStr} POP BX");
        }
        else if (opcode == 0x5d)
        {
            // POP BP
            _bp = pop();

            Log.DoLog($"{prefixStr} POP BP");
        }
        else if (opcode == 0x5e)
        {
            // POP SI
            _si = pop();

            Log.DoLog($"{prefixStr} POP SI");
        }
        else if (opcode == 0x5f)
        {
            // POP DI
            _di = pop();

            Log.DoLog($"{prefixStr} POP DI");
        }
        else if (opcode == 0xa4)
        {
            // MOVSB
            byte v = ReadMemByte(_ds, _si);
            WriteMemByte(_es, _di, v);

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

            Log.DoLog($"{prefixStr} MOVSB ({v:X2} / {(v > 32 && v < 127 ? (char)v : ' ')})");
        }
        else if (opcode == 0xa5)
        {
            // MOVSW
            WriteMemWord(_es, _di, ReadMemWord(_ds, _si));

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

            Log.DoLog($"{prefixStr} MOVSW");
        }
        else if (opcode == 0xa6)
        {
            // CMPSB
            byte v1 = ReadMemByte(_ds, _si);
            byte v2 = ReadMemByte(_es, _di);

            int result = v1 - v2;

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

            Log.DoLog($"{prefixStr} CMPSB ({v1:X2}/{(v1 > 32 && v1 < 127 ? (char)v1 : ' ')}, {v2:X2}/{(v2 > 32 && v2 < 127 ? (char)v2 : ' ')})");
        }
        else if (opcode == 0xe3)
        {
            // JCXZ np
            sbyte offset = (sbyte)GetPcByte();

            ushort addr = (ushort)(_ip + offset);

            if (GetCX() == 0)
                _ip = addr;

            Log.DoLog($"{prefixStr} JCXZ {addr:X}");
        }
        else if (opcode == 0xe9)
        {
            // JMP np
            short offset = (short)GetPcWord();

            _ip = (ushort)(_ip + offset);

            Log.DoLog($"{prefixStr} JMP {_ip:X} ({offset:X4})");
        }
        else if (opcode == 0x50)
        {
            // PUSH AX
            push(GetAX());

            Log.DoLog($"{prefixStr} PUSH AX");
        }
        else if (opcode == 0x51)
        {
            // PUSH CX
            push(GetCX());

            Log.DoLog($"{prefixStr} PUSH CX");
        }
        else if (opcode == 0x52)
        {
            // PUSH DX
            push(GetDX());

            Log.DoLog($"{prefixStr} PUSH DX");
        }
        else if (opcode == 0x53)
        {
            // PUSH BX
            push(GetBX());

            Log.DoLog($"{prefixStr} PUSH BX");
        }
        else if (opcode == 0x55)
        {
            // PUSH BP
            push(_bp);

            Log.DoLog($"{prefixStr} PUSH BP");
        }
        else if (opcode == 0x56)
        {
            // PUSH SI
            push(_si);

            Log.DoLog($"{prefixStr} PUSH SI");
        }
        else if (opcode == 0x57)
        {
            // PUSH DI
            push(_di);

            Log.DoLog($"{prefixStr} PUSH DI");
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

            if (opcode == 0x80)
            {
                (r1, name1, a_valid, seg, addr) = GetRegisterMem(reg, mod, false);

                r2 = GetPcByte();
            }
            else if (opcode == 0x81)
            {
                (r1, name1, a_valid, seg, addr) = GetRegisterMem(reg, mod, true);

                r2 = GetPcWord();

                word = true;
            }
            else if (opcode == 0x83)
            {
                (r1, name1, a_valid, seg, addr) = GetRegisterMem(reg, mod, true);

                r2 = GetPcByte();

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
                UpdateRegisterMem(reg, mod, a_valid, seg, addr, word, (ushort)result);

            Log.DoLog($"{prefixStr} {iname} {name1},${r2:X2}");
        }
        else if (opcode == 0x84 || opcode == 0x85)
        {
            // TEST ...,...
            bool word = (opcode & 1) == 1;
            byte o1 = GetPcByte();

            int mod = o1 >> 6;
            int reg1 = (o1 >> 3) & 7;
            int reg2 = o1 & 7;

            (ushort r1, string name1, bool a_valid, ushort seg, ushort addr) = GetRegisterMem(reg2, mod, word);
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

            Log.DoLog($"{prefixStr} TEST {name1},{name2}");
        }
        else if (opcode == 0x86)
        {
            // XCHG
            bool word = (opcode & 1) == 1;
            byte o1 = GetPcByte();

            int mod = o1 >> 6;
            int reg1 = (o1 >> 3) & 7;
            int reg2 = o1 & 7;

            (ushort r1, string name1, bool a_valid, ushort seg, ushort addr) = GetRegisterMem(reg2, mod, word);
            (ushort r2, string name2) = GetRegister(reg1, word);

            ushort temp = r1;
            r1 = r2;
            r2 = temp;

            UpdateRegisterMem(reg2, mod, a_valid, seg, addr, word, r1);

            PutRegister(reg1, word, r2);

            Log.DoLog($"{prefixStr} XCHG {name1},{name2}");
        }
        else if (opcode == 0x90)
        {
            // NOP
            Log.DoLog($"{prefixStr} NOP");
        }
        else if (opcode >= 0x91 && opcode <= 0x97)
        {
            // XCHG AX,...
            int reg_nr = opcode - 0x90;

            (ushort v, string name_other) = GetRegister(reg_nr, true);

            ushort old_ax = GetAX();
            SetAX(v);

            PutRegister(reg_nr, true, old_ax);

            Log.DoLog($"{prefixStr} XCHG AX,{name_other}");
        }
        else if (opcode == 0x98)
        {
            ushort new_value = _al;

            if ((_al & 128) == 128)
                new_value |= 0xff00;

            SetAX(new_value);

            Log.DoLog($"{prefixStr} CBW");
        }
        else if (opcode == 0x99)
        {
            // CWD
            if ((_ah & 32768) == 32768)
                SetDX(0xffff);
            else
                SetDX(0);

            Log.DoLog($"{prefixStr} CDW");
        }
        else if (opcode == 0x9c)
        {
            // PUSHF
            push(_flags);

            Log.DoLog($"{prefixStr} PUSHF");
        }
        else if (opcode == 0x9d)
        {
            // POPF
            _flags = pop();

            Log.DoLog($"{prefixStr} POPF");
        }
        else if (opcode == 0xac)
        {
            // LODSB
            _al = ReadMemByte(_ds, _si);

            if (GetFlagD())
                _si--;
            else
                _si++;

            Log.DoLog($"{prefixStr} LODSB");
        }
        else if (opcode == 0xad)
        {
            // LODSW
            SetAX(ReadMemWord(_ds, _si));

            if (GetFlagD())
                _si -= 2;
            else
                _si += 2;

            Log.DoLog($"{prefixStr} LODSW");
        }
        else if (opcode == 0xc3)
        {
            // RET
            _ip = pop();

            Log.DoLog($"{prefixStr} RET");
        }
        else if (opcode == 0xc5)
        {
            // LDS
            byte o1 = GetPcByte();
            int reg = o1 & 7;

            ushort v = GetPcWord();
            _ds = (ushort)(v + 2);

            string name = PutRegister(reg, true, v);

            Log.DoLog($"{prefixStr} LDS {name},${v:X4}");
        }
        else if (opcode == 0xcd)
        {
            // INT 0x..
            byte @int = GetPcByte();

            uint addr = (uint)(@int * 4);

            if (intercept_int(@int) == false)
            {
                push(_flags);
                push(_cs);
                push(_ip);

                _ip = (ushort)(_b.ReadByte(addr + 0) + (_b.ReadByte(addr + 1) << 8));
                _cs = (ushort)(_b.ReadByte(addr + 2) + (_b.ReadByte(addr + 3) << 8));
            }

            Log.DoLog($"{prefixStr} INT {@int:X2} -> ${_cs * 16 + _ip:X6} (from {addr:X4})");
        }
        else if (opcode == 0xcf)
        {
            // IRET
            _ip = pop();
            _cs = pop();
            _flags = pop();

            Log.DoLog($"{prefixStr} IRET");
        }
        else if ((opcode >= 0x00 && opcode <= 0x03) || (opcode >= 0x10 && opcode <= 0x13) || (opcode >= 0x28 && opcode <= 0x2b) || (opcode >= 0x18 && opcode <= 0x1b) || (opcode >= 0x38 && opcode <= 0x3b))
        {
            bool word = (opcode & 1) == 1;
            bool direction = (opcode & 2) == 2;
            byte o1 = GetPcByte();

            int mod = o1 >> 6;
            int reg1 = (o1 >> 3) & 7;
            int reg2 = o1 & 7;

            (ushort r1, string name1, bool a_valid, ushort seg, ushort addr) = GetRegisterMem(reg2, mod, word);
            (ushort r2, string name2) = GetRegister(reg1, word);

            string name = "error";
            int result = 0;
            bool is_sub = false;
            bool apply = true;
            bool use_flag_c = false;
           
            if (opcode <= 0x03)
            {
                result = r1 + r2;

                name = "ADD";
            }
            else if (opcode >= 0x10 && opcode <= 0x13)
            {
                use_flag_c = true;

                result = r1 + r2 + (GetFlagC() ? 1 : 0);

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

                    Log.DoLog($"{prefixStr} {name} {name2},{name1}");
                }
                else
                {
                    UpdateRegisterMem(reg2, mod, a_valid, seg, addr, word, (ushort)result);

                    Log.DoLog($"{prefixStr} {name} {name1},{name2}");
                }
            }
            else
            {
                if (direction)
                    Log.DoLog($"{prefixStr} {name} {name2},{name1}");
                else
                    Log.DoLog($"{prefixStr} {name} {name1},{name2}");
            }
        }
        else if (opcode == 0x3c || opcode == 0x3d)
        {
            // CMP
            bool word = (opcode & 1) == 1;

            int result = 0;

            ushort r1 = 0;
            ushort r2 = 0;

            if (opcode == 0x3d)
            {
                r1 = GetAX();
                r2 = GetPcWord();

                result = r1 - r2;

                Log.DoLog($"{prefixStr} CMP AX,#${r2:X4}");
            }
            else if (opcode == 0x3c)
            {
                r1 = _al;
                r2 = GetPcByte();

                result = r1 - r2;

                Log.DoLog($"{prefixStr} CMP AL,#${r2:X2}");
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

            (ushort r1, string name1, bool a_valid, ushort seg, ushort addr) = GetRegisterMem(reg2, mod, word);
            (ushort r2, string name2) = GetRegister(reg1, word);

            string name = "error";

            ushort result = 0;

            int function = opcode >> 4;

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

            string affected;

            if (direction)
            {
                affected = PutRegister(reg1, word, result);

                Log.DoLog($"{prefixStr} {name} {name1},{name2}");
            }
            else
            {
                affected = UpdateRegisterMem(reg2, mod, a_valid, seg, addr, word, result);

                Log.DoLog($"{prefixStr} {name} {name2},{name1}");
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

            Log.DoLog($"{prefixStr} {name} {tgt_name},${bHigh:X2}{bLow:X2}");
        }
        else if (opcode == 0xe8)
        {
            // CALL
            short a = (short)GetPcWord();

            push(_ip);

            _ip = (ushort)(a + _ip);

            Log.DoLog($"{prefixStr} CALL {a:X4} (${_ip:X4} -> ${_cs * 16 + _ip:X6})");
        }
        else if (opcode == 0xea)
        {
            // JMP far ptr
            ushort temp_ip = GetPcWord();
            ushort temp_cs = GetPcWord();

            _ip = temp_ip;
            _cs = temp_cs;

            Log.DoLog($"{prefixStr} JMP ${_cs:X} ${_ip:X}: ${_cs * 16 + _ip:X}");
        }
        else if (opcode == 0xf6)
        {
            // TEST and others
            bool word = (opcode & 1) == 1;

            byte o1 = GetPcByte();

            int mod = o1 >> 6;
            int reg1 = o1 & 7;

            (ushort r1, string name1, bool a_valid, ushort seg, ushort addr) = GetRegisterMem(reg1, mod, word);

            string name2 = "";

            string cmd_name = "error";

            int function = (o1 >> 3) & 7;

            if (function == 0)
            {
                // TEST
                byte r2 = GetPcByte();
                name2 = $",{r2:X2}";

                ushort result = (ushort)(r1 & r2);
                SetLogicFuncFlags(word, result);

                SetFlagC(false);

                cmd_name = "TEST";
            }
            else if (function == 2)
            {
                // NOT
                UpdateRegisterMem(reg1, mod, a_valid, seg, addr, word, (ushort)~r1);

                cmd_name = "NOT";
            }
            else if (function == 3)
            {
                // NEG
                int result = (ushort)-r1;

                cmd_name = "NEG";

                SetAddSubFlags(false, 0, r1, -r1, true, false);
                SetFlagC(r1 != 0);

                UpdateRegisterMem(reg1, mod, a_valid, seg, addr, word, (ushort)result);
            }
            else if (function == 4)
            {
                // MUL
                int result = _al * r1;
                SetAX((ushort)result);

                bool flag = _ah != 0;
                SetFlagC(flag);
                SetFlagO(flag);

                cmd_name = "MUL";
            }
            else if (function == 6)
            {
                // DIV
                ushort ax = GetAX();

                if (r1 == 0 || ax / r1 > 0x100)
                    invoke_interrupt(r1 == 0 ? 0x00 : 0x10);  // divide by zero or divisor too small
                else
                {
                    _al = (byte)(ax / r1);
                    _ah = (byte)(ax % r1);
                }

                cmd_name = "DIV";
            }
            else
            {
                Log.DoLog($"{prefixStr} opcode {opcode:X2} o1 {o1:X2} function {function} not implemented");
            }

            Log.DoLog($"{prefixStr} {cmd_name} {name1}{name2}");
        }
        else if (opcode == 0xf7)
        {
            // MUL and others
            byte o1 = GetPcByte();

            int mod = o1 >> 6;
            int reg1 = o1 & 7;
            int reg2 = (o1 >> 3) & 7;

            (ushort r1, string name1, bool a_valid, ushort seg, ushort addr) = GetRegisterMem(reg1, mod, true);

            string use_name1 = "";
            string use_name2 = "";

            string cmd_name = "error";
            ushort result = 0;
            bool put = false;

            int function = (o1 >> 3) & 7;

            if (function == 0)
            {
                // TEST
                ushort r2 = GetPcWord();
                use_name2 = $"{r2:X4}";

                result = (ushort)(r1 & r2);
                SetLogicFuncFlags(true, result);

                SetFlagC(false);

                cmd_name = "TEST";
            }
            else if (function == 2)
            {
                // NOT
                result = (ushort)~r1;

                put = true;

                cmd_name = "NOT";

                use_name1 = name1;
            }
            else if (function == 3)
            {
                // NEG
                result = (ushort)-r1;

                put = true;

                cmd_name = "NEG";

                use_name1 = name1;

                SetAddSubFlags(true, 0, r1, -r1, true, false);
                SetFlagC(r1 != 0);
            }
            else if (function == 4)
            {
                // MUL
                ushort ax = GetAX();
                int resulti = ax * r1;

                uint dx_ax = (uint)resulti;
                SetAX((ushort)dx_ax);
                SetDX((ushort)(dx_ax >> 16));

                bool flag = GetDX() != 0;
                SetFlagC(flag);
                SetFlagO(flag);

                use_name1 = "DX:AX";
                use_name2 = name1;

                cmd_name = "MUL";
            }
            else if (function == 5)
            {
                // IMUL
                short ax = (short)GetAX();
                int resulti = ax * (short)r1;

                uint dx_ax = (uint)resulti;
                SetAX((ushort)dx_ax);
                SetDX((ushort)(dx_ax >> 16));

                bool flag = dx_ax >= 0x10000;
                SetFlagC(flag);
                SetFlagO(flag);

                use_name1 = "DX:AX";
                use_name2 = name1;

                cmd_name = "IMUL";
            }
            else if (function == 6)
            {
                // DIV
                uint dx_ax = (uint)((GetDX() << 16) | GetAX());

                if (r1 == 0 || dx_ax / r1 >= 0x10000)
                    invoke_interrupt(r1 == 0 ? 0x00 : 0x10);  // divide by zero or divisor too small
                else
                {
                    SetAX((ushort)(dx_ax / r1));
                    SetDX((ushort)(dx_ax % r1));
                }

                use_name1 = "DX:AX";
                use_name2 = name1;

                cmd_name = "DIV";
            }
            else
            {
                Log.DoLog($"{prefixStr} opcode {opcode:X2} o1 {o1:X2} function {function} not implemented");
            }

            if (put)
                UpdateRegisterMem(reg1, mod, a_valid, seg, addr, true, result);

            Log.DoLog($"{prefixStr} {cmd_name} {use_name1},{use_name2}");
        }
        else if (opcode == 0xfa)
        {
            // CLI
            ClearFlagBit(9); // IF

            Log.DoLog($"{prefixStr} CLI");
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

            Log.DoLog($"{prefixStr} MOV {name},${v:X}");
        }
        else if (opcode == 0xa0)
        {
            // MOV AL,[...]
            ushort a = GetPcWord();

            _al = _b.ReadByte((uint)(a + (_ds << 4)));

            Log.DoLog($"{prefixStr} MOV AL,{a:X4}");
        }
        else if (opcode == 0xa1)
        {
            // MOV AX,[...]
            ushort a = GetPcWord();

            SetAX(ReadMemWord(_ds, a));

            Log.DoLog($"{prefixStr} MOV AX,{a:X4}");
        }
        else if (opcode == 0xa2)
        {
            // MOV AL,[...]
            ushort a = GetPcWord();

            _b.WriteByte((uint)(a + (_ds << 4)), _al);

            Log.DoLog($"{prefixStr} MOV [${a:X4}],AL");
        }
        else if (opcode == 0xa3)
        {
            // MOV [...],AX
            ushort a = GetPcWord();

            WriteMemWord(_ds, a, GetAX());

            Log.DoLog($"{prefixStr} MOV [${a:X4}],AX");
        }
        else if (opcode == 0xa8)
        {
            // TEST AL,..
            byte v = GetPcByte();

            byte result = (byte)(_al & v);

            SetLogicFuncFlags(false, result);

            SetFlagC(false);

            Log.DoLog($"{prefixStr} TEST AL,${v:X2}");
        }
        else if (opcode == 0xa9)
        {
            // TEST AX,..
            ushort v = GetPcWord();

            ushort result = (ushort)(GetAX() & v);

            SetLogicFuncFlags(true, result);

            SetFlagC(false);

            Log.DoLog($"{prefixStr} TEST AX,${v:X4}");
        }
        else if (((opcode & 0b11111100) == 0b10001000 /* 0x88 */) || opcode == 0b10001110 /* 0x8e */|| opcode == 0x8c)
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

            // Log.DoLog($"{opcode:X}|{o1:X} mode {mode}, reg {reg}, rm {rm}, dir {dir}, word {word}, sreg {sreg}");

            if (dir)
            {
                // to 'REG' from 'rm'
                (ushort v, string fromName, bool a_valid, ushort seg, ushort addr) = GetRegisterMem(rm, mode, word);

                string toName;

                if (sreg)
                    toName = PutSRegister(reg, v);
                else
                    toName = PutRegister(reg, word, v);

                Log.DoLog($"{prefixStr} MOV {toName},{fromName}");
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

                string toName = PutRegisterMem(rm, mode, word, v);

                Log.DoLog($"{prefixStr} MOV {toName},{fromName}");
            }
        }
        else if (opcode == 0x8d)
        {
            // LEA
            byte o1 = GetPcByte();
            int mod = o1 >> 6;
            int reg = (o1 >> 3) & 7;
            int rm = o1 & 7;

            // might introduce problems when the dereference of *addr reads from i/o even
            // when it is not required
            (ushort val, string name_from, bool a_valid, ushort seg, ushort addr) = GetRegisterMem(rm, mod, true);

            string name_to = PutRegister(reg, true, addr);

            Log.DoLog($"{prefixStr} LEA {name_to},{name_from}");
        }
        else if ((opcode & 0xf8) == 0xb8)
        {
            // MOV immed to reg
            bool word = (opcode & 8) == 8; // b/w
            int reg = opcode & 7;

            ushort val = GetPcByte();

            if (word)
                val |= (ushort)(GetPcByte() << 8);

            string toName = PutRegister(reg, word, val);

            Log.DoLog($"{prefixStr} MOV {toName},${val:X}");
        }
        else if (opcode == 0x9e)
        {
            // SAHF
            ushort keep = (ushort)(_flags & 0b1111111100101010);
            ushort add = (ushort)(_ah & 0b11010101);

            _flags = (ushort)(keep | add);

            Log.DoLog($"{prefixStr} SAHF (set to {GetFlagsAsString()})");
        }
        else if (opcode == 0x9f)
        {
            // LAHF
            _ah = (byte)_flags;

            Log.DoLog($"{prefixStr} LAHF");
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

            if (isDec)
                Log.DoLog($"{prefixStr} DEC {name}");
            else
                Log.DoLog($"{prefixStr} INC {name}");
        }
        else if (opcode == 0xaa)
        {
            // STOSB
            WriteMemByte(_es, _di, _al);

            _di += (ushort)(GetFlagD() ? -1 : 1);

            Log.DoLog($"{prefixStr} STOSB");
        }
        else if (opcode == 0xab)
        {
            // STOSW
            WriteMemWord(_es, _di, GetAX());

            _di += (ushort)(GetFlagD() ? -2 : 2);

            Log.DoLog($"{prefixStr} STOSW");
        }
        else if (opcode == 0xaf)
        {
            // SCASW
            ushort ax = GetAX();
            ushort v = ReadMemWord(_es, _di);

            int result = ax - v;

            SetAddSubFlags(true, ax, v, result, true, false);

            _di += (ushort)(GetFlagD() ? -2 : 2);

            Log.DoLog($"{prefixStr} SCASW");
        }
        else if (opcode == 0xc4)
        {
            // LES
            byte o1 = GetPcByte();
            int mod = o1 >> 6;
            int reg = (o1 >> 3) & 7;
            int rm = o1 & 7;

            (ushort val, string name_from, bool a_valid, ushort seg, ushort addr) = GetRegisterMem(rm, mod, true);

            SetBX(ReadMemWord(seg, (ushort)(addr + 0)));
            _es = ReadMemWord(seg, (ushort)(addr + 2));

            Log.DoLog($"{prefixStr} LES {name_from},{val:X4}");
        }
        else if (opcode == 0xc6 || opcode == 0xc7)
        {
            // MOV
            bool word = (opcode & 1) == 1;

            byte o1 = GetPcByte();

            int mod = o1 >> 6;

            int mreg = o1 & 7;

            // get address to write to ('seg, addr'))
            (ushort dummy, string name, bool a_valid, ushort seg, ushort addr) = GetRegisterMem(mreg, mod, word);

            if (word)
            {
                // the value follows
                ushort v = GetPcWord();

                UpdateRegisterMem(mreg, mod, a_valid, seg, addr, word, v);

                Log.DoLog($"{prefixStr} MOV word {name},${v:X4}");
            }
            else
            {
                // the value follows
                byte v = GetPcByte();

                UpdateRegisterMem(mreg, mod, a_valid, seg, addr, word, v);

                Log.DoLog($"{prefixStr} MOV byte {name},${v:X2}");
            }
        }
        else if (opcode == 0xca || opcode == 0xcb)
        {
            // RETF
            ushort nToRelease = GetPcWord();

            _ip = pop();
            _cs = pop();

            if (opcode == 0xca)
            {
                _ss += nToRelease;

                Log.DoLog($"{prefixStr} RETF ${nToRelease:X4}");
            }
            else
            {
                Log.DoLog($"{prefixStr} RETF");
            }
        }
        else if ((opcode & 0xf8) == 0xd0)
        {
            bool word = (opcode & 1) == 1;
            byte o1 = GetPcByte();

            int mod = o1 >> 6;
            int reg1 = o1 & 7;

            (ushort v1, string vName, bool a_valid, ushort seg, ushort addr) = GetRegisterMem(reg1, mod, word);

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

                Log.DoLog($"{prefixStr} ROL {vName},{countName}");
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

                Log.DoLog($"{prefixStr} ROR {vName},{countName}");
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

                Log.DoLog($"{prefixStr} RCL {vName},{countName}");
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

                Log.DoLog($"{prefixStr} RCR {vName},{countName}");
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

                    Log.DoLog($"b6: {b6}, b7: {b7}: flagO: {b7 != b6}");

                    SetFlagO(b7 != b6);
                }
                else
                {
                    SetFlagO(false);  // undefined!
                }

                set_flags = count != 0;

                Log.DoLog($"{prefixStr} SAL {vName},{countName}");
            }
            else if (mode == 5)
            {
                // SHR
                for (int i = 0; i < count; i++)
                {
                    bool newCarry = (v1 & 1) == 1;

                    v1 >>= 1;

                    SetFlagC(newCarry);
                }

                if (count_1_of)
                    SetFlagO(((v1 & check_bit) == check_bit) ^ ((v1 & check_bit2) == check_bit2));

                set_flags = count != 0;

                Log.DoLog($"{prefixStr} SHR {vName},{countName}");
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

                Log.DoLog($"{prefixStr} SAR {vName},{countName}");
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

            UpdateRegisterMem(reg1, mod, a_valid, seg, addr, word, v1);
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
                state = GetFlagZ() || GetFlagS() != GetFlagO();
                name = "JLE";
            }
            else if (opcode == 0x7f)
            {
                state = GetFlagZ() && GetFlagS() == GetFlagO();
                name = "JNLE";
            }
            else
            {
                Log.DoLog($"{prefixStr} opcode {opcode:x2} not implemented");
            }

            ushort newAddress = (ushort)(_ip + (sbyte)to);

            if (state)
                _ip = newAddress;

            Log.DoLog($"{prefixStr} {name} {to} ({_cs:X4}:{newAddress:X4} -> {_cs * 16 + newAddress:X6})");
        }
        else if (opcode == 0xe2)
        {
            // LOOP
            byte to = GetPcByte();

            (ushort cx, string dummy) = GetRegister(1, true);

            cx--;

            PutRegister(1, true, cx);

            ushort newAddresses = (ushort)(_ip + (sbyte)to);

            if (cx > 0)
                _ip = newAddresses;

            Log.DoLog($"{prefixStr} LOOP {to} ({newAddresses:X4})");
        }
        else if (opcode == 0xe4)
        {
            // IN AL,ib
            byte @from = GetPcByte();

            _al = _io.In(_scheduled_interrupts, @from);

            Log.DoLog($"{prefixStr} IN AL,${from:X2}");
        }
        else if (opcode == 0xec)
        {
            // IN AL,DX
            _al = _io.In(_scheduled_interrupts, GetDX());

            Log.DoLog($"{prefixStr} IN AL,DX");
        }
        else if (opcode == 0xe6)
        {
            // OUT
            byte to = GetPcByte();

            _io.Out(_scheduled_interrupts, @to, _al);

            Log.DoLog($"{prefixStr} OUT ${to:X2},AL");
        }
        else if (opcode == 0xee)
        {
            // OUT
            _io.Out(_scheduled_interrupts, GetDX(), _al);

            Log.DoLog($"{prefixStr} OUT DX,AL");
        }
        else if (opcode == 0xeb)
        {
            // JMP
            byte to = GetPcByte();

            _ip = (ushort)(_ip + (sbyte)to);

            Log.DoLog($"{prefixStr} JP ${_ip:X4} ({_cs * 16 + _ip:X6})");
        }
        else if (opcode == 0xf4)
        {
            // HLT
            _ip--;

            Log.DoLog($"{prefixStr} HLT");

            Console.WriteLine($"{address:X6} HLT");

            if (_is_test)
                System.Environment.Exit(_si == 0xa5ee ? 123 : 0);

            System.Environment.Exit(0);
        }
        else if (opcode == 0xf5)
        {
            // CMC
            SetFlagC(! GetFlagC());

            Log.DoLog($"{prefixStr} CMC");
        }
        else if (opcode == 0xf8)
        {
            // CLC
            SetFlagC(false);

            Log.DoLog($"{prefixStr} CLC");
        }
        else if (opcode == 0xf9)
        {
            // STC
            SetFlagC(true);

            Log.DoLog($"{prefixStr} STC");
        }
        else if (opcode == 0xfb)
        {
            // STI
            SetFlagBit(9); // IF

            Log.DoLog($"{prefixStr} STI");
        }
        else if (opcode == 0xfc)
        {
            // CLD
            SetFlagD(false);

            Log.DoLog($"{prefixStr} CLD");
        }
        else if (opcode == 0xfd)
        {
            // STD
            SetFlagD(true);

            Log.DoLog($"{prefixStr} STD");
        }
        else if (opcode == 0xfe || opcode == 0xff)
        {
            // DEC and others
            bool word = (opcode & 1) == 1;

            byte o1 = GetPcByte();

            int mod = o1 >> 6;
            int reg = o1 & 7;

            (ushort v, string name, bool a_valid, ushort seg, ushort addr) = GetRegisterMem(reg, mod, word);

            int function = (o1 >> 3) & 7;

            if (function == 0)
            {
                // INC
                v++;

                SetFlagO(word ? v == 0x8000 : v == 0x80);
                SetFlagA((v & 15) == 0);

                SetFlagS(word ? (v & 0x8000) == 0x8000 : (v & 0x80) == 0x80);
                SetFlagZ(word ? v == 0 : (v & 0xff) == 0);
                SetFlagP((byte)v);

                Log.DoLog($"{prefixStr} INC {name}");
            }
            else if (function == 1)
            {
                // DEC
                v--;

                SetFlagO(word ? v == 0x7fff : v == 0x7f);
                SetFlagA((v & 15) == 15);

                SetFlagS(word ? (v & 0x8000) == 0x8000 : (v & 0x80) == 0x80);
                SetFlagZ(word ? v == 0 : (v & 0xff) == 0);
                SetFlagP((byte)v);

                Log.DoLog($"{prefixStr} DEC {name}");
            }
            else if (function == 2)
            {
                // CALL
                ushort a = GetPcWord();

                push(_ip);

                _ip = (ushort)(a + _ip);

                Log.DoLog($"{prefixStr} CALL {a:X4} (${_ip:X4} -> ${_cs * 16 + _ip:X6})");
            }
            else if (function == 5)
            {
                // JMP
                _cs = ReadMemWord(seg, (ushort)(addr + 2));
                _ip = ReadMemWord(seg, addr);

                Log.DoLog($"{prefixStr} JMP {_cs:X4}:{_ip:X4}");
            }
            else
            {
                Log.DoLog($"{prefixStr} opcode {opcode:X2} function {function} not implemented");
            }

            if (!word)
                v &= 0xff;

            UpdateRegisterMem(reg, mod, a_valid, seg, addr, word, v);
        }
        else
        {
            Log.DoLog($"{prefixStr} opcode {opcode:x} not implemented");
        }

        segment_override_set = false;

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
            }
        }
    }
}
