using System.Runtime.InteropServices;

namespace ApertureOS.Services;

public readonly struct GamepadSnapshot
{
    public bool Up { get; init; }
    public bool Down { get; init; }
    public bool Left { get; init; }
    public bool Right { get; init; }
    public bool A { get; init; }
    public bool B { get; init; }
    public bool Back { get; init; }
    public bool Start { get; init; }
    public bool LeftShoulder { get; init; }
    public bool RightShoulder { get; init; }
    public bool X { get; init; }
    public bool Y { get; init; }
}

/// <summary>
/// Tracks a repeating gamepad button/direction: fires immediately on press,
/// then again every RepeatInterval while held.
/// </summary>
public sealed class GamepadRepeater
{
    private static readonly TimeSpan RepeatInterval = TimeSpan.FromMilliseconds(200);

    private bool _held;
    private DateTime _lastRepeat;

    public void Update(bool pressed, Action action)
    {
        if (!pressed)
        {
            _held = false;
            return;
        }

        var now = DateTime.UtcNow;
        if (!_held)
        {
            action();
            _lastRepeat = now;
            _held = true;
        }
        else if (now - _lastRepeat > RepeatInterval)
        {
            action();
            _lastRepeat = now;
        }
    }

    /// <summary>
    /// Resyncs to the actual current physical state instead of assuming nothing's held - use this
    /// (never a blind "assume released") whenever polling resumes after a gap this tracker wasn't
    /// watching through (a new window/dialog opening, a hidden window reappearing). A still-held
    /// direction from whatever input caused that transition would otherwise read as a brand-new
    /// press the instant polling resumes and repeat immediately, instead of waiting for the normal
    /// repeat interval like a real held direction would.
    /// </summary>
    public void Sync(bool currentlyPressed)
    {
        _held = currentlyPressed;
        if (currentlyPressed)
        {
            _lastRepeat = DateTime.UtcNow;
        }
    }
}

/// <summary>
/// Tracks a non-repeating gamepad button: fires once on the press edge only.
/// </summary>
public sealed class GamepadEdge
{
    private bool _held;

    public void Update(bool pressed, Action action)
    {
        if (pressed && !_held)
        {
            action();
        }

        _held = pressed;
    }

    /// <summary>
    /// Resyncs to the actual current physical state instead of assuming nothing's held - use this
    /// (never a blind "assume released") whenever polling resumes after a gap this tracker wasn't
    /// watching through (a new window/dialog opening, a hidden window reappearing). A blind
    /// "assume released" here is exactly what let a button still physically held from whatever
    /// action caused that transition (e.g. the same A press used to confirm "Exit Game" on the
    /// pause overlay, still down the instant the launcher reappears) get misread as a brand-new
    /// press and fire immediately - instead of correctly requiring an actual release first.
    /// </summary>
    public void Sync(bool currentlyPressed) => _held = currentlyPressed;
}

public static class GamepadService
{
    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint dwPacketNumber;
        public XInputGamepad Gamepad;
    }

    [DllImport("xinput1_4.dll")]
    private static extern int XInputGetState(int dwUserIndex, out XInputState pState);

    private const ushort DPadUp = 0x0001;
    private const ushort DPadDown = 0x0002;
    private const ushort DPadLeft = 0x0004;
    private const ushort DPadRight = 0x0008;
    private const ushort ButtonStart = 0x0010;
    private const ushort ButtonBack = 0x0020;
    private const ushort ButtonLeftShoulder = 0x0100;
    private const ushort ButtonRightShoulder = 0x0200;
    private const ushort ButtonA = 0x1000;
    private const ushort ButtonB = 0x2000;
    private const ushort ButtonX = 0x4000;
    private const ushort ButtonY = 0x8000;
    // Microsoft's own recommended XInput deadzones are 7849 (left stick) and 8689 (right stick)
    // out of a max of 32767 - this was 15000, nearly double that, so the stick had to be pushed
    // almost halfway to full deflection before a direction registered at all.
    private const short StickDeadzone = 8000;

    /// <summary>Polls every connected controller (up to 4) and OR-combines their state.</summary>
    public static GamepadSnapshot Poll()
    {
        bool up = false, down = false, left = false, right = false;
        bool a = false, b = false, back = false, start = false;
        bool leftShoulder = false, rightShoulder = false;
        bool x = false, y = false;

        for (int i = 0; i < 4; i++)
        {
            if (XInputGetState(i, out var state) != 0)
                continue;

            var pad = state.Gamepad;
            up |= (pad.wButtons & DPadUp) != 0 || pad.sThumbLY > StickDeadzone;
            down |= (pad.wButtons & DPadDown) != 0 || pad.sThumbLY < -StickDeadzone;
            left |= (pad.wButtons & DPadLeft) != 0 || pad.sThumbLX < -StickDeadzone;
            right |= (pad.wButtons & DPadRight) != 0 || pad.sThumbLX > StickDeadzone;
            a |= (pad.wButtons & ButtonA) != 0;
            b |= (pad.wButtons & ButtonB) != 0;
            back |= (pad.wButtons & ButtonBack) != 0;
            start |= (pad.wButtons & ButtonStart) != 0;
            leftShoulder |= (pad.wButtons & ButtonLeftShoulder) != 0;
            rightShoulder |= (pad.wButtons & ButtonRightShoulder) != 0;
            x |= (pad.wButtons & ButtonX) != 0;
            y |= (pad.wButtons & ButtonY) != 0;
        }

        return new GamepadSnapshot
        {
            Up = up, Down = down, Left = left, Right = right,
            A = a, B = b, Back = back, Start = start,
            LeftShoulder = leftShoulder, RightShoulder = rightShoulder,
            X = x, Y = y
        };
    }
}
