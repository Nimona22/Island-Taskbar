using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Windows.Win32;
using Windows.Win32.System.Com;

namespace IslandTaskbar.Adapters.Implementations
{
    /// <summary>Owns synchronous shell COM operations on one STA, never the UI thread (D-015).</summary>
    internal sealed class ApplicationShellWorker : IAsyncDisposable
    {
        private readonly object _gate = new();
        private readonly BlockingCollection<Action<Exception?>> _work = new();
        private readonly CancellationTokenSource _shutdown = new();
        private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _disposed;
        private Task? _disposeTask;

        public ApplicationShellWorker()
        {
            Thread thread = new(RunApartment) { IsBackground = true, Name = "IslandTaskbar application shell" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        public Task<T> RunAsync<T>(Func<CancellationToken, T> operation, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(operation);
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<T>(cancellationToken);
                var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
                var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
                _work.Add(initializationFailure =>
                {
                    using (linked)
                    {
                        try
                        {
                            if (initializationFailure is not null) throw initializationFailure;
                            linked.Token.ThrowIfCancellationRequested();
                            // After an irreversible operation, the operation owns late-cancellation semantics.
                            completion.TrySetResult(operation(linked.Token));
                        }
                        catch (OperationCanceledException) when (linked.IsCancellationRequested)
                        { completion.TrySetCanceled(linked.Token); }
                        catch (Exception failure) { completion.TrySetException(failure); }
                    }
                });
                return completion.Task;
            }
        }

        private unsafe void RunApartment()
        {
            bool initialized = false;
            Exception? initializationFailure = null;
            try
            {
                int result = PInvoke.CoInitializeEx(null, COINIT.COINIT_APARTMENTTHREADED).Value;
                initialized = result >= 0;
                if (!initialized) initializationFailure = new System.Runtime.InteropServices.COMException("Shell apartment initialization failed.", result);
                foreach (var operation in _work.GetConsumingEnumerable()) operation(initializationFailure);
            }
            catch (Exception failure)
            {
                // Complete every accepted request even if apartment setup itself fails.
                while (_work.TryTake(out var operation)) operation(failure);
                _exited.TrySetException(failure);
            }
            finally
            {
                if (initialized) PInvoke.CoUninitialize();
                _exited.TrySetResult();
            }
        }

        public ValueTask DisposeAsync()
        {
            lock (_gate)
            {
                if (_disposeTask is not null) return new(_disposeTask);
                _disposed = true;
                _work.CompleteAdding();
                _disposeTask = DisposeCoreAsync();
                return new(_disposeTask);
            }
        }

        private async Task DisposeCoreAsync()
        {
            await _shutdown.CancelAsync().ConfigureAwait(false);
            await _exited.Task.ConfigureAwait(false);
            _work.Dispose();
            _shutdown.Dispose();
        }
    }
}
