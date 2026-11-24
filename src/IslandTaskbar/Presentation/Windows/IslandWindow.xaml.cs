using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Composition;
using DispatcherQueuePriority = Microsoft.UI.Dispatching.DispatcherQueuePriority;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI.ViewManagement;
using Windows.System;
using IslandTaskbar.Models;
using IslandTaskbar.ViewModels;
using IslandTaskbar.Presentation.Backdrops;

namespace IslandTaskbar.Presentation.Windows
{
    using Windows = global::Windows;

    /// <summary>
    /// Phase 3.2 music UI prototype. Native window behavior remains behind the controller/adapters.
    /// </summary>
    public sealed partial class IslandWindow : Window
    {
        private readonly DispatcherTimer _transitionTimer;
        private readonly Stopwatch _transitionClock = new();
        private readonly List<UIElement> _waveBars = new();
        private readonly List<(Visual Visual, string Property)> _compositionLoops = new();
        private readonly List<ScalarKeyFrameAnimation> _waveAnimations = new();
        private readonly List<CompositionObject> _animationResources = new();
        private bool _motionPrepared;
        private bool? _musicMotionActive;
        private readonly UISettings _uiSettings = new();
        private readonly double _notchRadius;
        private readonly double _expandedRadius;
        private readonly double _durationMilliseconds;
        private bool _isExpanded;
        private bool _isClosed;
        private bool _isTransitioning;
        private double _progress;
        private double _transitionStart;
        private double _rotationSeconds;
        private bool _isWindowVisible;
        private bool _isInViewport = true;
        private bool _surfaceLayoutDirty = true;
        private XamlRoot? _observedXamlRoot;
        private IReadOnlyList<IslandSurfacePoint>? _surfaceOutline;
        private IslandSurfaceLayoutEventArgs? _lastLayout;
        private IslandMode _expandedMode = IslandMode.Music;
        private double _expandedWidth = IslandPrototypeGeometry.ExpandedWidth;
        private double _expandedHeight = IslandPrototypeGeometry.ExpandedHeight;
        private ScrollViewer? _taskbarScroll;
        private bool _taskbarLayoutDirty = true;

        internal event EventHandler<IslandSurfaceLayoutEventArgs>? SurfaceLayoutChanged;
        internal MusicPrototypeViewModel MusicState { get; }
        internal TaskbarViewModel TaskbarState { get; }
        internal IslandViewModel IslandState { get; }

