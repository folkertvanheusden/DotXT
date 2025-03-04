internal enum DataState { NotSet, WaitCmd, HaveData, WantData }

class FloppyDisk : Device
{
    private ushort [] _registers = new ushort[8];
    private i8237 _dma_controller = null;
    protected int _irq_nr = 6;
    private bool _dma = false;
    private string [] _io_names = new string[] { "status reg a", "status reg b", "digital output reg", "tape drive reg", "main status reg", "data fifo", null, "digital input reg", "cfg control reg" };
    private DataState _data_state = DataState.NotSet;
    private byte [] _data = null;
    private int _data_offset = 0;
    private List<string> _filenames = null;
    private bool _just_resetted = false;
    private bool _busy = false;
    private int [] _cylinder = new int[4];
    private int [] _head = new int[4];
    private int _cylinder_seek_result = 0;
    private readonly System.Threading.Lock _filenames_lock = new();

    public FloppyDisk(List<string> filenames)
    {
        Log.Cnsl("Floppy drive instantiated");
        _filenames = filenames;
    }

    public int GetUnitCount()
    {
        return _filenames.Count();
    }

    public string GetUnitFilename(int unit)
    {
        lock(_filenames_lock)
        {
            if (unit < _filenames.Count())
            {
                return _filenames[unit];
            }
        }

        return null;
    }

    public bool SetUnitFilename(int unit, string filename)
    {
        lock(_filenames_lock)
        {
            if (unit < _filenames.Count() && File.Exists(filename))
            {
                _filenames[unit] = filename;
                return true;
            }
        }

        return false;
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

    public bool FifoHasDataForCpu()
    {
        return _data_state == DataState.HaveData;
    }

    public bool FifoExpectsData()
    {
        return _data_state == DataState.WantData;
    }

    public override (ushort, bool) IO_Read(ushort port)
    {
        int bytes_left = _data == null ? 0 : (_data.Length - _data_offset);
        Log.DoLog($"Floppy-IN {_io_names[port - 0x3f0]}: {port:X4} {_data_state}, bytes left: {bytes_left}", LogLevel.DEBUG);

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
            Log.DoLog($"Floppy-IN for MSR returns {rc:X2}", LogLevel.DEBUG);
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
                Log.DoLog($"Floppy-IN reading from empty FIFO", LogLevel.DEBUG);
            }
            Log.DoLog($"Floppy-IN for FIFO returns {rc:X2}", LogLevel.DEBUG);
            return (rc, false);
        }

        Log.DoLog("Floppy-IN read from cache", LogLevel.DEBUG);

