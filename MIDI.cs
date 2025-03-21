using Commons.Music.Midi;

internal class MIDI : Device
{
    private int _irq_nr = -1;
    private IMidiAccess _instance = MidiAccessManager.Default;
    private IMidiOutput _midi_out;
    private byte [] _buffer = null;
    private int _buffer_offset = 0;

    public MIDI()
    {
        Log.Cnsl("MIDI instantiated");
        _midi_out = _instance.OpenOutputAsync(_instance.Outputs.Last().Id).Result;
    }

    public override int GetIRQNumber()
    {
        return _irq_nr;
    }

    public override String GetName()
    {
        return "MIDI";
    }

    public override void RegisterDevice(Dictionary <ushort, Device> mappings)
    {
        mappings[0x0330] = this;
        mappings[0x0331] = this;
    }

    public override byte IO_Read(ushort port)
    {
        if (port == 0x331)
            return 0;

        return 0xaa;
    }

    public override bool IO_Write(ushort port, byte value)
    {
        // maybe buffer upto the expected byte-count first?
        if (port == 0x330)
        {
            if (_buffer == null)
            {
                _buffer = new byte[3];  // TODO
                _buffer_offset = 0;
            }

            if (_buffer_offset < _buffer.Length)
            {
                _buffer[_buffer_offset++] = value;
            }

            if (_buffer_offset == _buffer.Length)
            {
                _midi_out.Send(_buffer, 0, _buffer.Length, 0);
                _buffer = null;
            }
        }

        return false;
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

    public override bool Ticks()
    {
        return false;
    }

    public override bool Tick(int ticks, long ignored)
    {
        return false;
    }
}
