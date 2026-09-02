using System;
using System.Runtime.InteropServices;

namespace PaperCare;

/// <summary>
/// Captures one shortcut only while the editor window is active. The hook is
/// deliberately short lived and never stores or forwards the key stream.
/// </summary>
internal sealed class ShortcutCaptureSession : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const uint LlkhfUp = 0x0080;
    private const uint VirtualKeyLWin = 0x5B;
    private const uint VirtualKeyRWin = 0x5C;
    private const uint VirtualKeyLShift = 0xA0;
    private const uint VirtualKeyRShift = 0xA1;
    private const uint VirtualKeyLControl = 0xA2;
    private const uint VirtualKeyRControl = 0xA3;
    private const uint VirtualKeyLAlt = 0xA4;
    private const uint VirtualKeyRAlt = 0xA5;
    private const uint PhysicalLShift = 1 << 0;
    private const uint PhysicalRShift = 1 << 1;
    private const uint PhysicalLControl = 1 << 2;
    private const uint PhysicalRControl = 1 << 3;
    private const uint PhysicalLAlt = 1 << 4;
    private const uint PhysicalRAlt = 1 << 5;
    private const uint PhysicalLWin = 1 << 6;
    private const uint PhysicalRWin = 1 << 7;

    private readonly Func<bool> _isEditorActive;
    private readonly Action<ShortcutGesture> _onCaptured;
    private readonly Action _onFinished;
    private readonly Action _onCancelled;
    private readonly Action<string> _onError;
    private readonly Action<uint> _onModifiersChanged;
    private readonly LowLevelKeyboardProc _callback;
    private IntPtr _hook;
    private uint _pressedPhysicalKeys;
    private bool _disposed;
    private bool _testMode;
    private bool _capturingCandidate;
    private uint _capturedVirtualKey;

    public bool IsActive => _hook != IntPtr.Zero;

    public ShortcutCaptureSession(
        Func<bool> isEditorActive,
        Action<ShortcutGesture> onCaptured,
        Action onFinished,
        Action onCancelled,
        Action<string> onError,
        Action<uint> onModifiersChanged)
    {
        _isEditorActive = isEditorActive;
        _onCaptured = onCaptured;
        _onFinished = onFinished;
        _onCancelled = onCancelled;
        _onError = onError;
        _onModifiersChanged = onModifiersChanged;
        _callback = HookCallback;
    }

    public bool Start(out string? error)
    {
        error = null;
        if (_disposed)
        {
            error = "快捷键录制会话已结束。";
            return false;
        }
        if (!_isEditorActive())
        {
            error = "请先打开并激活自定义快捷键页面。";
            return false;
        }

        _pressedPhysicalKeys = SamplePhysicalKeys();
        _capturingCandidate = false;
        _capturedVirtualKey = 0;
        _hook = SetWindowsHookEx(WhKeyboardLl, _callback, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
        {
            var code = Marshal.GetLastWin32Error();
            _pressedPhysicalKeys = 0;
            error = code == 0
                ? "无法开始快捷键录制，请重试。"
                : $"无法开始快捷键录制（系统错误 {code}），请重试。";
            return false;
        }
        return true;
    }

    public void Stop()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
        _pressedPhysicalKeys = 0;
        _capturingCandidate = false;
        _capturedVirtualKey = 0;
    }

    /// <summary>
    /// Feeds a single synthetic keyboard transition through the same state
    /// machine used by the native hook. This is for deterministic tests only;
    /// it never calls SendInput or posts a key to another window.
    /// </summary>
    internal void ProcessKeyForTest(uint virtualKey, bool isDown)
    {
        if (_disposed) return;
        if (!_isEditorActive())
        {
            Stop();
            SafeCancel();
            return;
        }
        _testMode = true;
        try
        {
            ProcessKeyboardEvent(virtualKey, isDown, !isDown, 0, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            _testMode = false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }

    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (code < 0 || _hook == IntPtr.Zero)
                return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);

            if (!_isEditorActive())
            {
                Stop();
                SafeCancel();
                return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
            }

            var message = unchecked((int)wParam.ToInt64());
            var isDown = message == WmKeyDown || message == WmSysKeyDown;
            var isUp = message == WmKeyUp || message == WmSysKeyUp;
            if (!isDown && !isUp)
                return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);

            var data = Marshal.PtrToStructure<KeyboardData>(lParam);
            return ProcessKeyboardEvent(data.VirtualKey, isDown, isUp, data.Flags, code, wParam, lParam);
        }
        catch (Exception)
        {
            // Never allow a managed callback exception to cross the native hook
            // boundary. Stop first, then leave the key stream to Windows.
            try { Stop(); } catch { }
            SafeCancel();
            return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        }
    }

    private IntPtr ProcessKeyboardEvent(uint virtualKey, bool isDown, bool isUp, uint flags, int code, IntPtr wParam, IntPtr lParam)
    {
        if (_capturingCandidate)
        {
            if (virtualKey == _capturedVirtualKey && isUp)
            {
                Stop();
                SafeFinished();
                return new IntPtr(1);
            }

            // Consume repeats of the selected key until its matching key-up.
            // Modifier transitions continue through the normal input path.
            if (virtualKey == _capturedVirtualKey && isDown)
                return new IntPtr(1);
            return PassThrough(code, wParam, lParam);
        }

        if (TryGetModifier(virtualKey, out var modifier, out var physicalKey))
        {
            if (isDown && (flags & LlkhfUp) == 0)
            {
                _pressedPhysicalKeys |= physicalKey;
                SafeModifiersChanged(GetTrackedModifiers());
            }
            else if (isUp || (flags & LlkhfUp) != 0)
            {
                _pressedPhysicalKeys &= ~physicalKey;
            }
            return PassThrough(code, wParam, lParam);
        }

        if (virtualKey is VirtualKeyLWin or VirtualKeyRWin)
        {
            if (isDown && (flags & LlkhfUp) == 0)
                _pressedPhysicalKeys |= virtualKey == VirtualKeyLWin ? PhysicalLWin : PhysicalRWin;
            else if (isUp || (flags & LlkhfUp) != 0)
                _pressedPhysicalKeys &= virtualKey == VirtualKeyLWin ? ~PhysicalLWin : ~PhysicalRWin;

            if (isDown && (flags & LlkhfUp) == 0)
            {
                Stop();
                SafeError("Windows 键不可用于快捷键，本次录制已取消。");
                SafeCancel();
            }
            return PassThrough(code, wParam, lParam);
        }

        if (!isDown || (flags & LlkhfUp) != 0)
            return PassThrough(code, wParam, lParam);

        var modifiers = SampleModifiers();
        var hasWindowsKey = IsWindowsKeyDown();
        var isTab = virtualKey == 0x09;
        var isEscape = virtualKey == 0x1B;
        if (isEscape && modifiers == 0 && !hasWindowsKey)
        {
            Stop();
            SafeCancel();
            return new IntPtr(1);
        }

        // Bare Tab and Shift+Tab remain normal focus navigation. The hook is
        // removed before the event continues to the active WPF window.
        if (isTab && !hasWindowsKey && (modifiers == 0 || modifiers == ShortcutGesture.ModifierShift))
        {
            Stop();
            SafeCancel();
            return PassThrough(code, wParam, lParam);
        }

        if (hasWindowsKey)
        {
            Stop();
            SafeError("Windows 键不可用于快捷键，本次录制已取消。");
            SafeCancel();
            return PassThrough(code, wParam, lParam);
        }

        if (modifiers == 0)
        {
            SafeError("快捷键至少需要一个 Ctrl、Alt 或 Shift 修饰键。");
            return PassThrough(code, wParam, lParam);
        }

        if (!ShortcutGesture.IsSupportedKey(virtualKey) ||
            ShortcutGesture.HasSystemReservedCombination(modifiers, virtualKey))
        {
            SafeError("此按键不能录制，请使用字母、数字、方向键或 F1-F11。");
            return PassThrough(code, wParam, lParam);
        }

        var gesture = new ShortcutGesture(modifiers, virtualKey);
        // Consume the candidate before Windows can dispatch a matching
        // RegisterHotKey notification to the existing backend registration.
        _capturingCandidate = true;
        _capturedVirtualKey = virtualKey;
        SafeCaptured(gesture);
        return new IntPtr(1);
    }

    private uint SampleModifiers()
    {
        var modifiers = GetTrackedModifiers();
        if (_testMode) return modifiers;

        if (IsAsyncKeyDown(VirtualKeyLControl) || IsAsyncKeyDown(VirtualKeyRControl))
            modifiers |= ShortcutGesture.ModifierControl;
        if (IsAsyncKeyDown(VirtualKeyLAlt) || IsAsyncKeyDown(VirtualKeyRAlt))
            modifiers |= ShortcutGesture.ModifierAlt;
        if (IsAsyncKeyDown(VirtualKeyLShift) || IsAsyncKeyDown(VirtualKeyRShift))
            modifiers |= ShortcutGesture.ModifierShift;
        return modifiers;
    }

    private bool IsWindowsKeyDown()
    {
        if ((_pressedPhysicalKeys & (PhysicalLWin | PhysicalRWin)) != 0) return true;
        return !_testMode && (IsAsyncKeyDown(VirtualKeyLWin) || IsAsyncKeyDown(VirtualKeyRWin));
    }

    private uint SamplePhysicalKeys()
    {
        if (_testMode) return _pressedPhysicalKeys;
        var keys = 0u;
        if (IsAsyncKeyDown(VirtualKeyLShift)) keys |= PhysicalLShift;
        if (IsAsyncKeyDown(VirtualKeyRShift)) keys |= PhysicalRShift;
        if (IsAsyncKeyDown(VirtualKeyLControl)) keys |= PhysicalLControl;
        if (IsAsyncKeyDown(VirtualKeyRControl)) keys |= PhysicalRControl;
        if (IsAsyncKeyDown(VirtualKeyLAlt)) keys |= PhysicalLAlt;
        if (IsAsyncKeyDown(VirtualKeyRAlt)) keys |= PhysicalRAlt;
        if (IsAsyncKeyDown(VirtualKeyLWin)) keys |= PhysicalLWin;
        if (IsAsyncKeyDown(VirtualKeyRWin)) keys |= PhysicalRWin;
        return keys;
    }

    private uint GetTrackedModifiers()
    {
        var modifiers = 0u;
        if ((_pressedPhysicalKeys & (PhysicalLControl | PhysicalRControl)) != 0)
            modifiers |= ShortcutGesture.ModifierControl;
        if ((_pressedPhysicalKeys & (PhysicalLAlt | PhysicalRAlt)) != 0)
            modifiers |= ShortcutGesture.ModifierAlt;
        if ((_pressedPhysicalKeys & (PhysicalLShift | PhysicalRShift)) != 0)
            modifiers |= ShortcutGesture.ModifierShift;
        return modifiers;
    }

    private IntPtr PassThrough(int code, IntPtr wParam, IntPtr lParam) =>
        _testMode ? IntPtr.Zero : CallNextHookEx(IntPtr.Zero, code, wParam, lParam);

    private void SafeCaptured(ShortcutGesture gesture)
    {
        try { _onCaptured(gesture); } catch { }
    }

    private void SafeCancel()
    {
        try { _onCancelled(); } catch { }
    }

    private void SafeFinished()
    {
        try { _onFinished(); } catch { }
    }

    private void SafeError(string error)
    {
        try { _onError(error); } catch { }
    }

    private void SafeModifiersChanged(uint modifiers)
    {
        try { _onModifiersChanged(modifiers); } catch { }
    }

    private static bool TryGetModifier(uint virtualKey, out uint modifier, out uint physicalKey)
    {
        (modifier, physicalKey) = virtualKey switch
        {
            VirtualKeyLShift => (ShortcutGesture.ModifierShift, PhysicalLShift),
            VirtualKeyRShift => (ShortcutGesture.ModifierShift, PhysicalRShift),
            0x10 => (ShortcutGesture.ModifierShift, PhysicalLShift | PhysicalRShift),
            VirtualKeyLControl => (ShortcutGesture.ModifierControl, PhysicalLControl),
            VirtualKeyRControl => (ShortcutGesture.ModifierControl, PhysicalRControl),
            0x11 => (ShortcutGesture.ModifierControl, PhysicalLControl | PhysicalRControl),
            VirtualKeyLAlt => (ShortcutGesture.ModifierAlt, PhysicalLAlt),
            VirtualKeyRAlt => (ShortcutGesture.ModifierAlt, PhysicalRAlt),
            0x12 => (ShortcutGesture.ModifierAlt, PhysicalLAlt | PhysicalRAlt),
            _ => (0u, 0u)
        };
        return modifier != 0;
    }

    private static bool IsAsyncKeyDown(uint virtualKey) =>
        (GetAsyncKeyState((int)virtualKey) & 0x8000) != 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardData
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    private delegate IntPtr LowLevelKeyboardProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
