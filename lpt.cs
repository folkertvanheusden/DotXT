class LPT : Device
{
    private int _irq_nr = -1;
    private byte [] ports = new byte[3];

    public LPT()
    {
        Log.Cnsl("LPT instantiated");
        _irq_nr = 7;
    }

    public override int GetIRQNumber()
    {
        return _irq_nr;
    }

    public override String GetName()
    {
        return "LPT";
    }

    public override void RegisterDevice(Dictionary <ushort, Device> mappings)
    {
        mappings[0x03bc] = this;
        mappings[0x03bd] = this;
        mappings[0x03be] = this;
    }

    public override byte IO_Read(ushort port)
    {
        Log.DoLog($"LPT IO_Read {port:X04}", LogLevel.DEBUG);
        return ports[port - 0x3bc];
    }

    public override bool IO_Write(ushort port, byte value)
    {
        Log.DoLog($"LPT IO_Write {port:X04} {value:X02}", LogLevel.DEBUG);
        ports[port - 0x3bc] = value;
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
        return false;
    }

    public override bool Tick(int ticks, long ignored)
    {
        return false;
    }
}
