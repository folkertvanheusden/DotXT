using Commons.Music.Midi;

internal class Adlib : Device
{
    private int _irq_nr = -1;
    private byte [] _address = new byte[2];
    private AdlibChannel [,] channels = new AdlibChannel[2, 9];
    private readonly System.Threading.Lock _channel_lock = new();
    private readonly System.Threading.Lock _samples_lock = new();
    private Thread _thread = null;
    private short [] _samples = new short[100];
    private byte [,] _registers = new byte[2, 256];
    private int _samples_version = 0;
    private readonly object _sync_primitive = new object();
    private long [] _prev_timer_1 = new long[2];
    private long [] _prev_timer_2 = new long[2];
    private byte [] _status_byte = new byte[2];
    private const double volume_scaler = 9;

    public Adlib()
    {
        Log.Cnsl("Adlib instantiated");

        _thread = new Thread(Adlib.Player);
        _thread.Name = "adlib-thread";
        _thread.Start(this);
    }

    public override int GetIRQNumber()
    {
        return _irq_nr;
    }

    public override String GetName()
    {
        return "Adlib";
    }

    public override void RegisterDevice(Dictionary <ushort, Device> mappings)
    {
        mappings[0x0220] = this;  // soundblaster left
        mappings[0x0221] = this;
        mappings[0x0222] = this;  // soundblaster right
        mappings[0x0223] = this;
        mappings[0x0388] = this;  // adlib / mono
        mappings[0x0389] = this;
    }

    public override (ushort, bool) IO_Read(ushort port)
    {
        Log.DoLog($"Adlib::IO_Read {port:X04}", LogLevel.TRACE);

        if (port == 0x0388 || port == 0x0220)
            return (_status_byte[0], false);

        if (port == 0x0222)
            return (_status_byte[1], false);

        return (0x00, false);
    }

    public byte GetRegister(int ear, byte i)
    {
        return _registers[ear, i];
    }

    public static void Player(object o_parameters)
    {
        Log.Cnsl("Adlib Player-thread started");

        int freq = 44100;
        int interval = 100;
        Adlib a = (Adlib)o_parameters;
        int [,] frequencies = new int[2,9];
        double [,] phase_add = new double[2,9];
        double [,] phase = new double[2,9];
        double [,] volume = new double[2,9];
        int [,] waveform = new int[2,9];

        FilterButterworth filter = new FilterButterworth(freq / 2 * 0.90, freq, FilterButterworth.PassType.Lowpass, Math.Sqrt(2.0));

        for(;;)
        {
            long start_ts = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;

            for(int ear=0; ear<2; ear++)
            {
                for(int ch_nr=0; ch_nr<9; ch_nr++)
                {
                    var channel = a.GetChannel(ear, ch_nr);
                    if (channel.updated == false)
                        continue;

                    if (channel.on_)
                    {
                        frequencies[ear,ch_nr] = channel.f_number * 49716 / (2 << (19 - channel.block));

                        Log.DoLog($"Adlib: set frequency of channel {ear}:{ch_nr} to frequency {frequencies[ear,ch_nr]} Hz (block {channel.block}, f_number {channel.f_number})", LogLevel.TRACE);
                        phase_add[ear,ch_nr] = 2.0 * Math.PI * frequencies[ear,ch_nr] / freq;
                        phase[ear,ch_nr] = 0.0;
                        volume[ear,ch_nr] = channel.volume / volume_scaler;
                        waveform[ear,ch_nr] = channel.waveform;
                    }
                    else
                    {
                        Log.DoLog($"Adlib: set channel {ear}:{ch_nr} off", LogLevel.TRACE);
                        phase_add[ear,ch_nr] = 0.0;
                        frequencies[ear,ch_nr] = 0;
                    }
                }
            }

            int count = freq/interval * 10 / 9;  // 11.1% more samples because of jitter
            short [] samples = new short[count * 2];
            bool too_loud = false;
            double min = 10;
            double max = -10;
            int buffer_offset = 0;
            for(int sample=0; sample<count; sample++)
            {
                for(int ear=0; ear<2; ear++) {
                    double v = 0.0;

                    for(int ch_nr=0; ch_nr<9; ch_nr++)
                    {
                        if (frequencies[ear,ch_nr] <= 0.0)
                            continue;

                        double cur_v = Math.Sin(phase[ear,ch_nr]) * volume[ear,ch_nr];
                        if ((a.GetRegister(ear, 1) & 32) != 0)
                        {
                            if (waveform[ear,ch_nr] == 0)
                                v += cur_v;
                            else if (waveform[ear,ch_nr] == 1)
                            {
                                if (cur_v >= 0)
                                    v += cur_v;
                            }
                            else if (waveform[ear,ch_nr] == 2)
                                v += Math.Abs(cur_v);
                            else  // 3
                            {
                                if (Math.IEEERemainder(Math.Abs(phase[ear,ch_nr]), phase_add[ear,ch_nr]) <= phase_add[ear,ch_nr] / 4)
                                    v += cur_v;
                            }
                        }
                        else
                        {
                            v += cur_v;
                        }

                        phase[ear,ch_nr] += phase_add[ear,ch_nr];
                    }

                    if (v < -1.0)
                    {
                        min = Math.Min(min, v);
                        v = -1.0;
                        too_loud = true;
                    }
                    else if (v > 1.0)
                    {
                        max = Math.Max(max, v);
                        v = 1.0;
                        too_loud = true;
                    }

                    filter.Update(v);
                    samples[buffer_offset++] = (short)(filter.Value * 32767);
                }
            }

            a.PushSamples(samples);

            if (too_loud)
                Log.DoLog($"Adlib: audio is clipping (too loud: {min}...{max})", LogLevel.DEBUG);

            long end_ts = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;

            Thread.Sleep((int)((1000 - (end_ts - start_ts)) / interval));
        }

        Log.Cnsl("Adlib Player-thread terminating");
    }

