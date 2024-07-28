class FloppyDisk : Device
{
    private i8237 _dma_controller = null;
    protected new int _irq_nr = 6;

    public FloppyDisk()
    {
    }

    public override int GetIRQNumber()
    {
        return _irq_nr;
    }

    public void SetDma(i8237 dma_instance)
    {
        _dma_controller = dma_instance;
    }

    public override void SetPic(pic8259 pic_instance)
    {
        _pic = pic_instance;
    }

    public override String GetName()
    {
        return "FloppyDisk";
    }

    public override void RegisterDevice(Dictionary <ushort, Device> mappings)
    {
        for(ushort port=0x03f0; port<0x03f8; port++)
            mappings[port] = this;
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

    public override void SyncClock(int clock)
    {
    }

    public override bool Tick(int cycles)
    {
        if (CheckScheduledInterrupt(cycles))
            _pic.RequestInterrupt(_irq_nr + _pic.GetInterruptOffset());

        return false;
    }

    public override (byte, bool) IO_Read(ushort port)
    {
        Log.DoLog($"Floppy-IN {port:X4}", true);

        if (port == 0x3f4)
            return (128, false);

        return (0x00, false);
    }

    public override bool IO_Write(ushort port, byte value)
    {
        Log.DoLog($"Floppy-OUT {port:X4} {value:X2}", true);

        if (port == 0x3f2)
            ScheduleInterrupt(100);  // FDC enable (controller reset) (IRQ 6), 100 cycles is a guess

        return false;  // TODO
    }
}
