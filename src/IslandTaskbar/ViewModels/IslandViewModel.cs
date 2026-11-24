using System.ComponentModel;
using IslandTaskbar.Models;

namespace IslandTaskbar.ViewModels
{
    internal sealed class IslandViewModel : INotifyPropertyChanged
    {
        private IslandMode _mode;
        private string? _lastSessionId;
        private bool _lastPlaying;
        public event PropertyChangedEventHandler? PropertyChanged;
        public IslandMode Mode => _mode;
        public void OpenTaskbar() => SetMode(IslandMode.Taskbar);
        public void OpenMusic(bool hasSession) { if (hasSession) SetMode(IslandMode.Music); }
        public void Collapse() => SetMode(IslandMode.Idle);

        public void UpdateMedia(string? id, bool playing, bool hasSession, bool dismissalPending)
        {
            // Explicit Taskbar Mode wins over automatic playback/dismissal, while tracking
            // the latest session so closing it does not immediately reopen Music Mode.
            if (_mode != IslandMode.Taskbar)
            {
                if (!hasSession && !dismissalPending) SetMode(IslandMode.Idle);
                else if (hasSession && (_lastSessionId != id || (!_lastPlaying && playing)))
                    SetMode(IslandMode.Music);
            }
            _lastSessionId = id;
            _lastPlaying = playing;
        }

        private void SetMode(IslandMode mode)
        {
            if (_mode == mode) return;
            _mode = mode;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Mode)));
        }
    }
}
