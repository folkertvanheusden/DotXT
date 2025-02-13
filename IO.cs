internal struct Timer
{
    public ushort counter_cur { get; set; }
    public ushort counter_prv { get; set; }
    public ushort counter_ini { get; set; }
    public int    mode        { get; set; }
    public int    latch_type  { get; set; }
    public int    latch_n     { get; set; }
    public int    latch_n_cur { get; set; }
    public bool   is_running  { get; set; }
    public bool   is_pending  { get; set; }
}

internal class i8253 : Device
{
    Timer [] _timers = new Timer[3];
    protected new int _irq_nr = 0;
    i8237 _i8237 = null;
    int clock = 0;

    // using a static seed to make it behave
    // the same every invocation (until threads
    // and timers are introduced)
    private Random _random = new Random(1);

    public i8253()
    {
        for(int i=0; i<_timers.Length; i++)
            _timers[i] = new Timer();
    }

    public override int GetIRQNumber()
    {
        return _irq_nr;
    }

    public override String GetName()
    {
        return "i8253";
    }

    public override void RegisterDevice(Dictionary <ushort, Device> mappings)
    {
        mappings[0x0040] = this;
        mappings[0x0041] = this;
        mappings[0x0042] = this;
        mappings[0x0043] = this;
    }

    public override (byte, bool) IO_Read(ushort port)
    {
        if (port == 0x0040)
            return (GetCounter(0), _timers[0].is_pending);

        if (port == 0x0041)
            return (GetCounter(1), _timers[1].is_pending);

        if (port == 0x0042)
            return (GetCounter(2), _timers[2].is_pending);

        return (0xaa, false);
    }

    public override bool IO_Write(ushort port, byte value)
    {
        if (port == 0x0040)
            LatchCounter(0, value);
        else if (port == 0x0041)
            LatchCounter(1, value);
        else if (port == 0x0042)
            LatchCounter(2, value);
        else if (port == 0x0043)
            Command(value);

        return _timers[0].is_pending || _timers[1].is_pending || _timers[2].is_pending;
    }

