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
    private int _cylinder = 0;  // TODO per drive
    private int _head = 0;  // TODO per drive

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
        int bytes_left = _data == null ? 0 : (_data.Length - _data_offset);
        Log.DoLog($"Floppy-IN {_io_names[port - 0x3f0]}: {port:X4} {_data_state}, bytes left: {bytes_left}", true);

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
        int head = (_data[1] & 4) == 4 ? 1 : 0;
        int lba = (_cylinder * 2 + head) * 9 + sector - 1;
        int n = _data[5];

#if DEBUG
        Log.DoLog($"Floppy-ReadData HS {_data[1] & 4:X02} C {_data[2]} H {_data[3]} R {_data[4]} N {_data[5]}");
        Log.DoLog($"Floppy-ReadData SEEK H {_head} C {_cylinder}");
        Log.DoLog($"Floppy-ReadData LBA {lba}, offset {lba * 512}");
#endif

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

        byte[] b = new byte[256];
        for(int nr=0; nr<n; nr++)
        {
            using (FileStream fs = File.Open(_filename, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                long start = (lba * 2 + nr) * b.Length;
                fs.Seek(start, SeekOrigin.Begin);
                if (fs.Read(b, 0, b.Length) != b.Length)
                    Log.DoLog($"Floppy-ReadData failed reading from backend (offset: {start})");
                if (fs.Position != start + b.Length)
                    Log.DoLog($"Floppy-ReadData backend data processing error?");
            }

#if DEBUG
            for(int i=0; i<16; i++) {
                string str = $"{i * 16 + nr * b.Length:X02}: ";
                for(int k=0; k<16; k++) {
                    int o = k + i * 16;
                    if (b[o] > 32 && b[o] < 127)
                        str += $" {(char)b[o]} ";
                    else
                        str += $" {b[o]:X02}";
                }
                Log.DoLog($"Floppy-ReadData {str}");
            }
#endif

            for(int i=0; i<b.Length; i++)
            {
                if (_dma_controller.SendToChannel(2, b[i]) == false)
                {
                    Log.DoLog($"Floppy-ReadData DMA failed at byte position {i}, sector {nr + 1} out of {n}. Position: cylinder {_cylinder}, head {head}, sector {sector}, lba {lba}");
                    _data[0] = 0x40;  // abnormal termination of command
                    _data[1] = 0x10;  // FDC not serviced by host
                    nr = n;  // break outer loop
                    break;
                }
            }
        }

        return true;
    }

    private bool Seek()
    {
        // no response, only an interrupt
        _data_state = DataState.WaitCmd;
        _head = (_data[1] & 4) == 4 ? 1 : 0;
        _cylinder = _data[2];
        Log.DoLog($"Floppy SEEK to head {_head} cylinder {_cylinder}");
        return true;
    }

    public void DumpReply()
    {
#if DEBUG
        string str = "";
        for(int i=0; i<_data.Length; i++)
            str += $" {_data[i]:X02}";
        Log.DoLog($"Floppy-reply:{str}");
#endif
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
            if (_dma == false)
                Log.DoLog($"Floppy-OUT DMA disabled!");
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
                    _data[1] = (byte)_cylinder;
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
                        DumpReply();
                    }
                    else if (_data[0] == 0x0f)  // SEEK
                    {
                        want_interrupt |= Seek();
                        DumpReply();
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
                }
            }
            else
            {
                Log.DoLog($"Floppy-OUT invalid state ({_data_state})");
            }
        }
        else
        {
            Log.DoLog($"Floppy-OUT write to unexpected port {port:X04}");
        }

        if (want_interrupt)
        {
            Log.DoLog($"Floppy-OUT triggers IRQ");
            _pic.RequestInterruptPIC(_irq_nr);
        }

        return want_interrupt;
    }
}
