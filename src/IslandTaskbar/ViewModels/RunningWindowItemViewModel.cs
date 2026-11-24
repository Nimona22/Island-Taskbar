using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using IslandTaskbar.Models;

namespace IslandTaskbar.ViewModels
{
    public sealed class RunningWindowItemViewModel : INotifyPropertyChanged
    {
        private RunningWindow _window;
        private ImageSource? _icon;
        internal RunningWindowItemViewModel(RunningWindow window) { _window = window; SetIcon(window.Icon); }
        internal RunningWindow Window => _window;
        internal RunningWindowId Id => _window.Id;
        public event PropertyChangedEventHandler? PropertyChanged;
        public string Title => string.IsNullOrWhiteSpace(_window.Title) ? _window.ProcessName : _window.Title;
        public string AccessibleName => $"{(_window.IsActive ? "Active, " : "")}{(_window.IsMinimized ? "Minimized, " : "")}{_window.ProcessName}: {Title}";
        public string AutomationId => $"RunningWindow_{_window.Id.Handle:X}_{_window.Id.Instance}";
        public ImageSource? Icon => _icon;
        public Visibility IconVisibility => _icon is null ? Visibility.Collapsed : Visibility.Visible;
        public Visibility FallbackVisibility => _icon is null ? Visibility.Visible : Visibility.Collapsed;
        public double IndicatorWidth => (double)Application.Current.Resources[_window.IsActive ? "Taskbar.Indicator.ActiveWidth" : "Space.Micro"];
        public Brush IndicatorBrush => (Brush)Application.Current.Resources[_window.IsActive ? "Music.Waveform.Fill" : "Music.Text.Secondary"];

        internal void Update(RunningWindow window)
        {
            if (_window == window) return;
            bool iconChanged = !ReferenceEquals(_window.Icon, window.Icon);
            _window = window;
            if (iconChanged) SetIcon(window.Icon);
            foreach (string property in new[] { nameof(Title), nameof(AccessibleName), nameof(Icon), nameof(IconVisibility),
                nameof(FallbackVisibility), nameof(IndicatorWidth), nameof(IndicatorBrush) })
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }

        private void SetIcon(WindowIcon? icon)
        {
            _icon = null;
            if (icon is null) return;
            var bitmap = new WriteableBitmap(icon.Width, icon.Height);
            using Stream pixels = bitmap.PixelBuffer.AsStream();
            pixels.Write(icon.Pixels);
            bitmap.Invalidate();
            _icon = bitmap;
        }
    }
}
