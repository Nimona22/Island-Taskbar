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
using Windows.Win32;
using Windows.Win32.Foundation;
using WinRT.Interop;

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
        private readonly DispatcherTimer _autoDismissTimer;
        private readonly DispatcherTimer _outsideClickTimer;
        private readonly PointerEventHandler _surfacePressedHandler;
        private readonly PointerEventHandler _surfaceReleasedHandler;
        private readonly PointerEventHandler _surfaceCaptureLostHandler;
        private readonly PointerEventHandler _surfaceWheelHandler;
        private nint _windowHandle;
        private bool _isPointerInside;
        private bool _isPointerPressedInside;
        private bool _hasKeyboardFocusInside;
        private bool _isContextMenuOpen;
        private bool _wasMouseDown;
        private bool _openedByKeyboard;
        private bool _allowClose;
        private bool _closing;
        private bool _measuringTaskbar;
        private bool _taskbarMeasureDirty = true;
        private double _availableDisplayPixels = double.PositiveInfinity;

        internal event EventHandler<IslandSurfaceLayoutEventArgs>? SurfaceLayoutChanged;
        internal MusicPrototypeViewModel MusicState { get; }
        internal TaskbarViewModel TaskbarState { get; }
        internal IslandViewModel IslandState { get; }
        internal nint NativeWindowHandle => _windowHandle;
        internal bool IsClosing => _closing;

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
            glass.GradientStops.Add(new GradientStop { Offset = 0, Color = Windows.UI.Color.FromArgb(20, 255, 255, 255) });
            glass.GradientStops.Add(new GradientStop { Offset = 1, Color = Windows.UI.Color.FromArgb(5, 0, 0, 0) });
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
            _windowHandle = WindowNative.GetWindowHandle(this);
            AppWindow.Closing += OnAppWindowClosing;
            _transitionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.0 / 60) };
            _transitionTimer.Tick += OnTransitionTick;
            _autoDismissTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            _autoDismissTimer.Tick += OnAutoDismissTick;
            _outsideClickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(45) };
            _outsideClickTimer.Tick += OnOutsideClickTick;
            _surfacePressedHandler = new PointerEventHandler(OnSurfacePointerPressed);
            _surfaceReleasedHandler = new PointerEventHandler(OnSurfacePointerReleased);
            _surfaceCaptureLostHandler = new PointerEventHandler(OnSurfacePointerCaptureLost);
            _surfaceWheelHandler = new PointerEventHandler(OnSurfacePointerWheelChanged);
            IslandSurface.PointerEntered += OnSurfacePointerEntered;
            IslandSurface.PointerExited += OnSurfacePointerExited;
            IslandSurface.AddHandler(UIElement.PointerPressedEvent, _surfacePressedHandler, true);
            IslandSurface.AddHandler(UIElement.PointerReleasedEvent, _surfaceReleasedHandler, true);
            IslandSurface.AddHandler(UIElement.PointerCaptureLostEvent, _surfaceCaptureLostHandler, true);
            IslandSurface.AddHandler(UIElement.PointerWheelChangedEvent, _surfaceWheelHandler, true);
            IslandSurface.GotFocus += OnSurfaceGotFocus;
            IslandSurface.LostFocus += OnSurfaceLostFocus;
            Activated += OnWindowActivated;
            IslandSurface.EffectiveViewportChanged += OnEffectiveViewportChanged;
            WindowRoot.SizeChanged += OnRootSizeChanged;
            _uiSettings.AnimationsEnabledChanged += OnAnimationsEnabledChanged;
            VisibilityChanged += OnPrototypeVisibilityChanged;
            Closed += OnPrototypeClosed;
            MusicState.MediaStateChanged += OnMediaStateChanged;
            IslandState.PropertyChanged += OnModeChanged;
            TaskbarState.Windows.CollectionChanged += OnRunningWindowsChanged;
            TaskbarState.Pins.CollectionChanged += OnPinsChanged;
            TaskbarState.PropertyChanged += OnTaskbarStateChanged;
            RunningWindowsListView.ContainerContentChanging += OnContainerContentChanging;
            ApplyProgress(0);
        }

        internal Task InitializeMediaAsync(CancellationToken cancellationToken) => MusicState.InitializeAsync(cancellationToken);
        internal Task InitializeTaskbarAsync(CancellationToken cancellationToken) => TaskbarState.InitializeAsync(cancellationToken);

        internal void SetAvailableDisplayHeight(double pixels) => _availableDisplayPixels = pixels;

        private async void OnAppWindowClosing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
        {
            if (_allowClose) return;
            args.Cancel = true;
            if (_closing) return;
            _closing = true;
            try { await TaskbarState.DisposeAsync(); }
            finally { _allowClose = true; Close(); }
        }

        private void OnMediaStateChanged(object? sender, EventArgs e)
        {
            if (_isClosed) return;
            IslandState.UpdateMedia(MusicState.SessionId, MusicState.IsPlaying, MusicState.HasSession, MusicState.IsSessionTransitioning);
            UpdateVisualTimer();
        }

        private void OnIslandToggleClick(object sender, RoutedEventArgs e)
        {
            _openedByKeyboard = IslandToggle.FocusState == FocusState.Keyboard;
            IslandState.OpenTaskbar();
        }

        private void OnCollapseClick(object sender, RoutedEventArgs e) => IslandState.Collapse();
        private void OnOpenTaskbarClick(object sender, RoutedEventArgs e)
        {
            _openedByKeyboard = false;
            IslandState.OpenTaskbar();
        }
        private void OnOpenMusicClick(object sender, RoutedEventArgs e) => IslandState.OpenMusic(MusicState.HasSession);
        private void OnContextFlyoutOpening(object? sender, object e)
        {
            _isContextMenuOpen = true;
            _autoDismissTimer.Stop();
        }
        private void OnContextFlyoutClosed(object? sender, object e)
        {
            _isContextMenuOpen = false;
            EvaluateAutoDismiss();
        }

        private async void OnWindowFlyoutOpening(object? sender, object e)
        {
            OnContextFlyoutOpening(sender, e);
            if (sender is MenuFlyout flyout && flyout.Items[0] is MenuFlyoutItem { Tag: RunningWindowItemViewModel window } action)
            {
                action.IsEnabled = false;
                action.Text = "Checking application…";
                action.Visibility = Visibility.Visible;
                bool canPin = await TaskbarState.CanPinWindowAsync(window);
                if (_isClosed || _closing) return;
                action.IsEnabled = canPin;
                action.Text = canPin ? "Pin application" : "Pinning unavailable for this window";
            }
        }

        private void OnModeChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_isClosed || e.PropertyName != nameof(IslandViewModel.Mode)) return;
            _taskbarMeasureDirty = true;
            if (IslandState.Mode != IslandMode.Idle)
            {
                _expandedMode = IslandState.Mode;
                _expandedWidth = _expandedMode == IslandMode.Taskbar ? IslandPrototypeGeometry.TaskbarWidth : IslandPrototypeGeometry.ExpandedWidth;
                _expandedHeight = _expandedMode == IslandMode.Taskbar ? CalculateTaskbarHeight() : IslandPrototypeGeometry.ExpandedHeight;
            }
            if (IslandState.Mode == IslandMode.Taskbar)
            {
                _wasMouseDown = false;
                _outsideClickTimer.Start();
            }
            else
            {
                _autoDismissTimer.Stop();
                _outsideClickTimer.Stop();
                _isPointerPressedInside = false;
                _hasKeyboardFocusInside = false;
                _openedByKeyboard = false;
                _wasMouseDown = false;
            }
            SetExpanded(IslandState.Mode != IslandMode.Idle);
            ApplyProgress(_progress);
            if (IslandState.Mode != IslandMode.Idle && !_isTransitioning)
            {
                if (_expandedMode != IslandMode.Taskbar || _openedByKeyboard) FocusExpandedContent();
            }
            UpdateVisualTimer();
            EvaluateAutoDismiss();
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
                if (_isExpanded && (_expandedMode != IslandMode.Taskbar || _openedByKeyboard) && IslandToggle.FocusState != FocusState.Unfocused)
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
                    if (_isExpanded && (_expandedMode != IslandMode.Taskbar || _openedByKeyboard))
                    {
                        if (IslandToggle.FocusState != FocusState.Unfocused || _expandedMode == IslandMode.Taskbar)
                        {
                            FocusExpandedContent();
                        }
                    }
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
            _progress = progress;
            IslandSurface.Width = Lerp(IslandPrototypeGeometry.NotchWidth, _expandedWidth, progress);
            IslandSurface.Height = Lerp(IslandPrototypeGeometry.NotchHeight, _expandedHeight, progress);
            IslandSurface.Margin = new Thickness(0, 0, 0, 0);
            IdleSurface.Opacity = 1 - progress;
            MusicSurface.Opacity = progress;
            MusicSurface.Visibility = progress > 0 ? Visibility.Visible : Visibility.Collapsed;
            MusicSurfaceRim.Opacity = progress;
            MusicSurfaceRim.Visibility = MusicSurface.Visibility;
            IslandToggle.CornerRadius = (CornerRadius)Application.Current.Resources["Radius.Notch"];
            _surfaceOutline = progress > 0 ? IslandSurfaceOutline.Create(IslandSurface.Width, IslandSurface.Height,
                IslandPrototypeGeometry.ShoulderInset * progress, Lerp(_notchRadius, _expandedRadius, progress)) : null;
            if (_surfaceOutline is not null)
            {
                var fillFigure = new PathFigure { StartPoint = new Point(_surfaceOutline[0].X, _surfaceOutline[0].Y), IsClosed = true };
                for (int index = 1; index < _surfaceOutline.Count; index++)
                {
                    fillFigure.Segments.Add(new LineSegment { Point = new Point(_surfaceOutline[index].X, _surfaceOutline[index].Y) });
                }
                var fillGeometry = new PathGeometry { FillRule = FillRule.Nonzero };
                fillGeometry.Figures.Add(fillFigure);
                MusicSurface.Data = fillGeometry;

                // Trace shoulders, sides, and bottom while omitting the top y=0 edge so the notch stays flush.
                var rimFigure = new PathFigure { StartPoint = new Point(_surfaceOutline[1].X, _surfaceOutline[1].Y), IsClosed = false };
                for (int index = 2; index < _surfaceOutline.Count; index++)
                {
                    rimFigure.Segments.Add(new LineSegment { Point = new Point(_surfaceOutline[index].X, _surfaceOutline[index].Y) });
                }
                rimFigure.Segments.Add(new LineSegment { Point = new Point(_surfaceOutline[0].X, _surfaceOutline[0].Y) });
                var rimGeometry = new PathGeometry();
                rimGeometry.Figures.Add(rimFigure);
                MusicSurfaceRim.Data = rimGeometry;
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
            if (_taskbarMeasureDirty && TaskbarContent.Visibility == Visibility.Visible) UpdateDynamicTaskbarHeight();
            ReportSurfaceLayout();
        }
        private void OnRootSizeChanged(object sender, SizeChangedEventArgs e) { _surfaceLayoutDirty = true; }
        private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs e) { _surfaceLayoutDirty = true; _taskbarMeasureDirty = true; }

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
                IslandSurface.XamlRoot.RasterizationScale, 0, _surfaceOutline,
                _expandedMode == IslandMode.Taskbar ? Math.Max(IslandSurface.ActualHeight, _expandedHeight) : null);
            if (_lastLayout is not null && layout.Left == _lastLayout.Left && layout.Top == _lastLayout.Top
                && layout.Width == _lastLayout.Width && layout.Height == _lastLayout.Height
                && layout.UpperCornerRadius == _lastLayout.UpperCornerRadius
                && layout.LowerCornerRadius == _lastLayout.LowerCornerRadius
                && layout.RasterizationScale == _lastLayout.RasterizationScale
                && layout.HostHeight == _lastLayout.HostHeight
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
            AppWindow.Closing -= OnAppWindowClosing;
            Bindings.StopTracking();
            MusicState.MediaStateChanged -= OnMediaStateChanged;
            MusicState.Dispose();
            IslandState.PropertyChanged -= OnModeChanged;
            TaskbarState.Windows.CollectionChanged -= OnRunningWindowsChanged;
            TaskbarState.Pins.CollectionChanged -= OnPinsChanged;
            TaskbarState.PropertyChanged -= OnTaskbarStateChanged;
            RunningWindowsListView.ContainerContentChanging -= OnContainerContentChanging;
            TaskbarState.Dispose();
            _autoDismissTimer.Stop();
            _autoDismissTimer.Tick -= OnAutoDismissTick;
            _outsideClickTimer.Stop();
            _outsideClickTimer.Tick -= OnOutsideClickTick;
            _transitionTimer.Stop();
            _transitionTimer.Tick -= OnTransitionTick;
            _transitionClock.Stop();
            foreach (var loop in _compositionLoops) loop.Visual.StopAnimation(loop.Property);
            foreach (var resource in _animationResources) resource.Dispose();
            _compositionLoops.Clear();
            _animationResources.Clear();
            _uiSettings.AnimationsEnabledChanged -= OnAnimationsEnabledChanged;
            VisibilityChanged -= OnPrototypeVisibilityChanged;
            Activated -= OnWindowActivated;
            IslandSurface.PointerEntered -= OnSurfacePointerEntered;
            IslandSurface.PointerExited -= OnSurfacePointerExited;
            IslandSurface.RemoveHandler(UIElement.PointerPressedEvent, _surfacePressedHandler);
            IslandSurface.RemoveHandler(UIElement.PointerReleasedEvent, _surfaceReleasedHandler);
            IslandSurface.RemoveHandler(UIElement.PointerCaptureLostEvent, _surfaceCaptureLostHandler);
            IslandSurface.RemoveHandler(UIElement.PointerWheelChangedEvent, _surfaceWheelHandler);
            IslandSurface.GotFocus -= OnSurfaceGotFocus;
            IslandSurface.LostFocus -= OnSurfaceLostFocus;
            IslandSurface.EffectiveViewportChanged -= OnEffectiveViewportChanged;
            WindowRoot.SizeChanged -= OnRootSizeChanged;
            if (_observedXamlRoot is not null) _observedXamlRoot.Changed -= OnXamlRootChanged;
            Closed -= OnPrototypeClosed;
        }

        private async void OnRunningWindowClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: RunningWindowItemViewModel entry })
            {
                bool switched = await TaskbarState.ActivateAsync(entry, CancellationToken.None);
                if (switched && !_isClosed && IslandState.Mode == IslandMode.Taskbar)
                {
                    IslandState.Collapse();
                }
            }
        }

        private async void OnPinnedAppClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: PinnedAppItemViewModel pin })
                await TaskbarState.LaunchAsync(pin);
        }

        private async void OnAddApplicationClick(object sender, RoutedEventArgs e) => await TaskbarState.AddApplicationAsync();
        private async void OnReloadPinsClick(object sender, RoutedEventArgs e) => await TaskbarState.ReloadPinsAsync();
        private async void OnUnpinClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: PinnedAppItemViewModel pin })
            {
                await TaskbarState.UnpinAsync(pin);
                if (!_isClosed && !_closing) AddApplicationButton.Focus(FocusState.Keyboard);
            }
        }
        private async void OnMoveEarlierClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: PinnedAppItemViewModel pin }) { await TaskbarState.MoveAsync(pin, -1); FocusPin(pin); }
        }
        private async void OnMoveLaterClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: PinnedAppItemViewModel pin }) { await TaskbarState.MoveAsync(pin, 1); FocusPin(pin); }
        }
        private async void OnRetryAvailabilityClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: PinnedAppItemViewModel pin }) await TaskbarState.RetryAvailabilityAsync(pin);
        }
        private async void OnPinWindowClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: RunningWindowItemViewModel window }) await TaskbarState.PinWindowAsync(window);
        }

        private void OnRunningWindowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            _taskbarMeasureDirty = true;
            WindowRoot.InvalidateMeasure();
        }

        private void OnPinsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            _taskbarMeasureDirty = true;
            WindowRoot.InvalidateMeasure();
        }

        private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (!args.InRecycleQueue) _taskbarMeasureDirty = true;
        }

        private void OnTaskbarStateChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TaskbarViewModel.StatusVisibility))
            {
                _taskbarMeasureDirty = true;
                WindowRoot.InvalidateMeasure();
            }
            else if (e.PropertyName == nameof(TaskbarViewModel.PickerActive))
            {
                if (TaskbarState.PickerActive) _autoDismissTimer.Stop(); else EvaluateAutoDismiss();
            }
        }

        private double CalculateTaskbarHeight()
        {
            // D-013: measure headings, feedback and actual item containers before budgeting either viewport.
            double width = IslandPrototypeGeometry.TaskbarWidth - TaskbarContent.Margin.Left - TaskbarContent.Margin.Right;
            double scale = IslandSurface.XamlRoot?.RasterizationScale ?? 1;
            double available = _availableDisplayPixels / scale;
            double windowRow = MeasureItemRow(RunningWindowsListView);
            double pinRow = MeasureItemRow(PinnedAppsListView);
            PinnedAppsListView.MaxHeight = pinRow;
            RunningWindowsListView.MaxHeight = windowRow * 3;
            TaskbarContent.Measure(new Size(width, double.PositiveInfinity));
            double desired = TaskbarContent.DesiredSize.Height;
            double chrome = desired - PinnedAppsListView.DesiredSize.Height - RunningWindowsListView.DesiredSize.Height;
            double viewportBudget = Math.Max(0, available - chrome);
            double pins = Math.Min(PinnedAppsListView.DesiredSize.Height, pinRow);
            double windows = Math.Min(RunningWindowsListView.DesiredSize.Height, windowRow * 3);
            if (pins + windows > viewportBudget)
            {
                double pinMinimum = TaskbarState.Pins.Count > 0 ? Math.Min(pinRow, pins) : pins;
                double windowMinimum = TaskbarState.Windows.Count > 0 ? Math.Min(windowRow, windows) : windows;
                double minimum = pinMinimum + windowMinimum;
                if (minimum > viewportBudget && minimum > 0)
                {
                    pins = viewportBudget * pinMinimum / minimum;
                    windows = viewportBudget - pins;
                }
                else
                {
                    pins = pinMinimum;
                    windows = Math.Min(windows, Math.Max(windowMinimum, viewportBudget - pins));
                }
            }
            PinnedAppsListView.MaxHeight = pins + windows < viewportBudget ? pinRow : Math.Max(0, pins);
            RunningWindowsListView.MaxHeight = pins + windows < viewportBudget ? windowRow * 3 : Math.Max(0, windows);
            TaskbarContent.Measure(new Size(width, double.PositiveInfinity));
            return Math.Max(IslandPrototypeGeometry.NotchHeight, Math.Min(available, TaskbarContent.DesiredSize.Height));
        }

        private static double MeasureItemRow(ListView list)
        {
            if (list.Items.Count > 0 && (list.ContainerFromIndex(0) as FrameworkElement ?? FindChild<ListViewItem>(list)) is FrameworkElement item)
            {
                item.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                return item.DesiredSize.Height + list.Padding.Top + list.Padding.Bottom;
            }
            // Initial virtualized row uses the same token as the item container; subsequent layouts remeasure it.
            return (double)Application.Current.Resources["Taskbar.Row.Height"] + list.Padding.Top + list.Padding.Bottom;
        }

        private void UpdateDynamicTaskbarHeight()
        {
            if (_isClosed || IslandState.Mode != IslandMode.Taskbar || _measuringTaskbar) return;
            _measuringTaskbar = true;
            _taskbarMeasureDirty = false;
            double nextHeight;
            try { nextHeight = CalculateTaskbarHeight(); }
            finally { _measuringTaskbar = false; }
            if (Math.Abs(nextHeight - _expandedHeight) < 0.5) return;
            _expandedHeight = nextHeight;
            _surfaceLayoutDirty = true;
            WindowRoot.InvalidateMeasure();
            if (!_isTransitioning)
            {
                ApplyProgress(_progress);
            }
        }

        private void OnSurfacePointerEntered(object sender, PointerRoutedEventArgs e)
        {
            _isPointerInside = true;
            _autoDismissTimer.Stop();
        }

        private void OnSurfacePointerExited(object sender, PointerRoutedEventArgs e)
        {
            _isPointerInside = false;
            EvaluateAutoDismiss();
        }

        private void OnSurfacePointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _isPointerPressedInside = true;
            _autoDismissTimer.Stop();
        }

        private void OnSurfacePointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _isPointerPressedInside = false;
            EvaluateAutoDismiss();
        }

        private void OnSurfacePointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            _isPointerPressedInside = false;
            EvaluateAutoDismiss();
        }

        private void OnSurfacePointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            if (_isClosed || IslandState.Mode != IslandMode.Taskbar) return;
            _autoDismissTimer.Stop();
            EvaluateAutoDismiss();
        }

        private void OnSurfaceGotFocus(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is Control control && control.FocusState == FocusState.Keyboard)
            {
                _hasKeyboardFocusInside = true;
                _autoDismissTimer.Stop();
            }
        }

        private void OnSurfaceLostFocus(object sender, RoutedEventArgs e)
        {
            if (_isClosed || WindowRoot.XamlRoot is null)
            {
                _hasKeyboardFocusInside = false;
                return;
            }
            if (FocusManager.GetFocusedElement(WindowRoot.XamlRoot) is DependencyObject focused && IsDescendantOf(focused, IslandSurface))
            {
                return;
            }
            _hasKeyboardFocusInside = false;
            EvaluateAutoDismiss();
        }

        private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
        {
            if (_isClosed || args.WindowActivationState != WindowActivationState.Deactivated) return;
            if (_isExpanded && IslandState.Mode == IslandMode.Taskbar && !_isPointerPressedInside && !_isContextMenuOpen && !TaskbarState.PickerActive)
            {
                IslandState.Collapse();
            }
        }

        private void OnAutoDismissTick(object? sender, object e)
        {
            _autoDismissTimer.Stop();
            if (_isClosed || IslandState.Mode != IslandMode.Taskbar) return;
            if (_isPointerInside || _isPointerPressedInside || _hasKeyboardFocusInside || _isContextMenuOpen || TaskbarState.PickerActive) return;
            IslandState.Collapse();
        }

        private void OnOutsideClickTick(object? sender, object e)
        {
            if (_isClosed || IslandState.Mode != IslandMode.Taskbar)
            {
                _outsideClickTimer.Stop();
                return;
            }
            bool isDown = (PInvoke.GetAsyncKeyState(0x01) & 0x8000) != 0
                || (PInvoke.GetAsyncKeyState(0x02) & 0x8000) != 0
                || (PInvoke.GetAsyncKeyState(0x04) & 0x8000) != 0;
            bool newPress = isDown && !_wasMouseDown;
            _wasMouseDown = isDown;

            // Authoritative pointer-inside tracking: the layered window with a native region
            // does not reliably raise XAML PointerExited when the cursor leaves the region, so
            // poll the cursor against the surface layout on every tick and drive the auto-dismiss
            // countdown from the inside/outside transitions.
            bool nowInside = _isPointerInside;
            if (_windowHandle != 0 && _lastLayout is not null
                && PInvoke.GetCursorPos(out var pt)
                && PInvoke.ScreenToClient((HWND)_windowHandle, ref pt))
            {
                double x = pt.X / _lastLayout.RasterizationScale;
                double y = pt.Y / _lastLayout.RasterizationScale;
                nowInside = x >= _lastLayout.Left && x <= _lastLayout.Left + _lastLayout.Width
                    && y >= _lastLayout.Top && y <= _lastLayout.Top + _lastLayout.Height;
            }
            if (nowInside != _isPointerInside)
            {
                _isPointerInside = nowInside;
                if (nowInside)
                {
                    _autoDismissTimer.Stop(); // re-entry cancels the pending dismissal
                }
                else
                {
                    EvaluateAutoDismiss(); // leaving starts a fresh 1.5 s countdown
                }
            }

            if (!newPress || nowInside || _isPointerPressedInside || _isContextMenuOpen || TaskbarState.PickerActive) return;
            IslandState.Collapse();
        }

        private void EvaluateAutoDismiss()
        {
            if (TaskbarState.PickerActive)
            {
                _autoDismissTimer.Stop();
                return;
            }
            if (_isClosed || IslandState.Mode != IslandMode.Taskbar)
            {
                _autoDismissTimer.Stop();
                return;
            }
            if (_isPointerInside || _isPointerPressedInside || _hasKeyboardFocusInside || _isContextMenuOpen)
            {
                _autoDismissTimer.Stop();
                return;
            }
            // Restart a fresh 1.5 s countdown when pointer/focus leaves so brief re-entry resets it.
            _autoDismissTimer.Stop();
            _autoDismissTimer.Start();
        }

        private static bool IsDescendantOf(DependencyObject child, DependencyObject parent)
        {
            DependencyObject? current = child;
            while (current is not null)
            {
                if (ReferenceEquals(current, parent)) return true;
                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }

        private void OnTaskbarKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key is not (VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down or VirtualKey.Home or VirtualKey.End)) return;
            if (e.OriginalSource is FrameworkElement { DataContext: PinnedAppItemViewModel pin })
            {
                int pinIndex = TaskbarState.Pins.IndexOf(pin);
                pinIndex = e.Key switch
                {
                    VirtualKey.Left => pinIndex - 1, VirtualKey.Right => pinIndex + 1,
                    VirtualKey.Up => pinIndex - 4, VirtualKey.Down => pinIndex + 4,
                    VirtualKey.Home => 0, _ => TaskbarState.Pins.Count - 1
                };
                if (TaskbarState.Pins.Count > 0) FocusPin(TaskbarState.Pins[Math.Clamp(pinIndex, 0, TaskbarState.Pins.Count - 1)]);
                e.Handled = true;
                return;
            }
            if (e.OriginalSource is not FrameworkElement { DataContext: RunningWindowItemViewModel entry }) return;
            _hasKeyboardFocusInside = true;
            _autoDismissTimer.Stop();
            int index = TaskbarState.Windows.IndexOf(entry);
            index = e.Key switch
            {
                VirtualKey.Left => index - 1,
                VirtualKey.Right => index + 1,
                VirtualKey.Up => index - 4,
                VirtualKey.Down => index + 4,
                VirtualKey.Home => 0,
                _ => TaskbarState.Windows.Count - 1
            };
            FocusRunningWindow(index);
            e.Handled = true;
        }

        private void FocusExpandedContent()
        {
            if (_expandedMode == IslandMode.Taskbar) AddApplicationButton.Focus(FocusState.Keyboard);
            else PlayPauseButton.Focus(FocusState.Programmatic);
        }

        private void FocusPin(PinnedAppItemViewModel pin)
        {
            if (_isClosed || _closing || !TaskbarState.Pins.Contains(pin)) return;
            PinnedAppsListView.ScrollIntoView(pin, ScrollIntoViewAlignment.Default);
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                if (_isClosed || _closing) return;
                if (PinnedAppsListView.ContainerFromItem(pin) is DependencyObject container)
                    FindChild<Button>(container)?.Focus(FocusState.Keyboard);
            });
        }

        private void FocusRunningWindow(int index)
        {
            if (TaskbarState.Windows.Count == 0) { TaskbarCollapseButton.Focus(FocusState.Programmatic); return; }
            var entry = TaskbarState.Windows[Math.Clamp(index, 0, TaskbarState.Windows.Count - 1)];
            RunningWindowsListView.ScrollIntoView(entry, ScrollIntoViewAlignment.Default);
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                if (_isClosed || IslandState.Mode != IslandMode.Taskbar) return;
                if (RunningWindowsListView.ContainerFromItem(entry) is DependencyObject container)
                {
                    var button = FindChild<Button>(container);
                    if (button is not null)
                    {
                        _hasKeyboardFocusInside = _openedByKeyboard || _hasKeyboardFocusInside;
                        button.Focus(_openedByKeyboard ? FocusState.Keyboard : FocusState.Programmatic);
                    }
                }
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
