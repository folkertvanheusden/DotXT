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
        Log.DoLog($"PushKeyboardScancode({scan_code})", true);

        _keyboard_buffer_lock.WaitOne();

        _keyboard_buffer.Enqueue(scan_code);

        _keyboard_buffer_lock.ReleaseMutex();
    }

    public static void KeyboardThread(object o_kb)
    {
        Log.DoLog("KeyboardThread started", true);

        Keyboard kb = (Keyboard)o_kb;

        for(;;)
        {
            ConsoleKeyInfo cki = Console.ReadKey(true);
            ConsoleKey ck = cki.Key;

            Log.DoLog($"Key pressed: {cki.Key.ToString()}", true);

            if (ck == ConsoleKey.F1)  // F1
                kb.PushKeyboardScancode(0x3a);
        }

        Log.DoLog("KeyboardThread terminating", true);
    }

    public override String GetName()
    {
        return "Keyboard";
    }

    public override void RegisterDevice(Dictionary <ushort, Device> mappings)
    {
        mappings[0x60] = this;
        mappings[0x61] = this;
        mappings[0x64] = this;
    }

    public override bool IO_Write(ushort port, byte value)
    {
        if (port == 0x0061)
        {
            if ((value & 0x40) == 0x00)
            {
                _keyboard_buffer_lock.WaitOne();

                _keyboard_buffer.Clear();
                _keyboard_buffer.Enqueue(0xaa);  // power on reset reply

                _keyboard_buffer_lock.ReleaseMutex();
            }
        }

        return false;
    }

    public override (byte, bool) IO_Read(ushort port)
    {
        if (port == 0x60)
        {
            _keyboard_buffer_lock.WaitOne();

            byte rc = 0;

            if (_keyboard_buffer.Count > 0)
                rc = (byte)_keyboard_buffer.Dequeue();

            _keyboard_buffer_lock.ReleaseMutex();

            Log.DoLog($"Keyboard: scan code {rc:X2}", true);

            return (rc, false);
        }
        else if (port == 0x64)
        {
            Log.DoLog($"Keyboard: 0x64", true);

            _keyboard_buffer_lock.WaitOne();

            bool keys_pending = _keyboard_buffer.Count > 0;

            _keyboard_buffer_lock.ReleaseMutex();

            return ((byte)(keys_pending ? 21 : 20), false);
        }

        return (0x00, false);
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

        // TODO set flag in 8259

        _pi.pending = any_keys;

        _pic.SetPendingInterrupt(_pi.int_vec);

        return any_keys;
    }

    public override List<PendingInterrupt> GetPendingInterrupts()
    {
        // Log.DoLog("keyboard::GetPendingInterrupts");

        if (_pi.pending)
        {
            List<PendingInterrupt> rc = new();

            // TODO: only when Count > 0
            rc.Add(_pi);

            return rc;
        }

        return null;
    }
}
