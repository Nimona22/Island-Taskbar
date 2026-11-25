using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IslandTaskbar.Models;

namespace IslandTaskbar.Persistence
{
    /// <summary>Version-1 pin storage with a lifetime writer lease and compare-before-commit revisions (D-014).</summary>
    internal sealed class PinsFileService : IPinsFileStore
    {
        private static readonly string[] RootProperties = { "schemaVersion", "pins" };
        private static readonly string[] PinProperties = { "id", "displayName", "launchReference", "iconSource", "order" };
        private readonly string _directory;
        private readonly string _path;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly object _lifetimeGate = new();
        private readonly CancellationTokenSource _lifetime = new();
        private FileStream? _lease;
        private PinsData? _loadedData;
        private string? _loadedRevision;
        private bool _writable;
        private bool _disposing;
        private Task? _disposeTask;

        public PinsFileService(string? dataDirectory = null)
        {
            if (dataDirectory is null)
            {
                string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrWhiteSpace(root))
                    throw new InvalidOperationException("The local application data directory is unavailable.");
                dataDirectory = Path.Combine(root, PinsStorageContract.DirectoryName);
            }
            _directory = Path.GetFullPath(dataDirectory);
            _path = Path.Combine(_directory, PinsStorageContract.FileName);
        }

        public Task<PinsLoadResult> LoadAsync(CancellationToken cancellationToken) =>
            RunAsync(LoadCore, cancellationToken);

        public Task<PinsSaveResult> SaveAsync(PinsData data, string? expectedRevision,
            CancellationToken cancellationToken) =>
            RunAsync(token => SaveCore(data, expectedRevision, token), cancellationToken);