    public override void SyncClock(int clock)
    {
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

    public void SetDma(i8237 dma_instance)
    {
        _i8237 = dma_instance;
    }

    private void LatchCounter(int nr, byte v)
    {
#if DEBUG
        Log.DoLog($"OUT 8253: timer {nr} to {v} (type {_timers[nr].latch_type}, {_timers[nr].latch_n_cur} out of {_timers[nr].latch_n})", true);
#endif

        if (_timers[nr].latch_n_cur > 0)
        {
            if (_timers[nr].latch_type == 1)
            {
                _timers[nr].counter_ini &= 0xff00;
                _timers[nr].counter_ini |= v;
                // assert _timers[nr].latch_n_cur == 1
            }
            else if (_timers[nr].latch_type == 2)
            {
                _timers[nr].counter_ini &= 0x00ff;
                _timers[nr].counter_ini |= (ushort)(v << 8);
                // assert _timers[nr].latch_n_cur == 1
            }
            else if (_timers[nr].latch_type == 3)
            {
                if (_timers[nr].latch_n_cur == 2)
                {
                    _timers[nr].counter_ini &= 0xff00;
                    _timers[nr].counter_ini |= v;
                }
                else
                {
                    _timers[nr].counter_ini &= 0x00ff;
                    _timers[nr].counter_ini |= (ushort)(v << 8);
                }

                // assert _timers[nr].latch_n_cur >= 1
            }

            _timers[nr].latch_n_cur--;

            if (_timers[nr].latch_n_cur == 0)
            {
#if DEBUG
                Log.DoLog($"OUT 8253: timer {nr} started (count start: {_timers[nr].counter_ini})", true);
#endif

                _timers[nr].latch_n_cur = _timers[nr].latch_n;  // restart setup
                _timers[nr].counter_cur = _timers[nr].counter_ini;
                _timers[nr].is_running = true;
                _timers[nr].is_pending = false;
            }
        }
    }

    private byte AddNoiseToLSB(int nr)
    {
        ushort current_prv = _timers[nr].counter_prv;

        _timers[nr].counter_prv = _timers[nr].counter_cur;

        if (Math.Abs(_timers[nr].counter_cur - current_prv) >= 2)
            return (byte)(_random.Next(2) == 1 ? _timers[nr].counter_cur ^ 1 : _timers[nr].counter_cur);

        return (byte)_timers[nr].counter_cur;
    }

    private byte GetCounter(int nr)
    {
#if DEBUG
        Log.DoLog($"OUT 8253: GetCounter {nr}: {(byte)_timers[nr].counter_cur} ({_timers[nr].latch_type}|{_timers[nr].latch_n_cur}/{_timers[nr].latch_n})", true);
#endif

        byte rc = 0;

        if (_timers[nr].latch_type == 1)
            rc = AddNoiseToLSB(nr);
        else if (_timers[nr].latch_type == 2)
            rc = (byte)(_timers[nr].counter_cur >> 8);
        else if (_timers[nr].latch_type == 3)
        {
            if (_timers[nr].latch_n_cur == 2)
                rc = AddNoiseToLSB(nr);
            else
                rc = (byte)(_timers[nr].counter_cur >> 8);
        }

        _timers[nr].latch_n_cur--;

        if (_timers[nr].latch_n_cur == 0)
            _timers[nr].latch_n_cur = _timers[nr].latch_n;

        return rc;
    }

    private void Command(byte v)
    {
        int nr    = v >> 6;
        int latch = (v >> 4) & 3;
        int mode  = (v >> 1) & 7;
        int type  = v & 1;

        if (latch != 0)
        {
#if DEBUG
            Log.DoLog($"OUT 8253: command timer {nr}, latch {latch}, mode {mode}, type {type}", true);
#endif
            _timers[nr].mode       = mode;
            _timers[nr].latch_type = latch;
            _timers[nr].is_running = false;

            _timers[nr].counter_ini = 0;

            if (_timers[nr].latch_type == 1 || _timers[nr].latch_type == 2)
                _timers[nr].latch_n = 1;
            else if (_timers[nr].latch_type == 3)
                _timers[nr].latch_n = 2;

            _timers[nr].latch_n_cur = _timers[nr].latch_n;
        }
        else
        {
#if DEBUG
            Log.DoLog($"OUT 8253: query timer {nr} (reset value: {_timers[nr].counter_ini}, current value: {_timers[nr].counter_cur})", true);
#endif
        }
    }

    public override bool Tick(int ticks)
    {
        clock += ticks;

#if DEBUG
//        Log.DoLog($"i8253: {clock} cycles, {ticks} added", true);
#endif

        bool interrupt = false;

        while (clock >= 4)
        {
            for(int i=0; i<3; i++)
            {
                if (_timers[i].is_running == false)
                    continue;

                _timers[i].counter_cur--;

#if DEBUG
//                Log.DoLog($"i8253: timer {i} is now {_timers[i].counter_cur}", true);
#endif

                if (_timers[i].counter_cur == 0)
                {
//                    Log.DoLog($"i8253 reset counter", true);

                    // timer 1 is RAM refresh counter
                    if (i == 1)
                        _i8237.TickChannel0();

		    if (_timers[i].mode != 1)
			    _timers[i].counter_cur = _timers[i].counter_ini;

                    // mode 0 generates an interrupt
                    if (_timers[i].mode == 0 || _timers[i].mode == 3)
                    {
                        _timers[i].is_pending = true;
                        interrupt = true;
#if DEBUG
                        Log.DoLog($"i8253: interrupt for timer {i} fires ({_timers[i].counter_ini})", true);
#endif
                    }
                }   
            }

            clock -= 4;
        }

        if (interrupt)
            ScheduleInterrupt(1);  // Timers are on IRQ0

        if (CheckScheduledInterrupt(ticks))
            _pic.RequestInterruptPIC(_irq_nr);

        return interrupt;
    }
}

internal class FlipFlop
{
    bool state = false;

    public bool get_state()
    {
        bool rc = state;
        state = !state;
        return rc;
    }

    public void reset()
    {
        state = false;
    }
}

internal class b16buffer
{
    ushort _value;
    FlipFlop _f;

    public b16buffer(FlipFlop f)
    {
        _f = f;
    }

    public void Put(byte v)
    {
        bool low_high = _f.get_state();

        if (low_high)
        {
            _value &= 0xff;
            _value |= (ushort)(v << 8);
        }
        else
        {
            _value &= 0xff00;
            _value |= v;
        }
    }

    public ushort GetValue()
    {
        return _value;
    }

    public void SetValue(ushort v)
    {
        _value = v;
    }

    public byte Get()
    {
        bool low_high = _f.get_state();

        if (low_high)
            return (byte)(_value >> 8);

        return (byte)(_value & 0xff);
    }
}

internal class i8237
{
    byte [] _channel_page = new byte[4];
    b16buffer [] _channel_address_register = new b16buffer[4];
    b16buffer [] _channel_word_count = new b16buffer[4];
    byte _command;
    bool [] _channel_mask = new bool[4];
    bool [] _reached_tc = new bool[4];
    byte [] _channel_mode = new byte[4];
    FlipFlop _ff = new();
    bool _dma_enabled = true;
    Bus _b;

