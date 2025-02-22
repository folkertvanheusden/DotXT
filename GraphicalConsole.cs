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

    public GraphicalFrame GetFrame()
    {
        return _d.GetFrame();
    }
};