        internal IslandWindow(MusicPrototypeViewModel musicState, TaskbarViewModel taskbarState, IslandViewModel islandState)
        {
            MusicState = musicState;
            TaskbarState = taskbarState;
            IslandState = islandState;
            InitializeComponent();
            // Window x:Bind normally waits for Activated; this overlay deliberately avoids activation.
            Bindings.Initialize();
            SystemBackdrop = new TransparentIslandBackdrop();
            ResourceDictionary resources = Application.Current.Resources;
            _notchRadius = ((CornerRadius)resources["Radius.Notch"]).BottomLeft;
            _expandedRadius = ((CornerRadius)resources["Radius.Island"]).BottomLeft;
            _durationMilliseconds = (double)resources["Motion.Standard"];
            ExpandedContent.Margin = new Thickness((double)resources["Space.Major"], 0,
                (double)resources["Space.Major"], 0);
            // A glass-inspired highlight using existing palette tokens, without implying desktop blur.
            var glass = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            glass.GradientStops.Add(new GradientStop { Offset = 0, Color = Windows.UI.Color.FromArgb(77, 32, 32, 40) });  // #4D202028
            glass.GradientStops.Add(new GradientStop { Offset = 1, Color = Windows.UI.Color.FromArgb(102, 20, 20, 26) }); // #6614141A
            IdleSurface.Background = glass;
            _rotationSeconds = (double)resources["Music.Artwork.RotationSeconds"];
            var artwork = new BitmapImage(new Uri("ms-appx:///Assets/PrototypeAlbum.png"))
            {
                DecodePixelWidth = (int)(double)resources["Music.Artwork.DecodeWidth"]
            };
            AlbumDisc.Fill = new ImageBrush { ImageSource = artwork, Stretch = Stretch.UniformToFill };
            double center = (double)resources["Music.Ring.Size"] / 2;
            for (int line = 0; line < 24; line++)
            {
                double angle = line * Math.PI * 2 / 24;
                double length = 2 + MusicState.GetIllustrativeScale(line % MusicState.BarCount, 0) * 2;
                IllustrativeRing.Children.Add(new Line
                {
                    X1 = center + Math.Cos(angle) * (center - 4),
                    Y1 = center + Math.Sin(angle) * (center - 4),
                    X2 = center + Math.Cos(angle) * (center - 4 + length),
                    Y2 = center + Math.Sin(angle) * (center - 4 + length),
                    Stroke = (SolidColorBrush)resources["Music.Ring.Fill"],
                    StrokeThickness = (double)resources["Music.Ring.StrokeThickness"]
                });
            }
            for (int bar = 0; bar < MusicState.BarCount; bar++)
            {
                var waveBar = new Border
                {
                    Width = (double)resources["Music.Waveform.BarWidth"],
                    Height = (double)resources["Music.Waveform.Height"],
                    CornerRadius = new CornerRadius((double)resources["Music.Waveform.BarRadius"]),
                    Background = (SolidColorBrush)resources["Music.Waveform.Fill"],
                    RenderTransformOrigin = new Point(.5, .5)
                };
                _waveBars.Add(waveBar);
                IllustrativeWaveform.Children.Add(waveBar);
            }
            _transitionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.0 / 60) };
            _transitionTimer.Tick += OnTransitionTick;
            IslandSurface.EffectiveViewportChanged += OnEffectiveViewportChanged;
            WindowRoot.SizeChanged += OnRootSizeChanged;
            _uiSettings.AnimationsEnabledChanged += OnAnimationsEnabledChanged;
            VisibilityChanged += OnPrototypeVisibilityChanged;
            Closed += OnPrototypeClosed;
            MusicState.MediaStateChanged += OnMediaStateChanged;
            IslandState.PropertyChanged += OnModeChanged;
            TaskbarState.Windows.CollectionChanged += OnRunningWindowsChanged;
            ApplyProgress(0);
        }

        internal Task InitializeMediaAsync(CancellationToken cancellationToken) => MusicState.InitializeAsync(cancellationToken);
        internal Task InitializeTaskbarAsync(CancellationToken cancellationToken) => TaskbarState.InitializeAsync(cancellationToken);

        private void OnMediaStateChanged(object? sender, EventArgs e)
        {
            if (_isClosed) return;
            IslandState.UpdateMedia(MusicState.SessionId, MusicState.IsPlaying, MusicState.HasSession, MusicState.IsSessionTransitioning);
            UpdateVisualTimer();
        }

        private void OnIslandToggleClick(object sender, RoutedEventArgs e)
        {
            IslandState.OpenTaskbar();
        }

        private void OnCollapseClick(object sender, RoutedEventArgs e) => IslandState.Collapse();
        private void OnOpenTaskbarClick(object sender, RoutedEventArgs e) => IslandState.OpenTaskbar();
        private void OnOpenMusicClick(object sender, RoutedEventArgs e) => IslandState.OpenMusic(MusicState.HasSession);

        private void OnModeChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_isClosed || e.PropertyName != nameof(IslandViewModel.Mode)) return;
            if (IslandState.Mode != IslandMode.Idle)
            {
                _expandedMode = IslandState.Mode;
                _expandedWidth = _expandedMode == IslandMode.Taskbar ? IslandPrototypeGeometry.TaskbarWidth : IslandPrototypeGeometry.ExpandedWidth;
                _expandedHeight = _expandedMode == IslandMode.Taskbar ? IslandPrototypeGeometry.TaskbarHeight : IslandPrototypeGeometry.ExpandedHeight;
            }
            SetExpanded(IslandState.Mode != IslandMode.Idle);
            ApplyProgress(_progress);
            if (IslandState.Mode != IslandMode.Idle && !_isTransitioning) FocusExpandedContent();
            UpdateVisualTimer();
        }

        private void OnPrototypeKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (_isExpanded && e.Key == VirtualKey.Escape)
            {
                IslandState.Collapse();
                e.Handled = true;
            }
        }

        private void SetExpanded(bool expanded)
        {
            IslandToggle.IsHitTestVisible = !expanded;
            IslandToggle.IsTabStop = !expanded;
            ExpandedContent.IsHitTestVisible = expanded && _expandedMode == IslandMode.Music;
            TaskbarContent.IsHitTestVisible = expanded && _expandedMode == IslandMode.Taskbar;
            // A Music/Taskbar change keeps the envelope expanded, but still changes its input owner.
            if (_isExpanded == expanded) return;
            _isExpanded = expanded;
            if (!expanded)
            {
                IslandToggle.Focus(FocusState.Programmatic);
            }

            if (!_uiSettings.AnimationsEnabled)
            {
                _isTransitioning = false;
                ApplyProgress(_isExpanded ? 1 : 0);
                if (_isExpanded && IslandToggle.FocusState != FocusState.Unfocused)
                {
                    FocusExpandedContent();
                }
                UpdateVisualTimer();
                return;
            }

            // Reverse from the current geometry rather than jumping to a previous endpoint.
            _transitionStart = _progress;
            _isTransitioning = true;
            _transitionClock.Restart();
            UpdateVisualTimer();
        }

        private void OnTransitionTick(object? sender, object e)
        {
            if (_isTransitioning)
            {
                double target = _isExpanded ? 1 : 0;
                double distance = Math.Abs(target - _transitionStart);
                double time = distance == 0 ? 1 : Math.Min(1,
                    _transitionClock.Elapsed.TotalMilliseconds / (_durationMilliseconds * distance));
                double eased = _isExpanded ? 1 - Math.Pow(1 - time, 3) : Math.Pow(time, 3);
                ApplyProgress(_transitionStart + (target - _transitionStart) * eased);
                if (time >= 1)
                {
                    _isTransitioning = false;
                    _transitionClock.Stop();
                    if (_isExpanded && IslandToggle.FocusState != FocusState.Unfocused)
                    {
                        FocusExpandedContent();
                    }
                    else if (_isExpanded && _expandedMode == IslandMode.Taskbar) FocusExpandedContent();
                }
            }

            UpdateVisualTimer();
        }

        private async void OnPlayPauseClick(object sender, RoutedEventArgs e)
        {
            await MusicState.ExecuteAsync(MediaCommand.TogglePlayback, CancellationToken.None);
        }

        private async void OnPreviousClick(object sender, RoutedEventArgs e)
        {
            await MusicState.ExecuteAsync(MediaCommand.Previous, CancellationToken.None);
        }

        private async void OnNextClick(object sender, RoutedEventArgs e)
        {
            await MusicState.ExecuteAsync(MediaCommand.Next, CancellationToken.None);
        }

        private void PrepareMusicMotion()
        {
            if (_motionPrepared) return;
            var album = ElementCompositionPreview.GetElementVisual(AlbumDisc);
            album.CenterPoint = new Vector3((float)AlbumDisc.Width / 2, (float)AlbumDisc.Height / 2, 0);
            StartLoop(album, "RotationAngleInDegrees", 0, 360, _rotationSeconds, false);
            var ring = ElementCompositionPreview.GetElementVisual(IllustrativeRing);
            ring.CenterPoint = new Vector3((float)IllustrativeRing.Width / 2, (float)IllustrativeRing.Height / 2, 0);
            StartLoop(ring, "Scale.X", .96f, 1, 1.2, true);
            StartLoop(ring, "Scale.Y", .96f, 1, 1.2, true);
            StartLoop(ring, "Opacity", .48f, 1, 1.2, true);
            for (int index = 0; index < _waveBars.Count; index++)
            {
                var bar = (FrameworkElement)_waveBars[index];
                var visual = ElementCompositionPreview.GetElementVisual(bar);
                visual.CenterPoint = new Vector3((float)bar.Width / 2, (float)bar.Height / 2, 0);
                float scale = (float)MusicState.GetIllustrativeScale(index, 0);
                _waveAnimations.Add(StartLoop(visual, "Scale.Y", scale * .55f, scale, 1.2, true));
            }
            _motionPrepared = true;
            _musicMotionActive = null;
        }

        private ScalarKeyFrameAnimation StartLoop(Visual visual, string property, float from, float to,
            double seconds, bool alternate)
        {
            var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
            var easing = visual.Compositor.CreateLinearEasingFunction();
            animation.InsertKeyFrame(0, from);
            animation.InsertKeyFrame(1, to, easing);
            animation.Duration = TimeSpan.FromSeconds(seconds);
            animation.IterationBehavior = AnimationIterationBehavior.Forever;
            animation.Direction = alternate ? AnimationDirection.Alternate : AnimationDirection.Normal;
            visual.StartAnimation(property, animation);
            _compositionLoops.Add((visual, property));
            _animationResources.Add(animation);
            _animationResources.Add(easing);
            return animation;
        }

        private bool ShouldAnimateMusic => _isExpanded && IslandState.Mode == IslandMode.Music && MusicState.IsPlaying
            && _isWindowVisible && _isInViewport && _uiSettings.AnimationsEnabled;

        private void OnPrototypeVisibilityChanged(object sender, WindowVisibilityChangedEventArgs e)
        {
            _isWindowVisible = e.Visible;
            UpdateVisualTimer();
        }

        private void OnEffectiveViewportChanged(FrameworkElement sender, EffectiveViewportChangedEventArgs e)
        {
            bool inViewport = e.EffectiveViewport.Width > 0 && e.EffectiveViewport.Height > 0;
            if (_isInViewport == inViewport) return;
            _isInViewport = inViewport;
            UpdateVisualTimer();
        }

        private void UpdateVisualTimer()
        {
            bool animateMusic = ShouldAnimateMusic;
            if (_motionPrepared && _musicMotionActive != animateMusic)
            {
                foreach (var loop in _compositionLoops)
                {
                    var controller = loop.Visual.TryGetAnimationController(loop.Property);
                    if (animateMusic) controller?.Resume(); else controller?.Pause();
                }
                _musicMotionActive = animateMusic;
            }

            // Continuous music motion runs on composition; the UI timer serves geometry transitions only.
            if (!_isClosed && _isWindowVisible && _isInViewport && _isTransitioning)
            {
                if (_isTransitioning && !_transitionClock.IsRunning)
                {
                    _transitionClock.Start();
                }
                _transitionTimer.Start();
            }
            else
            {
                _transitionTimer.Stop();
                _transitionClock.Stop();
            }
        }

        private void ApplyProgress(double progress)
        {
            _surfaceLayoutDirty = true;
            _taskbarLayoutDirty = true;
            _progress = progress;
            IslandSurface.Width = Lerp(IslandPrototypeGeometry.NotchWidth, _expandedWidth, progress);
            IslandSurface.Height = Lerp(IslandPrototypeGeometry.NotchHeight, _expandedHeight, progress);
            IslandSurface.Margin = new Thickness(0, 0, 0, 0);
            IdleSurface.Opacity = 1 - progress;
            MusicSurface.Opacity = progress;
            MusicSurface.Visibility = progress > 0 ? Visibility.Visible : Visibility.Collapsed;
            IslandToggle.CornerRadius = (CornerRadius)Application.Current.Resources["Radius.Notch"];
            _surfaceOutline = progress > 0 ? IslandSurfaceOutline.Create(IslandSurface.Width, IslandSurface.Height,
                IslandPrototypeGeometry.ShoulderInset * progress, Lerp(_notchRadius, _expandedRadius, progress)) : null;
            if (_surfaceOutline is not null)
            {
                var figure = new PathFigure { StartPoint = new Point(_surfaceOutline[0].X, _surfaceOutline[0].Y), IsClosed = true };
                for (int index = 1; index < _surfaceOutline.Count; index++)
                {
                    figure.Segments.Add(new LineSegment { Point = new Point(_surfaceOutline[index].X, _surfaceOutline[index].Y) });
                }
                var geometry = new PathGeometry { FillRule = FillRule.Nonzero };
                geometry.Figures.Add(figure);
                MusicSurface.Data = geometry;
            }
            // Fade the whole idle button, including any pointer-over wash retained after the click.
            IslandToggle.Opacity = 1 - Math.Min(1, progress * 3);
            NotchIcon.Visibility = progress >= 1 ? Visibility.Collapsed : Visibility.Visible;
            ExpandedContent.Opacity = Math.Max(0, (progress - 0.4) / 0.6);
            ExpandedContent.Visibility = progress > 0.4 && _expandedMode == IslandMode.Music ? Visibility.Visible : Visibility.Collapsed;
            TaskbarContent.Opacity = ExpandedContent.Opacity;
            TaskbarContent.Visibility = progress > 0.4 && _expandedMode == IslandMode.Taskbar ? Visibility.Visible : Visibility.Collapsed;
            // Layout animation is deliberate: layout and native hit testing must use the same geometry.
            // LayoutUpdated reports after width, height, position and radii have all been applied.
        }

        private static double Lerp(double from, double to, double progress) => from + (to - from) * progress;

        private void OnSurfaceLoaded(object sender, RoutedEventArgs e)
        {
            _isWindowVisible = Visible;
            if (_observedXamlRoot is null && IslandSurface.XamlRoot is XamlRoot root)
            {
                _observedXamlRoot = root;
                root.Changed += OnXamlRootChanged;
            }
            _surfaceLayoutDirty = true;
            PrepareMusicMotion();
            ReportSurfaceLayout();
            UpdateVisualTimer();
        }

        private void OnSurfaceLayoutUpdated(object? sender, object e)
        {
            ReportSurfaceLayout();
            if (_taskbarLayoutDirty && IslandState.Mode == IslandMode.Taskbar && TaskbarContent.Visibility == Visibility.Visible)
            {
                _taskbarLayoutDirty = false;
                OnTaskbarLoaded(this, new RoutedEventArgs());
            }
        }
        private void OnRootSizeChanged(object sender, SizeChangedEventArgs e) { _surfaceLayoutDirty = true; _taskbarLayoutDirty = true; }
        private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs e) { _surfaceLayoutDirty = true; _taskbarLayoutDirty = true; }

        private void ReportSurfaceLayout()
        {
            if (!_surfaceLayoutDirty || _isClosed || IslandSurface is null || IslandSurface.XamlRoot is null
                || IslandSurface.ActualWidth <= 0 || IslandSurface.ActualHeight <= 0)
            {
                return;
            }
            _surfaceLayoutDirty = false;

            Point position = IslandSurface.TransformToVisual(WindowRoot).TransformPoint(new Point(0, 0));
            double currentRadius = Lerp(_notchRadius, _expandedRadius, _progress);
            var layout = new IslandSurfaceLayoutEventArgs(position.X, position.Y,
                IslandSurface.ActualWidth, IslandSurface.ActualHeight, currentRadius,
                IslandSurface.XamlRoot.RasterizationScale, 0, _surfaceOutline);
            if (_lastLayout is not null && layout.Left == _lastLayout.Left && layout.Top == _lastLayout.Top
                && layout.Width == _lastLayout.Width && layout.Height == _lastLayout.Height
                && layout.UpperCornerRadius == _lastLayout.UpperCornerRadius
                && layout.LowerCornerRadius == _lastLayout.LowerCornerRadius
                && layout.RasterizationScale == _lastLayout.RasterizationScale
                && ReferenceEquals(layout.Outline, _lastLayout.Outline))
            {
                return;
            }

            _lastLayout = layout;
            SurfaceLayoutChanged?.Invoke(this, layout);
        }

        private void OnAnimationsEnabledChanged(UISettings sender, object args)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!_isClosed && !_uiSettings.AnimationsEnabled)
                {
                    _isTransitioning = false;
                    _transitionClock.Stop();
                    ApplyProgress(_isExpanded ? 1 : 0);
                    if (_isExpanded && IslandToggle.FocusState != FocusState.Unfocused)
                    {
                        FocusExpandedContent();
                    }
                }
                if (!_isClosed)
                {
                    UpdateVisualTimer();
                }
            });
        }

        private void OnPrototypeClosed(object sender, WindowEventArgs args)
        {
            _isClosed = true;
            Bindings.StopTracking();
            MusicState.MediaStateChanged -= OnMediaStateChanged;
            MusicState.Dispose();
            IslandState.PropertyChanged -= OnModeChanged;
            TaskbarState.Windows.CollectionChanged -= OnRunningWindowsChanged;
            TaskbarState.Dispose();
            if (_taskbarScroll is not null) _taskbarScroll.ViewChanged -= OnTaskbarViewChanged;
            _transitionTimer.Stop();
            _transitionTimer.Tick -= OnTransitionTick;
            _transitionClock.Stop();
            foreach (var loop in _compositionLoops) loop.Visual.StopAnimation(loop.Property);
            foreach (var resource in _animationResources) resource.Dispose();
            _compositionLoops.Clear();
            _animationResources.Clear();
            _uiSettings.AnimationsEnabledChanged -= OnAnimationsEnabledChanged;
            VisibilityChanged -= OnPrototypeVisibilityChanged;
            IslandSurface.EffectiveViewportChanged -= OnEffectiveViewportChanged;
            WindowRoot.SizeChanged -= OnRootSizeChanged;
            if (_observedXamlRoot is not null) _observedXamlRoot.Changed -= OnXamlRootChanged;
            Closed -= OnPrototypeClosed;
        }

        private async void OnRunningWindowClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: RunningWindowItemViewModel entry })
                await TaskbarState.ActivateAsync(entry, CancellationToken.None);
        }

        private void OnTaskbarLoaded(object sender, RoutedEventArgs e)
        {
            if (_taskbarScroll is null)
            {
                RunningWindowsListView.ApplyTemplate();
                _taskbarScroll = FindChild<ScrollViewer>(RunningWindowsListView);
                if (_taskbarScroll is not null) _taskbarScroll.ViewChanged += OnTaskbarViewChanged;
            }
            UpdateScrollButtons();
        }

        private void OnRunningWindowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            _taskbarLayoutDirty = true;
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                if (_isClosed) return;
                UpdateScrollButtons();
            });
        }

        private void OnTaskbarViewChanged(object? sender, ScrollViewerViewChangedEventArgs e) => UpdateScrollButtons();
        private void UpdateScrollButtons()
        {
            ScrollApplicationsLeft.IsEnabled = _taskbarScroll?.HorizontalOffset > 0.5;
            ScrollApplicationsRight.IsEnabled = _taskbarScroll is not null
                && _taskbarScroll.HorizontalOffset + 0.5 < _taskbarScroll.ScrollableWidth;
        }

        private void OnScrollApplicationsLeft(object sender, RoutedEventArgs e) => ScrollApplications(-1);
        private void OnScrollApplicationsRight(object sender, RoutedEventArgs e) => ScrollApplications(1);
        private void ScrollApplications(int direction)
        {
            if (_taskbarScroll is null) OnTaskbarLoaded(this, new RoutedEventArgs());
            if (_taskbarScroll is null) return;
            double step = (double)Application.Current.Resources["Taskbar.Item.Extent"];
            _taskbarScroll.ChangeView(Math.Clamp(_taskbarScroll.HorizontalOffset + direction * step, 0, _taskbarScroll.ScrollableWidth), null, null, true);
        }

        private void OnTaskbarKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key is not (VirtualKey.Left or VirtualKey.Right or VirtualKey.Home or VirtualKey.End)) return;
            if (e.OriginalSource is not FrameworkElement { DataContext: RunningWindowItemViewModel entry }) return;
            int index = TaskbarState.Windows.IndexOf(entry);
            index = e.Key switch
            {
                VirtualKey.Left => index - 1, VirtualKey.Right => index + 1,
                VirtualKey.Home => 0, _ => TaskbarState.Windows.Count - 1
            };
            FocusRunningWindow(index);
            e.Handled = true;
        }

        private void FocusExpandedContent()
        {
            if (_expandedMode == IslandMode.Taskbar) FocusRunningWindow(0);
            else PlayPauseButton.Focus(FocusState.Programmatic);
        }

        private void FocusRunningWindow(int index)
        {
            if (TaskbarState.Windows.Count == 0) { TaskbarCollapseButton.Focus(FocusState.Programmatic); return; }
            var entry = TaskbarState.Windows[Math.Clamp(index, 0, TaskbarState.Windows.Count - 1)];
            RunningWindowsListView.ScrollIntoView(entry, ScrollIntoViewAlignment.Leading);
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                if (_isClosed || IslandState.Mode != IslandMode.Taskbar) return;
                if (RunningWindowsListView.ContainerFromItem(entry) is DependencyObject container)
                    FindChild<Button>(container)?.Focus(FocusState.Programmatic);
            });
        }

        private static T? FindChild<T>(DependencyObject root) where T : DependencyObject
        {
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            {
                var child = VisualTreeHelper.GetChild(root, index);
                if (child is T match) return match;
                if (FindChild<T>(child) is T descendant) return descendant;
            }
            return null;
        }
    }
}