    public i8237(Bus b)
    {
        for(int i=0; i<4; i++) {
            _channel_address_register[i] = new b16buffer(_ff);
            _channel_word_count[i] = new b16buffer(_ff);
        }

        _b = b;
    }

    public bool Tick(int ticks)
    {
        return false;
    }

    public void TickChannel0()
    {
        // RAM refresh
        _channel_address_register[0].SetValue((ushort)(_channel_address_register[0].GetValue() + 1));

        ushort count = _channel_word_count[0].GetValue();
        count--;
#if DEBUG
//        Log.DoLog($"8237_TickChannel0, mask: {_channel_mask[0]}, tc: {_reached_tc[0]}, mode: {_channel_mode[0]}, dma enabled: {_dma_enabled}, {count}", true);
#endif
        _channel_word_count[0].SetValue(count);

         if (count == 0xffff)
             _reached_tc[0] = true;
    }

    public (byte, bool) In(ushort addr)
    {
        byte v = 0;

        if (addr == 0 || addr == 2 || addr == 4 || addr == 6)
        {
            v = _channel_address_register[addr / 2].Get();
        }

        else if (addr == 1 || addr == 3 || addr == 5 || addr == 7)
        {
            v = _channel_word_count[addr / 2].Get();
        }

        else if (addr == 8)  // status register
        {
            for(int i=0; i<4; i++)
            {
                if (_reached_tc[i])
                {
                    _reached_tc[i] = false;

                    v |= (byte)(1 << i);
                }
            }
        }

        // Log.DoLog($"8237_IN: {addr:X4} {v:X2}", true);

        return (v, false);
    }

    void reset_masks(bool state)
    {
        for(int i=0; i<4; i++)
            _channel_mask[i] = state;
    }

    public bool Out(ushort addr, byte value)
    {
        // Log.DoLog($"8237_OUT: {addr:X4} {value:X2}", true);

        if (addr == 0 || addr == 2 || addr == 4 || addr == 6)
        {
            _channel_address_register[addr / 2].Put(value);
            Log.DoLog($"8237 set channel {addr / 2} to address {_channel_address_register[addr / 2].GetValue()}", true);
        }

        else if (addr == 1 || addr == 3 || addr == 5 || addr == 7)
        {
            _channel_word_count[addr / 2].Put(value);
            Log.DoLog($"8237 set channel {addr / 2} to count {_channel_word_count[addr / 2].GetValue()}", true);
        }

        else if (addr == 8)
        {
            _command = value;

            _dma_enabled = (_command & 4) == 0;
        }

        else if (addr == 0x0a)  // mask
            _channel_mask[value & 3] = (value & 4) == 4;  // dreq enable/disable

        else if (addr == 0x0b)  // mode register
            _channel_mode[value & 3] = value;

        else if (addr == 0x0c)  // reset flipflop
            _ff.reset();

        else if (addr == 0x0d)  // master reset
        {
            reset_masks(true);
            _ff.reset();
            // TODO: clear status
        }

        else if (addr == 0x0e)  // reset masks
        {
            reset_masks(false);
        }

        else if (addr == 0x0f)  // multiple mask
        {
            for(int i=0; i<4; i++)
                _channel_mask[i] = (value & (1 << i)) != 0;
        }
        else if (addr == 0x87)
        {
            _channel_page[0] = (byte)(value & 0x0f);
        }
        else if (addr == 0x83)
        {
            _channel_page[1] = (byte)(value & 0x0f);
        }
        else if (addr == 0x81)
        {
            _channel_page[2] = (byte)(value & 0x0f);
        }
        else if (addr == 0x82)
        {
            _channel_page[3] = (byte)(value & 0x0f);
        }

        return false;
    }

    // used by devices (floppy etc) to send data to memory
    public bool SendToChannel(int channel, byte value)
    {
        if (_dma_enabled == false)
            return false;

        if (_channel_mask[channel])
            return false;

        if (_reached_tc[channel])
            return false;

        ushort addr = _channel_address_register[channel].GetValue();
        uint full_addr = (uint)((_channel_page[channel] * 16) | addr);
        _b.WriteByte(full_addr, value);

        addr++;
         _channel_address_register[channel].SetValue(addr);

         ushort count = _channel_word_count[channel].GetValue();
         count--;
         if (count == 0xffff)
             _reached_tc[channel] = true;
         _channel_word_count[channel].SetValue(count);

        return true;
    }
}

internal class PPI : Device
{
    byte _control = 0;
    bool _dipswitches_high = false;
    byte [] _cache = new byte[4];

    public PPI()
    {
    }

