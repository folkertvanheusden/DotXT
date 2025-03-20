using DotXT;


class Tools
{
    public static void LoadBin(Bus b, string file, uint addr)
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

    public static void Assert(bool v, string reason)
    {
        if (v == false)
        {
            Console.WriteLine($"Assertion failed ({reason})");
            Console.WriteLine(new System.Diagnostics.StackTrace().ToString());
            System.Environment.Exit(1);
        }
    }

    public static int GetValue(string v, bool hex)
    {
        string[] aparts = v.Split(":");
        if (aparts.Length == 2)
            return Convert.ToInt32(aparts[0], 16) * 16 + Convert.ToInt32(aparts[1], 16);

        if (v.Length > 2 && v[0] == '0' && v[1] == 'x')
            return Convert.ToInt32(v.Substring(2), 16);

        if (hex)
            return Convert.ToInt32(v, 16);

        return Convert.ToInt32(v, 10);
    }

    public static byte[] GraphicalFrameToBmp(GraphicalFrame g)
    {
        int out_len = g.width * g.height * 3 + 2 + 12 + 40;
        byte [] out_ = new byte[out_len];

        int offset = 0;
        out_[offset++] = (byte)'B';
        out_[offset++] = (byte)'M';
        out_[offset++] = (byte)out_len;  // file size in bytes
        out_[offset++] = (byte)(out_len >> 8);
        out_[offset++] = (byte)(out_len >> 16);
        out_[offset++] = (byte)(out_len >> 24);
        out_[offset++] = 0x00;  // reserved
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;
        out_[offset++] = 54;  // offset of start (2 + 12 + 40)
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;
        //assert(offset == 0x0e);
        out_[offset++] = 40;  // header size
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;
        out_[offset++] = (byte)g.width;
        out_[offset++] = (byte)(g.width >> 8);
        out_[offset++] = (byte)(g.width >> 16);
        out_[offset++] = 0x00;
        out_[offset++] = (byte)g.height;
        out_[offset++] = (byte)(g.height >> 8);
        out_[offset++] = (byte)(g.height >> 16);
        out_[offset++] = 0x00;
        out_[offset++] = 0x01;  // color planes
        out_[offset++] = 0x00;
        out_[offset++] = 24;  // bits per pixel
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;  // compression method
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;  // image size
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;
        out_[offset++] = (byte)g.width;
        out_[offset++] = (byte)(g.width >> 8);
        out_[offset++] = (byte)(g.width >> 16);
        out_[offset++] = 0x00;
        out_[offset++] = (byte)g.height;
        out_[offset++] = (byte)(g.height >> 8);
        out_[offset++] = (byte)(g.height >> 16);
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;  // color count
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;  // important colors
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;

        for(int y=g.height - 1; y >= 0; y--) {
            int in_o = y * g.width * 3;
            for(int x=0; x<g.width; x++) {
                int in_o2 = in_o + x * 3;
                out_[offset++] = g.rgb_pixels[in_o2 + 2];
                out_[offset++] = g.rgb_pixels[in_o2 + 1];
                out_[offset++] = g.rgb_pixels[in_o2 + 0];
            }
        }

        return out_;
    }
}
