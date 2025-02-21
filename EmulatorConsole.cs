abstract class EmulatorConsole
{
    protected Display _d = null;

    public EmulatorConsole()
    {
    }

    public void RegisterDisplay(Display d)
    {
        _d = d;
    }

    public abstract void Write(string what);
};
