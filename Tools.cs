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
}
