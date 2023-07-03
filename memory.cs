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
        if (address < _contents.Length)
            return _contents[address];

        return 0xee;
    }
}

class Bus
{
    private Memory _m;

//    private readonly Rom _bios = new("roms/BIOS_5160_16AUG82_U18_5000026.BIN");
//    private readonly Rom _basic = new("roms/BIOS_5160_16AUG82_U19_5000027.BIN");
    private readonly Rom _bios = new("roms/BIOS_5160_09MAY86_U18_59X7268_62X0890_27256_F800.BIN");
    private readonly Rom _basic = new("roms/BIOS_5160_09MAY86_U19_62X0819_68X4370_27256_F000.BIN");
//    private readonly Rom _bios = new("roms/t/eproms/ibmxt/u18.rom");
//    private readonly Rom _basic = new("roms/t/eproms/ibmxt/u19.rom");

    private bool _use_bios;

    public Bus(uint size, bool use_bios)
    {
        _m = new Memory(size);

        _use_bios = use_bios;
    }

    public byte ReadByte(uint address)
    {
        if (_use_bios)
        {
            if (address is >= 0x000f8000 and <= 0x000fffff)
                return _bios.ReadByte(address - 0x000f8000);

            if (address is >= 0x000f0000 and <= 0x000f7fff)
                return _basic.ReadByte(address - 0x000f0000);
        }

        return _m.ReadByte(address);
    }

    public void WriteByte(uint address, byte v)
    {
        _m.WriteByte(address, v);
    }
}
