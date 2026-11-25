using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using IslandTaskbar.Adapters.Interfaces;
using IslandTaskbar.Models;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Ole;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;

namespace IslandTaskbar.Adapters.Implementations
{
    /// <summary>Owns one real-window-parented dialog; M3 owns the surrounding dismissal guard (D-015).</summary>
    internal sealed class Win32ApplicationSourcePicker : IApplicationSourcePicker
    {
        private readonly Func<nint> _ownerWindowProvider;
        private readonly ApplicationShellWorker _worker;
        private int _picking;
        private int _disposed;
        [ThreadStatic] private static DialogSession? _dialog;

        private sealed class DialogSession
        {
            internal nint Pointer;
            internal nint WindowInterface;
            internal nint Owner;
            internal CancellationToken Token;
            internal bool CloseRequested;
            internal bool OwnerLost;
        }

        public Win32ApplicationSourcePicker(Func<nint> ownerWindowProvider)
        {
            _ownerWindowProvider = ownerWindowProvider ?? throw new ArgumentNullException(nameof(ownerWindowProvider));
            _worker = new();
        }

        public Task<ApplicationSourceSelectionResult> PickAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (Interlocked.CompareExchange(ref _picking, 1, 0) != 0)
                return Task.FromResult(new ApplicationSourceSelectionResult(ApplicationSourceSelectionStatus.Unavailable, null, "An application chooser is already open."));
            try
            {
                // Deliberately on the caller/UI thread before posting any apartment work (D-015).
                nint owner = _ownerWindowProvider();
                return PickCoreAsync(owner, cancellationToken);
            }
            catch (Exception failure) when (Win32ApplicationResolver.IsExpectedFailure(failure))
            {
                Interlocked.Exchange(ref _picking, 0);
                return Task.FromResult(new ApplicationSourceSelectionResult(ApplicationSourceSelectionStatus.Unavailable, null, "The application chooser owner is unavailable."));
            }
            catch
            {
                Interlocked.Exchange(ref _picking, 0);
                throw;
            }
        }

        private async Task<ApplicationSourceSelectionResult> PickCoreAsync(nint owner, CancellationToken token)
        {
            try { return await _worker.RunAsync(cancellation => ShowOnApartment(owner, cancellation), token).ConfigureAwait(false); }
            finally { Interlocked.Exchange(ref _picking, 0); }
        }

        private static unsafe bool IsOwner(nint owner)
        {
            if (owner == 0 || !PInvoke.IsWindow((HWND)owner)) return false;
            uint pid = 0;
            PInvoke.GetWindowThreadProcessId((HWND)owner, &pid);
            return pid == (uint)Environment.ProcessId;
        }

