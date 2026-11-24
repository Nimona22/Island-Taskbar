using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using IslandTaskbar.Models;

namespace IslandTaskbar.Adapters.Implementations
{
    internal sealed class WindowIconLoader : IDisposable
    {
        private readonly Func<string, WindowIcon?> _load;
        private readonly object _gate = new();
        private readonly Dictionary<string, WindowIcon?> _cache = new(StringComparer.OrdinalIgnoreCase);
        private bool _loading;
        private bool _disposed;
        public WindowIconLoader(Func<string, WindowIcon?> load) => _load = load;
        public event EventHandler? Changed;

        public WindowIcon? Get(string executable)
        {
            lock (_gate)
            {
                if (_disposed) return null;
                if (_cache.TryGetValue(executable, out var icon)) return icon;
                // Shell extraction is optional and cannot be cancelled. Keep one outstanding
                // lookup; discovery uses its generic fallback immediately even if it hangs.
                if (_loading) return null;
                _loading = true;
                _ = Task.Run(() => Load(executable));
                return null;
            }
        }

        private void Load(string executable)
        {
            WindowIcon? icon = null;
            try { icon = _load(executable); }
            catch (Exception failure) when (failure is Win32Exception or COMException
                or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            { Debug.WriteLine($"Executable icon lookup failed: {failure.GetType().Name}."); }
            lock (_gate)
            {
                _loading = false;
                if (_disposed) return;
                _cache[executable] = icon;
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
                _cache.Clear();
            }
        }
    }
}
