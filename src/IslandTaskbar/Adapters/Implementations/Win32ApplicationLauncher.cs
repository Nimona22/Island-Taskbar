using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using IslandTaskbar.Adapters.Interfaces;
using IslandTaskbar.Models;
using Windows.Win32;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;

namespace IslandTaskbar.Adapters.Implementations
{
    /// <summary>Dispatches explicit launch requests, without selecting any existing window (D-014).</summary>
    internal sealed class Win32ApplicationLauncher : IApplicationLauncher
    {
        private readonly object _gate = new();
        private readonly HashSet<LaunchReference> _inFlight = new();
        private readonly ApplicationShellWorker _worker = new();
        private bool _disposed;

        public Win32ApplicationLauncher() { }

        public async Task<ApplicationLaunchResult> LaunchAsync(LaunchReference launchReference, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Win32ApplicationResolver.ValidSource(launchReference)) return new(ApplicationLaunchStatus.Unsupported, "This application target is not supported.");
            LaunchReference request = launchReference.Kind == LaunchReferenceKind.PackagedApplication ? launchReference
                : new(launchReference.Kind, Path.GetFullPath(launchReference.Value));
            LaunchReference? canonicalReservation = null;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (!_inFlight.Add(request)) return new(ApplicationLaunchStatus.Busy, "A launch request for this application is already in progress.");
            }
            try
            {
                var resolution = await _worker.RunAsync(token => Win32ApplicationResolver.ResolveOnApartment(request, token, false), cancellationToken).ConfigureAwait(false);
                if (resolution.Status != ApplicationResolutionStatus.Available) return FromResolution(resolution);
                LaunchReference target = resolution.Application!.LaunchReference;
                if (target != request)
                {
                    lock (_gate)
                    {
                        if (!_inFlight.Add(target)) return new(ApplicationLaunchStatus.Busy, "A launch request for this application is already in progress.");
                        canonicalReservation = target;
                    }
                }
                return await _worker.RunAsync(token => Dispatch(target, token), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                lock (_gate)
                {
                    _inFlight.Remove(request);
                    if (canonicalReservation is { } canonical) _inFlight.Remove(canonical);
                }
            }
        }

        private static unsafe ApplicationLaunchResult Dispatch(LaunchReference target, CancellationToken token)
        {
            // Revalidate on the native apartment immediately before dispatch, without losing LNK intent.
            var resolution = Win32ApplicationResolver.ResolveOnApartment(target, token, false);
            if (resolution.Status != ApplicationResolutionStatus.Available) return FromResolution(resolution);
            bool mayHaveDispatched = false;
            IApplicationActivationManager* activation = null;
            try
            {
                var plan = Win32ApplicationResolver.PrepareDispatch(target);
                if (plan.Target.Kind == LaunchReferenceKind.PackagedApplication)
                {
                    Guid clsid = new("45BA127D-10A8-46EA-8AB7-56EA9078943C");
                    // Out-of-process lifetime keeps activation arguments valid after this adapter is disposed.
                    PInvoke.CoCreateInstance(clsid, null, CLSCTX.CLSCTX_LOCAL_SERVER, out activation).ThrowOnFailure();
                    token.ThrowIfCancellationRequested();
                    fixed (char* appId = plan.Target.Value)
                    {
                        uint processId = 0;
                        mayHaveDispatched = true;
                        activation->ActivateApplication(appId, null, ACTIVATEOPTIONS.AO_NOERRORUI, &processId);
                    }
                }
                else
                {
                    var start = new ProcessStartInfo(plan.Target.Value)
                    {
                        // CreateProcess-backed dispatch cannot request UAC through shell/compatibility
                        // auto-elevation. Preserve the shell link's original argument string, directory
                        // and show state; never reconstruct an interpreter command or persist a flattened link.
                        UseShellExecute = false, Arguments = plan.Arguments, WorkingDirectory = plan.WorkingDirectory,
                        WindowStyle = plan.WindowStyle
                    };
                    token.ThrowIfCancellationRequested();
                    mayHaveDispatched = true;
                    using var launched = Process.Start(start);
                    if (launched is null) return new(ApplicationLaunchStatus.OutcomeUnknown, "The launch outcome is unknown. Check the application before trying again.");
                }
                // Cancellation after the native call cannot undo an accepted request.
                return new(ApplicationLaunchStatus.Accepted);
            }
            catch (Exception failure) when (Win32ApplicationResolver.IsExpectedFailure(failure))
            {
                if (!mayHaveDispatched) return FromResolution(Win32ApplicationResolver.ResolutionFailure(failure));
                return DispatchFailure(failure is System.ComponentModel.Win32Exception native ? native.NativeErrorCode : failure.HResult & 0xFFFF,
                    mayHaveDispatched);
            }
            finally { if (activation != null) activation->Release(); }
        }

        private static ApplicationLaunchResult FromResolution(ApplicationResolutionResult resolution) => new(resolution.Status switch
        {
            ApplicationResolutionStatus.Missing => ApplicationLaunchStatus.Unavailable,
            ApplicationResolutionStatus.AccessDenied => ApplicationLaunchStatus.Denied,
            ApplicationResolutionStatus.Unsupported => ApplicationLaunchStatus.Unsupported,
            _ => ApplicationLaunchStatus.Failed
        }, resolution.Message);

        private static ApplicationLaunchResult DispatchFailure(int code, bool mayHaveDispatched) => code switch
        {
            2 or 3 or 267 => new(ApplicationLaunchStatus.Unavailable, "The application target is no longer available."),
            5 or 740 => new(ApplicationLaunchStatus.Denied, "Windows denied this launch request."),
            11 or 193 => new(ApplicationLaunchStatus.Unsupported, "Windows does not support this application target."),
            1223 => new(ApplicationLaunchStatus.Canceled, "The launch request was canceled before dispatch."),
            _ when mayHaveDispatched => new(ApplicationLaunchStatus.OutcomeUnknown, "The launch outcome is unknown. Check the application before trying again."),
            _ => new(ApplicationLaunchStatus.Failed, "The application could not be launched.")
        };

        public ValueTask DisposeAsync()
        {
            lock (_gate) _disposed = true;
            return _worker.DisposeAsync();
        }
    }
}