        private static unsafe ApplicationSourceSelectionResult ShowOnApartment(nint owner, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!IsOwner(owner)) return new(ApplicationSourceSelectionStatus.Unavailable, null, "The application chooser needs the current Island window.");
            IFileOpenDialog* dialog = null;
            IOleWindow* window = null;
            IShellItem* selected = null;
            PWSTR path = default;
            nuint timer = 0;
            try
            {
                Trace.WriteLine("M2 chooser: creating shell dialog.");
                Guid clsid = new("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");
                PInvoke.CoCreateInstance(clsid, null, CLSCTX.CLSCTX_INPROC_SERVER, out dialog).ThrowOnFailure();
                dialog->QueryInterface(out window).ThrowOnFailure();
                Trace.WriteLine("M2 chooser: shell dialog created.");
                FILEOPENDIALOGOPTIONS options = 0;
                dialog->GetOptions(&options);
                dialog->SetOptions(options | FILEOPENDIALOGOPTIONS.FOS_FORCEFILESYSTEM | FILEOPENDIALOGOPTIONS.FOS_FILEMUSTEXIST |
                    FILEOPENDIALOGOPTIONS.FOS_PATHMUSTEXIST | FILEOPENDIALOGOPTIONS.FOS_NOCHANGEDIR | FILEOPENDIALOGOPTIONS.FOS_NODEREFERENCELINKS);
                fixed (char* title = "Add application") dialog->SetTitle(title);
                fixed (char* name = "Applications (*.exe; *.lnk)")
                fixed (char* pattern = "*.exe;*.lnk")
                {
                    COMDLG_FILTERSPEC filter = new() { pszName = name, pszSpec = pattern };
                    dialog->SetFileTypes(1, &filter);
                }
                _dialog = new() { Pointer = (nint)dialog, WindowInterface = (nint)window, Owner = owner, Token = token };
                timer = PInvoke.SetTimer(default, 0, 100, &CheckCancellation);
                if (timer == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                token.ThrowIfCancellationRequested();
                Trace.WriteLine("M2 chooser: entering modal show.");
                try { dialog->Show((HWND)owner); }
                catch (COMException failure) when (failure.HResult == unchecked((int)0x800704C7))
                {
                    Trace.WriteLine("M2 chooser: modal show returned cancellation.");
                    token.ThrowIfCancellationRequested();
                    return _dialog.OwnerLost
                        ? new(ApplicationSourceSelectionStatus.Unavailable, null, "The chooser's owner window closed.")
                        : new(ApplicationSourceSelectionStatus.Canceled, null);
                }
                token.ThrowIfCancellationRequested();
                if (!IsOwner(owner)) return new(ApplicationSourceSelectionStatus.Unavailable, null, "The chooser's owner window closed.");
                dialog->GetResult(&selected);
                selected->GetDisplayName(SIGDN.SIGDN_FILESYSPATH, &path);
                string value = path.ToString();
                var kind = string.Equals(System.IO.Path.GetExtension(value), ".lnk", StringComparison.OrdinalIgnoreCase)
                    ? LaunchReferenceKind.Shortcut : LaunchReferenceKind.Executable;
                LaunchReference source = new(kind, value);
                if (!Win32ApplicationResolver.ValidSource(source)) return new(ApplicationSourceSelectionStatus.Unavailable, null, "Choose a supported local executable or shortcut.");
                return new(ApplicationSourceSelectionStatus.Selected, source);
            }
            catch (Exception failure) when (Win32ApplicationResolver.IsExpectedFailure(failure))
            {
                Debug.WriteLine($"Application chooser failed: {failure.GetType().Name}.");
                return new(ApplicationSourceSelectionStatus.Failed, null, "The application chooser could not complete. Try again.");
            }
            finally
            {
                // Kill before releasing COM; a later queued timer callback observes no active session.
                if (timer != 0 && !PInvoke.KillTimer(default, timer)) Debug.WriteLine("Application chooser timer cleanup failed.");
                _dialog = null;
                if (path.Value != null) PInvoke.CoTaskMemFree(path.Value);
                if (selected != null) selected->Release();
                if (window != null) window->Release();
                if (dialog != null) dialog->Release();
                Trace.WriteLine("M2 chooser: apartment resources released.");
            }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
        private static unsafe void CheckCancellation(HWND window, uint message, nuint timer, uint time)
        {
            try
            {
                var session = _dialog;
                if (session is null) return;
                session.OwnerLost = !IsOwner(session.Owner);
                if (!session.Token.IsCancellationRequested && !session.OwnerLost) return;
                ((IOleWindow*)session.WindowInterface)->GetWindow(out HWND dialogWindow);
                if (!PInvoke.IsWindow(dialogWindow) || !PInvoke.IsWindowVisible(dialogWindow)) return;
                if (!session.CloseRequested) Trace.WriteLine("M2 chooser: closing canceled dialog on its owning apartment.");
                // Runs in the dialog's own modal message loop, never through a cross-apartment pointer.
                ((IFileOpenDialog*)session.Pointer)->Close(new HRESULT(unchecked((int)0x800704C7)));
                // Close may be acknowledged during initialization before the modal window handles it.
                // A normal close message to this exact dialog completes the user-cancel path as well.
                if (PInvoke.IsWindow(dialogWindow)) PInvoke.PostMessage(dialogWindow, 0x0010, default, default);
                if (!session.CloseRequested) Trace.WriteLine("M2 chooser: Close returned successfully.");
                // Shell initialization may acknowledge Close before its modal UI is ready. Keep honoring
                // cancellation until Show actually returns; a successful Close is not completion evidence.
                session.CloseRequested = true;
            }
            catch (Exception failure) { Debug.WriteLine($"Application chooser cancellation failed: {failure.GetType().Name}."); }
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _disposed, 1);
            return _worker.DisposeAsync();
        }
    }
}
