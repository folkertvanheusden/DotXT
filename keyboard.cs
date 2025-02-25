using System.Threading;

class Keyboard : Device
{
    protected int _irq_nr = 1;
    protected int _kb_reset_irq_delay = 4770;  // cycles for 1ms @ 4.77 MHz
    protected int _kb_key_irq = 4770000/50;
    private Mutex _keyboard_buffer_lock = new();
    private Queue<int> _keyboard_buffer = new();
    private bool _clock_low = false;
    private byte _0x61_bits = 0;
    private byte _last_scan_code = 0;

    public Keyboard()
    {
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
            byte rc = _last_scan_code;
            if (_keyboard_buffer.Count > 0)
            {
                rc = (byte)_keyboard_buffer.Dequeue();
                _last_scan_code = rc;
            }
            _keyboard_buffer_lock.ReleaseMutex();

            Log.DoLog($"Keyboard: scan code {rc:X02}", true);

            return (rc, false);
        }
        else if (port == 0x61)
            return (_0x61_bits, false);
        else if (port == 0x64)
        {
            Log.DoLog($"Keyboard: 0x64", true);
            return (0x10, false);
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

    public override bool Tick(int cycles, int clock)
    {
        if (CheckScheduledInterrupt(cycles))
            _pic.RequestInterruptPIC(_irq_nr);

        return false;
    }
}
