using System.Threading;

class Keyboard : Device
{
    private Thread _keyboard_thread;
    protected new int _irq_nr = 1;
    protected int _irq_trigger_delay = 4770;  // cycles for 1ms @ 4.77 MHz
    private Mutex _keyboard_buffer_lock = new();
    private Queue<int> _keyboard_buffer = new();
    private bool _clock_low = false;
    private byte _0x61_bits = 0;

    public Keyboard()
    {
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

        ScheduleInterrupt(_irq_trigger_delay);  // the value is a guess, need to protect this with a mutex
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
	    else
                kb.PushKeyboardScancode(cki.KeyChar);
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

                ScheduleInterrupt(_irq_trigger_delay);  // the value is a guess, need to protect this with a mutex
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
        else if (port == 0x61)
            return (_0x61_bits, false);
        else if (port == 0x64)
        {
            Log.DoLog($"Keyboard: 0x64", true);

            _keyboard_buffer_lock.WaitOne();
            bool keys_pending = _keyboard_buffer.Count > 0;
            _keyboard_buffer_lock.ReleaseMutex();

            return ((byte)(keys_pending ? 0x21 : 0x20), false);  // TODO 0x21/0x20?
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
            _pic.RequestInterrupt(_irq_nr + _pic.GetInterruptOffset());  // Keyboard is on IRQ1

        return false;
    }
}