        return (_registers[port - 0x3f0], false);
    }

    private int GetSectorsPerTrack(int unit)
    {
        long file_size = new System.IO.FileInfo(_filenames[unit]).Length;
        int sectors_per_track = 9;
        if (file_size >= 819200)
            sectors_per_track = 18;

        return sectors_per_track;
    }

    private byte [] GetFromFloppyImage(int unit, int cylinder, int head, int sector, int n)
    {
        int sectors_per_track = GetSectorsPerTrack(unit);
        if (sector > sectors_per_track)
            Log.DoLog($"GetFromFloppyImage: reading beyond sector-count? ({sector} > {sectors_per_track})", LogLevel.DEBUG);

        byte[] b = new byte[256 * n];
        int lba = (cylinder * 2 + head) * sectors_per_track + sector - 1;
        long offset = lba * b.Length;
        Log.DoLog($"GetFromFloppyImage {unit}, LBA {lba}, offset {offset}, n {n} C {cylinder} H {head} S {sector} ({sectors_per_track})", LogLevel.TRACE);

        lock(_filenames_lock)
        {
            if (sector > sectors_per_track)
                Log.DoLog($"Floppy-ReadData: reading beyond sector-count? ({sector} > {sectors_per_track})", LogLevel.DEBUG);

            for(int nr=0; nr<n; nr++)
            {
                using (FileStream fs = File.Open(_filenames[unit], FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    fs.Seek(offset, SeekOrigin.Begin);
                    if (fs.Read(b, 256 * nr, 256) != 256)
                        Log.DoLog($"Floppy-ReadData failed reading from backend ({_filenames[unit]}, offset: {offset})", LogLevel.ERROR);
                    if (fs.Position != offset + 256)
                        Log.DoLog($"Floppy-ReadData backend data processing error?", LogLevel.ERROR);
                    offset += 256;
                }
            }
        }

        return b;
    }

    private bool ReadData(int unit)
    {
        lock(_filenames_lock)
        {
            if (unit >= _filenames.Count())
                return false;
        }

        int sector = _data[4];
        int head = (_data[1] & 4) == 4 ? 1 : 0;
        int n = _data[5];

        int sectors_per_track = GetSectorsPerTrack(unit);

        Log.DoLog($"Floppy-ReadData HS {head:X02} C {_data[2]} H {_data[3]} R {_data[4]} Sz {_data[5]} EOT {_data[6]} GPL {_data[7]} DTL {_data[8]}", LogLevel.DEBUG);
        Log.DoLog($"Floppy-ReadData SEEK H {_head[unit]} C {_cylinder[unit]}, unit {unit}", LogLevel.DEBUG);

        byte [] old_data = _data;
        _data = new byte[7];
        _data[0] = (byte)(unit | (head << 2));  // ST0
        _data[1] = 0;
        _data[2] = 0;
        _data[3] = old_data[2];  // cylinder
        _data[4] = old_data[3];  // head
        _data[6] = old_data[5];  // sector size
        _data_offset = 0;
        _data_state = DataState.HaveData;

        bool dma_finished = false;
        do
        {
            byte[] b = GetFromFloppyImage(unit, _cylinder[unit], head, sector, n);

#if DEBUG
            for(int i=0; i<b.Length / 16; i++) {
                string str = $"{i * 16:X02}: ";
                for(int k=0; k<16; k++) {
                    int o = k + i * 16;
                    if (b[o] > 32 && b[o] < 127)
                        str += $" {(char)b[o]} ";
                    else
                        str += $" {b[o]:X02}";
                }
                Log.DoLog($"Floppy-ReadData {str}", LogLevel.TRACE);
            }
#endif

            for(int i=0; i<b.Length; i++)
            {
                if (_dma_controller.SendToChannel(2, b[i]) == false)
                {
                    if (sector == old_data[4])
                    {
                        Log.DoLog($"Floppy-ReadData DMA failed at byte position {i}. Position: cylinder {_cylinder[unit]}, head {head}, sector {sector}, unit {unit}", LogLevel.INFO);
                        _data[0] = 0x40;  // abnormal termination of command
                        _data[1] = 0x10;  // FDC not serviced by host
                    }
                    dma_finished = true;
                    break;
                }
            }

            if (dma_finished == false)
                sector++;
        }
        while(dma_finished == false && sector <= sectors_per_track);

        if (sector > sectors_per_track)
        {
            _data[3] = (byte)(old_data[2] + 1);
            _data[5] = 1;  // sector number
        }
        else
        {
            _data[5] = (byte)sector;
        }

        Log.DoLog($"Floppy-ReadData {sector - old_data[4]} sector(s) read", LogLevel.DEBUG);

        return true;
    }

    private void WriteToFloppyImage(int unit, int cylinder, int head, int sector, byte [] b)
    {
        int sectors_per_track = GetSectorsPerTrack(unit);
        if (sector > sectors_per_track)
            Log.DoLog($"WriteToFloppyImage reading beyond sector-count? ({sector} > {sectors_per_track})", LogLevel.DEBUG);

        int lba = (cylinder * 2 + head) * sectors_per_track + sector - 1;
        long offset = lba * 512;
        Log.DoLog($"WriteToFloppyImage LBA {lba}, offset {offset}, C {cylinder} H {head} S {sector} ({sectors_per_track})", LogLevel.TRACE);

        using (FileStream fs = File.Open(_filenames[unit], FileMode.Open, FileAccess.Write, FileShare.None))
        {
            fs.Seek(offset, SeekOrigin.Begin);
            fs.Write(b);
            offset += b.Length;
            if (fs.Position != offset)
                Log.DoLog($"WriteToFloppyImage backend data processing error?", LogLevel.ERROR);
        }
    }

    private bool WriteData(int unit)
    {
        if (unit >= _filenames.Count())
            return false;

        int sectors_per_track = GetSectorsPerTrack(unit);

        int sector = _data[4];
        int head = (_data[1] & 4) == 4 ? 1 : 0;
        int n = _data[5];

        Log.DoLog($"Floppy-WriteData HS {head:X02} C {_data[2]} H {_data[3]} R {_data[4]} Sz {_data[5]} EOT {_data[6]} GPL {_data[7]} DTL {_data[8]}", LogLevel.DEBUG);
        Log.DoLog($"Floppy-WriteData SEEK H {_head[unit]} C {_cylinder[unit]}, unit {unit}", LogLevel.DEBUG);

        byte [] old_data = _data;
        _data = new byte[7];
        _data[0] = (byte)(unit | (head << 2));  // ST0
        _data[1] = 0;
        _data[2] = 0;
        _data[3] = old_data[2];
        _data[4] = old_data[3];
        _data[6] = old_data[5];
        _data_offset = 0;
        _data_state = DataState.HaveData;

        bool dma_finished = false;
        do
        {
            _data[5] = (byte)sector;

            byte[] b = new byte[n * 256];
            for(int i=0; i<b.Length; i++)
            {
                int e = _dma_controller.ReceiveFromChannel(2);
                if (e == -1)
                {
                    if (sector == old_data[4])
                    {
                        Log.DoLog($"Floppy-WriteData DMA failed at byte position {i}. Position: cylinder {_cylinder[unit]}, head {head}, sector {sector}, unit {unit}", LogLevel.DEBUG);
                        _data[0] = 0x40;  // abnormal termination of command
                        _data[1] = 0x10;  // FDC not serviced by host
                    }
                    dma_finished = true;
                    break;
                }
                b[i] = (byte)e;
            }

            if (dma_finished == false)
            {
                WriteToFloppyImage(unit, _cylinder[unit], head, sector, b);
                sector++;
            }
        }
        while(dma_finished == false && sector <= sectors_per_track);

        if (sector > sectors_per_track)
        {
            _data[3] = (byte)(old_data[2] + 1);
            _data[5] = 1;  // sector number
        }
        else
        {
            _data[5] = old_data[4];
        }

        Log.DoLog($"Floppy-WriteData {sector - old_data[4]} sector(s) written", LogLevel.TRACE);

        return true;
    }

    private bool Seek(int unit)
    {
        // no response, only an interrupt
        _data_state = DataState.WaitCmd;
        _head[unit] = (_data[1] & 4) == 4 ? 1 : 0;
        _cylinder[unit] = _data[2];
        _cylinder_seek_result = _cylinder[unit];
        Log.DoLog($"Floppy SEEK to head {_head[unit]} cylinder {_cylinder[unit]}, unit {unit}, relative {_data[0] & 128} direction {_data[0] & 64}", LogLevel.TRACE);
        return true;
    }

    private bool Format(int unit)
    {
        // no response, only an interrupt
        _data_state = DataState.WaitCmd;
        int head = (_data[1] & 4) == 4 ? 1 : 0;

        Log.DoLog($"Floppy FORMAT unit {unit} head {_head[unit]} cylinder {_cylinder[unit]}, filler {_data[5]:X02}", LogLevel.TRACE);

        byte[]b = new byte[512];  // TODO retrieve from _data[2]
        var span = new Span<byte>(b);
        span.Fill(_data[5]);

        byte [] old_data = _data;
        _data = new byte[7];
        _data[0] = (byte)(unit | (head << 2));  // ST0
        _data[3] = (byte)_cylinder[unit];
        _data[4] = (byte)head;
        _data[5] = old_data[4];
        _data[6] = old_data[2];
        _data_offset = 0;
        _data_state = DataState.HaveData;

        int sectors_per_track = GetSectorsPerTrack(unit);

        for(int sector=1; sector<=sectors_per_track; sector++)
        {
            bool dma_finished = false;
            for(int i=0; i<b.Length; i++)
            {
                int e = _dma_controller.ReceiveFromChannel(2);
                if (e == -1)
                {
                    Log.DoLog($"Floppy-Format DMA failed at byte position {i}. Position: cylinder {_cylinder[unit]}, head {head}, sector {sector}, unit {unit}", LogLevel.DEBUG);
                    _data[0] = 0x40;  // abnormal termination of command
                    _data[1] = 0x10;  // FDC not serviced by host
                    dma_finished = true;
                    break;
                }
                b[i] = (byte)e;
            }

            if (dma_finished)
                break;

            WriteToFloppyImage(unit, _cylinder[unit], head, sector, b);
        }

        return true;
    }

    public void DumpReply()
    {
#if DEBUG
        string str = "";
        for(int i=0; i<_data.Length; i++)
            str += $" {_data[i]:X02}";
        Log.DoLog($"Floppy-reply:{str}", LogLevel.TRACE);
#endif
    }

    public override bool IO_Write(ushort port, ushort value)
    {
        if (_data_state == DataState.WantData || _data_state == DataState.HaveData)
            Log.DoLog($"Floppy-OUT {_io_names[port - 0x3f0]}: {port:X4} {value:X2} {_data_state} cmd:{_data[0]:X} data left:{_data.Length - _data_offset}", LogLevel.DEBUG);
        else
            Log.DoLog($"Floppy-OUT {_io_names[port - 0x3f0]}: {port:X4} {value:X2} {_data_state}", LogLevel.DEBUG);

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
                Log.DoLog($"Floppy-OUT DMA disabled!", LogLevel.DEBUG);
        }
        else if (port == 0x3f5)  // data fifo
        {
            if (_data_state != DataState.WaitCmd && _data_state != DataState.WantData)
            {
                Log.DoLog($"Floppy-OUT was in {_data_state} mode, going to WaitCmd (forcibly)", LogLevel.DEBUG);
                _data_state = DataState.WaitCmd;
            }

            if (_data_state == DataState.WaitCmd)
            {
                _busy = true;

                byte cmd = (byte)(value & 31);
                if (cmd == 0x08)
                {
                    Log.DoLog($"Floppy-OUT command SENSE INTERRUPT STATUS", LogLevel.DEBUG);
                    _data = new byte[2];
                    _data[0] = (byte)(_just_resetted ? 0xc0 : 0x20);  // TODO | drive_number
                    _data[1] = (byte)_cylinder_seek_result;
                    _data_offset = 0;
                    _data_state = DataState.HaveData;
                    want_interrupt = true;
                    _just_resetted = false;
                }
                else if (cmd == 0x06)
                {
                    Log.DoLog($"Floppy-OUT command READ DATA", LogLevel.DEBUG);
                    _data = new byte[9];
                    _data[0] = cmd;
                    _data_offset = 1;
                    _data_state = DataState.WantData;
                }
                else if (cmd == 0x05)
                {
                    Log.DoLog($"Floppy-OUT command WRITE DATA", LogLevel.DEBUG);
                    _data = new byte[9];
                    _data[0] = cmd;
                    _data_offset = 1;
                    _data_state = DataState.WantData;
                }
                else if (cmd == 0x0d)
                {
                    Log.DoLog($"Floppy-OUT command FORMAT", LogLevel.DEBUG);
                    _data = new byte[6];
                    _data[0] = cmd;
                    _data_offset = 1;
                    _data_state = DataState.WantData;
                }
                else if (cmd == 0x0f)
                {
                    Log.DoLog($"Floppy-OUT command SEEK", LogLevel.DEBUG);
                    _data = new byte[3];
                    _data[0] = cmd;
                    _data_offset = 1;
                    _data_state = DataState.WantData;
                }
                else if (cmd == 0x03)
                {
                    Log.DoLog($"Floppy-OUT command SPECIFY", LogLevel.DEBUG);
                    _data = new byte[3];
                    _data[0] = cmd;
                    _data_offset = 1;
                    _data_state = DataState.WantData;
                }
                else if (cmd == 0x07)
                {
                    Log.DoLog($"Floppy-OUT command RECALIBRATE", LogLevel.DEBUG);
                    _data = new byte[2];
                    _data[0] = cmd;
                    _data_offset = 1;
                    _data_state = DataState.WantData;
                }
                else
                {
                    Log.DoLog($"Floppy-OUT command {cmd:X2} not implemented ({value:X2})", LogLevel.WARNING);
                    _busy = false;
                }

                if (_data_state == DataState.HaveData)
                {
                    Log.DoLog($"Floppy-OUT queued {_data.Length - _data_offset} bytes", LogLevel.DEBUG);
                }
                else if (_data_state == DataState.WantData)
                {
                    Log.DoLog($"Floppy-OUT waiting for {_data.Length - _data_offset} bytes", LogLevel.DEBUG);
                }
            }
            else if (_data_state == DataState.WantData)
            {
                if (_data_offset >= _data.Length)
                {
                        Log.DoLog($"Floppy-OUT command buffer overflow", LogLevel.WARNING);
                        return false;
                }

                _data[_data_offset++] = (byte)value;  // FIXME
                if (_data_offset == _data.Length)
                {
                    if (_data[0] == 0x06)  // READ DATA
                    {
                        want_interrupt |= ReadData(_data[1] & 3);
                        DumpReply();
                    }
                    else if (_data[0] == 0x05)  // WRITE DATA
                    {
                        want_interrupt |= WriteData(_data[1] & 3);
                        DumpReply();
                    }
                    else if (_data[0] == 0x0f)  // SEEK
                    {
                        want_interrupt |= Seek(_data[1] & 3);
                        DumpReply();
                    }
                    else if (_data[0] == 0x0d)  // FORMAT
                    {
                        want_interrupt |= Format(_data[1] & 3);
                        DumpReply();
                    }
                    else if (_data[0] == 0x03)  // SPECIFY
                    {
                        // do nothing (normally it sets timing parameters)
                        _data_state = DataState.WaitCmd;
                    }
                    else if (_data[0] == 0x07)  // RECALIBRATE
                    {
                        int unit = _data[1] & 3;
                        _head[unit] = 0;
                        _cylinder[unit] = 0;
                        _data_state = DataState.WaitCmd;
                        want_interrupt = true;
                    }
                    else
                    {
                        Log.DoLog($"Floppy-OUT unexpected command-after-data {_data[0]:X2}", LogLevel.WARNING);
                    }

                    _just_resetted = false;
                }
            }
            else
            {
                Log.DoLog($"Floppy-OUT invalid state ({_data_state})", LogLevel.WARNING);
            }
        }
        else
        {
            Log.DoLog($"Floppy-OUT write to unexpected port {port:X04}", LogLevel.WARNING);
        }

        if (want_interrupt)
        {
            Log.DoLog($"Floppy-OUT triggers IRQ", LogLevel.TRACE);
            _pic.RequestInterruptPIC(_irq_nr);
        }

        return want_interrupt;
    }
}
