using System.Diagnostics;
using Microsoft.UI.Xaml;
using IslandTaskbar.Presentation.Windows;

namespace IslandTaskbar
{
    /// <summary>
    /// Application entry point and top-level lifecycle coordinator for IslandTaskbar.
    /// </summary>
    public partial class App : Application
    {
        private IslandWindow? _mainWindow;

        /// <summary>
        /// Initializes a new instance of <see cref="App"/>.
        /// </summary>
        public App()
        {
            InitializeComponent();
            UnhandledException += OnUnhandledException;
        }

        /// <summary>
        /// Creates and activates the initial empty application window on launch.
        /// </summary>
        /// <param name="args">Launch activation details provided by the Windows App SDK.</param>
        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            _mainWindow = new IslandWindow();
            _mainWindow.Closed += OnMainWindowClosed;
            _mainWindow.Activate();
        }

        private void OnMainWindowClosed(object sender, WindowEventArgs args)
        {
            if (_mainWindow is not null)
            {
                _mainWindow.Closed -= OnMainWindowClosed;
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
