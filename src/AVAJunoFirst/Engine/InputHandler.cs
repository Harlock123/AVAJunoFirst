using Avalonia.Input;

namespace AVAJunoFirst.Engine;

/// <summary>
/// Keyboard and gamepad boiled down to the controls: left and right, forward and back, fire and warp.
/// </summary>
public sealed class InputHandler : IDisposable
{
    private readonly List<Key> _held = [];
    private readonly Dictionary<PhysicalKey, Key> _pressedAs = [];
    private readonly ControllerManager _pad = new(0);

    public void OnKeyDown(Key key, PhysicalKey physical = PhysicalKey.None)
    {
        if (physical != PhysicalKey.None)
        {
            if (_pressedAs.TryGetValue(physical, out var was) && was != key) _held.Remove(was);
            _pressedAs[physical] = key;
        }
        _held.Remove(key);
        _held.Add(key);
    }

    /// <summary>A key comes up. The key it went down as is let go too: with the keyboard option
    /// shift:both_capslock_cancel, letting go of a Shift arrives as CapsLock, and the Shift would stay held.</summary>
    public void OnKeyUp(Key key, PhysicalKey physical = PhysicalKey.None)
    {
        _held.Remove(key);
        if (physical != PhysicalKey.None && _pressedAs.Remove(physical, out var was)) _held.Remove(was);
    }

    public bool Has(Key key) => _held.Contains(key);
    private bool Any(params Key[] keys) => keys.Any(_held.Contains);

    public void Update() => _pad.Update();

    public Controls Controls
    {
        get
        {
            int dx = (Any(Key.Right, Key.D, Key.NumPad6) ? 1 : 0) - (Any(Key.Left, Key.A, Key.NumPad4) ? 1 : 0);
            if (dx == 0) dx = _pad.DPadLeft || _pad.LeftStickX < -0.4f ? -1 : _pad.DPadRight || _pad.LeftStickX > 0.4f ? 1 : 0;
            int dz = (Any(Key.Up, Key.W, Key.NumPad8) ? 1 : 0) - (Any(Key.Down, Key.S, Key.NumPad2) ? 1 : 0);
            if (dz == 0) dz = _pad.DPadUp || _pad.LeftStickY < -0.4f ? 1 : _pad.DPadDown || _pad.LeftStickY > 0.4f ? -1 : 0;
            bool fire = Any(Key.Space, Key.Z, Key.LeftCtrl, Key.RightCtrl) || _pad.ButtonA || _pad.RightTrigger > 0.5f;
            bool warp = Any(Key.X, Key.LeftShift, Key.RightShift) || _pad.ButtonB || _pad.ButtonX;
            return new Controls(dx, dz, fire, warp);
        }
    }

    public bool Start => Any(Key.Enter) || _pad.ButtonStart;

    public bool PadUp => _pad.DPadUp || _pad.LeftStickY < -0.5f;
    public bool PadDown => _pad.DPadDown || _pad.LeftStickY > 0.5f;
    public bool PadLeft => _pad.DPadLeft || _pad.LeftStickX < -0.5f;
    public bool PadRight => _pad.DPadRight || _pad.LeftStickX > 0.5f;
    public bool PadAccept => _pad.ButtonA || _pad.ButtonX || _pad.ButtonB;
    public bool PadDone => _pad.ButtonStart;
    public bool PadPause => _pad.ButtonStart;
    public bool PadBack => _pad.ButtonBack;

    public void Dispose() => _pad.Dispose();
}
