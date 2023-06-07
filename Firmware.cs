using System.IO.Compression;

internal class Firmware
{
    private readonly string _zipfilename;

    public Firmware(string zipfilename)
    {
        _zipfilename = zipfilename;
    }

    public Rom Read(string filename)
    {
        using var archive = ZipFile.OpenRead(_zipfilename);

        var entry = archive.GetEntry(filename) ?? throw new FileNotFoundException(filename);
        using var stream = entry.Open();
        using var reader = new BinaryReader(stream ?? throw new InvalidOperationException());
        
        var contents = reader.ReadBytes((int)entry.Length);

        return new Rom(contents);
    }
}