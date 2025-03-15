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
}
