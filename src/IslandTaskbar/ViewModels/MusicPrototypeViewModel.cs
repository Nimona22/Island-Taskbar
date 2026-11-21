using System;
using System.ComponentModel;

namespace IslandTaskbar.ViewModels
{
    /// <summary>
    /// Phase 3.2 local UI demonstration only; no audio or media-session data.
    /// </summary>
    internal sealed class MusicPrototypeViewModel : INotifyPropertyChanged
    {
        private static readonly double[][] IllustrativePatterns =
        {
            new[] { .28, .46, .73, .95, .56, .38, .67, 1.0, .75, .43, .25 },
            new[] { .64, .91, .48, .29, .58, .84, 1.0, .63, .41, .27, .52 },
            new[] { .31, .53, .79, .98, .76, .46, .28, .54, .86, .66, .39 }
        };

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool IsPlaying { get; private set; }
        public int PatternIndex { get; private set; }
        public string StatusText => $"Illustrative · {(IsPlaying ? "Playing" : "Paused")} · {PatternIndex + 1}";
        public string PlayPauseName => IsPlaying ? "Pause music prototype" : "Play music prototype";
        public string PlayPauseGlyph => IsPlaying ? "\uE769" : "\uE768";
        internal int BarCount => IllustrativePatterns[0].Length;

        public void TogglePlayback()
        {
            IsPlaying = !IsPlaying;
            Notify(nameof(IsPlaying));
            Notify(nameof(StatusText));
            Notify(nameof(PlayPauseName));
            Notify(nameof(PlayPauseGlyph));
        }

        public void Previous() => SelectPattern(-1);
        public void Next() => SelectPattern(1);

        private void SelectPattern(int direction)
        {
            PatternIndex = (PatternIndex + direction + IllustrativePatterns.Length) % IllustrativePatterns.Length;
            Notify(nameof(PatternIndex));
            Notify(nameof(StatusText));
        }

        internal double GetIllustrativeScale(int bar, double phaseSeconds)
        {
            // These are visual proportions, never sampled/estimated audio levels. No randomness.
            double shape = IllustrativePatterns[PatternIndex][bar];
            double modulation = .76 + .24 * Math.Sin(phaseSeconds * 4 + bar * .63 + PatternIndex);
            return Math.Max(.16, shape * modulation);
        }

        private void Notify(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}
