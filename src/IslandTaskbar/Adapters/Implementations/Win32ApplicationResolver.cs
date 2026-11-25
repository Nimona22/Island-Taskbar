using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using IslandTaskbar.Adapters.Interfaces;
using IslandTaskbar.Models;
using Windows.ApplicationModel;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Com.StructuredStorage;
using Windows.Win32.System.Variant;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.PropertiesSystem;

namespace IslandTaskbar.Adapters.Implementations
{
    /// <summary>Resolves explicitly supplied targets/windows; never enumerates, launches or activates (D-014).</summary>
    internal sealed class Win32ApplicationResolver : IApplicationResolver
    {
        private readonly ApplicationShellWorker _worker = new();
        internal static readonly Guid ShellLinkClass = new("00021401-0000-0000-C000-000000000046");
        private static readonly PROPERTYKEY AppIdKey = new()
        { fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), pid = 5 };
        internal sealed record DispatchPlan(LaunchReference Target, string Arguments, string WorkingDirectory, ProcessWindowStyle WindowStyle);

        public Win32ApplicationResolver() { }

        public Task<ApplicationResolutionResult> ResolveAsync(LaunchReference source, CancellationToken cancellationToken)
            => _worker.RunAsync(token => ResolveOnApartment(source, token, loadIcon: true), cancellationToken);

        public Task<ApplicationWindowIdentityResult> IdentifyWindowAsync(RunningWindowId runningWindowId, CancellationToken cancellationToken)
            => _worker.RunAsync(token => IdentifyOnApartment(runningWindowId, token), cancellationToken);

