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
        {
#if DEBUG
            Log.DoLog($"Memory::ReadByte: {address} > {_m.Length}");
#endif
            return 0xee;
        }

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

    private Dictionary <uint, string> _annotations = new();
    private Dictionary <uint, string> _scripts = new();

    public Rom(string filename)
    {
        _contents = File.ReadAllBytes(filename);

        string annotations_file = filename + ".ann";
        if (File.Exists(annotations_file))
        {
            Console.WriteLine($"Loading annotations file {annotations_file}");

            int n = 0;

            using(StreamReader sr = File.OpenText(annotations_file))
            {
                for(;;)
                {
                    string s = sr.ReadLine();
                    if (s == null)
                        break;

                    int pipe = s.IndexOf("|");
                    if (pipe == -1)
                        continue;

                    string key = s.Substring(0, pipe);
                    string val = s.Substring(pipe + 1);
                    string scr = null;

                    uint key_uint = Convert.ToUInt32(key, 16);

                    int val_pipe = val.IndexOf("|");

                    if (val_pipe != -1)
                    {
                        scr = val.Substring(val_pipe + 1);

                        _scripts[key_uint] = scr;

                        val = val.Substring(0, val_pipe);
                    }

                    _annotations[key_uint] = val;

                    n++;

                    // Console.WriteLine($"{n} {key_uint} {val}");
                }
            }

            Console.WriteLine($"Loaded {n} annotations for {filename}");
        }
    }

    public uint GetSize()
    {
        return (uint)_contents.Length;
    }

    public string GetAnnotation(uint address)
    {
        if (_annotations.ContainsKey(address))
                return _annotations[address];

        return null;
    }

    public string GetScript(uint address)
    {
        if (_scripts.ContainsKey(address))
                return _scripts[address];

        return null;
    }

    public byte ReadByte(uint address)
    {
        if (address < _contents.Length)
            return _contents[address];

#if DEBUG
        Log.DoLog($"Rom::ReadByte: {address} > {_contents.Length}");
#endif

        return 0xee;
    }
}

class Bus
{
    private Memory _m;

//    private readonly Rom _bios = new("roms/10jan86-bios/BIOS_5160_10JAN86_U18_62X0851_27256_F800.BIN");
    private readonly Rom _bios = new("roms/GLABIOS.ROM");
//    private readonly Rom _bios = new("roms/xtramtest.32k");

    private readonly Rom _basic = new("roms/10jan86-bios/BIOS_5160_10JAN86_U19_62X0854_27256_F000.BIN");
    // private readonly Rom _basic = new("roms/BIOS_5160_09MAY86_U19_62X0819_68X4370_27256_F000.BIN");

    private List<Device> _devices;

    private bool _use_bios;

    private uint _size;
    private uint _bios_base;

    public Bus(uint size, bool use_bios, ref List<Device> devices)
    {
        _size = size;
        _m = new Memory(size);

        _use_bios = use_bios;

        _bios_base = 0x00100000 - _bios.GetSize();

        _devices = devices;
    }

    public void ClearMemory()
    {
        _m = new Memory(_size);
    }

    public string GetAnnotation(uint address)
    {
        address &= 0x000fffff;

        if (address < 640 * 1024 || _use_bios == false)
            return null;

        if (address >= _bios_base && address <= 0x000fffff)
            return _bios.GetAnnotation(address);

        if (address is >= 0x000f0000 and <= 0x000f7fff)
            return _basic.GetAnnotation(address);

        return null;
    }

    public string GetScript(uint address)
    {
        address &= 0x000fffff;

        if (address < 640 * 1024 || _use_bios == false)
            return null;

        if (address >= _bios_base && address <= 0x000fffff)
            return _bios.GetScript(address);

        if (address is >= 0x000f0000 and <= 0x000f7fff)
            return _basic.GetScript(address);

        return null;
    }

    public byte ReadByte(uint address)
    {
        address &= 0x000fffff;

        if (_use_bios)
        {
            if (address >= _bios_base && address <= 0x000fffff)
                return _bios.ReadByte(address - _bios_base);

            if (address is >= 0x000f0000 and <= 0x000f7fff)
                return _basic.ReadByte(address - 0x000f0000);
        }

        foreach(var device in _devices)
        {
            if (device.HasAddress(address))
                return device.ReadByte(address);
        }

        if (address < 1024 * 1024)
            return _m.ReadByte(address);

#if DEBUG
        Log.DoLog($"Bus::ReadByte: {address} > {_size}");
#endif
        return 0xee;
    }

    public void WriteByte(uint address, byte v)
    {
        address &= 0x000fffff;

        foreach(var device in _devices)
        {
            if (device.HasAddress(address))
            {
                device.WriteByte(address, v);
                return;
            }
        }

        if (address < 1024 * 1024)
            _m.WriteByte(address, v);
    }
}
