internal struct GraphicalFrame
{
    public int width { get; set; }
    public int height { get; set; }
    public byte [] rgb_pixels { get; set; }
};

abstract class GraphicalConsole: EmulatorConsole
{
    public GraphicalConsole()
    {
    }

    public override void Write(string what)
    {
    }

    public int GetFrameVersion()
    {
        return _d.GetFrameVersion();
    }

    public GraphicalFrame GetFrame(bool force)
    {
        return _d.GetFrame(force);
    }

    public byte [] GetBmp()
    {
        return _d.GraphicalFrameToBmp(GetFrame(false));
    }
};
