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
        GraphicalFrame in_ = _d.GetFrame();

        GraphicalFrame gf = new();
        gf.width = in_.width;
        gf.height = in_.height;
        gf.rgb_pixels = in_.rgb_pixels.ToArray();
        return gf;
    }
};
