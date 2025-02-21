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
    protected int _irq_nr = 0;
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

    // TODO WHY?!?!
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
                    if ((_timers[i].mode == 0 || _timers[i].mode == 3) && i == 0)
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
            _pic.RequestInterruptPIC(_irq_nr); // Timers are on IRQ0

        return interrupt;
    }
}
