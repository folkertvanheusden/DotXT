class SerialMouse : Device
{
    private ushort _base_port = 0;
    private int _irq_nr = -1;
    private byte [] ports = new byte[8];
    private int _last_x = -1;
    private int _last_y = -1;
    private List<byte> _mouse_msgs = null;
    private string [] _io_names = new string[] { "RHR/THR", "IER", "ISR/FCR", "LCR", "MCR", "LSR", "MSR", "SPR" };
    private bool _reset_on = false;
    private bool _reset_on_bit_toggle = false;
    private long _reset_since = 0;
    private bool prts = false;

    public SerialMouse(ushort base_port, int irq)
    {
        _base_port = base_port;
        _irq_nr = irq;
        Log.Cnsl($"SerialMouse instantiated on port {_base_port:X04} with IRQ {_irq_nr}");
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
            _mouse_msgs = new();

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
        else {
            _last_x = x;
            _last_y = y;
        }
        _last_x += delta_x;
        _last_y += delta_y;

        byte b1 = (byte)(64 | ((buttons & 1) << 5) | ((buttons & 2) << 3) | (((delta_y >> 6) & 3) << 2) | ((delta_x >> 6) & 3));
        byte b2 = (byte)(delta_x & 63);
        byte b3 = (byte)(delta_y & 63);
        _mouse_msgs.Add(b1);
        _mouse_msgs.Add(b2);
        _mouse_msgs.Add(b3);
        ScheduleInterrupt(0);
        // Console.WriteLine($"{_mouse_msgs.Count} - {delta_x} {delta_y} {buttons} | {b1:X02} {b2:X02} {b3:X02} | {next_interrupt.Count()}");
    }

    public override byte IO_Read(ushort port)
    {
        Log.DoLog($"SerialMouse IO_Read {port:X04} ({_io_names[port - _base_port]})", LogLevel.DEBUG);

        int index = port - _base_port;

        if (index == 0)
        {
            if (_mouse_msgs != null)
            {
                byte rc = _mouse_msgs[0];
                _mouse_msgs.RemoveAt(0);
                if (_mouse_msgs.Count == 0)
                    _mouse_msgs = null;
                else
                    ScheduleInterrupt(100);
                return rc;
            }
            return 128;
        }

        if (index == 2)  // ISR
            return (byte)((ports[index] & 0xf0) | (_mouse_msgs != null ? 0 : 4));  // interrupt identification code 2, data ready

        if (index == 5)  // LSR
        {
            if (_mouse_msgs != null)
            {
                bool bit = _reset_on_bit_toggle;
                _reset_on_bit_toggle = !_reset_on_bit_toggle;
                byte v = ports[index];
                v &= 190;
                return (byte)(v | (bit ? 64 + 1 : 0));  // Data ready set, Transmitter empty set
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
            bool rts = (value & 1) == 1;
            if (rts != prts) {
                Log.DoLog($"SerialMouse RTS {prts} -> {rts}, since {_reset_since}", LogLevel.DEBUG);
                prts = rts;
            }

            if (rts == false && prts == true)
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
            else if (rts == true && prts == false && _reset_on && _clock - _reset_since >= 4770000 / 150)
            {
                if (_mouse_msgs == null)
                    _mouse_msgs = new();
                else
                    _mouse_msgs.Clear();
                Log.DoLog($"SerialMouse reset", LogLevel.DEBUG);
                _mouse_msgs.Add(0x4d);  // reset ack
                ScheduleInterrupt(0);
                _reset_on = false;
            }

            prts = rts;
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

    public override bool Tick(int cycles, long clock)
    {
        if (CheckScheduledInterrupt(cycles)) {
            Log.DoLog("Fire SerialPort interrupt", LogLevel.TRACE);
            _pic.RequestInterruptPIC(_irq_nr);
            return true;
        }

        return base.Tick(cycles, clock);
    }
}
