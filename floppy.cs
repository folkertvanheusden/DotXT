class FloppyDisk : Device
{
    i8237 _dma_controller;
    pic8259 _pic;
    PendingInterrupt _pi = new();

    public FloppyDisk()
    {
    }

    public void SetDma(i8237 dma_instance)
    {
        _dma_controller = dma_instance;
    }

    public void SetPic(pic8259 pic_instance)
    {
        _pic = pic_instance;

        _pi.int_vec = _pic.GetInterruptOffset() + 6;
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
        return false;
    }

    public override List<PendingInterrupt> GetPendingInterrupts()
    {
        if (_pi.pending)
        {
            List<PendingInterrupt> rc = new();

            rc.Add(_pi);

            return rc;
        }

        return null;
    }

    public override (byte, bool) IO_Read(ushort port)
    {
        Log.DoLog($"Floppy-IN {port:X4}");

        if (port == 0x3f4)
            return (128, _pi.pending);

        return (0x00, _pi.pending);
    }

    public override bool IO_Write(ushort port, byte value)
    {
        Log.DoLog($"Floppy-OUT {port:X4} {value:X2}");

        if (port == 0x3f2)
            _pi.pending = true;  // FDC enable (controller reset) (IRQ 6)

        return _pi.pending;
    }
}
