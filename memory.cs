class Memory : Device
{
    private readonly byte[] _m;

    public Memory(uint size)
    {
        _m = new byte[size];
        Array.Fill<byte>(_m, 255);
    }

    public override byte ReadByte(uint address)
    {
        return _m[address];
    }

    public override void WriteByte(uint address, byte v)
    {
        _m[address] = v;
    }

    public override string GetName()
    {
        return "RAM";
    }

    public override byte IO_Read(ushort port)
    {
        return 0xff;
    }

    public override int GetIRQNumber()
    {
        return -1;
    }

    public override List<Tuple<uint, int> > GetAddressList()
    {
        return new() { new(0, _m.Length) };
    }

    public override bool IO_Write(ushort port, byte value)
    {
        return false;
    }

    public override void RegisterDevice(Dictionary <ushort, Device> mappings)
    {
    }

    public override bool Ticks()
    {
        return false;
    }
}

class Rom : Device
{
    private readonly byte[] _contents;
    private readonly uint _offset;

    public Rom(string filename, uint offset)
    {
        _contents = File.ReadAllBytes(filename);
        _offset = offset;

        if (_contents[0] != 0x55 || _contents[1] != 0xaa)
        {
            string msg = $"ROM {filename} might not be valid! (0x55aa header missing)";
            Log.DoLog(msg, LogLevel.INFO);
            Log.Cnsl(msg);
        }
    }

    public override byte ReadByte(uint address)
    {
        return _contents[address - _offset];
    }

    public override string GetName()
    {
        return "ROM";
    }

    public override void WriteByte(uint address, byte v)
    {
    }

    public override List<Tuple<uint, int> > GetAddressList()
    {
        return new() { new(_offset, _contents.Length) };
    }

    public override byte IO_Read(ushort port)
    {
        return 0xff;
    }

    public override int GetIRQNumber()
    {
        return -1;
    }

    public override bool IO_Write(ushort port, byte value)
    {
        return false;
    }

    public override void RegisterDevice(Dictionary <ushort, Device> mappings)
    {
    }

    public override bool Ticks()
    {
        return false;
    }
}

struct CacheEntry
{
    public uint start_addr { get; set; }
    public uint end_addr { get; set; }
    public int wait_states { get; set; }
    public Device device { get; set; }
}

class Bus
{
    private Memory _m;

    private List<Device> _devices;
    private List<Rom> _roms;
    private List<CacheEntry> _cache;

    private uint _size;

    public Bus(uint size, List<Device> devices, List<Rom> roms)
    {
        _size = size;
        _m = new Memory(size);

        _devices = devices;
        _roms = roms;

        RecreateCache();
    }

    public List<string> GetState()
    {
        List<string> @out = new();
        foreach(var entry in _cache)
            @out.Add($"{entry.device.GetName()}, start address: {entry.start_addr:X06}, end address: {entry.end_addr:X06}, wait states: {entry.wait_states}");
        return @out;
    }

    private void AddEntries(List<Device> devices)
    {
        Log.DoLog($"Adding {devices.Count()} devices to cache", LogLevel.DEBUG);
        foreach(var device in devices)
        {
            var segments = device.GetAddressList();
            Log.DoLog($"Adding device {device.GetName()} with {segments.Count()} segments", LogLevel.DEBUG);
            foreach(var segment in segments)
            {
                CacheEntry entry = new();
                entry.start_addr = segment.Item1;
                entry.end_addr = (uint)(entry.start_addr + segment.Item2);
                entry.wait_states = device.GetWaitStateCycles();  // different per segment?
                entry.device = device;
                Log.DoLog($"Start address: {entry.start_addr:X06}, end address: {entry.end_addr:X06}, wait states: {entry.wait_states}", LogLevel.DEBUG);
                _cache.Add(entry);
            }
        }
    }

    public void RecreateCache()
    {
        Log.DoLog("Recreate bus cache", LogLevel.DEBUG);
        _cache = new();

        AddEntries(_devices);

        foreach(var rom in _roms)
            AddEntries(new List<Device> { rom });

        // last! because it is a full 1 MB
        AddEntries(new List<Device> { _m });
    }

    public void ClearMemory()
    {
        _m = new Memory(_size);

        RecreateCache();
    }

    public (byte, int) ReadByte(uint address)
    {
        address &= 0x000fffff;

        foreach(var entry in _cache)
        {
            if (address >= entry.start_addr && address < entry.end_addr)
                return (entry.device.ReadByte(address), entry.wait_states);
        }

        Log.DoLog($"{address:X06} not found for READ ({_cache.Count()})", LogLevel.INFO);

        return (0xff, 1);  // TODO
    }

    public int WriteByte(uint address, byte v)
    {
        address &= 0x000fffff;

        foreach(var entry in _cache)
        {
            if (address >= entry.start_addr && address < entry.end_addr)
            {
                entry.device.WriteByte(address, v);
                return entry.wait_states;
            }
        }

        Log.DoLog($"{address:X06} not found for WRITE ({_cache.Count()})", LogLevel.INFO);

        return 1;  // TODO
    }
}
