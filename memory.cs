internal class Memory
{
    private readonly byte[] _m;

    public Memory(uint size)
    {
        _m = new byte[size];
        var span = new Span<byte>(_m);
        span.Fill(255);
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

    public Rom(string filename)
    {
        _contents = File.ReadAllBytes(filename);
    }

    public uint GetSize()
    {
        return (uint)_contents.Length;
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

    //private readonly Rom _bios = new("roms/10jan86-bios/BIOS_5160_10JAN86_U18_62X0851_27256_F800.BIN");
    private readonly Rom _bios = new("roms/GLABIOS.ROM");
    //    private readonly Rom _bios = new("roms/xtramtest.32k");
    //private readonly Rom _bios = new("roms/Supersoft_PCXT_8KB.bin");
    //private readonly Rom _bios = new("roms/RUUD.rom");  // ruuds_diagnostic_rom_v5.4

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

        return _m.ReadByte(address);
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

        _m.WriteByte(address, v);
    }
}
