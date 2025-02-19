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
    private readonly uint _offset;

    public Rom(string filename, uint offset)
    {
        _contents = File.ReadAllBytes(filename);
        _offset = offset;
    }

    public uint GetSize()
    {
        return (uint)_contents.Length;
    }

    public uint GetOffset()
    {
        return _offset;
    }

    public byte ReadByte(uint address)
    {
        address -= _offset;

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

    private List<Device> _devices;
    private List<Rom> _roms;

    private uint _size;

    public Bus(uint size, ref List<Device> devices, ref List<Rom> roms)
    {
        _size = size;
        _m = new Memory(size);

        _devices = devices;
        _roms = roms;
    }

    public void ClearMemory()
    {
        _m = new Memory(_size);
    }

    public byte ReadByte(uint address)
    {
        address &= 0x000fffff;

        foreach(var rom in _roms)
        {
            if (address >= rom.GetOffset() && address < rom.GetOffset() + rom.GetSize())
                return rom.ReadByte(address);
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
