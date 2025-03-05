internal class RTC : Device
{
    private int _irq_nr = -1;
    private byte _cmos_ram_index = 0;
    private bool _busy = false;
    private byte [] _ram = new byte[128];

    public RTC()
    {
        Log.Cnsl("RTC instantiated");
    }

    public override int GetIRQNumber()
    {
        return _irq_nr;
    }

    public override String GetName()
    {
        return "RTC";
    }

    public override void RegisterDevice(Dictionary <ushort, Device> mappings)
    {
        mappings[0x0070] = this;
        mappings[0x0071] = this;
        mappings[0x0240] = this;
        mappings[0x0241] = this;
        mappings[0x02c0] = this;
        mappings[0x02c1] = this;
    }

    private byte ToBCD(int v)
    {
        int d1 = v / 10;
        int d2 = v % 10;
        return (byte)((d1 << 4) | d2);
    }

    public override (ushort, bool) IO_Read(ushort port)
    {
        byte rc = 0xff;

        if (port == 0x71 || port == 0x241 || port == 0x2c1)
        {
            DateTime now = DateTime.Now;

            if (_cmos_ram_index == 0)
                rc = ToBCD(now.Second);
            else if (_cmos_ram_index == 2)
                rc = ToBCD(now.Minute);
            else if (_cmos_ram_index == 4)
                rc = ToBCD(now.Hour);
            else if (_cmos_ram_index == 6)
                rc = ToBCD((int)now.DayOfWeek);
            else if (_cmos_ram_index == 7)
                rc = ToBCD(now.Day);
            else if (_cmos_ram_index == 8)
                rc = ToBCD(now.Month);
            else if (_cmos_ram_index == 9)
                rc = ToBCD(now.Year % 100);
            else if (_cmos_ram_index == 0x32)
                rc = ToBCD(now.Year / 100);
            else if (_cmos_ram_index == 0x0b)
                rc = 2;  // 24h mode
            else if (_cmos_ram_index == 0x10)
                rc = 4;  // 1.44 MB floppy drive
            else if (_cmos_ram_index == 0x0a)
            {
                rc = _ram[_cmos_ram_index];
                //rc |= (byte)(_busy ? 128 : 0);
                _busy = !_busy;
            }
            else
            {
                rc = _ram[_cmos_ram_index];
                Log.DoLog($"RTC register {_cmos_ram_index:X02} not implemented", LogLevel.WARNING);
            }
        }
        else
        {
            Log.DoLog($"RTC reading from {port:X04} not implemented", LogLevel.WARNING);
        }

        Log.DoLog($"RTC IN {port:X04} (index: {_cmos_ram_index:X02}), returning {rc:X02}", LogLevel.TRACE);

        return (rc, false);
    }

    public override bool IO_Write(ushort port, ushort value)
    {
        Log.DoLog($"RTC OUT {port:X04}, value {value:X02} (index: {_cmos_ram_index:X02})", LogLevel.TRACE);

        if (port == 0x070 || port == 0x240 || port == 0x2c0)
            _cmos_ram_index = (byte)(value & 127);
        else
        {
            _ram[_cmos_ram_index] = (byte)value;
        }

        return false;
    }

    public override bool HasAddress(uint addr)
    {
        return false;
    }

    public override void WriteByte(uint offset, byte value)
    {
    }

    public override byte ReadByte(uint offset)
    {
        return 0xee;
    }

    public override bool Tick(int ticks, long ignored)
    {
        return false;
    }
}
