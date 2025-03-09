using DotXT;

internal class XTServer : Device
{
    private int _irq_nr = -1;
    private string _bin_file;
    private uint _bin_file_load_address = 0;
    private Display _d = null;

    public XTServer(string bin_file, uint bin_file_load_address, Display d)
    {
        Log.Cnsl("XTServer instantiated");
        _bin_file = bin_file;
        _bin_file_load_address = bin_file_load_address;
        _d = d;
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
    }

    private void LoadBin(Bus b, string file, uint addr)
    {
        Log.DoLog($"Load {file} at {addr:X6}", LogLevel.INFO);

        try
        {
            using(Stream source = File.Open(file, FileMode.Open))
            {
                byte[] buffer = new byte[512];

                for(;;)
                {
                    int n_read = source.Read(buffer, 0, 512);
                    if (n_read == 0)
                        break;

                    for(int i=0; i<n_read; i++)
                        b.WriteByte(addr++, buffer[i]);
                }
            }
        }
        catch(Exception e)
        {
            Log.DoLog($"Failed to load {file}: {e}", LogLevel.ERROR);
        }
    }

    public override (ushort, bool) IO_Read(ushort port)
    {
        return (0xaa, false);
    }

    public override bool IO_Write(ushort port, ushort value)
    {
        Log.DoLog($"XTServer emulation {value:X2}", LogLevel.DEBUG);

        if (port == 0xf001)
        {
            if (value == 0x01)  //  screenshot
            {
                if (_d != null)
                {
                    try
                    {
                        var frame = _d.GetFrame();
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
                LoadBin(_b, _bin_file, _bin_file_load_address);
            }
        }

        return false;
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

    public override bool Tick(int ticks, long ignored)
    {
        return false;
    }
}