        private async Task<T> RunAsync<T>(Func<CancellationToken, T> work, CancellationToken cancellationToken)
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
                    // Flush-to-disk and atomic file APIs are synchronous: never execute them on the dispatcher.
                    return await Task.Run(() => work(linked.Token), CancellationToken.None).ConfigureAwait(false);
                }
                finally { _gate.Release(); }
            }
        }

        private PinsLoadResult LoadCore(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            _writable = false;
            string? leaseMessage = AcquireLease();
            try
            {
                byte[]? bytes = ReadCurrent(token);
                string? revision = Revision(bytes);
                if (bytes is null)
                {
                    _loadedData = new PinsData(PinsData.CurrentSchemaVersion, ImmutableArray<PinnedApp>.Empty);
                    _loadedRevision = null;
                    _writable = _lease is not null;
                    return new(PinsLoadStatus.Missing, _loadedData, null, Access, leaseMessage);
                }
                if (!TryParse(bytes, out var data, out var failure))
                {
                    _loadedData = null;
                    _loadedRevision = revision;
                    return new(failure, null, revision, PinsWriteAccess.ReadOnly,
                        failure == PinsLoadStatus.UnsupportedVersion
                            ? "This pin file uses an unsupported version. Keep the original and use a compatible version."
                            : "The pin file needs recovery. Keep the original, repair or restore it, then reload pins.");
                }
                _loadedData = data;
                _loadedRevision = revision;
                _writable = _lease is not null;
                return new(PinsLoadStatus.Loaded, data, revision, Access, leaseMessage);
            }
            catch (UnauthorizedAccessException)
            {
                return LoadFailure(PinsLoadStatus.AccessDenied, "The pin file cannot be read. Check access and reload pins.");
            }
            catch (IOException)
            {
                return LoadFailure(PinsLoadStatus.Failed, "The pin file is unavailable or locked. Close other editors and reload pins.");
            }
        }

        private PinsWriteAccess Access => _writable ? PinsWriteAccess.Writable : PinsWriteAccess.ReadOnly;

        private string? AcquireLease()
        {
            if (_lease is not null) return null;
            try
            {
                Directory.CreateDirectory(_directory);
                // Never delete the coordination file: its open handle, not its existence, is the lease.
                _lease = new FileStream(Path.Combine(_directory, PinsStorageContract.WriterLeaseFileName),
                    FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return null;
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                return "Pins are read-only. Close another IslandTaskbar instance or check folder access, then reload pins.";
            }
        }

        private PinsLoadResult LoadFailure(PinsLoadStatus status, string message)
        {
            _loadedData = null;
            _loadedRevision = null;
            _writable = false;
            return new(status, null, null, PinsWriteAccess.ReadOnly, message);
        }

        private PinsSaveResult SaveCore(PinsData data, string? expectedRevision, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!_writable || _lease is null || _loadedData is null)
                return new(PinsSaveStatus.ReadOnly, null, "Pins are read-only. Resolve the storage issue and reload pins.");
            if (!ValidData(data))
                return new(PinsSaveStatus.Failed, null, "The proposed pin data is invalid. Reload pins before retrying.");
            string? temporary = null;
            try
            {
                byte[]? current = ReadCurrent(token);
                if (expectedRevision != _loadedRevision || expectedRevision != Revision(current))
                    return Conflict();
                if (data.Pins.SequenceEqual(_loadedData.Pins))
                    return new(PinsSaveStatus.Unchanged, _loadedRevision);

                byte[] bytes = Serialize(data);
                string revision = Revision(bytes)!;
                temporary = Path.Combine(_directory, PinsStorageContract.FileName + "." + Guid.NewGuid().ToString("N") + ".tmp");
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    for (int offset = 0; offset < bytes.Length; offset += 81920)
                    {
                        token.ThrowIfCancellationRequested();
                        output.Write(bytes, offset, Math.Min(81920, bytes.Length - offset));
                    }
                    output.Flush(flushToDisk: true);
                }
                if (expectedRevision != Revision(ReadCurrent(token))) return Conflict();
                token.ThrowIfCancellationRequested();

                // No cancellation point after the atomic operation: a successful commit is never "undone".
                if (current is null) File.Move(temporary, _path, overwrite: false);
                else File.Replace(temporary, _path, destinationBackupFileName: null);
                _loadedData = data;
                _loadedRevision = revision;
                return new(PinsSaveStatus.Saved, revision);
            }
            catch (UnauthorizedAccessException)
            {
                _writable = false;
                return new(PinsSaveStatus.AccessDenied, null, "Pins could not be saved. Check folder/file access and reload pins.");
            }
            catch (IOException)
            {
                // An external creation/deletion can race even the final revision read. Never overwrite it.
                try
                {
                    if (expectedRevision != Revision(ReadCurrent(CancellationToken.None))) return Conflict();
                }
                catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
                {
                    Debug.WriteLine("Pin revision could not be rechecked after a failed commit.");
                }
                _writable = false;
                return new(PinsSaveStatus.Failed, null, "Pins could not be saved. Close file editors, check storage and reload pins.");
            }
            finally
            {
                if (temporary is not null)
                {
                    try { File.Delete(temporary); }
                    catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
                    {
                        // Cleanup must not turn a committed save into a reported failure.
                        Debug.WriteLine("An owned pin temporary file could not be removed.");
                    }
                }
            }
        }

        private PinsSaveResult Conflict()
        {
            _writable = false;
            return new(PinsSaveStatus.Conflict, null, "Pins changed outside this snapshot. Reload pins before making changes.");
        }

        private byte[]? ReadCurrent(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                // Atomic replacement remains possible while readers finish an older committed snapshot.
                using var input = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                using var bytes = new MemoryStream();
                var buffer = new byte[81920];
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) != 0)
                {
                    token.ThrowIfCancellationRequested();
                    bytes.Write(buffer, 0, read);
                }
                token.ThrowIfCancellationRequested();
                return bytes.ToArray();
            }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
        }

        private static string? Revision(byte[]? bytes) => bytes is null
            ? null : Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        private static bool HasProperties(JsonElement element, string[] properties)
        {
            if (element.ValueKind != JsonValueKind.Object) return false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
                if (!properties.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name)) return false;
            return seen.Count == properties.Length;
        }

        private static bool TryParse(byte[] bytes, out PinsData? data, out PinsLoadStatus failure)
        {
            data = null;
            failure = PinsLoadStatus.Corrupt;
            try
            {
                using var json = JsonDocument.Parse(bytes);
                var root = json.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return false;
                var versions = root.EnumerateObject().Where(property => property.NameEquals("schemaVersion")).ToArray();
                if (versions.Length != 1 || versions[0].Value.ValueKind != JsonValueKind.Number ||
                    !versions[0].Value.TryGetInt32(out int version)) return false;
                if (version != PinsData.CurrentSchemaVersion)
                {
                    failure = PinsLoadStatus.UnsupportedVersion;
                    return false;
                }
                if (!HasProperties(root, RootProperties) || root.GetProperty("pins").ValueKind != JsonValueKind.Array)
                    return false;
                var pins = ImmutableArray.CreateBuilder<PinnedApp>();
                foreach (var item in root.GetProperty("pins").EnumerateArray())
                {
                    if (!HasProperties(item, PinProperties) ||
                        item.GetProperty("id").ValueKind != JsonValueKind.String ||
                        !Guid.TryParse(item.GetProperty("id").GetString(), out var id) ||
                        item.GetProperty("displayName").ValueKind != JsonValueKind.String ||
                        item.GetProperty("launchReference").ValueKind != JsonValueKind.String ||
                        !TryParseReference(item.GetProperty("launchReference").GetString(), out var reference) ||
                        item.GetProperty("order").ValueKind != JsonValueKind.Number ||
                        !item.GetProperty("order").TryGetInt32(out int order)) return false;
                    var icon = item.GetProperty("iconSource");
                    if (icon.ValueKind is not (JsonValueKind.Null or JsonValueKind.String)) return false;
                    pins.Add(new PinnedApp(new PinId(id), item.GetProperty("displayName").GetString()!,
                        reference, icon.GetString(), order));
                }
                var candidate = new PinsData(version, pins.ToImmutable());
                if (!ValidData(candidate)) return false;
                data = candidate;
                return true;
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException)
            {
                return false;
            }
        }

        private static bool ValidData(PinsData? data)
        {
            if (data is null || data.SchemaVersion != PinsData.CurrentSchemaVersion || data.Pins.IsDefault) return false;
            var ids = new HashSet<PinId>();
            var references = new HashSet<LaunchReference>();
            for (int order = 0; order < data.Pins.Length; order++)
            {
                var pin = data.Pins[order];
                if (pin is null || pin.Id.Value == Guid.Empty || !ids.Add(pin.Id) ||
                    !PinDataValidation.ValidText(pin.DisplayName) || pin.Order != order ||
                    (pin.IconSource is not null && !PinDataValidation.ValidText(pin.IconSource)) ||
                    !PinDataValidation.ValidReference(pin.LaunchReference) || !references.Add(pin.LaunchReference)) return false;
            }
            return true;
        }

        private static bool TryParseReference(string? encoded, out LaunchReference reference)
        {
            reference = default;
            if (encoded is null) return false;
            int separator = encoded.IndexOf(':');
            if (separator < 0) return false;
            LaunchReferenceKind? kind = encoded[..separator] switch
            {
                "exe" => LaunchReferenceKind.Executable,
                "lnk" => LaunchReferenceKind.Shortcut,
                "packaged" => LaunchReferenceKind.PackagedApplication,
                _ => null
            };
            if (kind is null) return false;
            reference = new LaunchReference(kind.Value, encoded[(separator + 1)..]);
            return PinDataValidation.ValidReference(reference);
        }

        private static byte[] Serialize(PinsData data)
        {
            using var bytes = new MemoryStream();
            using (var json = new Utf8JsonWriter(bytes, new JsonWriterOptions { Indented = true }))
            {
                json.WriteStartObject();
                json.WriteNumber("schemaVersion", PinsData.CurrentSchemaVersion);
                json.WriteStartArray("pins");
                foreach (var pin in data.Pins)
                {
                    json.WriteStartObject();
                    json.WriteString("id", pin.Id.Value.ToString("D"));
                    json.WriteString("displayName", pin.DisplayName);
                    string tag = pin.LaunchReference.Kind switch
                    {
                        LaunchReferenceKind.Executable => "exe:",
                        LaunchReferenceKind.Shortcut => "lnk:",
                        _ => "packaged:"
                    };
                    json.WriteString("launchReference", tag + pin.LaunchReference.Value);
                    json.WriteString("iconSource", pin.IconSource);
                    json.WriteNumber("order", pin.Order);
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteEndObject();
            }
            return bytes.ToArray();
        }

        public ValueTask DisposeAsync()
        {
            lock (_lifetimeGate)
            {
                if (_disposeTask is not null) return new ValueTask(_disposeTask);
                _disposing = true;
                _disposeTask = DisposeCoreAsync();
                return new ValueTask(_disposeTask);
            }
        }

        private async Task DisposeCoreAsync()
        {
            _lifetime.Cancel();
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                _writable = false;
                _lease?.Dispose();
                _lease = null;
            }
            finally
            {
                _gate.Release();
                _lifetime.Dispose();
            }
        }
    }

    /// <summary>M1 structural checks only; no filesystem resolution, application guessing or native verification.</summary>
    internal static class PinDataValidation
    {
        private static readonly UTF8Encoding StrictUtf8 = new(false, true);

        internal static bool ValidText([NotNullWhen(true)] string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            try { _ = StrictUtf8.GetByteCount(value); return true; }
            catch (EncoderFallbackException) { return false; }
        }

        internal static bool ValidIdentity(ApplicationIdentity identity) => Enum.IsDefined(identity.Kind) &&
            (identity.Kind == ApplicationIdentityKind.ExecutablePath ? CanonicalPath(identity.Value) : OpaqueId(identity.Value));

        internal static bool ValidReference(LaunchReference reference)
        {
            if (!Enum.IsDefined(reference.Kind)) return false;
            if (reference.Kind == LaunchReferenceKind.PackagedApplication) return OpaqueId(reference.Value);
            return CanonicalPath(reference.Value) && string.Equals(Path.GetExtension(reference.Value),
                reference.Kind == LaunchReferenceKind.Executable ? ".exe" : ".lnk", StringComparison.OrdinalIgnoreCase);
        }

        private static bool OpaqueId(string? value) => ValidText(value) && !value.Any(char.IsControl);

        private static bool CanonicalPath(string? value)
        {
            if (!ValidText(value) || value.Any(char.IsControl) || value.StartsWith(@"\\.\", StringComparison.Ordinal))
                return false;
            try
            {
                // Validate DOS/UNC syntax also for extended paths; never accept arbitrary device namespaces.
                string path = value.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) ? @"\\" + value[8..]
                    : value.StartsWith(@"\\?\", StringComparison.Ordinal) ? value[4..] : value;
                string components = path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':' ? path[2..] : path;
                return components.IndexOfAny(new[] { '<', '>', '|', ':', '"', '*', '?' }) < 0 &&
                    Path.IsPathFullyQualified(path) && Path.GetFileName(path).Length != 0 &&
                    string.Equals(Path.GetFullPath(path), path, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return false;
            }
        }
    }
}

