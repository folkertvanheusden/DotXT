class SerialMouse : Device
{
    private ushort _base_port = 0;
    private int _irq_nr = -1;
    private byte [] ports = new byte[8];
    private int _last_x = -1;
    private int _last_y = -1;
    private List<byte> _mouse_msgs = null;
    private int _mouse_msgs_idx = -1;
    private string [] _io_names = new string[] { "RHR/THR", "IER", "ISR/FCR", "LCR", "MCR", "LSR", "MSR", "SPR" };
    private bool _reset_on = false;
    private bool _reset_on_bit_toggle = false;
    private long _reset_since = 0;

    public SerialMouse(ushort base_port, int irq)
    {
        Log.Cnsl("SerialMouse instantiated");
        _base_port = base_port;
        _irq_nr = irq;
    }

    public override int GetIRQNumber()
    {
        return _irq_nr;
    }

    public override String GetName()
    {
        return "SerialMouse";
    }

    public override void RegisterDevice(Dictionary <ushort, Device> mappings)
    {
        for(ushort p=_base_port; p<_base_port + 8; p++)
            mappings[p] = this;
    }

    public void SetPosition(int x, int y, int buttons)
    {
        if (_mouse_msgs == null)
        {
            _mouse_msgs = new();
            _mouse_msgs_idx = 0;
        }

        int delta_x = 0;
        int delta_y = 0;
        if (_last_x != -1)
        {
            delta_x = x - _last_x;
            if (delta_x < -127)
                delta_x = -127;
            else if (delta_x > 127)
                delta_x = 127;
            delta_y = y - _last_y;
            if (delta_y < -127)
                delta_y = -127;
            else if (delta_y > 127)
                delta_y = 127;
        }
        _last_x += delta_x;
        _last_y += delta_y;

        _mouse_msgs.Add((byte)(64 | ((buttons & 1) << 5) | ((buttons & 2) << 3) | (((delta_y >> 6) & 3) << 2) | ((delta_x >> 6) & 3)));
        _mouse_msgs.Add((byte)(delta_x & 63));
        _mouse_msgs.Add((byte)(delta_y & 63));
        ScheduleInterrupt(1);
        Console.WriteLine($"{_mouse_msgs_idx} {_mouse_msgs.Count}");
    }

    public override byte IO_Read(ushort port)
    {
        Log.DoLog($"SerialMouse IO_Read {port:X04} ({_io_names[port - _base_port]})", LogLevel.DEBUG);

        int index = port - _base_port;

        if (index == 0)
        {
            if (_mouse_msgs != null)
            {
                byte rc = _mouse_msgs[_mouse_msgs_idx++];
                if (_mouse_msgs_idx == _mouse_msgs.Count)
                    _mouse_msgs = null;
                return rc;
            }
            return 0;
        }

        if (index == 2)  // ISR
            return (byte)((ports[index] & 0xf0) | (_mouse_msgs != null ? 0 : 4));  // interrupt identification code 2, data ready

        if (index == 5)  // LSR
        {
            if (_mouse_msgs != null)
            {
                bool bit = _reset_on_bit_toggle;
                _reset_on_bit_toggle = !_reset_on_bit_toggle;
                return (byte)(ports[index] | (bit ? 1 : 0) | 64);  // Data ready set, Transmitter empty set
            }
        }

        return ports[index];  // RHR
    }

    public override bool IO_Write(ushort port, byte value)
    {
        Log.DoLog($"SerialMouse IO_Write {port:X04} ({_io_names[port - _base_port]}) {value:X02}", LogLevel.DEBUG);
        int index = port - _base_port;
        if (index == 4)
        {
            if ((value & 1) == 0)
            {
                if (_reset_on == false)
                {
                    Log.DoLog($"SerialMouse RTS high", LogLevel.DEBUG);
                    _reset_on = true;
                    _reset_since = _clock;
                }
                else
                {
                    Log.DoLog($"SerialMouse RTS high for {_clock - _reset_since} ticks", LogLevel.DEBUG);
                }
            }
            else if (_reset_on && _clock - _reset_since >= 4770000 / 150)
            {
                if (_mouse_msgs == null)
                    _mouse_msgs = new();
                else
                    _mouse_msgs.Clear();
                Log.DoLog($"SerialMouse reset", LogLevel.DEBUG);
                _mouse_msgs.Add(0x4d);  // reset ack
                _mouse_msgs_idx = 0;
                ScheduleInterrupt(0);
                _reset_on = false;
            }
        }

        ports[index] = value;

        return false;
    }

    public override List<Tuple<uint, int> > GetAddressList()
    {
        return new() { };
    }

    public override void WriteByte(uint addr, byte value)
    {
    }

    public override byte ReadByte(uint addr)
    {
        return 0xee;
    }

    public override bool Ticks()
    {
        return true;
    }
}
