class FloppyDisk : Device
{
    private byte [] _registers = new byte[8];
    private i8237 _dma_controller = null;
    protected new int _irq_nr = 6;
    private byte [] _fifo = null;
    private int _fifo_offset = 0;
    private bool _fifo_get = false;
    private bool _dma = false;
    private string [] _io_names = new string[] { "status reg a", "status reg b", "digital output reg", "tape drive reg", "main status reg", "data fifo", null, "digital input reg", "cfg control reg" };
    private bool _expecting_cmd = false;

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
	{
            if (port == 0x3f6)
		    continue;
            mappings[port] = this;
	}
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
            _pic.RequestInterruptPIC(_irq_nr);

        return false;
    }

    public bool FifoHasDataForCpu()
    {
	    return _fifo_get && _fifo != null && _fifo_offset < _fifo.Count();
    }

    public bool FifoExpectsData()
    {
	    return _fifo_get == false && _fifo != null && _fifo_offset < _fifo.Count();
    }

    public override (byte, bool) IO_Read(ushort port)
    {
        Log.DoLog($"Floppy-IN {_io_names[port - 0x3f0]}: {port:X4}", true);

        if (port == 0x3f4)  // main status register
	{
	    byte rc = 0;
	    if (_fifo != null)
		    rc |= 128;  // Data register is ready for data transfer
	    if (FifoHasDataForCpu())
		    rc |= 64;  // has data for cpu
	    if (_dma == false)
		    rc |= 32; 
            Log.DoLog($"Floppy-IN returns {rc:X2}");
	    return (rc, false);
	}

        if (port == 0x3f5)  // fifo
	{
		byte rc = 0;
		if (_fifo == null)
			rc = 0xee;
		else if (_fifo_offset < _fifo.Count())
			rc = _fifo[_fifo_offset++];
		else
		{
			_fifo = null;
			_fifo_offset = 0;
			rc = 0xaa;
		}
                Log.DoLog($"Floppy-IN returns {rc:X2}");
		return (rc, false);
	}

        return (_registers[port - 0x3f0], false);
    }

    public override bool IO_Write(ushort port, byte value)
    {
        Log.DoLog($"Floppy-OUT {_io_names[port - 0x3f0]}: {port:X4} {value:X2}", true);

	_registers[port - 0x3f0] = value;

        if (port == 0x3f2)  // digital output register
	{
		if ((value & 4) == 0)
		{
		    ScheduleInterrupt(2);  // FDC enable (controller reset) (IRQ 6)

		    _fifo = new byte[3];
		    _fifo[0] = 0;
		    _fifo[1] = 0;
		    _fifo[2] = 0;
		    _fifo_get = false;
		    _expecting_cmd = true;
		}
		_dma = (value & 8) == 1;
	}

	else if (port == 0x3f5)  // data fifo
	{
		if (_expecting_cmd)
		{
			if (value == 8)
			{
			    _fifo = new byte[2];
			    _fifo_get = false;
			}
		}
	}

        return false;  // TODO
    }
}
