using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using IslandTaskbar.Adapters.Interfaces;
using IslandTaskbar.Models;
using IslandTaskbar.Persistence;

namespace IslandTaskbar.Services
{
    /// <summary>Serialized save-before-publish pin management; runtime enrichment never persists (D-014/D-015).</summary>
    internal sealed class PinManagerService : IPinManagerService
    {
        private readonly IPinsFileStore _store;
        private readonly IApplicationResolver _resolver;
        private readonly WindowManagerService _windows;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly object _lifetimeGate = new();
        private readonly CancellationTokenSource _lifetime = new();
        private PinManagerSnapshot _current = new(false, ImmutableArray<PinnedApp>.Empty,
            ImmutableDictionary<PinId, PinnedAppRuntimeState>.Empty, PinsWriteAccess.ReadOnly, null);
        private bool _disposing;
        private bool _presenceRequested;
        private Task? _presenceTask;
        private Task? _disposeTask;

        public PinManagerService(IPinsFileStore store, IApplicationResolver resolver, WindowManagerService windows)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _windows = windows ?? throw new ArgumentNullException(nameof(windows));
            _windows.Changed += OnWindowsChanged;
        }

        public event EventHandler? Changed;
        public PinManagerSnapshot Current => Volatile.Read(ref _current);

        public Task<PinsLoadResult> InitializeAsync(CancellationToken cancellationToken) =>
            RunAsync(async token =>
            {
                PinsLoadResult result;
                try { result = await _store.LoadAsync(token).ConfigureAwait(false); }
                catch (Exception failure) when (IsOperationalFailure(failure))
                {
                    result = new(PinsLoadStatus.Failed, null, null, PinsWriteAccess.ReadOnly,
                        "Pins could not be loaded. Check storage and reload pins.");
                }
                var previous = Current;
                if (result.Status is PinsLoadStatus.Loaded or PinsLoadStatus.Missing && result.Data is not null)
                {
                    var states = result.Data.Pins.ToImmutableDictionary(pin => pin.Id,
                        _ => new PinnedAppRuntimeState(null, ApplicationPresence.Unknown));
                    Publish(new(true, result.Data.Pins, states, result.WriteAccess, result.Revision, result.Message));
                    foreach (var pin in result.Data.Pins)
                    {
                        var resolution = await ResolveCoreAsync(pin.LaunchReference, token).ConfigureAwait(false);
                        Publish(Current with { RuntimeStates = Current.RuntimeStates.SetItem(pin.Id,
                            new PinnedAppRuntimeState(resolution, ApplicationPresence.Unknown)) });
                    }
                    await RefreshPresenceCoreAsync(token).ConfigureAwait(false);
                }
                else
                {
                    Publish(previous with { IsInitialized = true, WriteAccess = PinsWriteAccess.ReadOnly,
                        Message = result.Message ?? "Pins need recovery. Keep the original file and reload after repair." });
                }
                return result;
            }, cancellationToken);

        public Task<PinMutationResult> PinAsync(LaunchReference source, CancellationToken cancellationToken) =>
            RunAsync(async token =>
            {
                var blocked = MutationBlocked();
                if (blocked is not null) return blocked;
                var resolution = await ResolveCoreAsync(source, token).ConfigureAwait(false);
                if (resolution.Status != ApplicationResolutionStatus.Available || resolution.Application is null)
                    return new PinMutationResult(PinMutationStatus.InvalidSource, ResolutionStatus: resolution.Status,
                        Message: resolution.Message ?? "The application cannot be pinned. Check its source and try again.");
                var application = resolution.Application;
                var duplicate = Current.Pins.FirstOrDefault(pin => pin.LaunchReference == application.LaunchReference);
                if (duplicate is not null) return new(PinMutationStatus.AlreadyPinned, duplicate.Id);
                var pin = new PinnedApp(new PinId(Guid.NewGuid()), application.DisplayName,
                    application.LaunchReference, null, Current.Pins.Length);
                return await CommitAsync(Current.Pins.Add(pin), pin.Id, resolution, token).ConfigureAwait(false);
            }, cancellationToken);

