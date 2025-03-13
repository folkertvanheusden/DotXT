using Commons.Music.Midi;

internal class LotechEMS : Device
{
    private int _irq_nr = -1;
    private byte[][] _pages = new byte[256][];
    private byte[] _page_number = new byte[4];
    private const int _page_size = 16384;
    private const int _page_mask = _page_size - 1;

    public LotechEMS()
    {
        Log.Cnsl("LotechEMS instantiated");
        _page_number[0] = 0;
        _page_number[1] = 1;
        _page_number[2] = 2;
        _page_number[3] = 3;

        for(int i=0; i<256; i++)
            _pages[i] = new byte[_page_size];
    }

    public override int GetIRQNumber()
    {
        return _irq_nr;
    }

    public override String GetName()
    {
        return "LotechEMS";
    }

    public override void RegisterDevice(Dictionary <ushort, Device> mappings)
    {
        mappings[0x0260] = this;
        mappings[0x0261] = this;
        mappings[0x0262] = this;
        mappings[0x0263] = this;
    }

    public override (ushort, bool) IO_Read(ushort port)
    {
        return (_page_number[port - 0x260], false);
    }

    public override bool IO_Write(ushort port, ushort value)
    {
        _page_number[port - 0x260] = (byte)value;

        return false;
    }

    public override bool HasAddress(uint addr)
    {
        return addr >= 0xe0000 && addr <= 0xeffff;
    }

    public override void WriteByte(uint addr, byte value)
    {
        int page = (int)(addr / _page_size) & 3;
        _pages[_page_number[page]][addr & _page_mask] = value;
    }

    public override byte ReadByte(uint addr)
    {
        int page = (int)(addr / _page_size) & 3;
        return _pages[_page_number[page]][addr & _page_mask];
    }

    public override bool Tick(int ticks, long ignored)
    {
        return false;
    }
}