    public AdlibChannel GetChannel(int ear, int channel)
    {
        lock(_channel_lock)
        {
            var data = channels[ear, channel];
            channels[ear, channel].updated = false;
            return data;
        }
    }

    public void PushSamples(short [] samples)
    {
        lock(_samples_lock)
        {
            _samples = samples;
            _samples_version++;
        }

        lock(_sync_primitive)
        {
            Monitor.Pulse(_sync_primitive);
        }
    }

    public (short [], int) GetSamples(int version)
    {
        lock(_sync_primitive)
        {
            while(version == _samples_version)
            {
                Monitor.Wait(_sync_primitive);
            }

            return (_samples, _samples_version);
        }
    }

    public void SetFNumber(int ear, int channel, byte value)
    {
        channels[ear, channel].f_number = (channels[ear, channel].f_number & 0x300) | value;
    }

    public void SetBlock(int ear, int channel, byte value)
    {
        channels[ear, channel].f_number = (channels[ear, channel].f_number & 0xff) | ((value & 3) << 8);
        channels[ear, channel].block = (value >> 2) & 7;
        channels[ear, channel].on_ = (value & 32) != 0;
        channels[ear, channel].updated = true;
    }

    public void SetVolume(int ear, int channel, byte value)
    {
        channels[ear, channel - 1].volume = (63 - (value & 63)) / 63.0;
    }

    public void SetWaveform(int ear, int channel, byte value)
    {
        channels[ear, channel - 1].waveform = value & 3;
    }

    public void SetParameters(int ear, byte value)
    {
        byte [] map = new byte[] { 1, 2, 3, 1, 2, 3, 0, 0, 4, 5, 6, 4, 5, 6, 0, 0, 7, 8, 9, 7, 8, 9 };

        if (_address[ear] >= 0xa0 && _address[ear] <= 0xa8)
        {
            int channel = _address[ear] - 0xa0;
            SetFNumber(ear, channel, value);
        }
        else if (_address[ear] >= 0xb0 && _address[ear] <= 0xb8)
        {
            int channel = _address[ear] - 0xb0;
            SetBlock(ear, channel, value);
        }
        else if (_address[ear] >= 0x40 && _address[ear] <= 0x55)
        {
            int channel = map[_address[ear] - 0x40];
            if (channel > 0)
                SetVolume(ear, channel, value);
            Log.DoLog($"Set channel {ear}:{channel - 1} to volume {channels[ear, channel - 1].volume}", LogLevel.DEBUG);
        }
        else if (_address[ear] >= 0xe0 && _address[ear] <= 0xf5)
        {
            int channel = map[_address[ear] - 0xe0];
            if (channel > 0)
                SetWaveform(ear, channel, value);
            Log.DoLog($"Set channel {ear}:{channel - 1} to waveform {channels[ear, channel - 1].waveform}", LogLevel.DEBUG);
        }
    }

    public override bool IO_Write(ushort port, ushort value)
    {
        Log.DoLog($"Adlib::IO_Write {port:X4} {value:X4}", LogLevel.TRACE);

        if (port == 0x388)
            _address[0] = _address[1] = (byte)value;
        else if (port == 0x220)
            _address[0] = (byte)value;
        else if (port == 0x222)
            _address[1] = (byte)value;
        else if (port == 0x389 || port == 0x221 || port == 0x223)
        {
            if (port == 0x221 || port == 0x389)
                _registers[0, _address[0]] = (byte)value;
            if (port == 0x223 || port == 0x389)
                _registers[1, _address[1]] = (byte)value;

            if (_address[0] == 4)
                _status_byte[0] = 0;
            if (_address[1] == 4)
                _status_byte[1] = 0;

            lock(_channel_lock)
            {
                if (port == 0x221 || port == 0x389)
                    SetParameters(0, (byte)value);
                if (port == 0x223 || port == 0x389)
                    SetParameters(1, (byte)value);
            }
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

    public override bool Tick(int ticks, long clock)
    {
        for(int ear=0; ear<2; ear++)
        {
            const long timer_1_interval = 4770000 / 80;
            if (clock - _prev_timer_1[ear] >= timer_1_interval && (_registers[ear, 4] & 1) != 0 && (_registers[ear, 4] & 64) == 0 && (_registers[ear, 4] & 128) == 0)
            {
                _prev_timer_1[ear] += timer_1_interval;
                _status_byte[ear] |= 128 + 64;
            }

            const long timer_2_interval = 4770000 / 320;
            if (clock - _prev_timer_2[ear] >= timer_2_interval && (_registers[ear, 4] & 2) != 0 && (_registers[ear, 4] & 32) == 0 && (_registers[ear, 4] & 128) == 0)
            {
                _prev_timer_2[ear] += timer_2_interval;
                _status_byte[ear] |= 128 + 32;
            }
        }

        return false;
    }
}

internal struct AdlibChannel
{
    public int f_number { get; set; }
    public bool on_ { get; set; }
    public int block { get; set; }
    public bool updated { get; set; }
    public double volume { get; set; }
    public int waveform { get; set; }
};
