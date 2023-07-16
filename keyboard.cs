using System.Threading;

class Keyboard : Device
{
    private Thread _keyboard_thread;

    private Mutex _keyboard_buffer_lock = new();
    private Queue<int> _keyboard_buffer = new();

    private PendingInterrupt _pi = new();

    public Keyboard()
    {
        _pi.int_vec = 9;  // IRQ 9

        _keyboard_thread = new Thread(Keyboard.KeyboardThread);
        _keyboard_thread.Name = "keyboard_thread";
        _keyboard_thread.Start(this);
    }

    public void PushKeyboardScancode(int scan_code)
    {
        Log.DoLog($"PushKeyboardScancode({scan_code})");

        _keyboard_buffer_lock.WaitOne();

        _keyboard_buffer.Enqueue(scan_code);

        _keyboard_buffer_lock.ReleaseMutex();
    }

    public static void KeyboardThread(object o_kb)
    {
        Log.DoLog("KeyboardThread started");

        Keyboard kb = (Keyboard)o_kb;

        for(;;)
        {
            ConsoleKeyInfo cki = Console.ReadKey(true);
            ConsoleKey ck = cki.Key;

            Log.DoLog($"Key pressed: {cki.Key.ToString()}");

            if (ck == ConsoleKey.F1)  // F1
                kb.PushKeyboardScancode(0x3a);
        }

        Log.DoLog("KeyboardThread terminating");
    }

    public override String GetName()
    {
        return "Keyboard";
    }

    public override void RegisterDevice(Dictionary <ushort, Device> mappings)
    {
        mappings[0x60] = this;
        mappings[0x64] = this;
    }

    public override bool IO_Write(ushort port, byte value)
    {
        return false;
    }

    public override (byte, bool) IO_Read(ushort port)
    {
        bool irq = false;

        _keyboard_buffer_lock.WaitOne();

        irq = _keyboard_buffer.Count > 0;

        _keyboard_buffer_lock.ReleaseMutex();

        if (port == 0x60)
        {
            _keyboard_buffer_lock.WaitOne();
            byte scan_code = 0;

            if (_keyboard_buffer.Count > 0)
                scan_code = (byte)_keyboard_buffer.Dequeue();

            // again, because one character has been dequeued
            irq = _keyboard_buffer.Count > 0;

            _keyboard_buffer_lock.ReleaseMutex();

            return (scan_code, irq);
        }
        else if (port == 0x64)
        {
            return ((byte)(irq ? 5 : 4), irq);
        }

        return (0xff, irq);
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

    public override void SyncClock(int clock)
    {
    }

    public override bool Tick(int cycles)
    {
        _keyboard_buffer_lock.WaitOne();

        bool any_keys = _keyboard_buffer.Count > 0;

        _keyboard_buffer_lock.ReleaseMutex();

        _pi.pending = any_keys;

        return any_keys;
    }

    public override List<PendingInterrupt> GetPendingInterrupts()
    {
        if (_pi.pending)
        {
            List<PendingInterrupt> rc = new();

            rc.Add(_pi);

            return rc;
        }

        return null;
    }
}
