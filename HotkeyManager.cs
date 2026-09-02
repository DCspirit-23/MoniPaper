using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace PaperCare;

internal enum HotkeyAction
{
    ShowPanel,
    ToggleOverlay,
    Toggle = ToggleOverlay,
    IncreaseIntensity,
    DecreaseIntensity
}

/// <summary>
/// The small registration boundary keeps the transaction logic deterministic
/// in self-tests while the application uses the Win32 implementation below.
/// </summary>
internal interface IHotkeyRegistration
{
    bool Register(int id, ShortcutGesture gesture);
    bool Unregister(int id);
}

internal sealed class HotkeyManager : IDisposable
{
    private const uint ModNoRepeat = 0x4000;
    private const int MaximumApplicationHotkeyId = 0xBFFF;
    private readonly HotkeyWindow? _window;
    private readonly IHotkeyRegistration _registration;
    private readonly Action<HotkeyAction> _onPressed;
    private readonly Dictionary<HotkeyAction, RegisteredHotkey> _registrations = new();
    private readonly List<string> _failedHotkeys = new();
    private int _nextId = 1;
    private bool _disposed;

    public IReadOnlyList<string> FailedHotkeys => _failedHotkeys;

    internal HotkeyManager(Action<HotkeyAction> onPressed, HotkeyConfiguration configuration, IHotkeyRegistration? registration = null)
    {
        _onPressed = onPressed;
        if (registration is null)
        {
            _window = new HotkeyWindow(this);
            _registration = new NativeHotkeyRegistration(_window.Handle);
        }
        else
        {
            _registration = registration;
        }

        RegisterInitial(configuration);
    }

    private void RegisterInitial(HotkeyConfiguration configuration)
    {
        if (!configuration.TryValidate(out var validationError))
        {
            _failedHotkeys.Add(validationError ?? "快捷键配置无效。");
            return;
        }

        foreach (var descriptor in Describe(configuration))
        {
            var id = AllocateId();
            if (_registration.Register(id, descriptor.Gesture))
            {
                _registrations.Add(descriptor.Action, new RegisteredHotkey(id, descriptor.Gesture));
            }
            else
            {
                _failedHotkeys.Add($"{descriptor.Name}（{descriptor.Gesture.DisplayText}）");
            }
        }
    }

    /// <summary>
    /// Applies a complete binding set as one synchronous transaction. New
    /// registrations are made first, settings are persisted before old
    /// registrations are released, and every failure restores the old map.
    /// </summary>
    internal bool TryApply(
        HotkeyConfiguration candidate,
        Func<bool> commitSettings,
        Func<bool> rollbackSettings,
        out string? error)
    {
        error = null;
        if (_disposed)
        {
            error = "快捷键管理器已退出。";
            return false;
        }

        if (!candidate.TryValidate(out error))
            return false;

        var byGesture = _registrations.Values.ToDictionary(binding => binding.Gesture);
        var proposed = new Dictionary<HotkeyAction, RegisteredHotkey>();
        var added = new List<RegisteredHotkey>();

        foreach (var descriptor in Describe(candidate))
        {
            if (byGesture.TryGetValue(descriptor.Gesture, out var existing))
            {
                // Reusing by gesture, rather than by action, makes an internal
                // swap of two shortcuts work without a false conflict.
                proposed.Add(descriptor.Action, existing);
                continue;
            }

            RegisteredHotkey registration;
            try
            {
                registration = new RegisteredHotkey(AllocateId(added), descriptor.Gesture);
            }
            catch (InvalidOperationException ex)
            {
                UnregisterAdded(added);
                error = ex.Message;
                return false;
            }
            if (!_registration.Register(registration.Id, registration.Gesture))
            {
                var cleanedUp = UnregisterAdded(added);
                error = $"快捷键无法注册：{descriptor.Name}（{descriptor.Gesture.DisplayText}），可能已被系统或其他程序占用。";
                if (!cleanedUp) error += " 新增快捷键清理失败。";
                return false;
            }

            added.Add(registration);
            proposed.Add(descriptor.Action, registration);
        }

        bool committed;
        try
        {
            committed = commitSettings();
        }
        catch (Exception ex)
        {
            committed = false;
            error = $"设置无法保存：{ex.Message}";
        }

        if (!committed)
        {
            var cleanedUp = UnregisterAdded(added);
            error ??= "设置无法保存，请检查配置目录的写入权限。";
            if (!cleanedUp) error += " 新增快捷键清理失败。";
            return false;
        }

        var proposedIds = proposed.Values.Select(binding => binding.Id).ToHashSet();
        var oldToRelease = _registrations.Values
            .Where(binding => !proposedIds.Contains(binding.Id))
            .ToArray();
        var released = new List<RegisteredHotkey>();
        foreach (var oldBinding in oldToRelease)
        {
            if (_registration.Unregister(oldBinding.Id))
            {
                released.Add(oldBinding);
                continue;
            }

            // A native UnregisterHotKey failure is unusual, but retaining the
            // old map is safer than leaving the application with half a map.
            var restoredAll = true;
            foreach (var restored in released)
                restoredAll &= _registration.Register(restored.Id, restored.Gesture);
            var addedCleanedUp = UnregisterAdded(added);
            var settingsRestored = false;
            try { settingsRestored = rollbackSettings(); } catch { /* Report the failed recovery below. */ }
            error = $"无法释放旧快捷键：{oldBinding.Gesture.DisplayText}。";
            if (!restoredAll) error += "旧快捷键恢复失败。";
            if (!addedCleanedUp) error += "新增快捷键清理失败。";
            if (!settingsRestored) error += "设置文件恢复失败。";
            return false;
        }

        _registrations.Clear();
        foreach (var pair in proposed)
            _registrations.Add(pair.Key, pair.Value);
        _failedHotkeys.Clear();
        return true;
    }

