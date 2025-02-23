class XTIDE : Device
{
    private string _disk_filename;
    private string [] _io_names = new string[] { "IDE data port", "error register", "sector count", "sector number", "cylinder low", "cylinder high", "drive/head register", "command/status register" };
    private byte _status_register = 0;
    private byte _error_register = 0;
    private int _drv = 0;
    private byte[] _sector_buffer = null;
    private int _sector_buffer_offset = 0;
    private ushort _cylinder_count = 100;
    private ushort _head_count = 15;
    private ushort _sectors_per_track = 64;

    public XTIDE(string disk_filename)
    {
        _disk_filename = disk_filename;
    }

    private void PushSectorBufferWord(ushort v)
    {
        _sector_buffer[_sector_buffer_offset++] = (byte)v;
        _sector_buffer[_sector_buffer_offset++] = (byte)(v >> 8);
    }

    private void PushSectorBufferString(string what, int length)
    {
        for(int i=0; i<length; i += 2)
        {
            ushort word = 0;
            if (i < what.Length)
                word |= (ushort)((byte)what[i] << 8);
            if (i + 1 < what.Length)
                word |= (byte)what[i + 1];
            PushSectorBufferWord(word);
        }
    }

    private void CMDIdentifyDrive()
    {
        Log.DoLog("XT-IDE: CMDIdentifyDrive");

        _sector_buffer = new byte[512];
        _sector_buffer_offset = 0;

        PushSectorBufferWord(64 /* fixed drive) */ + 32 /* no motor control */);
        PushSectorBufferWord(_cylinder_count);
        PushSectorBufferWord(0);  // reserved, 2
        PushSectorBufferWord(_head_count);
        PushSectorBufferWord((ushort)(512 * _sectors_per_track));
        PushSectorBufferWord(512);  // bytes per sector
        PushSectorBufferWord(_sectors_per_track);
        PushSectorBufferWord(0);  // reserved, 7
        PushSectorBufferWord(0);  // reserved
        PushSectorBufferWord(0);  // reserved, 9
        for(int i=0; i<10; i++)
            PushSectorBufferWord(0);  // serial number, ascii
        PushSectorBufferWord(0);  // buffer type
        PushSectorBufferWord(0);  // buffer size
        PushSectorBufferWord(0);  // ECC byte count
        for(int i=0; i<4; i++)
            PushSectorBufferWord(0);  // firmware revision, ascii
        PushSectorBufferString("DotXT", 40); // model number, ascii
        PushSectorBufferWord(1);  // Maximum number of sectors that can be transferred per interrupt on read and write multiple commands
        PushSectorBufferWord(0);  // no doubleword transfers
        PushSectorBufferWord(1024);  // LBA supported
        PushSectorBufferWord(0);  // reserved
        PushSectorBufferWord(0);  // PIO data transfer cycle timing mode
        PushSectorBufferWord(0);  // DMA data transfer cycle timing mode
        PushSectorBufferWord(0);  // the fields reported in words 54-58 may be valid
        PushSectorBufferWord(_cylinder_count);
        PushSectorBufferWord(_head_count);
        PushSectorBufferWord(_sectors_per_track);
        ulong total_sector_count = (ulong)_cylinder_count * _head_count * _sectors_per_track;
        PushSectorBufferWord((ushort)total_sector_count);  // capacity in sectors
        PushSectorBufferWord((ushort)(total_sector_count >> 16));  // ^
        PushSectorBufferWord(1);  //  Current setting for number of sectors that can be transferred per interrupt on R/W multiple commands
        PushSectorBufferWord((ushort)total_sector_count);  // Total number of user addressable sectors (LBA mode only) 
        PushSectorBufferWord((ushort)(total_sector_count >> 16));  // ^
        PushSectorBufferWord(0);  // dma related
        PushSectorBufferWord(0);  // dma related
        for(int i=0; i<256-64; i++)
            PushSectorBufferWord(0);  // reserved
        System.Diagnostics.Debug.Assert(_sector_buffer_offset == 512, "CMDIdentifyDrive bug");
        _sector_buffer_offset = 0;
    }

    public override int GetIRQNumber()
    {
        return -1;
    }

    public override void SetPic(pic8259 pic_instance)
    {
        // not used
    }

    public override String GetName()
    {
        return "XT-IDE";
    }

    public override void RegisterDevice(Dictionary <ushort, Device> mappings)
    {
        for(ushort port=0x0300; port<0x0310; port++)
        {
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

    private void SetBSY()
    {
        _status_register |= 128;
    }

    private void SetDRDY()
    {
        _status_register |= 64;
    }

    private void SetDRQ()
    {
        _status_register |= 8;
    }

    private void SetERR()
    {
        _status_register |= 1;
    }

    private void ResetStatusRegister()
    {
        _status_register = 0;
        SetDRDY();
    }

    public override (byte, bool) IO_Read(ushort port)
    {
        string name = _io_names[(port - 0x300) / 2];
        Log.DoLog($"XT-IDE IN {name}: {port:X4}", true);

        byte rc = 0xee;

        if (port == 0x300)  // Data register
        {
            if (_sector_buffer_offset < _sector_buffer.Length)
            {
                rc = _sector_buffer[_sector_buffer_offset++];
            }
        }
        else if (port == 0x302)  // error register
        {
            rc = _error_register;
            _error_register = 0;
        }
        else if (port == 0x30e)  // status register
        {
            if (_sector_buffer != null && _sector_buffer_offset < _sector_buffer.Length)
                SetDRQ();
            rc = _status_register;
            ResetStatusRegister();
        }
        else
        {
            Log.DoLog($"XT-IDE IN register {port:X4} not implemented", true);
        }

        Log.DoLog($"XT-IDE IN {name}: {port:X4}, value {rc:X02}", true);

        return (rc, false);
    }

    public override bool IO_Write(ushort port, byte value)
    {
        string name = _io_names[(port - 0x300) / 2];
        Log.DoLog($"XT-IDE OUT {name}: {port:X4} {value:X2}", true);

        if (port == 0x30c)  // drive/head register
        {
            _drv = (value & 16) != 0 ? 1 : 0;

            string log = ", DRIVE/HEAD reg:";
            if ((value & 64) != 0)
                log += "LBA";
            else
                log += "CHS";
            log += $", drive-{_drv}";
            if ((value & 64) == 0)
                log += $", head {value & 15}";
            Log.DoLog($"XT-IDE {log}");
        }
        else if (port == 0x30e)  // command register
        {
            if (value == 0xef)  // set features
            {
                // send aborted command error
                _error_register |= 4;  // ABRT
                SetERR();
            }
            else if (value == 0xec)  // identify drive
            {
                CMDIdentifyDrive();
            }
        }
        else
        {
            Log.DoLog($"XT-IDE OUT register {port:X4} not implemented", true);
        }

        return false;
    }
}
