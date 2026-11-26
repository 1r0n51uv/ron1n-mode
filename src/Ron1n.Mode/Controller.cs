using System.Runtime.InteropServices;
using Windows.Gaming.Input;

namespace Ron1n.Mode;

internal sealed class Controller
{
    public const int Up = 1, Down = 2, Left = 4, Right = 8, Menu = 16, View = 32;
    public const int Confirm = 0x1000, Back = 0x2000, X = 0x4000, Y = 0x8000;
    private readonly Dictionary<string, int> _previous = [];
    private readonly Dictionary<string, (bool[] Buttons, GameControllerSwitchPosition[] Hat, double[] Axes)> _readings = [];

    public bool Connected { get; private set; }

    public (int Pressed, bool Shortcut) Poll(int shortcut)
    {
        var pressed = 0;
        var activate = false;
        var seen = new HashSet<string>();
        var xbox = false;
        for (uint i = 0; i < 4; i++)
            if (XInputGetState(i, out var input) == 0)
            {
                xbox = true;
                var state = input.Buttons | Stick(input.LeftX / 32768.0, input.LeftY / 32768.0);
                Read("Xbox:" + i, state);
            }
        foreach (var pad in RawGameController.RawGameControllers)
        {
            try
            {
                if (xbox && pad.HardwareVendorId == 0x045e) continue;
                if (pad.HardwareVendorId is not (0x054c or 0x057e)) continue;
                var id = pad.NonRoamableId;
                if (!_readings.TryGetValue(id, out var raw))
                    _readings[id] = raw = (new bool[pad.ButtonCount], new GameControllerSwitchPosition[pad.SwitchCount], new double[pad.AxisCount]);
                pad.GetCurrentReading(raw.Buttons, raw.Hat, raw.Axes);
                if (raw.Buttons.Count(on => on) > 8) continue;
                var sony = pad.HardwareVendorId == 0x054c;
                var state = 0;
                Button(1, Confirm);
                Button(sony ? 2 : 0, Back);
                Button(sony ? 0 : 3, X);
                Button(sony ? 3 : 2, Y);
                Button(8, View);
                Button(9, Menu);
                if (raw.Hat.Length > 0)
                    state |= raw.Hat[0] switch
                    {
                        GameControllerSwitchPosition.Up => Up,
                        GameControllerSwitchPosition.Down => Down,
                        GameControllerSwitchPosition.Left => Left,
                        GameControllerSwitchPosition.Right => Right,
                        GameControllerSwitchPosition.UpLeft => Up | Left,
                        GameControllerSwitchPosition.UpRight => Up | Right,
                        GameControllerSwitchPosition.DownLeft => Down | Left,
                        GameControllerSwitchPosition.DownRight => Down | Right,
                        _ => 0
                    };
                if (raw.Axes.Length >= 2)
                    state |= Stick(2 * raw.Axes[0] - 1, 1 - 2 * raw.Axes[1]);
                Read(id, state);

                void Button(int index, int button)
                {
                    if (index < raw.Buttons.Length && raw.Buttons[index]) state |= button;
                }
            }
            catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException) { }
        }
        foreach (var old in _previous.Keys.Where(key => !seen.Contains(key)).ToArray())
        {
            _previous.Remove(old);
            _readings.Remove(old);
        }
        Connected = seen.Count > 0;
        return (pressed, activate);

        void Read(string key, int state)
        {
            seen.Add(key);
            if (_previous.TryGetValue(key, out var previous))
            {
                pressed |= state & ~previous;
                activate |= shortcut != 0 && (state & shortcut) == shortcut && (previous & shortcut) != shortcut;
            }
            _previous[key] = state;
        }
    }

    private static int Stick(double x, double y) => Math.Abs(x) > Math.Abs(y)
        ? x > .55 ? Right : x < -.55 ? Left : 0
        : y > .55 ? Up : y < -.55 ? Down : 0;

    [DllImport("xinput1_4.dll")] private static extern uint XInputGetState(uint slot, out State input);
    [StructLayout(LayoutKind.Sequential)]
    private struct State
    {
        public uint Packet;
        public ushort Buttons;
        public byte LeftTrigger, RightTrigger;
        public short LeftX, LeftY, RightX, RightY;
    }
}