        public Task<PinMutationResult> UnpinAsync(PinId pinId, CancellationToken cancellationToken) =>
            RunAsync(async token =>
            {
                var blocked = MutationBlocked();
                if (blocked is not null) return blocked;
                if (!Current.Pins.Any(pin => pin.Id == pinId)) return new(PinMutationStatus.NotFound);
                var pins = Normalize(Current.Pins.Where(pin => pin.Id != pinId));
                return await CommitAsync(pins, pinId, null, token).ConfigureAwait(false);
            }, cancellationToken);

        public Task<PinMutationResult> MoveAsync(PinId pinId, PinId? beforePinId, CancellationToken cancellationToken) =>
            RunAsync(async token =>
            {
                var blocked = MutationBlocked();
                if (blocked is not null) return blocked;
                var previous = Current.Pins;
                var pins = previous.ToList();
                var selected = pins.FirstOrDefault(pin => pin.Id == pinId);
                if (selected is null || (beforePinId is { } before && !pins.Any(pin => pin.Id == before)))
                    return new(PinMutationStatus.NotFound);
                if (beforePinId == pinId) return new(PinMutationStatus.Unchanged, pinId);
                pins.Remove(selected);
                int index = beforePinId is null ? pins.Count : pins.FindIndex(pin => pin.Id == beforePinId.Value);
                pins.Insert(index, selected);
                if (pins.Select(pin => pin.Id).SequenceEqual(previous.Select(pin => pin.Id)))
                    return new(PinMutationStatus.Unchanged, pinId);
                return await CommitAsync(Normalize(pins), pinId, null, token).ConfigureAwait(false);
            }, cancellationToken);

        public Task<ApplicationWindowIdentityResult> ResolveWindowAsync(RunningWindowId runningWindowId,
            CancellationToken cancellationToken) =>
            RunAsync(token => IdentifyCurrentWindowAsync(runningWindowId, token), cancellationToken);

        public async Task RefreshAvailabilityAsync(PinId pinId, CancellationToken cancellationToken)
        {
            await RunAsync(async token =>
            {
                var pin = Current.Pins.FirstOrDefault(candidate => candidate.Id == pinId);
                // A stale runtime refresh is explicitly harmless; it cannot recreate an unpinned item.
                if (pin is null) return false;
                var resolution = await ResolveCoreAsync(pin.LaunchReference, token).ConfigureAwait(false);
                Publish(Current with { RuntimeStates = Current.RuntimeStates.SetItem(pin.Id,
                    new PinnedAppRuntimeState(resolution, ApplicationPresence.Unknown)) });
                await RefreshPresenceCoreAsync(token).ConfigureAwait(false);
                return true;
            }, cancellationToken).ConfigureAwait(false);
        }

        private PinMutationResult? MutationBlocked() => !Current.IsInitialized
            ? new(PinMutationStatus.NotInitialized, Message: "Load pins before changing them.")
            : Current.WriteAccess != PinsWriteAccess.Writable
                ? new(PinMutationStatus.ReadOnly, Message: Current.Message ?? "Pins are read-only. Reload pins to recover writing.")
                : null;

        private static ImmutableArray<PinnedApp> Normalize(IEnumerable<PinnedApp> pins) =>
            pins.Select((pin, order) => pin with { Order = order }).ToImmutableArray();

        private async Task<PinMutationResult> CommitAsync(ImmutableArray<PinnedApp> pins, PinId affected,
            ApplicationResolutionResult? newResolution, CancellationToken token)
        {
            var previous = Current;
            PinsSaveResult result;
            try
            {
                result = await _store.SaveAsync(new PinsData(PinsData.CurrentSchemaVersion, pins),
                    previous.Revision, token).ConfigureAwait(false);
            }
            catch (Exception failure) when (IsOperationalFailure(failure))
            {
                result = new(PinsSaveStatus.Failed, null, "Pins could not be saved. Check storage and reload pins.");
            }
            if (result.Status is not (PinsSaveStatus.Saved or PinsSaveStatus.Unchanged))
            {
                Publish(previous with { WriteAccess = PinsWriteAccess.ReadOnly,
                    Message = result.Message ?? "Pins could not be saved. Resolve the storage issue and reload pins." });
                return new(result.Status switch
                {
                    PinsSaveStatus.Conflict => PinMutationStatus.Conflict,
                    PinsSaveStatus.ReadOnly => PinMutationStatus.ReadOnly,
                    _ => PinMutationStatus.SaveFailed
                }, Message: Current.Message);
            }

            // A canceled token after commit must not discard the new committed snapshot.
            var ids = pins.Select(pin => pin.Id).ToHashSet();
            var states = previous.RuntimeStates.Where(item => ids.Contains(item.Key)).ToImmutableDictionary();
            if (newResolution is not null)
                states = states.SetItem(affected, new PinnedAppRuntimeState(newResolution, ApplicationPresence.Unknown));
            Publish(previous with { Pins = pins, RuntimeStates = states, Revision = result.Revision, Message = null });
            RequestPresenceRefresh();
            return new(result.Status == PinsSaveStatus.Saved ? PinMutationStatus.Saved : PinMutationStatus.Unchanged, affected);
        }