    public override int GetIRQNumber()
    {
        return -1;
    }

    public override String GetName()
    {
        return "PPI";
    }

    public override void RegisterDevice(Dictionary <ushort, Device> mappings)
    {
        mappings[0x0060] = this;
        mappings[0x0061] = this;
        mappings[0x0062] = this;
        mappings[0x0063] = this;
    }

    public override (byte, bool) IO_Read(ushort port)
    {
        Log.DoLog($"PPI::IO_Read: {port:X4}", true);

        if (port == 0x0062)
        {
            byte switches = 0b00100000;  // 1 floppy, CGA80, 256kB, reserved

            if (_dipswitches_high == true)
                return ((byte)(switches & 0x0f), false);

            return ((byte)(switches >> 4), false);
        }

        return (_cache[port - 0x0060], false);
    }

    public override bool IO_Write(ushort port, byte value)
    {
        Log.DoLog($"PPI::IO_Write: {port:X4} {value:X2}", true);

        _cache[port - 0x0060] = value;

        if (port == 0x0061)
        {
            if ((_control & 4) == 4)  // dipswitches selection
                _dipswitches_high = (value & 8) == 8;

            if ((_control & 2) == 2)  // speaker
                return false;

            return false;
        }

        if (port == 0x0063)
        {
            _control = value;
            return false;
        }

        return false;
    }

    public override void SyncClock(int clock)
    {
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

    public override bool Tick(int ticks)
    {
        return false;
    }
}

class IO
{
    private pic8259 _pic;
    private i8237 _i8237;
    private PPI _ppi;

    private Bus _b;

    private Dictionary <ushort, byte> _values = new Dictionary <ushort, byte>();

    private Dictionary <ushort, Device> _io_map = new Dictionary <ushort, Device>();

    private List<Device> _devices;

    private int _clock;

    public IO(Bus b, ref List<Device> devices)
    {
        _b = b;

        _pic = new();
        _i8237 = new(_b);
        _ppi = new();

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
    }

    public pic8259 GetPIC()
    {
	    return _pic;
    }

    public (byte, bool) In(ushort addr)
    {
        // Log.DoLog($"IN: {addr:X4}", true);

        foreach(var device in _devices)
            device.SyncClock(_clock);

        if (addr <= 0x000f || addr == 0x81 || addr == 0x82 || addr == 0x83 || addr == 0xc2)
            return _i8237.In(addr);

        if (addr == 0x0008)  // DMA status register
            return (0x0f, false);  // 'transfer complete'

        if (addr == 0x0020 || addr == 0x0021)  // PIC
            return _pic.In(addr);

        if (addr == 0x0210)  // verify expansion bus data
            return (0xa5, false);

        if (_io_map.ContainsKey(addr))
            return _io_map[addr].IO_Read(addr);

#if DEBUG
        Log.DoLog($"IN: I/O port {addr:X4} not implemented", true);
#endif

//        if (_values.ContainsKey(addr))
 //           return (_values[addr], false);

        return (0, false);
    }

    public bool Tick(int ticks, int clock)
    {
        bool rc = false;

        foreach(var device in _devices)
            rc |= device.Tick(ticks);

        _i8237.Tick(ticks);

        _clock = clock;

        return rc;
    }

    public bool Out(ushort addr, ushort value)
    {
        // Log.DoLog($"OUT: I/O port {addr:X4} ({value:X2})", true);

        if (addr <= 0x000f || addr == 0x81 || addr == 0x82 || addr == 0x83 || addr == 0xc2) // 8237
            return _i8237.Out(addr, (byte)value);

        else if (addr == 0x0020 || addr == 0x0021)  // PIC
            return _pic.Out(addr, (byte)value);

        else if (addr == 0x0080)
            Log.DoLog($"Manufacturer systems checkpoint {value:X2}", true);

        else if (addr == 0x0322)
        {
            int harddisk_interrupt_nr = 14;

//FIXME            if (scheduled_interrupts.ContainsKey(harddisk_interrupt_nr) == false)
//FIXME                scheduled_interrupts[harddisk_interrupt_nr] = 31;  // generate (XT disk-)controller select pulse (IRQ 5)

#if DEBUG
            Log.DoLog($"OUT: I/O port {addr:X4} ({value:X2}) generate controller select pulse");
#endif
        }

        else
        {
            if (_io_map.ContainsKey(addr))
                return _io_map[addr].IO_Write(addr, (byte)value);

#if DEBUG
            // Log.DoLog($"OUT: I/O port {addr:X4} ({value:X2}) not implemented", true);
#endif
        }

        _values[addr] = (byte)value;

        return false;
    }
}
