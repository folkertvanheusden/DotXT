using DotXT;

using System.Text;

internal class XTServer : Device
{
    private int _irq_nr = -1;
    private string _bin_file;
    private uint _bin_file_load_address = 0;
    private Display _d = null;
    private string _trace_file = null;

    public static void AddXTServerBootROM(Bus b)
    {
        uint start_addr = 0xd000 * 16 + 0x0000;
        uint addr = start_addr;

        byte [] option_rom = new byte[] {
            0x55, 0xaa, 0x01, 0xba, 0x01, 0xf0, 0xb0, 0xff, 0xee, 0x31, 0xc0, 0x8e,
                0xd8, 0xbe, 0x80, 0x01, 0xb9, 0x0b, 0x00, 0xc7, 0x04, 0x5a, 0x00, 0xc7,
                0x44, 0x02, 0x00, 0xd0, 0x83, 0xc6, 0x04, 0xe0, 0xf2, 0xbe, 0x80, 0x01,
                0xc7, 0x04, 0x5b, 0x00, 0xc7, 0x44, 0x02, 0x00, 0xd0, 0xbe, 0x8c, 0x01,
                0xc7, 0x04, 0x71, 0x00, 0xc7, 0x44, 0x02, 0x00, 0xd0, 0xbe, 0x90, 0x01,
                0xc7, 0x04, 0x7e, 0x00, 0xc7, 0x44, 0x02, 0x00, 0xd0, 0xbe, 0x94, 0x01,
                0xc7, 0x04, 0x91, 0x00, 0xc7, 0x44, 0x02, 0x00, 0xd0, 0xea, 0x00, 0x00,
                0x00, 0x10, 0x00, 0x00, 0x00, 0x00, 0xcf, 0xa3, 0x56, 0x00, 0x89, 0x16,
                0x58, 0x00, 0xba, 0x01, 0xf0, 0xb8, 0x01, 0x60, 0xef, 0x8b, 0x16, 0x58,
                0x00, 0xa1, 0x56, 0x00, 0xcf, 0x89, 0x16, 0x58, 0x00, 0xba, 0x63, 0xf0,
                0xef, 0x8b, 0x16, 0x58, 0x00, 0xcf, 0x89, 0x16, 0x58, 0x00, 0xba, 0x64,
                0xf0, 0x8a, 0x04, 0x46, 0xee, 0x49, 0xe0, 0xf9, 0x8b, 0x16, 0x58, 0x00,
                0xcf, 0x89, 0x16, 0x58, 0x00, 0xba, 0x65, 0xf0, 0xee, 0x8b, 0x16, 0x58,
                0x00, 0xcf
        };

        for(int i=0; i<option_rom.Length; i++)
            b.WriteByte(addr++, option_rom[i]);

        byte checksum = 0;
        for(int i=0; i<512; i++)
            checksum += b.ReadByte((uint)(start_addr + i)).Item1;
        b.WriteByte(start_addr + 511, (byte)(~checksum));
    }

    public XTServer(string bin_file, uint bin_file_load_address, Display d, string trace_file)
    {
        Log.Cnsl("XTServer instantiated");
        _bin_file = bin_file;
        _bin_file_load_address = bin_file_load_address;
        _d = d;
        _trace_file = trace_file;
    }

    public override int GetIRQNumber()
    {
        return _irq_nr;
    }

    public override String GetName()
    {
        return "XTServer";
    }

    public override void RegisterDevice(Dictionary <ushort, Device> mappings)
    {
        mappings[0xf001] = this;
        mappings[0xf063] = this;
        mappings[0xf064] = this;
        mappings[0xf065] = this;
    }

    public override byte IO_Read(ushort port)
    {
        return 0xaa;
    }

    public override bool IO_Write(ushort port, byte value)
    {
        Log.DoLog($"XTServer emulation {value:X2}", LogLevel.DEBUG);

        if (port == 0xf001)
        {
            if (value == 0x6001)  //  screenshot
            {
                if (_d != null)
                {
                    try
                    {
                        var frame = _d.GetFrame(false);
                        var bmp = _d.GraphicalFrameToBmp(frame);

                        using (FileStream stream = new FileStream($"{DateTime.Now.Ticks}.bmp", FileMode.Create, FileAccess.Write))
                        {
                            stream.Write(bmp, 0, bmp.Length);
                        }
                    }
                    catch(Exception e)
                    {
                        Log.DoLog($"Failed to write screenshot: {e}", LogLevel.ERROR);
                    }
                }
            }
            else if (value == 0xff)  //  load test code
            {
                Tools.LoadBin(_b, _bin_file, _bin_file_load_address);
            }
        }
        else if (port == 0xf063)
        {
            if (_trace_file != null)
            {
                using (FileStream stream = new FileStream(_trace_file, FileMode.Append, FileAccess.Write))
                {
                    byte[] data = new UTF8Encoding(true).GetBytes($"{value:X4}");
                    stream.Write(data, 0, data.Length);
                }
            }
        }
        else if (port == 0xf064 || port == 0xf065)
        {
            // f065 has special escape codes TODO

            if (_trace_file != null)
            {
                using (FileStream stream = new FileStream(_trace_file, FileMode.Append, FileAccess.Write))
                {
                    byte[] data = new byte[] { value };
                    stream.Write(data, 0, data.Length);
                }
            }
        }

        return false;
    }

    public override List<Tuple<uint, int> > GetAddressList()
    {
        return new() { };
    }

    public override void WriteByte(uint offset, byte value)
    {
    }

    public override byte ReadByte(uint offset)
    {
        return 0xee;
    }

    public override bool Ticks()
    {
        return false;
    }

    public override bool Tick(int ticks, long ignored)
    {
        return false;
    }
}