        private async Task<ApplicationResolutionResult> ResolveCoreAsync(LaunchReference source, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var result = await _resolver.ResolveAsync(source, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (!Enum.IsDefined(result.Status) ||
                    (result.Status == ApplicationResolutionStatus.Available && (result.Application is null ||
                        !PinDataValidation.ValidText(result.Application.DisplayName) ||
                        !PinDataValidation.ValidReference(result.Application.LaunchReference))))
                    return new(ApplicationResolutionStatus.Failed, null, "Application information is unavailable. Retry availability.");
                if (result.Status != ApplicationResolutionStatus.Available) return result with { Application = null };
                if (result.Application!.Identity is { } identity && !PinDataValidation.ValidIdentity(identity))
                    return result with { Application = result.Application with { Identity = null } };
                return result;
            }
            catch (Exception failure) when (IsOperationalFailure(failure))
            {
                Debug.WriteLine("Pin target resolution failed: " + failure.GetType().Name);
                return new(ApplicationResolutionStatus.Failed, null, "Application information is unavailable. Retry availability.");
            }
        }

        private async Task<ApplicationWindowIdentityResult> IdentifyCurrentWindowAsync(RunningWindowId id,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!_windows.Current.Any(window => window.Id == id)) return MissingWindow();
            if (_windows.UnavailableReason is not null) return UnknownWindow();
            ApplicationWindowIdentityResult result;
            try { result = await _resolver.IdentifyWindowAsync(id, token).ConfigureAwait(false); }
            catch (Exception failure) when (IsOperationalFailure(failure))
            {
                Debug.WriteLine("Window application enrichment failed: " + failure.GetType().Name);
                result = UnknownWindow();
            }
            token.ThrowIfCancellationRequested();
            if (!_windows.Current.Any(window => window.Id == id)) return MissingWindow();
            if (_windows.UnavailableReason is not null) return UnknownWindow();
            if (!Enum.IsDefined(result.Status)) return UnknownWindow();
            if (result.Status == ApplicationResolutionStatus.Available)
            {
                if (result.Identity is not { } identity || !PinDataValidation.ValidIdentity(identity)) return UnknownWindow();
                return result.RelaunchReference is { } reference && !PinDataValidation.ValidReference(reference)
                    ? result with { RelaunchReference = null } : result;
            }
            return result with { Identity = null, RelaunchReference = null };
        }

        private static ApplicationWindowIdentityResult MissingWindow() =>
            new(ApplicationResolutionStatus.Missing, null, null, "That window is no longer available.");

        private static ApplicationWindowIdentityResult UnknownWindow() =>
            new(ApplicationResolutionStatus.Failed, null, null, "Window application identity is temporarily unavailable.");

        private async Task RefreshPresenceCoreAsync(CancellationToken token)
        {
            var previous = Current;
            var wanted = previous.RuntimeStates.Values
                .Select(state => state.Resolution?.Application?.Identity).OfType<ApplicationIdentity>().ToHashSet();
            var identities = new Dictionary<RunningWindowId, ApplicationIdentity>();
            if (wanted.Count != 0 && _windows.UnavailableReason is null)
            {
                foreach (var window in _windows.Current)
                {
                    var result = await IdentifyCurrentWindowAsync(window.Id, token).ConfigureAwait(false);
                    if (result.Status == ApplicationResolutionStatus.Available && result.Identity is { } identity)
                        identities[window.Id] = identity;
                }
            }
            token.ThrowIfCancellationRequested();
            var live = _windows.Current.Select(window => window.Id).ToHashSet();
            var positive = _windows.UnavailableReason is null
                ? identities.Where(item => live.Contains(item.Key)).Select(item => item.Value).ToHashSet()
                : new HashSet<ApplicationIdentity>();
            var states = previous.RuntimeStates.ToBuilder();
            bool changed = false;
            foreach (var pair in previous.RuntimeStates)
            {
                var identity = pair.Value.Resolution?.Application?.Identity;
                var presence = identity is { } known && positive.Contains(known)
                    ? ApplicationPresence.Running : ApplicationPresence.Unknown;
                if (presence == pair.Value.Presence) continue;
                states[pair.Key] = pair.Value with { Presence = presence };
                changed = true;
            }
            if (changed) Publish(previous with { RuntimeStates = states.ToImmutable() });
        }

        private void OnWindowsChanged(object? sender, EventArgs e) => RequestPresenceRefresh();

        private void RequestPresenceRefresh()
        {
            lock (_lifetimeGate)
            {
                if (_disposing) return;
                _presenceRequested = true;
                _presenceTask ??= Task.Run(PresenceLoopAsync);
            }
        }

        private async Task PresenceLoopAsync()
        {
            while (true)
            {
                lock (_lifetimeGate)
                {
                    if (_disposing || !_presenceRequested) { _presenceTask = null; return; }
                    _presenceRequested = false;
                }
                try
                {
                    await RunAsync(async token =>
                    {
                        await RefreshPresenceCoreAsync(token).ConfigureAwait(false);
                        return true;
                    }, CancellationToken.None).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { lock (_lifetimeGate) { _presenceTask = null; } return; }
                catch (ObjectDisposedException) { lock (_lifetimeGate) { _presenceTask = null; } return; }
            }
        }

        private void Publish(PinManagerSnapshot snapshot)
        {
            Volatile.Write(ref _current, snapshot);
            EventHandler? changed;
            lock (_lifetimeGate) { changed = _disposing ? null : Changed; }
            if (changed is null) return;
            foreach (EventHandler observer in changed.GetInvocationList())
            {
                try { observer(this, EventArgs.Empty); }
                catch (Exception failure)
                {
                    // Observers cannot turn a durable commit into a false SaveFailed result.
                    Debug.WriteLine("Pin state observer failed: " + failure.GetType().Name);
                }
            }
        }

        private async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
        {
            CancellationTokenSource linked;
            lock (_lifetimeGate)
            {
                ObjectDisposedException.ThrowIf(_disposing, this);
                linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            }
            using (linked)
            {
                await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
                try
                {
                    return await Task.Run(() => work(linked.Token), CancellationToken.None).ConfigureAwait(false);
                }
                finally { _gate.Release(); }
            }
        }

        private static bool IsOperationalFailure(Exception failure) =>
            failure is IOException or UnauthorizedAccessException or InvalidOperationException
                or NotSupportedException or ArgumentException or Win32Exception or COMException;

        public ValueTask DisposeAsync()
        {
            lock (_lifetimeGate)
            {
                if (_disposeTask is not null) return new ValueTask(_disposeTask);
                _disposing = true;
                _windows.Changed -= OnWindowsChanged;
                _disposeTask = DisposeCoreAsync(_presenceTask);
                return new ValueTask(_disposeTask);
            }
        }

        private async Task DisposeCoreAsync(Task? presenceTask)
        {
            try { _lifetime.Cancel(); }
            catch (AggregateException) { Debug.WriteLine("An application resolver cancellation callback failed."); }
            try
            {
                if (presenceTask is not null) await presenceTask.ConfigureAwait(false);
            }
            finally
            {
                await _gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    try { await _store.DisposeAsync().ConfigureAwait(false); }
                    finally { await _resolver.DisposeAsync().ConfigureAwait(false); }
                }
                finally
                {
                    _gate.Release();
                    _lifetime.Dispose();
                }
            }
        }
    }
}

