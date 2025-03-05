using Commons.Music.Midi;

internal class Adlib : Device
{
    private int _irq_nr = -1;
    private byte _address = 0;
    private AdlibChannel [] channels = new AdlibChannel[9];
    private readonly System.Threading.Lock _channel_lock = new();
    private readonly System.Threading.Lock _samples_lock = new();
    private Thread _thread = null;
    private short [] _samples = new short[100];
    private int _samples_version = 0;
    private readonly object _sync_primitive = new object();

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
        mappings[0x0388] = this;
        mappings[0x0389] = this;
    }

    public override (ushort, bool) IO_Read(ushort port)
    {
        Log.DoLog($"Adlib::IO_Read {port:X04}", LogLevel.TRACE);

        return (0x00, false);
    }

    public static void Player(object o_parameters)
    {
        Log.Cnsl("Adlib Player-thread started");

        int freq = 44100;
        int interval = 100;
        Adlib a = (Adlib)o_parameters;
        int [] frequencies = new int[9];
        double [] phase_add = new double[9];
        double [] phase = new double[9];

        for(;;)
        {
            for(int ch_nr=0; ch_nr<9; ch_nr++)
            {
                var channel = a.GetChannel(ch_nr);
                if (channel.updated == false)
                    continue;

                if (channel.on_)
                {
                    frequencies[ch_nr] = channel.base_frequency * (1 << channel.octave);
                    Log.DoLog($"Adlib: set frequency of channel {ch_nr} to frequency {frequencies[ch_nr]} Hz (octave {channel.octave}, base freq {channel.base_frequency})", LogLevel.TRACE);
                    phase_add[ch_nr] = 2.0 * Math.PI * frequencies[ch_nr] / freq;
                    phase_add[ch_nr] = 0.0;
                    phase[ch_nr] = 0.0;
                }
                else
                {
                    Log.DoLog($"Adlib: set channel {ch_nr} off", LogLevel.TRACE);
                    frequencies[ch_nr] = 0;
                }
            }

            int count = freq/interval;
            short [] samples = new short[count];

            for(int sample=0; sample<count; sample++)
            {
                double v = 0.0;

                for(int ch_nr=0; ch_nr<9; ch_nr++)
                {
                    if (frequencies[ch_nr] <= 0.0)
                        continue;

                    v += Math.Sin(phase[ch_nr]);
                    phase[ch_nr] += phase_add[ch_nr];
                }

                if (v < -1.0)
                    v = -1.0;
                else if (v > 1.0)
                    v = 1.0;
    
                samples[sample] = (short)(v * 32767);
            }

            a.PushSamples(samples);

            Thread.Sleep(1000 / interval);  // depending on how time it took to calculate all of this TODO
        }

        Log.Cnsl("Adlib Player-thread terminating");
    }

    public AdlibChannel GetChannel(int channel)
    {
        lock(_channel_lock)
        {
            var data = channels[channel];
            channels[channel].updated = false;
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

    public override bool IO_Write(ushort port, ushort value)
    {
        Log.DoLog($"Adlib::IO_Write {port:X4} {value:X4}", LogLevel.TRACE);

        if (port == 0x388)
            _address = (byte)value;
        else if (port == 0x389)
        {
            lock(_channel_lock)
            {
                if (_address >= 0xa0 && _address <= 0xa8)
                {
                    int channel = _address - 0xa0;
                    channels[channel].base_frequency = (channels[channel].base_frequency & 0x300) | value;
                    channels[channel].updated = true;
                }
                else if (_address >= 0xb0 && _address <= 0xb8)
                {
                    int channel = _address - 0xb0;
                    channels[channel].base_frequency = (channels[channel].base_frequency & 0xff) | ((value & 3) << 8);
                    channels[channel].octave = (value >> 2) & 7;
                    channels[channel].on_ = (value & 32) != 0;
                    channels[channel].updated = true;
                }
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
        return false;
    }
}

internal struct AdlibChannel
{
    public int base_frequency { get; set; }
    public bool on_ { get; set; }
    public int octave { get; set; }
    public bool updated { get; set; }
};