    private void HandleMessage(int id)
    {
        if (_disposed) return;
        foreach (var pair in _registrations)
        {
            if (pair.Value.Id == id)
            {
                _onPressed(pair.Key);
                return;
            }
        }
    }

    internal void DispatchForTest(int id) => HandleMessage(id);

    private bool UnregisterAdded(IEnumerable<RegisteredHotkey> added)
    {
        var allUnregistered = true;
        foreach (var binding in added)
            allUnregistered &= _registration.Unregister(binding.Id);
        return allUnregistered;
    }

    private int AllocateId(IReadOnlyCollection<RegisteredHotkey>? pending = null)
    {
        for (var attempt = 0; attempt < MaximumApplicationHotkeyId; attempt++)
        {
            var id = _nextId;
            _nextId = _nextId == MaximumApplicationHotkeyId ? 1 : _nextId + 1;
            if (_registrations.Values.Any(binding => binding.Id == id)) continue;
            if (pending is not null && pending.Any(binding => binding.Id == id)) continue;
            return id;
        }

        throw new InvalidOperationException("没有可用的快捷键注册标识。");
    }

    private static IEnumerable<HotkeyDescriptor> Describe(HotkeyConfiguration configuration)
    {
        yield return new HotkeyDescriptor(HotkeyAction.ShowPanel, "打开面板", configuration.ShowPanel);
        yield return new HotkeyDescriptor(HotkeyAction.ToggleOverlay, "开关覆盖", configuration.ToggleOverlay);
        yield return new HotkeyDescriptor(HotkeyAction.IncreaseIntensity, "增强", configuration.IncreaseIntensity);
        yield return new HotkeyDescriptor(HotkeyAction.DecreaseIntensity, "减弱", configuration.DecreaseIntensity);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var binding in _registrations.Values)
            _registration.Unregister(binding.Id);
        _registrations.Clear();
        _window?.Dispose();
    }

    private sealed record RegisteredHotkey(int Id, ShortcutGesture Gesture);
    private sealed record HotkeyDescriptor(HotkeyAction Action, string Name, ShortcutGesture Gesture);

    private sealed class NativeHotkeyRegistration : IHotkeyRegistration
    {
        private readonly IntPtr _handle;

        public NativeHotkeyRegistration(IntPtr handle) => _handle = handle;

        public bool Register(int id, ShortcutGesture gesture) =>
            Native.RegisterHotKey(_handle, id, gesture.Modifiers | ModNoRepeat, gesture.Key);

        public bool Unregister(int id) => Native.UnregisterHotKey(_handle, id);
    }

    private sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        private readonly HotkeyManager _owner;

        public HotkeyWindow(HotkeyManager owner)
        {
            _owner = owner;
            var createParams = new CreateParams
            {
                Caption = "MoniPaper 热键",
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
