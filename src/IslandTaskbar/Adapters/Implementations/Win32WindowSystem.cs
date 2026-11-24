using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using IslandTaskbar.Adapters.Interfaces;
using IslandTaskbar.Models;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace IslandTaskbar.Adapters.Implementations
{
    /// <summary>Owns event-driven application-window discovery, native icons and user-requested activation.</summary>
    internal sealed class Win32WindowSystem : IWindowSystem
    {
        private static readonly object OwnersGate = new();
        private static readonly Dictionary<nint, Win32WindowSystem> HookOwners = new();
        private readonly object _gate = new();
        private readonly uint _ownProcessId = (uint)Environment.ProcessId;
        private readonly List<HWINEVENTHOOK> _hooks = new();
        private readonly Dictionary<nint, RunningWindowId> _identities = new();
        private readonly Dictionary<RunningWindowId, WindowIcon?> _icons = new();
        private readonly WindowIconLoader _executableIcons;
        private long _nextInstance;
        private nint _lastForeground;
        private int _hookThread;
        private bool _disposed;

        private sealed class Enumeration
        {
            internal readonly List<HWND> Handles = new();
            internal readonly CancellationToken Token;
            internal Exception? Failure;
            internal Enumeration(CancellationToken token) => Token = token;
        }

        public event EventHandler? WindowsChanged;

        public Win32WindowSystem()
        {
            _executableIcons = new WindowIconLoader(ReadExecutableIcon);
            _executableIcons.Changed += OnExecutableIconLoaded;
        }

        private void OnExecutableIconLoaded(object? sender, EventArgs e)
        {
            lock (_gate)
            {
                if (_disposed) return;
                foreach (var id in _icons.Where(pair => pair.Value is null).Select(pair => pair.Key).ToArray())
                    _icons.Remove(id);
            }
            WindowsChanged?.Invoke(this, EventArgs.Empty);
        }

        public unsafe void Initialize()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_hooks.Count != 0) throw new InvalidOperationException("Window discovery is already initialized.");
            _hookThread = Environment.CurrentManagedThreadId;
            try
            {
                AddHook(PInvoke.EVENT_SYSTEM_FOREGROUND, PInvoke.EVENT_SYSTEM_FOREGROUND);
                AddHook(PInvoke.EVENT_SYSTEM_MINIMIZESTART, PInvoke.EVENT_SYSTEM_MINIMIZEEND);
                AddHook(PInvoke.EVENT_OBJECT_CREATE, PInvoke.EVENT_OBJECT_HIDE);
                AddHook(PInvoke.EVENT_OBJECT_NAMECHANGE, PInvoke.EVENT_OBJECT_NAMECHANGE);
                AddHook(PInvoke.EVENT_OBJECT_CLOAKED, PInvoke.EVENT_OBJECT_UNCLOAKED);
            }
            catch { ReleaseHooks(); throw; }
        }

        private unsafe void AddHook(uint first, uint last)
        {
            // WinUI already pumps this thread. Out-of-context callbacks never enumerate or touch XAML.
            HWINEVENTHOOK hook = PInvoke.SetWinEventHook(first, last, default(HMODULE), &OnWinEvent,
                0, 0, PInvoke.WINEVENT_SKIPOWNPROCESS);
            if (hook == default) throw NativeFailure("SetWinEventHook");
            lock (OwnersGate) HookOwners.Add((nint)hook, this);
            _hooks.Add(hook);
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
        private static void OnWinEvent(HWINEVENTHOOK hook, uint eventType, HWND hwnd,
            int objectId, int childId, uint threadId, uint time)
        {
            try
            {
                if (hwnd == default || (eventType >= PInvoke.EVENT_OBJECT_CREATE && (objectId != 0 || childId != 0))) return;
                Win32WindowSystem? owner;
                lock (OwnersGate) HookOwners.TryGetValue((nint)hook, out owner);
                owner?.OnWindowChanged(eventType, (nint)hwnd);
            }
            catch (Exception failure) { Debug.WriteLine($"WinEvent callback failed: {failure.Message}"); }
        }

        private void OnWindowChanged(uint eventType, nint hwnd)
        {
            lock (_gate)
            {
                if (_disposed) return;
                if (_identities.TryGetValue(hwnd, out var id))
                {
                    if (eventType == PInvoke.EVENT_OBJECT_DESTROY) _identities.Remove(hwnd);
                    if (eventType is PInvoke.EVENT_OBJECT_DESTROY or PInvoke.EVENT_OBJECT_SHOW)
                        _icons.Remove(id);
                }
            }
            WindowsChanged?.Invoke(this, EventArgs.Empty);
        }

        public Task<IReadOnlyList<RunningWindow>> EnumerateAsync(CancellationToken cancellationToken)
            => Task.Run(() => ReadWindows(cancellationToken), cancellationToken);

        private unsafe IReadOnlyList<RunningWindow> ReadWindows(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
            var context = new Enumeration(token);
            GCHandle state = GCHandle.Alloc(context);
            try
            {
                if (!PInvoke.EnumWindows(&OnEnumeratedWindow, (LPARAM)GCHandle.ToIntPtr(state)))
                {
                    token.ThrowIfCancellationRequested();
                    if (context.Failure is not null) throw context.Failure;
                    throw NativeFailure("EnumWindows");
                }
            }
            finally { state.Free(); }

            // Executable icon fallback uses shell APIs on this background thread.
            int comResult = PInvoke.CoInitializeEx(null, COINIT.COINIT_MULTITHREADED).Value;
            try
            {
                HWND foreground = PInvoke.GetForegroundWindow();
                uint foregroundProcess = 0;
                PInvoke.GetWindowThreadProcessId(foreground, &foregroundProcess);
                var windows = new List<RunningWindow>();
                foreach (HWND hwnd in context.Handles)
                {
                    token.ThrowIfCancellationRequested();
                    var candidate = ReadCandidate(hwnd);
                    if (candidate is null || !WindowEligibility.IsEligible(candidate, _ownProcessId)) continue;
                    RunningWindowId id = Identity(candidate);
                    string processName = "Application";
                    string? executable = null;
                    try
                    {
                        using var process = Process.GetProcessById((int)id.ProcessId);
                        processName = process.ProcessName;
                        try { executable = process.MainModule?.FileName; }
                        catch (Win32Exception failure) { Debug.WriteLine($"Read process image {id.ProcessId}: {failure.NativeErrorCode}"); }
                    }
                    catch (Exception failure) when (failure is ArgumentException or InvalidOperationException or Win32Exception)
                    { Debug.WriteLine($"Read process {id.ProcessId}: {failure.Message}"); }

                    WindowIcon? icon;
                    bool cached;
                    lock (_gate) cached = _icons.TryGetValue(id, out icon);
                    if (!cached) icon = ReadIcon(hwnd);
                    // A shell result may complete before this pass stores its null fallback.
                    // Consult its cache on later snapshots without requerying the window icon.
                    if (icon is null && executable is not null) icon = _executableIcons.Get(executable);
                    if (!cached || icon is not null)
                    {
                        lock (_gate)
                            if (!_disposed && _identities.TryGetValue((nint)hwnd, out var current) && current == id) _icons[id] = icon;
                    }
                    if (!PInvoke.IsWindow(hwnd) || !IsTracked(id)) continue;
                    windows.Add(new RunningWindow(id, candidate.Title, processName, executable,
                        candidate.IsMinimized, hwnd == foreground, icon));
                }
                lock (_gate)
                {
                    if (_disposed) return Array.Empty<RunningWindow>();
                    var live = windows.Select(window => window.Id.Handle).ToHashSet();
                    foreach (nint removed in _identities.Keys.Where(handle => !live.Contains(handle)).ToArray())
                    { _icons.Remove(_identities[removed]); _identities.Remove(removed); }
                    nint active = windows.FirstOrDefault(window => window.IsActive)?.Id.Handle ?? 0;
                    // Focusing our overlay retains the last eligible application indicator.
                    if (active == 0 && foregroundProcess == _ownProcessId && live.Contains(_lastForeground))
                        active = _lastForeground;
                    _lastForeground = active;
                    return windows.Select(window => window with { IsActive = window.Id.Handle == active }).ToArray();
                }
            }
            finally { if (comResult >= 0) PInvoke.CoUninitialize(); }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
        private static BOOL OnEnumeratedWindow(HWND hwnd, LPARAM state)
        {
            var context = (Enumeration)GCHandle.FromIntPtr(state.Value).Target!;
            if (context.Token.IsCancellationRequested) return false;
            try { context.Handles.Add(hwnd); return true; }
            catch (Exception failure) { context.Failure = failure; return false; }
        }

        private unsafe WindowCandidate? ReadCandidate(HWND hwnd)
        {
            if (!PInvoke.IsWindowVisible(hwnd)) return null;
            if (((WINDOW_STYLE)PInvoke.GetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_STYLE) & WINDOW_STYLE.WS_CHILD) != 0) return null;
            uint processId = 0;
            uint threadId = PInvoke.GetWindowThreadProcessId(hwnd, &processId);
            if (threadId == 0 || processId == _ownProcessId) return null;
            var style = (WINDOW_EX_STYLE)PInvoke.GetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
            Span<char> className = stackalloc char[256];
            int classLength = PInvoke.GetClassName(hwnd, className);
            int capacity = Math.Clamp(PInvoke.GetWindowTextLength(hwnd) + 1, 1, 4096);
            var title = new char[capacity];
            int titleLength = PInvoke.GetWindowText(hwnd, title);
            uint cloak = 0;
            int cloakResult = PInvoke.DwmGetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_CLOAKED, &cloak, sizeof(uint)).Value;
            if (cloakResult < 0)
            { Debug.WriteLine($"Read window cloak state: 0x{cloakResult:X8}"); return null; }
            return new WindowCandidate((nint)hwnd, processId, threadId, new string(title, 0, titleLength),
                new string(className.Slice(0, classLength)), true,
                (style & WINDOW_EX_STYLE.WS_EX_TOOLWINDOW) != 0, cloak != 0,
                PInvoke.GetWindow(hwnd, GET_WINDOW_CMD.GW_OWNER) != default,
                (style & WINDOW_EX_STYLE.WS_EX_APPWINDOW) != 0,
                (style & WINDOW_EX_STYLE.WS_EX_NOACTIVATE) != 0, PInvoke.IsIconic(hwnd));
        }

        private RunningWindowId Identity(WindowCandidate window)
        {
            lock (_gate)
            {
                if (_identities.TryGetValue(window.Handle, out var previous))
                {
                    if (previous.ProcessId == window.ProcessId && previous.ThreadId == window.ThreadId) return previous;
                    _icons.Remove(previous);
                }
                var id = new RunningWindowId(window.Handle, window.ProcessId, window.ThreadId, ++_nextInstance);
                _identities[window.Handle] = id;
                return id;
            }
        }

        private bool IsTracked(RunningWindowId id)
        {
            lock (_gate) return !_disposed && _identities.TryGetValue(id.Handle, out var current) && current == id;
        }

        private bool CanActivate(RunningWindow window)
        {
            if (!IsTracked(window.Id)) return false;
            var candidate = ReadCandidate((HWND)window.Id.Handle);
            return candidate is not null && candidate.ProcessId == window.Id.ProcessId
                && candidate.ThreadId == window.Id.ThreadId && WindowEligibility.IsEligible(candidate, _ownProcessId);
        }

        public async Task<WindowActivationResult> ActivateAsync(RunningWindow window, CancellationToken cancellationToken)
        {
            var result = await Task.Run(() => BeginActivation(window), cancellationToken).ConfigureAwait(false);
            if (result != WindowActivationResult.Success) return result;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!CanActivate(window)) return WindowActivationResult.Unavailable;
                if ((nint)PInvoke.GetForegroundWindow() == window.Id.Handle && !PInvoke.IsIconic((HWND)window.Id.Handle))
                    return WindowActivationResult.Success;
                // Only a bounded command-completion wait; discovery itself is event driven.
                await Task.Delay(25, cancellationToken).ConfigureAwait(false);
            }
            return WindowActivationResult.Denied;
        }

        private WindowActivationResult BeginActivation(RunningWindow window)
        {
            if (!CanActivate(window)) return WindowActivationResult.Unavailable;
            HWND hwnd = (HWND)window.Id.Handle;
            if (PInvoke.IsIconic(hwnd) && !PInvoke.ShowWindowAsync(hwnd, SHOW_WINDOW_CMD.SW_RESTORE))
            { Debug.WriteLine($"Restore window failed: {Marshal.GetLastWin32Error()}"); return WindowActivationResult.Failed; }
            bool accepted = PInvoke.SetForegroundWindow(hwnd);
            if (!accepted && PInvoke.GetForegroundWindow() != hwnd)
                Debug.WriteLine("Foreground request not yet accepted; verifying asynchronous restoration.");
            return CanActivate(window) ? WindowActivationResult.Success : WindowActivationResult.Unavailable;
        }

        private static unsafe WindowIcon? ReadIcon(HWND hwnd)
        {
            HICON owned = default;
            try
            {
                // Prefer the larger source for the taskbar's icon; fall back to small/window-class icons.
                foreach (nuint size in new nuint[] { 1, 2, 0 })
                {
                    nuint result = 0;
                    var sent = PInvoke.SendMessageTimeout(hwnd, 0x007F, (WPARAM)size, (LPARAM)(nint)PInvoke.GetDpiForWindow(hwnd),
                        SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_ABORTIFHUNG | SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_BLOCK, 40, &result);
                    if (sent.Value != 0 && result != 0) owned = PInvoke.CopyIcon((HICON)(nint)result);
                    if (owned != default) break;
                }
                if (owned == default)
                {
                    nuint borrowed = PInvoke.GetClassLongPtr(hwnd, GET_CLASS_LONG_INDEX.GCLP_HICONSM);
                    if (borrowed == 0) borrowed = PInvoke.GetClassLongPtr(hwnd, GET_CLASS_LONG_INDEX.GCLP_HICON);
                    if (borrowed != 0) owned = PInvoke.CopyIcon((HICON)(nint)borrowed);
                }
                return owned == default ? null : RasterizeIcon(owned);
            }
            finally
            {
                // WM_GETICON/class icons were borrowed; CopyIcon and SHGetFileInfo icons are owned.
                if (owned != default && !PInvoke.DestroyIcon(owned)) Debug.WriteLine("DestroyIcon failed.");
            }
        }

        private static unsafe WindowIcon? ReadExecutableIcon(string executable)
        {
            int comResult = PInvoke.CoInitializeEx(null, COINIT.COINIT_MULTITHREADED).Value;
            HICON owned = default;
            try
            {
                SHFILEINFOW info = default;
                if (PInvoke.SHGetFileInfo(executable, 0, ref info, SHGFI_FLAGS.SHGFI_ICON | SHGFI_FLAGS.SHGFI_SMALLICON) != 0)
                    owned = info.hIcon;
                return owned == default ? null : RasterizeIcon(owned);
            }
            finally
            {
                // A late shell result is rasterized and released before the loader can discard it.
                if (owned != default && !PInvoke.DestroyIcon(owned)) Debug.WriteLine("DestroyIcon failed.");
                if (comResult >= 0) PInvoke.CoUninitialize();
            }
        }

        private static unsafe WindowIcon? RasterizeIcon(HICON icon)
        {
            const int size = 32;
            HDC dc = PInvoke.CreateCompatibleDC(default);
            if (dc == default) { Debug.WriteLine("Create icon DC failed."); return null; }
            HBITMAP bitmap = default;
            HGDIOBJ previous = default;
            try
            {
                BITMAPINFO info = new() { bmiHeader = new BITMAPINFOHEADER
                { biSize = (uint)sizeof(BITMAPINFOHEADER), biWidth = size, biHeight = -size, biPlanes = 1, biBitCount = 32 } };
                void* pixels = null;
                bitmap = PInvoke.CreateDIBSection(dc, &info, DIB_USAGE.DIB_RGB_COLORS, &pixels, default, 0);
                if (bitmap == default || pixels == null) { Debug.WriteLine("Create icon bitmap failed."); return null; }
                previous = PInvoke.SelectObject(dc, (HGDIOBJ)bitmap);
                if (previous == default || (nint)previous == -1) { Debug.WriteLine("Select icon bitmap failed."); return null; }
                Span<byte> surface = new(pixels, size * size * 4);
                surface.Clear();
                if (!PInvoke.DrawIconEx(dc, 0, 0, icon, size, size, 0, default, DI_FLAGS.DI_NORMAL)) return null;
                byte[] black = surface.ToArray();
                surface.Fill(255);
                if (!PInvoke.DrawIconEx(dc, 0, 0, icon, size, size, 0, default, DI_FLAGS.DI_NORMAL)) return null;
                // Two backgrounds recover alpha for modern icons and legacy masked icons, including black pixels.
                for (int index = 0; index < black.Length; index += 4)
                {
                    int transparency = Math.Max(surface[index] - black[index],
                        Math.Max(surface[index + 1] - black[index + 1], surface[index + 2] - black[index + 2]));
                    black[index + 3] = (byte)Math.Clamp(255 - transparency, 0, 255);
                }
                return new WindowIcon(size, size, black);
            }
            finally
            {
                if (previous != default && (nint)previous != -1) PInvoke.SelectObject(dc, previous);
                if (bitmap != default && !PInvoke.DeleteObject((HGDIOBJ)bitmap)) Debug.WriteLine("Delete icon bitmap failed.");
                if (!PInvoke.DeleteDC(dc)) Debug.WriteLine("Delete icon DC failed.");
            }
        }

        private void ReleaseHooks()
        {
            foreach (var hook in _hooks)
            {
                lock (OwnersGate) HookOwners.Remove((nint)hook);
                if (!PInvoke.UnhookWinEvent(hook)) Debug.WriteLine($"UnhookWinEvent failed: {Marshal.GetLastWin32Error()}");
            }
            _hooks.Clear();
        }

        public void Dispose()
        {
            if (_hooks.Count != 0 && Environment.CurrentManagedThreadId != _hookThread)
                throw new InvalidOperationException("Window hooks must be disposed on their installing UI thread.");
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _identities.Clear();
                _icons.Clear();
            }
            _executableIcons.Changed -= OnExecutableIconLoaded;
            _executableIcons.Dispose();
            ReleaseHooks();
        }

        private static Win32Exception NativeFailure(string operation)
            => new(Marshal.GetLastWin32Error(), $"{operation} failed.");
    }
}
