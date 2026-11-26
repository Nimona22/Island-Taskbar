using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using IslandTaskbar.Models;

namespace IslandTaskbar.ViewModels
{
    public sealed class PinnedAppItemViewModel : INotifyPropertyChanged
    {
        private PinnedApp _pin;
        private PinnedAppRuntimeState? _runtime;
        private ImageSource? _icon;

        internal PinnedAppItemViewModel(PinnedApp pin, PinnedAppRuntimeState? runtime) { _pin = pin; _runtime = runtime; SetIcon(runtime?.Resolution?.Application?.Icon); }
        internal PinId Id => _pin.Id;
        public event PropertyChangedEventHandler? PropertyChanged;
        public string Title => _pin.DisplayName;
        public string AccessibleName => $"{Title}, pinned application, {Availability}";
        public string Availability => _runtime?.Resolution?.Status switch
        {
            ApplicationResolutionStatus.Available => _runtime.Presence == ApplicationPresence.Running ? "running" : "available",
            ApplicationResolutionStatus.Missing => "unavailable, target missing",
            ApplicationResolutionStatus.Unsupported => "unavailable, unsupported target",
            ApplicationResolutionStatus.AccessDenied => "unavailable, access denied",
            ApplicationResolutionStatus.Failed => "availability unknown, retry available",
            _ => "availability unknown"
        };
        public string AutomationId => $"PinnedApp_{_pin.Id.Value:N}";
        public ImageSource? Icon => _icon;
        public Visibility IconVisibility => _icon is null ? Visibility.Collapsed : Visibility.Visible;
        public Visibility FallbackVisibility => _icon is null ? Visibility.Visible : Visibility.Collapsed;
        public string StateGlyph => _runtime?.Resolution?.Status != ApplicationResolutionStatus.Available ? "!" :
            _runtime?.Presence == ApplicationPresence.Running ? "●" : "";
        public bool CanRetry => _runtime?.Resolution?.Status is not null and not ApplicationResolutionStatus.Available;

        internal void Update(PinnedApp pin, PinnedAppRuntimeState? runtime)
        {
            _pin = pin; _runtime = runtime; SetIcon(runtime?.Resolution?.Application?.Icon);
            foreach (string name in new[] { nameof(Title), nameof(AccessibleName), nameof(Availability), nameof(Icon),
                nameof(IconVisibility), nameof(FallbackVisibility), nameof(StateGlyph), nameof(CanRetry) })
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private void SetIcon(ApplicationIcon? icon)
        {
            _icon = null;
            if (icon is null || icon.Width <= 0 || icon.Height <= 0) return;
            var bitmap = new WriteableBitmap(icon.Width, icon.Height);
            using Stream pixels = bitmap.PixelBuffer.AsStream(); pixels.Write(icon.Pixels.AsSpan()); bitmap.Invalidate(); _icon = bitmap;
        }
    }
}
