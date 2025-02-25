class XTIDE : Device
{
    private List<string> _disk_filenames;
    private string [] _io_names = new string[] { "IDE data port", "error register", "sector count", "sector number", "cylinder low", "cylinder high", "drive/head register", "command/status register" };
    private byte _status_register = 0;
    private byte _error_register = 0;
    private int _drv = 0;
    private byte[] _sector_buffer = null;
    private int _sector_buffer_offset = 0;
    private long _target_lba = 0;
    private int _target_drive = 255;
    private ushort _cylinder_count = 614;
    private ushort _head_count = 4;
    private ushort _sectors_per_track = 17;
    private byte[] _registers = new byte[8];

    public XTIDE(List<string> disk_filenames)
    {
        _disk_filenames = disk_filenames;
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

        int drive_head = _registers[6];
        int drive = (drive_head & 16) != 0 ? 1 : 0;
        if (drive == 1 && _disk_filenames.Count() == 1)
        {
            _error_register |= 4;  // ABRT
            SetERR();
            return;
        }

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
        PushSectorBufferString($"{_disk_filenames[drive].GetHashCode()}", 20); // serial number, ascii
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

    private void CMDSeek()
    {
        Log.DoLog("XT-IDE CMDSeek");
        SetDSC();
    }

    private void CMDReadMultiple()
    {
        int sector_count = _registers[2];
        int sector_number = _registers[3];
        int cylinder = _registers[4] | (_registers[5] << 8);
        int drive_head = _registers[6];
        int drive = (drive_head & 16) != 0 ? 1 : 0;
        if (drive == 1 && _disk_filenames.Count() == 1)
        {
            _error_register |= 4;  // ABRT
            SetERR();
            return;
        }
        int head = drive_head & 15;
        if (sector_count == 0)
            sector_count = 256;
        int lba = (cylinder * _head_count + head) * _sectors_per_track + sector_number - 1;
        long offset = lba * 512;
        Log.DoLog($"XT-IDE CMDReadMultiple, drive {drive}, sector count {sector_count}, number {sector_number}, cylinder {cylinder}, head {head}, lba {lba}, offset {offset}");

        _sector_buffer = new byte[sector_count * 512];
        _sector_buffer_offset = 0;

        for(int nr=0; nr<sector_count; nr++)
        {
            using (FileStream fs = File.Open(_disk_filenames[drive], FileMode.Open, FileAccess.Read, FileShare.None))
            {
                fs.Seek(offset, SeekOrigin.Begin);
                if (fs.Read(_sector_buffer, 512 * nr, 512) != 512)
                    Log.DoLog($"XT-IDE-ReadData failed reading from backend ({_disk_filenames[drive]}, offset: {offset})", true);
                if (fs.Position != offset + 512)
                    Log.DoLog($"XT-IDE-ReadData backend data processing error?", true);
                offset += 512;
            }

            sector_number++;
            if (sector_number == _sectors_per_track)
            {
                sector_number = 1;
                head++;
                if (head == _head_count)
                {
                    head = 0;
                    cylinder++;
                }
            }

            if (nr < sector_count - 1)
            {
                _registers[3] = (byte)sector_number;
                _registers[4] = (byte)cylinder;
                _registers[5] = (byte)(cylinder >> 8);
                _registers[6] &= 0xf0;
                _registers[6] |= (byte)head;
            }
        }

        for(int i=0; i<_sector_buffer.Length / 16; i++) {
            string str = $"{i * 16:X02}: ";
            for(int k=0; k<16; k++) {
                int o = k + i * 16;
                if (_sector_buffer[o] > 32 && _sector_buffer[o] < 127)
                    str += $" {(char)_sector_buffer[o]} ";
                else
                    str += $" {_sector_buffer[o]:X02}";
            }
            Log.DoLog($"XT-IDE-ReadData {str}", true);
        }

        SetDRDY();
        SetDRQ();
    }

    private void CMDWriteMultiple()
    {
        int sector_count = _registers[2];
        int sector_number = _registers[3];
        int cylinder = _registers[4] | (_registers[5] << 8);
        int drive_head = _registers[6];
        int drive = (drive_head & 16) != 0 ? 1 : 0;
        if (drive == 1 && _disk_filenames.Count() == 1)
        {
            _error_register |= 4;  // ABRT
            SetERR();
            return;
        }
        int head = drive_head & 15;
        if (sector_count == 0)
            sector_count = 256;
        int lba = (cylinder * _head_count + head) * _sectors_per_track + sector_number - 1;
        long offset = lba * 512;
        Log.DoLog($"XT-IDE CMDWriteMultiple, drive {drive}, sector count {sector_count}, number {sector_number}, cylinder {cylinder}, head {head}, lba {lba}, offset {offset}");

        _sector_buffer = new byte[sector_count * 512];
        _sector_buffer_offset = 0;
        _target_lba = lba;
        _target_drive = drive;

        SetDRDY();
        SetDRQ();
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

    private void SetDSC()
    {
        _status_register |= 16;
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
        int register = (port - 0x300) / 2;
        string name = _io_names[register];

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
            Log.DoLog($"XT-IDE IN {name}: {port:X4}, value: {rc:X02}", true);
        }
        else if (port == 0x30e)  // status register
        {
            if (_sector_buffer != null && _sector_buffer_offset < _sector_buffer.Length)
                SetDRQ();
            rc = _status_register;
            Log.DoLog($"XT-IDE IN {name}: {port:X4}, value: {rc:X02}", true);
            ResetStatusRegister();
        }
        else
        {
            rc = _registers[register];
            Log.DoLog($"XT-IDE IN {name}: {port:X4}, value: {rc:X02}", true);
        }

        return (rc, false);
    }

    private void StoreSectorBuffer()
    {
        using (FileStream fs = File.Open(_disk_filenames[_target_drive], FileMode.Open, FileAccess.Write, FileShare.None))
        {
            fs.Seek(_target_lba * 512, SeekOrigin.Begin);
            fs.Write(_sector_buffer);
        }
    }

    public override bool IO_Write(ushort port, byte value)
    {
        int register = (port - 0x300) / 2;
        string name = _io_names[register];
        Log.DoLog($"XT-IDE OUT {name}: {port:X4} {value:X2}", true);

        _registers[register] = value;

        if (port == 0x300)  // data register
        {
            if (_sector_buffer_offset < _sector_buffer.Length)
            {
                _sector_buffer[_sector_buffer_offset++] = value;
            }
            else if (_target_drive != 255)
            {
                StoreSectorBuffer();
                _target_drive = 255;
            }
        }
        else if (port == 0x30c)  // drive/head register
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
            else if (value == 0x91)  // initialize drive parameters
            {
                // send aborted command error
                _error_register |= 4;  // ABRT
                SetERR();
            }
            else if ((value & 0xf0) == 0x70)  // seek
            {
                CMDSeek();
            }
            else if (value == 0xc6)  // set multiple mode
            {
                // do nothing
            }
            else if (value == 0xc4)  // read multiple
            {
                CMDReadMultiple();
            }
            else if (value == 0xc5)  // write multiple
            {
                CMDWriteMultiple();
            }
            else
            {
                Log.DoLog($"XT-IDE command {value:X02} not implemented");
            }
        }

        return false;
    }
}
