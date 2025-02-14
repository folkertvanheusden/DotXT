internal enum DataState { NotSet, WaitCmd, HaveData, WantData }

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
    private string _filename = null;
    private bool _just_resetted = false;
    private bool _busy = false;
    private int cylinder = 0;  // TODO per drive
    private int head = 0;  // TODO per drive

    public FloppyDisk(string filename)
    {
        _filename = filename;
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
        Log.DoLog($"Floppy-IN {_io_names[port - 0x3f0]}: {port:X4} {_data_state}", true);

        if (port == 0x3f4)  // main status register
        {
            byte rc = 0;
            if (_data_state == DataState.WantData || _data_state == DataState.WaitCmd || _data_state == DataState.HaveData)
                rc |= 128;  // Data register is ready for data transfer
            if (_data_state == DataState.HaveData)
                rc |= 64;  // has data for cpu
            if (_dma == false)
                rc |= 32;
            if (_busy == true)
                rc |= 16;
            Log.DoLog($"Floppy-IN for MSR returns {rc:X2}");
            return (rc, false);
        }

        if (port == 0x3f5)  // fifo
        {
            byte rc = 0;
            if (_data_state == DataState.HaveData) {
                rc = _data[_data_offset++];
                if (_data_offset == _data.Length)
                {
                    _data_state = DataState.WaitCmd;
                    _busy = false;
                }
            }
            else
            {
                Log.DoLog($"Floppy-IN reading from empty FIFO");
            }
            Log.DoLog($"Floppy-IN for FIFO returns {rc:X2}");
            return (rc, false);
        }

        return (_registers[port - 0x3f0], false);
    }

    private bool ReadData()
    {
        int sector = _data[4];
        int lba = (cylinder * 2 + head) * 9 + sector - 1;
        int n = _data[5];

        byte[] b = new byte[256];
        for(int nr=0; nr<n; nr++)
        {
            using (FileStream fs = File.Open(_filename, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                fs.Seek((lba + nr) * b.Length, SeekOrigin.Begin);
                fs.Read(b, 0, b.Length);
            }

            for(int i=0; i<b.Length; i++)
            {
                if (_dma_controller.SendToChannel(2, b[i]) == false)
                {
                    Log.DoLog($"Floppy-ReadData DMA failed at byte position {i}, sector {nr + 1} out of {n}. Position: cylinder {cylinder}, head {head}, sector {sector}, lba {lba}");
                    return false;
                }
            }
        }

        byte [] _old_data = _data;

        _data = new byte[7];
        _data[0] = 0;
        _data[1] = 0;
        _data[2] = 0;
        _data[3] = _old_data[2];
        _data[4] = _old_data[3];
        _data[5] = _old_data[4];
        _data[6] = _old_data[5];
        _data_offset = 0;
        _data_state = DataState.HaveData;

        return true;
    }

    private bool Seek()
    {
        // no response, only an interrupt
        _data_state = DataState.WaitCmd;
        head = (_data[1] & 4) == 4 ? 1 : 0;
        cylinder = _data[2];
        return true;
    }

    public override bool IO_Write(ushort port, byte value)
    {
        if (_data_state == DataState.WantData || _data_state == DataState.HaveData)
            Log.DoLog($"Floppy-OUT {_io_names[port - 0x3f0]}: {port:X4} {value:X2} {_data_state} cmd:{_data[0]:X} data left:{_data.Length - _data_offset}", true);
        else
            Log.DoLog($"Floppy-OUT {_io_names[port - 0x3f0]}: {port:X4} {value:X2} {_data_state}", true);

        bool want_interrupt = false;

        _registers[port - 0x3f0] = value;

        if (port == 0x3f2)  // digital output register
        {
            if ((value & 4) == 0)
            {
                want_interrupt = true;  // FDC enable (controller reset) (IRQ 6)
                _data_state = DataState.WaitCmd;
                _just_resetted = true;
            }
            _dma = (value & 8) == 8;
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
                _busy = true;

                byte cmd = (byte)(value & 31);
                if (cmd == 0x08)
                {
                    Log.DoLog($"Floppy-OUT command SENSE INTERRUPT STATUS");
                    _data = new byte[2];
                    _data[0] = (byte)(_just_resetted ? 0xc0 : 0x20);  // TODO | drive_number
                    _data[1] = 0;  // cylinder number
                    _data_offset = 0;
                    _data_state = DataState.HaveData;
                    want_interrupt = true;
                    _just_resetted = false;
                }
                else if (cmd == 0x06)
                {
                    Log.DoLog($"Floppy-OUT command READ DATA");
                    _data = new byte[9];
                    _data[0] = cmd;
                    _data_offset = 1;
                    _data_state = DataState.WantData;
                }
                else if (cmd == 0x0f)
                {
                    Log.DoLog($"Floppy-OUT command SEEK");
                    _data = new byte[3];
                    _data[0] = cmd;
                    _data_offset = 1;
                    _data_state = DataState.WantData;
                }
                else if (cmd == 0x03)
                {
                    Log.DoLog($"Floppy-OUT command SPECIFY");
                    _data = new byte[3];
                    _data[0] = cmd;
                    _data_offset = 1;
                    _data_state = DataState.WantData;
                }
                else if (cmd == 0x07)
                {
                    Log.DoLog($"Floppy-OUT command RECALIBRATE");
                    _data = new byte[2];
                    _data[0] = cmd;
                    _data_offset = 1;
                    _data_state = DataState.WantData;
                }
                else
                {
                    Log.DoLog($"Floppy-OUT command {cmd:X2} not implemented ({value:X2})");
                    _busy = false;
                }

                if (_data_state == DataState.HaveData)
                {
                    Log.DoLog($"Floppy-OUT queued {_data.Length - _data_offset} bytes");
                }
                else if (_data_state == DataState.WantData)
                {
                    Log.DoLog($"Floppy-OUT waiting for {_data.Length - _data_offset} bytes");
                }
            }
            else if (_data_state == DataState.WantData)
            {
                _data[_data_offset++] = value;
                if (_data_offset == _data.Length)
                {
                    if (_data[0] == 0x06)  // READ DATA
                    {
                        want_interrupt |= ReadData();
                    }
                    else if (_data[0] == 0x0f)  // SEEK
                    {
                        want_interrupt |= Seek();
                    }
                    else if (_data[0] == 0x03)  // SPECIFY
                    {
                        // do nothing (normally it sets timing parameters)
                        _data_state = DataState.WaitCmd;
                    }
                    else if (_data[0] == 0x07)  // RECALIBRATE
                    {
                        // do nothing (normally it sets timing parameters)
                        _data_state = DataState.WaitCmd;
                        want_interrupt = true;
                    }
                    else
                    {
                        Log.DoLog($"Floppy-OUT unexpected command-after-data {_data[0]:X2}");
                    }

                    _just_resetted = false;
                    _busy = false;
                }
            }
            else
            {
                Log.DoLog($"Floppy-OUT invalid state ({_data_state})");
            }
        }

        if (want_interrupt)
        {
            Log.DoLog($"Floppy-OUT triggers IRQ");
            _pic.RequestInterruptPIC(_irq_nr);
        }

        return want_interrupt;
    }
}
