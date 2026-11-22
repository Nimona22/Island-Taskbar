using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using IslandTaskbar.Adapters.Implementations;
using IslandTaskbar.Controllers;
using IslandTaskbar.Models;
using IslandTaskbar.Presentation.Windows;
using IslandTaskbar.Services;
using IslandTaskbar.ViewModels;

namespace IslandTaskbar
{
    /// <summary>
    /// Application entry point and top-level lifecycle coordinator for IslandTaskbar.
    /// </summary>
    public partial class App : Application
    {
        private IslandWindow? _mainWindow;
        private IIslandWindowController? _windowController;

        /// <summary>
        /// Initializes a new instance of <see cref="App"/>.
        /// </summary>
        public App()
        {
            InitializeComponent();
            UnhandledException += OnUnhandledException;
        }

        /// <summary>
        /// Creates and initializes the Phase 3.1 prototype without requesting activation.
        /// </summary>
        /// <param name="args">Launch activation details provided by the Windows App SDK.</param>
        protected override async void OnLaunched(LaunchActivatedEventArgs args)
        {
            _mainWindow = new IslandWindow(new MusicPrototypeViewModel(
                new MediaPlaybackService(new WindowsMediaSessionService()), DispatcherQueue.GetForCurrentThread()));
            _mainWindow.Closed += OnMainWindowClosed;
            _mainWindow.SurfaceLayoutChanged += OnSurfaceLayoutChanged;

            try
            {
                // Manual construction keeps prototype behavior behind the existing adapter boundary.
                _windowController = new IslandWindowController(
                    new WindowsAppSdkIslandWindowAdapter(_mainWindow),
                    new WindowsAppSdkDisplayService());
                _windowController.Initialize();
            }
            catch
            {
                _mainWindow.Closed -= OnMainWindowClosed;
                _mainWindow.SurfaceLayoutChanged -= OnSurfaceLayoutChanged;
                _windowController?.Dispose();
                _windowController = null;
                _mainWindow.Close();
                _mainWindow = null;
                throw;
            }
            try { await _mainWindow.InitializeMediaAsync(CancellationToken.None); }
            catch (OperationCanceledException) when (_mainWindow is null)
            {
                // Normal close cancels a still-pending GSMTC manager request.
            }
        }

        private void OnSurfaceLayoutChanged(object? sender, IslandSurfaceLayoutEventArgs e)
        {
            try
            {
                _windowController?.UpdateSurfaceRegion(e);
            }
            catch (Win32Exception failure)
            {
                Debug.WriteLine($"Prototype surface region failed ({failure.NativeErrorCode}): {failure.Message}");
                var window = _mainWindow;
                if (window is null)
                {
                    return;
                }

                window.SurfaceLayoutChanged -= OnSurfaceLayoutChanged;
                // Let the current layout/launch callback unwind before normal close/disposal.
                if (!window.DispatcherQueue.TryEnqueue(() =>
                    {
                        if (ReferenceEquals(_mainWindow, window))
                        {
                            window.Close();
                        }
                    }))
                {
                    window.Close();
                }
            }
        }

        private void OnMainWindowClosed(object sender, WindowEventArgs args)
        {
            if (_mainWindow is not null)
            {
                _mainWindow.Closed -= OnMainWindowClosed;
                _mainWindow.SurfaceLayoutChanged -= OnSurfaceLayoutChanged;
                _windowController?.Dispose();
                _windowController = null;
                _mainWindow = null;
                Exit();
            }
        }

        private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            // Phase 1 bootstrap: minimal local diagnostic output until IAppLogger (OD-022) is introduced.
            // Intentionally leave e.Handled as false so unexpected faults are not silently swallowed (CONVENTIONS.md §H.2).
            Debug.WriteLine($"Unhandled UI exception: {e.Exception.GetType().Name}");
        }
    }
}
