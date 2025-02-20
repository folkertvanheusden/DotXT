using System.Threading;

class Keyboard : Device
{
    private Thread _keyboard_thread;
    protected int _irq_nr = 1;
    protected int _kb_reset_irq_delay = 4770;  // cycles for 1ms @ 4.77 MHz
    protected int _kb_key_irq = 100;
    private Mutex _keyboard_buffer_lock = new();
    private Queue<int> _keyboard_buffer = new();
    private bool _clock_low = false;
    private byte _0x61_bits = 0;

    public Keyboard()
    {
        Console.TreatControlCAsInput = true;
        _keyboard_thread = new Thread(Keyboard.KeyboardThread);
        _keyboard_thread.Name = "keyboard_thread";
        _keyboard_thread.Start(this);
    }

    public override int GetIRQNumber()
    {
        return _irq_nr;
    }

    public void PushKeyboardScancode(int scan_code)
    {
        Log.DoLog($"PushKeyboardScancode({scan_code})", true);

        _keyboard_buffer_lock.WaitOne();
        _keyboard_buffer.Enqueue(scan_code);
        _keyboard_buffer_lock.ReleaseMutex();

        ScheduleInterrupt(_kb_key_irq);
    }

    public static void KeyboardThread(object o_kb)
    {
        Log.DoLog("KeyboardThread started", true);

        Keyboard kb = (Keyboard)o_kb;

        Dictionary<ConsoleKey, byte> key_map = new() {
            { ConsoleKey.Escape, 0x01 },
            { ConsoleKey.Enter, 0x1c },
            { ConsoleKey.D1, 0x02 },
            { ConsoleKey.D2, 0x03 },
            { ConsoleKey.D3, 0x04 },
            { ConsoleKey.D4, 0x05 },
            { ConsoleKey.D5, 0x06 },
            { ConsoleKey.D6, 0x07 },
            { ConsoleKey.D7, 0x08 },
            { ConsoleKey.D8, 0x09 },
            { ConsoleKey.D9, 0x0a },
            { ConsoleKey.D0, 0x0b },
            { ConsoleKey.A, 0x1e },
            { ConsoleKey.B, 0x30 },
            { ConsoleKey.C, 0x2e },
            { ConsoleKey.D, 0x20 },
            { ConsoleKey.E, 0x12 },
            { ConsoleKey.F, 0x21 },
            { ConsoleKey.G, 0x22 },
            { ConsoleKey.H, 0x23 },
            { ConsoleKey.I, 0x17 },
            { ConsoleKey.J, 0x24 },
            { ConsoleKey.K, 0x25 },
            { ConsoleKey.L, 0x26 },
            { ConsoleKey.M, 0x32 },
            { ConsoleKey.N, 0x31 },
            { ConsoleKey.O, 0x18 },
            { ConsoleKey.P, 0x19 },
            { ConsoleKey.Q, 0x10 },
            { ConsoleKey.R, 0x13 },
            { ConsoleKey.S, 0x1f },
            { ConsoleKey.T, 0x14 },
            { ConsoleKey.U, 0x16 },
            { ConsoleKey.V, 0x2f },
            { ConsoleKey.W, 0x11 },
            { ConsoleKey.X, 0x2d },
            { ConsoleKey.Y, 0x15 },
            { ConsoleKey.Z, 0x2c },
            { ConsoleKey.Spacebar,  0x39 },
            { ConsoleKey.Backspace, 0x0e },
        };

        for(;;)
        {
            ConsoleKeyInfo cki = Console.ReadKey(true);
            ConsoleKey ck = cki.Key;

            Log.DoLog($"Key pressed: {cki}", true);

            if ((cki.Modifiers & ConsoleModifiers.Shift) != 0)
                kb.PushKeyboardScancode(0x2a);  // shift make
            if ((cki.Modifiers & ConsoleModifiers.Control) != 0)
                kb.PushKeyboardScancode(0x1d);  // control make
            if (key_map.ContainsKey(ck))
            {
                kb.PushKeyboardScancode(key_map[ck]);  // key make
                kb.PushKeyboardScancode(key_map[ck] ^ 0x80);  // unmake
            }
            if ((cki.Modifiers & ConsoleModifiers.Shift) != 0)
                kb.PushKeyboardScancode(0x2a ^ 0x80);  // shift unmake
            if ((cki.Modifiers & ConsoleModifiers.Control) != 0)
                kb.PushKeyboardScancode(0x1d ^ 0x80);  // control unmake
        }

        Log.DoLog("KeyboardThread terminating", true);
    }

    public override String GetName()
    {
        return "Keyboard";
    }

    public override void RegisterDevice(Dictionary <ushort, Device> mappings)
    {
        // see PPI
    }

    public override bool IO_Write(ushort port, byte value)
    {
        if (port == 0x0061)
        {
            _0x61_bits = value;

            if ((value & 0x40) == 0x00)
            {
                Log.DoLog($"Keyboard::IO_Write: clock low ({value:X2})");
                _clock_low = true;
            }
            else if (_clock_low)
            {
                _clock_low = false;

                Log.DoLog($"Keyboard::IO_Write: reset triggered; clock high ({value:X2})");
                _keyboard_buffer_lock.WaitOne();
                _keyboard_buffer.Clear();
                _keyboard_buffer.Enqueue(0xaa);  // power on reset reply
                _keyboard_buffer_lock.ReleaseMutex();

                ScheduleInterrupt(_kb_reset_irq_delay);  // the value is a guess, need to protect this with a mutex
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
            bool interrupt_needed = _keyboard_buffer.Count > 0;
            _keyboard_buffer_lock.ReleaseMutex();

            Log.DoLog($"Keyboard: scan code {rc}", true);

            if (interrupt_needed)
                ScheduleInterrupt(_kb_key_irq);

            return (rc, interrupt_needed);
        }
        else if (port == 0x61)
            return (_0x61_bits, false);
        else if (port == 0x64)
        {
            Log.DoLog($"Keyboard: 0x64", true);

            _keyboard_buffer_lock.WaitOne();
            bool keys_pending = _keyboard_buffer.Count > 0;
            _keyboard_buffer_lock.ReleaseMutex();

            if (keys_pending)
                ScheduleInterrupt(_kb_key_irq);

            return ((byte)((keys_pending ? 2 : 0) | 0x10), keys_pending);
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
        if (CheckScheduledInterrupt(cycles))
            _pic.RequestInterruptPIC(_irq_nr);

        return false;
    }
}
