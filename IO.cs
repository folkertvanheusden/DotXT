class IO
{
    private pic8259 _pic;
    private i8237 _i8237;
    private Bus _b;
    private bool _test_mode = false;
    private Dictionary <ushort, byte> _values = new Dictionary <ushort, byte>();
    private Dictionary <ushort, Device> _io_map = new Dictionary <ushort, Device>();
    private List<Device> _devices;

    public IO(Bus b, ref List<Device> devices, bool test_mode)
    {
        _b = b;

        _pic = new();
        _i8237 = new(_b);

        foreach(var device in devices)
        {
            device.RegisterDevice(_io_map);

            if (device is i8253)
                ((i8253)device).SetDma(_i8237);

            if (device is FloppyDisk)
                ((FloppyDisk)device).SetDma(_i8237);

            device.SetPic(_pic);
        }

        _devices = devices;

        _test_mode = test_mode;
    }

    public pic8259 GetPIC()
    {
        return _pic;
    }

    public (ushort, bool) In(ushort addr)
    {
        if (_test_mode)
            return (65535, false);

        // Log.DoLog($"IN: {addr:X4}", true);

        if (addr <= 0x000f || addr == 0x81 || addr == 0x82 || addr == 0x83 || addr == 0xc2 || addr == 0x87)
            return _i8237.In(addr);

        if (addr == 0x0008)  // DMA status register
            return (0x0f, false);  // 'transfer complete'

        if (addr == 0x0020 || addr == 0x0021)  // PIC
            return _pic.In(addr);

        if (addr == 0x0210)  // verify expansion bus data
            return (0xa5, false);

        if (_io_map.ContainsKey(addr))
        {
            Log.DoLog($"IN: reading {addr:X4} from device", true);
            return _io_map[addr].IO_Read(addr);
        }

        Log.DoLog($"IN: I/O port {addr:X4} not implemented", true);

        return (0xff, false);
    }

    public bool Tick(int ticks, int clock)
    {
        bool rc = false;

        foreach(var device in _devices)
            rc |= device.Tick(ticks, clock);

        return rc;
    }

    public bool Out(ushort addr, ushort value)
    {
        // Log.DoLog($"OUT: I/O port {addr:X4} ({value:X2})", true);

        if (addr <= 0x000f || addr == 0x81 || addr == 0x82 || addr == 0x83 || addr == 0xc2 || addr == 0x87) // 8237
            return _i8237.Out(addr, (byte)value);

        else if (addr == 0x0020 || addr == 0x0021)  // PIC
            return _pic.Out(addr, (byte)value);

        else if (addr == 0x0080)
            Log.DoLog($"Manufacturer systems checkpoint {value:X2}", true);

        else
        {
            if (_io_map.ContainsKey(addr))
                return _io_map[addr].IO_Write(addr, value);
        }

#if DEBUG
        Log.DoLog($"OUT: I/O port {addr:X4} ({value:X2}) not implemented", true);
#endif
        _values[addr] = (byte)value;

        return false;
    }
}
