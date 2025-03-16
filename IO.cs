class IO
{
    private i8259 _pic;
    private i8237 _i8237;
    private Bus _b;
    private bool _test_mode = false;
    private Dictionary <ushort, Device> _io_map = new Dictionary <ushort, Device>();
    private List<Device> _devices;

    public IO(Bus b, ref List<Device> devices, bool test_mode)
    {
        _b = b;

        _pic = new();
        _i8237 = new(_b);

        foreach(var device in devices)
        {
            device.SetDma(_i8237);
            device.SetPic(_pic);
            device.SetBus(b);
        }

        devices.Add(_i8237);
        devices.Add(_pic);

        foreach(var device in devices)
            device.RegisterDevice(_io_map);

        _devices = devices;

        _test_mode = test_mode;
    }

    public i8259 GetPIC()
    {
        return _pic;
    }

    public ushort In(ushort addr, bool b16)
    {
        if (_test_mode)
            return 65535;

        if (_io_map.ContainsKey(addr))
        {
            ushort rc = _io_map[addr].IO_Read(addr);

            if (b16)
            {
                ushort next_port = (ushort)(addr + 1);
                if (_io_map.ContainsKey(next_port))
                {
                    ushort temp = _io_map[next_port].IO_Read(next_port);
                    rc |= (ushort)(temp << 8);
                }
                else
                {
                    Log.DoLog($"IN: confused: 'next port' ({next_port:X04}) for a 16 bit read is not mapped", LogLevel.WARNING);
                }
            }

            Log.DoLog($"IN: read {rc:X} from device on I/O port {addr:X4} (16 bit: {b16})", LogLevel.TRACE);

            return rc;
        }

        if (addr == 0x0210)  // verify expansion bus data
        {
            Tools.Assert(b16 == false, "expansion bus data");
            return 0xa5;
        }

        Log.DoLog($"IN: I/O port {addr:X4} not implemented", LogLevel.WARNING);

        return (ushort)(b16 ? 0xffff : 0xff);
    }

    public bool Tick(int ticks, long clock)
    {
        bool rc = false;

        foreach(var device in _devices)
            rc |= device.Tick(ticks, clock);

        return rc;
    }

    public bool Out(ushort addr, ushort value, bool b16)
    {
        if (_test_mode)
            return false;

        bool rc = false;

        if (_io_map.ContainsKey(addr))
        {
            rc |= _io_map[addr].IO_Write(addr, (byte)(value & 255));

            if (b16)
            {
                ushort next_port = (ushort)(addr + 1);
                if (_io_map.ContainsKey(next_port))
                    rc |= _io_map[next_port].IO_Write(next_port, (byte)(value >> 8));
                else
                    Log.DoLog($"OUT: confused: 'next port' ({next_port:X04}) for a 16 bit write is not mapped", LogLevel.WARNING);
            }
            else
            {
                Tools.Assert(value < 256, "b8");
            }

            return rc;
        }

        Log.DoLog($"OUT: I/O port {addr:X4} ({value:X2}) not implemented", LogLevel.WARNING);

        return false;
    }
}
