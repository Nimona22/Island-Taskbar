using System.Diagnostics;
using Microsoft.UI.Xaml;
using IslandTaskbar.Adapters.Implementations;
using IslandTaskbar.Controllers;
using IslandTaskbar.Presentation.Windows;

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
        /// Creates and initializes the Phase 2.2 test window without requesting activation.
        /// </summary>
        /// <param name="args">Launch activation details provided by the Windows App SDK.</param>
        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            _mainWindow = new IslandWindow();
            _mainWindow.Closed += OnMainWindowClosed;
            _mainWindow.TemporaryCloseRequested += OnTemporaryCloseRequested;

            try
            {
                // Phase 2.2 authorizes manual construction; no MVVM or DI framework is introduced.
                _windowController = new IslandWindowController(
                    new WindowsAppSdkIslandWindowAdapter(_mainWindow),
                    new WindowsAppSdkDisplayService());
                _windowController.Initialize();
            }
            catch
            {
                _mainWindow.Closed -= OnMainWindowClosed;
                _mainWindow.TemporaryCloseRequested -= OnTemporaryCloseRequested;
                _windowController?.Dispose();
                _windowController = null;
                _mainWindow.Close();
                _mainWindow = null;
                throw;
            }
        }

        private void OnTemporaryCloseRequested(object? sender, System.EventArgs e)
        {
            _mainWindow?.Close();
        }

        private void OnMainWindowClosed(object sender, WindowEventArgs args)
        {
            if (_mainWindow is not null)
            {
                _mainWindow.Closed -= OnMainWindowClosed;
                _mainWindow.TemporaryCloseRequested -= OnTemporaryCloseRequested;
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
