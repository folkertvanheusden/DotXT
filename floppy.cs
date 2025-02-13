internal enum DataState
{
    NotSet,
    WaitCmd,
    HaveData,
    WantData
}

class FloppyDisk : Device
{
    private byte [] _registers = new byte[8];
    private i8237 _dma_controller = null;
    protected new int _irq_nr = 6;
    private bool _dma = false;
    private string [] _io_names = new string[] { "status reg a", "status reg b", "digital output reg", "tape drive reg", "main status reg", "data fifo", null, "digital input reg", "cfg control reg" };
    private DataState _data_state = DataState.NotSet;
    private byte [] _data = null;
    private int _data_offset = 0;
    private byte _command = 255;

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
	    return _data_state == DataState.HaveData;
    }

    public bool FifoExpectsData()
    {
	    return _data_state == DataState.WantData;
    }

    public override (byte, bool) IO_Read(ushort port)
    {
        Log.DoLog($"Floppy-IN {_io_names[port - 0x3f0]}: {port:X4}", true);

        if (port == 0x3f4)  // main status register
	{
	    byte rc = 0;
	    if (_data_state == DataState.WantData || _data_state == DataState.WaitCmd || _data_state == DataState.HaveData)
		    rc |= 128;  // Data register is ready for data transfer
	    if (_data_state == DataState.HaveData)
		    rc |= 64;  // has data for cpu
	    if (_dma == false)
		    rc |= 32; 
            Log.DoLog($"Floppy-IN returns {rc:X2}");
	    return (rc, false);
	}

        if (port == 0x3f5)  // fifo
	{
		byte rc = 0;
		if (_data_state == DataState.HaveData) {
			rc = _data[_data_offset++];
			if (_data_offset == _data.Count())
			{
				_data_state = DataState.WaitCmd;
			}
		}
		else
		{
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

	bool want_interrupt = false;

	_registers[port - 0x3f0] = value;

        if (port == 0x3f2)  // digital output register
	{
		if ((value & 4) == 0)
		{
		    ScheduleInterrupt(2);  // FDC enable (controller reset) (IRQ 6)
		    _data_state = DataState.WaitCmd;
		}
		_dma = (value & 8) == 1;
	}

	else if (port == 0x3f5)  // data fifo
	{
		if (_data_state != DataState.WaitCmd && _data_state != DataState.WantData)
		{
			Log.DoLog($"Floppy-OUT was in {_data_state} mode, going to WaitCmd (forcibly)");
			_data_state = DataState.WaitCmd;
		}

		if (_data_state == DataState.WaitCmd)
		{
			byte cmd = (byte)(value & 31);
			if (cmd == 8)
			{
				Log.DoLog($"Floppy-OUT command SENSE INTERRUPT STATUS");
				_data = new byte[2];
				_data[0] = 0xc0;  // TODO | drive_number
				_data[1] = 0;  // cylinder number
				_data_offset = 0;
				_data_state = DataState.HaveData;
				want_interrupt = true;
			}
			else if (cmd == 6)
			{
				Log.DoLog($"Floppy-OUT command READ DATA");
				_command = cmd;
				_data = new byte[8];
				_data_offset = 0;
				_data_state = DataState.WantData;
				want_interrupt = true;
			}
			else
			{
				Log.DoLog($"Floppy-OUT command {cmd:X2} not implemented ({value:X2})");
			}
		}
		else if (_data_state == DataState.WantData)
		{
			_data[_data_offset++] = value;
			if (_data_offset == _data.Count())
			{
				if (_command == 6)  // READ DATA
				{
					// TODO
				}
				else
				{
					Log.DoLog($"Floppy-OUT unexpected command-after-data {_command:X2}");
				}

				_data_state = DataState.WaitCmd;
			}
		}
		else
		{
			Log.DoLog($"Floppy-OUT invalid state ({_data_state})");
		}
	}

        return want_interrupt;
    }
}
