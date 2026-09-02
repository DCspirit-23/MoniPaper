using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace PaperCare;

internal enum HotkeyAction
{
    Toggle,
    IncreaseIntensity,
    DecreaseIntensity
}

internal sealed class HotkeyManager : IDisposable
{
    private const int ToggleId = 1;
    private const int IncreaseId = 2;
    private const int DecreaseId = 3;
    private const uint ModControl = 0x0002;
    private const uint ModAlt = 0x0001;
    private const uint ModNoRepeat = 0x4000;

    private readonly HotkeyWindow _window;
    private readonly Action<HotkeyAction> _onPressed;
    private readonly Dictionary<int, HotkeyAction> _actions = new();

    public IReadOnlyList<string> FailedHotkeys { get; }

    public HotkeyManager(Action<HotkeyAction> onPressed)
    {
        _onPressed = onPressed;
        _window = new HotkeyWindow(this);

        var failed = new List<string>();
        TryRegister(ToggleId, 0x50, "Ctrl + Alt + P", failed);
        TryRegister(IncreaseId, 0x26, "Ctrl + Alt + ↑", failed);
        TryRegister(DecreaseId, 0x28, "Ctrl + Alt + ↓", failed);
        FailedHotkeys = failed;
    }

    private void TryRegister(int id, uint key, string label, ICollection<string> failed)
    {
        if (Native.RegisterHotKey(_window.Handle, id, ModControl | ModAlt | ModNoRepeat, key))
            _actions[id] = id switch
            {
                ToggleId => HotkeyAction.Toggle,
                IncreaseId => HotkeyAction.IncreaseIntensity,
                _ => HotkeyAction.DecreaseIntensity
            };
        else
            failed.Add(label);
    }

    private void HandleMessage(int id)
    {
        if (_actions.TryGetValue(id, out var action))
            _onPressed(action);
    }

    public void Dispose()
    {
        foreach (var id in _actions.Keys)
            Native.UnregisterHotKey(_window.Handle, id);
        _actions.Clear();
        _window.Dispose();
    }

    private sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        private readonly HotkeyManager _owner;

        public HotkeyWindow(HotkeyManager owner)
        {
            _owner = owner;
            var createParams = new CreateParams
            {
                Caption = "PaperCare 热键",
                ClassName = "STATIC"
            };
            createParams.ExStyle = 0x00000080;
            CreateHandle(createParams);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312)
            {
                _owner.HandleMessage(m.WParam.ToInt32());
                return;
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
                DestroyHandle();
        }
    }
}