        internal static ApplicationResolutionResult ResolveOnApartment(LaunchReference source, CancellationToken token, bool loadIcon)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (!ValidSource(source)) return new(ApplicationResolutionStatus.Unsupported, null, "Choose a local executable, shortcut or supported packaged application.");
                if (source.Kind == LaunchReferenceKind.PackagedApplication)
                {
                    var app = VerifiedPackage(source.Value);
                    token.ThrowIfCancellationRequested();
                    return new(ApplicationResolutionStatus.Available,
                        new(source, app.DisplayInfo.DisplayName, new(ApplicationIdentityKind.AppUserModelId, source.Value), null));
                }
                string path = CanonicalFile(source.Value);
                ApplicationIdentity? identity;
                string name;
                if (source.Kind == LaunchReferenceKind.Executable)
                {
                    ValidateExecutable(path);
                    identity = new(ApplicationIdentityKind.ExecutablePath, path);
                    name = FileVersionInfo.GetVersionInfo(path).FileDescription ?? string.Empty;
                }
                else
                {
                    identity = ReadShortcut(path, out name, out _);
                }
                token.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileNameWithoutExtension(path);
                ApplicationIcon? icon = null;
                if (loadIcon)
                {
                    try { icon = ApplicationIconLoader.Load(path); }
                    catch (Exception failure) when (IsExpectedFailure(failure))
                    { Debug.WriteLine($"Optional application icon unavailable: {failure.GetType().Name}."); }
                }
                token.ThrowIfCancellationRequested();
                return new(ApplicationResolutionStatus.Available, new(new(source.Kind, path), name, identity, icon));
            }
            catch (Exception failure) when (IsExpectedFailure(failure)) { return ResolutionFailure(failure); }
        }

        internal static bool ValidSource(LaunchReference source)
        {
            if (!Enum.IsDefined(source.Kind) || string.IsNullOrWhiteSpace(source.Value) || source.Value.Length > 32767) return false;
            foreach (char value in source.Value) if (char.IsControl(value)) return false;
            if (source.Kind == LaunchReferenceKind.PackagedApplication) return true;
            // Local files only. Reject UNC/device namespaces, relative paths and alternate data streams.
            if (source.Value.Length < 4 || !char.IsAsciiLetter(source.Value[0]) || source.Value[1] != ':' ||
                source.Value[2] != '\\' || source.Value.AsSpan(2).Contains(':') || !Path.IsPathFullyQualified(source.Value)) return false;
            return string.Equals(Path.GetExtension(source.Value), source.Kind == LaunchReferenceKind.Executable ? ".exe" : ".lnk", StringComparison.OrdinalIgnoreCase);
        }

        internal static string CanonicalFile(string path)
        {
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            char[] buffer = new char[32768];
            uint length = PInvoke.GetFinalPathNameByHandle(handle, buffer, 0);
            if (length == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (length >= buffer.Length) throw new NotSupportedException("Path exceeds supported length.");
            string result = new(buffer, 0, (int)length);
            if (result.StartsWith(@"\\?\", StringComparison.Ordinal)) result = result.Substring(4);
            if (result.StartsWith(@"UNC\", StringComparison.OrdinalIgnoreCase)) throw new NotSupportedException("Network application targets are not supported.");
            return Path.GetFullPath(result);
        }

        private static void ValidateExecutable(string path)
        {
            if (!string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("The target is not an executable.");
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var image = new PEReader(input);
            if (image.PEHeaders.PEHeader is null || (image.PEHeaders.CoffHeader.Characteristics & Characteristics.Dll) != 0 ||
                (image.PEHeaders.CoffHeader.Characteristics & Characteristics.ExecutableImage) == 0)
                throw new NotSupportedException("The selected file is not a supported application image.");
            ValidateManifest(image);
        }

        private static void ValidateManifest(PEReader image)
        {
            var resource = image.PEHeaders.PEHeader!.ResourceTableDirectory;
            if (resource.RelativeVirtualAddress == 0 || resource.Size == 0) return;
            ReadOnlySpan<byte> tree = image.GetSectionData(resource.RelativeVirtualAddress).GetContent().AsSpan();
            foreach (uint type in ResourceEntries(tree, 0, 24)) // RT_MANIFEST
            foreach (uint name in ResourceEntries(tree, checked((int)(type & 0x7FFFFFFF)), 1)) // CREATEPROCESS_MANIFEST_RESOURCE_ID
            foreach (uint language in ResourceEntries(tree, checked((int)(name & 0x7FFFFFFF)), null))
            {
                if ((language & 0x80000000) != 0) throw new BadImageFormatException("Invalid manifest resource tree.");
                int offset = checked((int)language);
                int rva = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(tree.Slice(offset, 4)));
                int size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(tree.Slice(offset + 4, 4)));
                if (size <= 0 || size > 1024 * 1024) throw new BadImageFormatException("Invalid manifest resource size.");
                using var content = new MemoryStream(image.GetSectionData(rva).GetContent(0, size).AsSpan().ToArray());
                using var xml = XmlReader.Create(content, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
                while (xml.Read())
                {
                    if (xml.NodeType != XmlNodeType.Element || xml.LocalName != "requestedExecutionLevel") continue;
                    string? level = xml.GetAttribute("level");
                    if (level is "requireAdministrator" or "highestAvailable")
                        throw new UnauthorizedAccessException("Applications requesting elevation are not supported.");
                }
            }
        }

        private static List<uint> ResourceEntries(ReadOnlySpan<byte> tree, int directory, uint? id)
        {
            int count = BinaryPrimitives.ReadUInt16LittleEndian(tree.Slice(directory + 12, 2)) +
                BinaryPrimitives.ReadUInt16LittleEndian(tree.Slice(directory + 14, 2));
            if (count > 4096) throw new BadImageFormatException("Invalid manifest directory size.");
            var result = new List<uint>();
            for (int index = 0; index < count; index++)
            {
                int entry = checked(directory + 16 + index * 8);
                uint key = BinaryPrimitives.ReadUInt32LittleEndian(tree.Slice(entry, 4));
                if (id is not null && key != id.Value) continue;
                uint value = BinaryPrimitives.ReadUInt32LittleEndian(tree.Slice(entry + 4, 4));
                if (id is not null && (value & 0x80000000) == 0) throw new BadImageFormatException("Invalid manifest directory entry.");
                result.Add(value);
            }
            return result;
        }

        internal static AppInfo VerifiedPackage(string appId)
        {
            var app = AppInfo.GetFromAppUserModelId(appId);
            if (app is null || !string.Equals(app.AppUserModelId, appId, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(app.PackageFamilyName))
                throw new FileNotFoundException("The packaged application is no longer registered.");
            return app;
        }

        private static unsafe ApplicationIdentity? ReadShortcut(string path, out string name, out DispatchPlan plan)
        {
            IShellLinkW* link = null;
            IPersistFile* file = null;
            IShellLinkDataList* data = null;
            IPropertyStore* properties = null;
            try
            {
                PInvoke.CoCreateInstance(ShellLinkClass, null, CLSCTX.CLSCTX_INPROC_SERVER, out link).ThrowOnFailure();
                link->QueryInterface(out file).ThrowOnFailure();
                fixed (char* source = path) file->Load(source, STGM.STGM_READ);
                link->QueryInterface(out data).ThrowOnFailure();
                uint flags = 0;
                data->GetFlags(&flags);
                if ((flags & 0x2000) != 0) throw new UnauthorizedAccessException("Elevated shortcuts are not supported.");
                if ((flags & (0x1000 | 0x20000)) != 0) throw new NotSupportedException("Installer-managed and compatibility-shim shortcuts are not supported.");
                string? appId = null;
                if (link->QueryInterface(out properties).Value >= 0) appId = ReadAppId(properties);
                char[] buffer = new char[32768];
                fixed (char* text = buffer) link->GetDescription(text, buffer.Length);
                name = ReadBuffer(buffer);
                if (appId is not null)
                {
                    // An explicit shortcut AUMID is trustworthy for presence, but not necessarily a package ID.
                    // File-backed ordinary shortcuts are still validated below.
                    Array.Clear(buffer);
                }
                fixed (char* text = buffer)
                {
                    Array.Clear(buffer);
                    link->GetPath(text, buffer.Length, null, 4).ThrowOnFailure(); // SLGP_RAWPATH: never track/repair/save a link.
                }
                string target = Environment.ExpandEnvironmentVariables(ReadBuffer(buffer));
                if (string.IsNullOrWhiteSpace(target))
                {
                    if (appId is null) throw new NotSupportedException("This shortcut has no supported local or packaged target.");
                    VerifiedPackage(appId);
                    plan = new(new(LaunchReferenceKind.PackagedApplication, appId), string.Empty, string.Empty, ProcessWindowStyle.Normal);
                }
                else
                {
                    if (!ValidSource(new(LaunchReferenceKind.Executable, target))) throw new NotSupportedException("The shortcut target is not a supported local executable.");
                    string executable = CanonicalFile(target);
                    ValidateExecutable(executable);
                    Array.Clear(buffer);
                    fixed (char* text = buffer) link->GetArguments(text, buffer.Length);
                    string arguments = ReadBuffer(buffer);
                    Array.Clear(buffer);
                    fixed (char* text = buffer) link->GetWorkingDirectory(text, buffer.Length);
                    string directory = Environment.ExpandEnvironmentVariables(ReadBuffer(buffer));
                    if (directory.Length != 0 && (!Path.IsPathFullyQualified(directory) || directory.StartsWith(@"\\", StringComparison.Ordinal)))
                        throw new NotSupportedException("The shortcut working directory is not a supported local directory.");
                    link->GetShowCmd(out var showCommand);
                    int show = (int)showCommand;
                    plan = new(new(LaunchReferenceKind.Executable, executable), arguments, directory,
                        show == 3 ? ProcessWindowStyle.Maximized : show is 2 or 6 or 7 ? ProcessWindowStyle.Minimized : ProcessWindowStyle.Normal);
                }
                // Never flatten a shortcut to its executable: arguments/profiles remain distinct intents.
                return appId is null ? null : new(ApplicationIdentityKind.AppUserModelId, appId);
            }
            finally
            {
                if (properties != null) properties->Release();
                if (data != null) data->Release();
                if (file != null) file->Release();
                if (link != null) link->Release();
            }
        }

        internal static DispatchPlan PrepareDispatch(LaunchReference source)
        {
            if (source.Kind == LaunchReferenceKind.Shortcut)
            {
                ReadShortcut(CanonicalFile(source.Value), out _, out var plan);
                return plan;
            }
            return new(source, string.Empty, string.Empty, ProcessWindowStyle.Normal);
        }

        private static string ReadBuffer(char[] buffer)
        {
            int length = Array.IndexOf(buffer, '\0');
            if (length < 0) throw new NotSupportedException("Shell metadata exceeds the supported length.");
            return new string(buffer, 0, length);
        }

        private static unsafe string? ReadAppId(IPropertyStore* properties)
        {
            PROPERTYKEY key = AppIdKey;
            PROPVARIANT value = default;
            try
            {
                properties->GetValue(&key, &value);
                string? result = value.vt == VARENUM.VT_LPWSTR && value.pwszVal.Value != null ? value.pwszVal.ToString() : null;
                return string.IsNullOrWhiteSpace(result) ? null : result;
            }
            finally { PInvoke.PropVariantClear(ref value).ThrowOnFailure(); }
        }

        private static unsafe string? WindowAppId(nint handle)
        {
            Guid iid = typeof(IPropertyStore).GUID;
            IPropertyStore* store = null;
            int result = PInvoke.SHGetPropertyStoreForWindow((HWND)handle, &iid, (void**)&store).Value;
            if (result < 0) return null;
            try { return ReadAppId(store); }
            finally { if (store != null) store->Release(); }
        }

        private static unsafe bool MatchesWindow(RunningWindowId id)
        {
            if (id.Handle == 0 || id.ProcessId == 0 || id.ThreadId == 0 || id.Instance <= 0 || !PInvoke.IsWindow((HWND)id.Handle)) return false;
            uint pid = 0;
            uint tid = PInvoke.GetWindowThreadProcessId((HWND)id.Handle, &pid);
            return pid == id.ProcessId && tid == id.ThreadId;
        }

        private static ApplicationWindowIdentityResult IdentifyOnApartment(RunningWindowId id, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!MatchesWindow(id)) return MissingWindow();
            try
            {
                using var process = Process.GetProcessById(checked((int)id.ProcessId));
                string? appId = WindowAppId(id.Handle);
                char[] buffer = new char[32768];
                uint length = (uint)buffer.Length;
                uint appResult = (uint)PInvoke.GetApplicationUserModelId(process.SafeHandle, ref length, buffer);
                if (appId is null && appResult == 0) appId = ReadBuffer(buffer);
                ApplicationIdentity? identity = null;
                LaunchReference? relaunch = null;
                length = (uint)buffer.Length;
                uint packageResult = (uint)PInvoke.GetPackageFullName(process.SafeHandle, ref length, buffer);
                if (!string.IsNullOrWhiteSpace(appId))
                {
                    identity = new(ApplicationIdentityKind.AppUserModelId, appId);
                    try { VerifiedPackage(appId); relaunch = new(LaunchReferenceKind.PackagedApplication, appId); }
                    catch (Exception failure) when (IsExpectedFailure(failure)) { /* A desktop AUMID need not be a package. */ }
                }
                string? executable = process.MainModule?.FileName;
                string basename = Path.GetFileName(executable ?? string.Empty);
                // Only APPMODEL_ERROR_NO_PACKAGE positively permits the ordinary desktop fallback.
                bool hosted = packageResult != 15700 || IsGenericHost(basename);
                if (!hosted && executable is not null)
                {
                    var resolved = ResolveOnApartment(new(LaunchReferenceKind.Executable, executable), token, false);
                    if (resolved.Status == ApplicationResolutionStatus.Available)
                    {
                        identity ??= resolved.Application!.Identity;
                        relaunch ??= resolved.Application!.LaunchReference;
                    }
                }
                token.ThrowIfCancellationRequested();
                if (process.HasExited || !MatchesWindow(id)) return MissingWindow();
                return identity is null
                    ? new(ApplicationResolutionStatus.Unsupported, null, null, "Reliable application identity is unavailable for this window.")
                    : new(ApplicationResolutionStatus.Available, identity, relaunch);
            }
            catch (ArgumentException) { return MissingWindow(); }
            catch (InvalidOperationException) { return MissingWindow(); }
            catch (Exception failure) when (IsExpectedFailure(failure))
            {
                if (!MatchesWindow(id)) return MissingWindow();
                var result = ResolutionFailure(failure);
                return new(result.Status, null, null, result.Message);
            }
        }

        private static ApplicationWindowIdentityResult MissingWindow()
            => new(ApplicationResolutionStatus.Missing, null, null, "The window is no longer available.");

        internal static bool IsExpectedFailure(Exception failure) => failure is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException or COMException or Win32Exception or BadImageFormatException or InvalidOperationException or XmlException or OverflowException;

        internal static bool IsGenericHost(string path)
        {
            string name = Path.GetFileName(path);
            return name.ToUpperInvariant() is "APPLICATIONFRAMEHOST.EXE" or "RUNTIMEBROKER.EXE" or "DLLHOST.EXE" or
                "RUNDLL32.EXE" or "SVCHOST.EXE" or "DOTNET.EXE" or "JAVA.EXE" or "JAVAW.EXE" or "PYTHON.EXE" or
                "PYTHONW.EXE" or "PY.EXE" or "NODE.EXE" or "DENO.EXE" or "BUN.EXE" or "POWERSHELL.EXE" or "PWSH.EXE" or
                "WSCRIPT.EXE" or "CSCRIPT.EXE" or "MSHTA.EXE" or "CMD.EXE" or "CONHOST.EXE" or "OPENCONSOLE.EXE";
        }

        internal static ApplicationResolutionResult ResolutionFailure(Exception failure)
        {
            int code = failure is Win32Exception native ? native.NativeErrorCode : failure.HResult & 0xFFFF;
            var status = failure is UnauthorizedAccessException || code is 5 or 740
                ? ApplicationResolutionStatus.AccessDenied
                : failure is FileNotFoundException or DirectoryNotFoundException || code is 2 or 3
                    ? ApplicationResolutionStatus.Missing
                    : failure is ArgumentException or NotSupportedException or BadImageFormatException or XmlException or OverflowException || code is 11 or 193
                        ? ApplicationResolutionStatus.Unsupported : ApplicationResolutionStatus.Failed;
            string message = status switch
            {
                ApplicationResolutionStatus.Missing => "The application target is missing or no longer registered.",
                ApplicationResolutionStatus.AccessDenied => "Access to this application target was denied.",
                ApplicationResolutionStatus.Unsupported => "This application target is not supported.",
                _ => "The application target could not be resolved."
            };
            Debug.WriteLine($"Application resolution: {failure.GetType().Name}, code {code}.");
            return new(status, null, message);
        }

        public ValueTask DisposeAsync() => _worker.DisposeAsync();
    }
}
