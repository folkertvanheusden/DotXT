// TIMER

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
    public bool   is_bcd      { get; set; }
}

internal class i8253 : Device
{
    private Timer [] _timers = new Timer[3];
    private protected int _irq_nr = 0;
    private i8237 _i8237 = null;
    private long clock = 0;
    private string [] _mode_names = new string[] { "interrupt on terminal count", "hardware retriggerable one-shot", "rate generator", "square wave", "software triggered strobe", "hardware triggered strobe", "rate generator", "square wave" };

    // using a static seed to make it behave
    // the same every invocation (until threads
    // and timers are introduced)
    private Random _random = new Random(1);

    public i8253()
    {
        for(int i=0; i<_timers.Length; i++)
            _timers[i] = new Timer();
    }

    public override List<string> GetState()
    {
        List<string> out_ = new();

        for(int i=0; i<_timers.Length; i++)
        {
            Timer t = _timers[i];
            out_.Add($"Timer {i}: counter cur/prv/ini {t.counter_cur}/{t.counter_prv}/{t.counter_ini}, mode {t.mode} ({_mode_names[t.mode]}) running {t.is_running} pending {t.is_pending}, BCD: {t.is_bcd}");
        }

        return out_;
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

    public override byte IO_Read(ushort port)
    {
        if (port == 0x0040)
            return GetCounter(0);

        if (port == 0x0041)
            return GetCounter(1);

        if (port == 0x0042)
            return GetCounter(2);

        return 0xaa;
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

    public override List<Tuple<uint, int> > GetAddressList()
    {
        return new() { };
    }

    public override void WriteByte(uint offset, byte value)
    {
    }

    public override byte ReadByte(uint offset)
    {
        return 0xee;
    }

    public override void SetDma(i8237 dma_instance)
    {
        _i8237 = dma_instance;
    }

    private void LatchCounter(int nr, byte v)
    {
        Log.DoLog($"OUT 8253: timer {nr} to {v} (type {_timers[nr].latch_type}, {_timers[nr].latch_n_cur} out of {_timers[nr].latch_n})", LogLevel.DEBUG);

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
                Log.DoLog($"OUT 8253: timer {nr} started (count start: {_timers[nr].counter_ini})", LogLevel.DEBUG);

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
        Log.DoLog($"OUT 8253: GetCounter {nr}: {(byte)_timers[nr].counter_cur} ({_timers[nr].latch_type}|{_timers[nr].latch_n_cur}/{_timers[nr].latch_n})", LogLevel.DEBUG);

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
            Log.DoLog($"OUT 8253: command timer {nr}, latch {latch}, mode {mode}, type {type}", LogLevel.DEBUG);
            _timers[nr].mode = mode;
            _timers[nr].latch_type = latch;
            _timers[nr].is_running = false;
            _timers[nr].is_bcd = type == 1;

            _timers[nr].counter_ini = 0;

            if (_timers[nr].latch_type == 1 || _timers[nr].latch_type == 2)
                _timers[nr].latch_n = 1;
            else if (_timers[nr].latch_type == 3)
                _timers[nr].latch_n = 2;

            _timers[nr].latch_n_cur = _timers[nr].latch_n;
        }
        else
        {
            Log.DoLog($"OUT 8253: query timer {nr} (reset value: {_timers[nr].counter_ini}, current value: {_timers[nr].counter_cur})", LogLevel.DEBUG);
        }
    }

    public override bool Ticks()
    {
        return true;
    }

    public override bool Tick(int ticks, long ignored)
    {
        clock += ticks;

        bool interrupt = false;

        while (clock >= 4)
        {
            for(int i=0; i<3; i++)
            {
                if (_timers[i].is_running == false)
                    continue;

                _timers[i].counter_cur--;

                if (_timers[i].counter_cur == 0)
                {
                    // timer 1 is RAM refresh counter
                    if (i == 1)
                        _i8237.TickChannel0();

                    if (_timers[i].mode != 1)
                        _timers[i].counter_cur = _timers[i].counter_ini;

                    if (i == 0)
                    {
                        _timers[i].is_pending = true;
                        interrupt = true;
                        Log.DoLog($"i8253: interrupt for timer {i} fires ({_timers[i].counter_ini})", LogLevel.TRACE);
                    }
                }   
            }

            clock -= 4;
        }

        if (interrupt)
            _pic.RequestInterruptPIC(_irq_nr);  // Timers are on IRQ0

        return interrupt;
    }
}
