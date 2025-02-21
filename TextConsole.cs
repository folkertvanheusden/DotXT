abstract class TextConsole
{
    protected Display _d = null;

    public TextConsole()
    {
    }

    public abstract void Write(string what);

    public void RegisterDisplay(Display d)
    {
        _d = d;
    }
};
