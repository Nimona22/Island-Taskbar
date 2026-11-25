# DATABASE_SCHEMA.md

**Project:** IslandTaskbar  
**Document status:** Draft v0.2  
**Last updated:** 2026-11-25

**Applies to:** Current product scope

---

## 0. AI Agent Rules (Read First)

> **These rules apply to every AI agent, coding assistant, or automated tool working on this project.**

### The agent MUST NOT:

- Create a database, SQL schema, migration files, or database infrastructure of any kind.
- Use SQLite, LiteDB, or any embedded database engine — even if it seems convenient.
- Use a remote database, cloud database, or any backend service.
- Describe or implement local JSON files as a "database" or use database terminology for them.
- Add a database because a future feature might need one — that decision requires approved requirements first.
- Store credentials, tokens, or sensitive personal data in any local file.
- Add telemetry or remote synchronization as an implicit side effect of any feature.

### The agent MUST:

- Use versioned JSON files for all local persistence — preferences, pins, layout settings.
- Store those files in the per-user application data directory, not beside installed binaries.
- Write files atomically: write to a temp file first, then replace the target file.
- Validate loaded values and provide safe in-memory defaults. For Phase 5.2 pins, only a missing file is empty writable data; corrupt/unsupported data is preserved in read-only recovery (D-014).
- Include a schema version number in every settings file from day one.

---

## 1. Purpose

This document records the project's database decision and prevents the introduction of unnecessary database infrastructure.

---

## 2. Current Decision: No Database

IslandTaskbar is a local-first Windows desktop application. Its current requirements do not need a relational database, remote database, backend service, cloud account, or internet connection.

**Therefore, there is no database schema for the current release.** Do not create SQL tables, migrations, database credentials, or database infrastructure unless an approved product requirement changes this decision.

---

## 3. Local Persistence Is Not a Database

The application may need to remember user preferences and pinned applications. The current architecture uses versioned JSON files in the application's per-user local data directory for this purpose.

Expected local data includes:

- User preferences, such as appearance and startup behavior.
- Pinned application metadata and ordering.
- User-selected layout or display preferences.
- A configuration schema version for future migrations.

These files are local application configuration. They are not a database, and they must not be described or implemented as one.

---

## 4. JSON File Structure (Starting Point)

> Settings/recovery remain starting points. The Phase 5.2 pins format in §4.1 is frozen at version 1 by D-014 and the approved architecture contract. No additional persistent data file is authorized; the lock/temp artifacts below are coordination/commit artifacts, not additional settings.

### `settings.json`
```json
{
  "schemaVersion": 1,
  "startup": {
    "launchOnLogin": false,
    "minimizeOnClose": false
  },
  "appearance": {
    "theme": "dark",
    "accentColor": null
  },
  "taskbar": {
    "hideNativeOnLaunch": true,
    "restoreNativeOnExit": true
  }
}
```

### `pins.json`
```json
{
  "schemaVersion": 1,
  "pins": [
    {
      "id": "b6a934f2-625e-4b7a-b568-2cfe05d39b92",
      "displayName": "App Name",
      "launchReference": "exe:C:\\Program Files\\Example\\Example.exe",
      "iconSource": null,
      "order": 0
    }
  ]
}
```

### 4.1 Frozen Phase 5.2 pins contract (D-014; OD-006 pins scope resolved)

**Runtime path:** `%LOCALAPPDATA%\IslandTaskbar\pins.json`. Resolve the per-user root using `Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)` and combine the relative names from `Persistence/PinsStorageContract.cs`. Never expand an arbitrary pin string into a command or store configuration beside binaries. Test-only constructors accept an isolated directory; production uses the default path.

**Wire mapping:** root `schemaVersion` is integer 1; `pins` is an array. Each record has `id` (nonempty GUID string), `displayName` (nonempty string), `launchReference` (tagged string), `iconSource` (null initially; optional nonempty string metadata, never authoritative identity), and `order` (nonnegative integer). Write GUIDs in D format. Array position and order must agree exactly, from zero without gaps. IDs and canonical launch references must be unique. `PinsData`/`PinnedApp` are domain records; M1 owns the explicit private JSON conversion so the wire is not the default object serialization of PinId/LaunchReference.

| Reference kind | Exact v1 wire value | Validation / meaning |
|---|---|---|
| Executable | `exe:<absolute path>` | Canonical absolute Windows file path with `.exe` extension. No relative path, arguments, URI or interpreter string. |
| Shortcut | `lnk:<absolute path>` | Canonical absolute `.lnk` file path. Preserve the shortcut as launch intent; do not replace it with its target EXE or lose its arguments/working directory. |
| PackagedApplication | `packaged:<verified AppUserModelID>` | Nonempty opaque identity; M2 must verify supported application activation before dispatch. No arbitrary shell identifier. |

Tags are the exact lower-case values above. Split at the first colon only; Windows drive colons remain in the value. Parse into `LaunchReferenceKind` and Value; never execute an unrecognized/free-form value. File existence/availability is a runtime check: a structurally valid reference whose target was removed is still a saved pin. Runtime resolution provides verified availability, metadata and optional reliable application identity. New pins write `iconSource: null`; runtime icon pixels and derived application identities never enter JSON.

No version bump or migration is introduced in M0. Versions other than 1 are unsupported and read-only, including newer versions. Invalid JSON, duplicate properties, missing/invalid required values, duplicate IDs/references or inconsistent ordering are read-only recovery, not an invitation to drop records or repair the user's file. Unrecognized v1 properties must not be silently lost on save; the initial implementation treats input it cannot round-trip losslessly as read-only recovery. The whole original file remains intact. The UI can show safe empty pins on first failure while Open windows/Music remain usable; it must not save that fallback over the original.

**Load/result rules:**

- `Loaded`: non-null validated Data, non-null raw-byte Revision. WriteAccess is Writable only while holding the writer lease; another instance can read valid data as ReadOnly.
- `Missing`: non-null empty v1 Data, null Revision. Writable still requires the lease. Do not create `pins.json` merely by starting/reading.
- `Corrupt`, `UnsupportedVersion`, `AccessDenied` or `Failed`: Data is null, WriteAccess is ReadOnly and Message explains recovery. A readable rejected file may have a revision token, but that never permits saving it.
- Explicit `LoadAsync` retries lease acquisition and reloads under the lease before enabling writes. Do not acquire a lease later and write an earlier read-only snapshot. No automatic corrupt-file reset, downgrade, migration or deletion.

**Writer lease:** M1 holds an exclusive handle to the empty same-directory `pins.json.lock` file (`FileShare.None`) for the writable store lifetime. A competing instance may read pins but cannot write. The coordination file may remain empty after handle release; do not unlink/recreate it while another handle might hold the lease. It contains no schema or user state. Asynchronous disposal waits for any owned commit to finish before releasing the handle. This is a file-handle lease, not an async thread-affine mutex and not another Windows adapter.

**Revision:** SHA-256 of the exact committed/read file bytes, represented as lowercase hexadecimal; null means expected absence, never “skip checking.” No revision field is added to JSON. Save compares expectedRevision against current bytes while holding the lease immediately before commit. Mismatch (including unexpected creation/deletion) returns Conflict and requires explicit reload before later mutations. A successful first creation must not replace a file that appeared meanwhile. The lease/revision protocol coordinates cooperating IslandTaskbar instances; it is not a filesystem-wide transaction against arbitrary external editors.

**Commit:** serialize/validate candidate v1 data off the UI thread, write a uniquely named `pins.json.<guid>.tmp` in the same directory, flush to disk, compare revision, then atomically replace the existing target or perform non-overwriting first creation. Return Saved with the new revision only after commit. Unchanged returns the existing revision without rewriting (possibly null for an absent empty document). ReadOnly/Conflict/AccessDenied/Failed return no committed revision; retain the prior committed in-memory pins/order and surface an actionable error. Temporary-file cleanup is best-effort and never changes a known committed result into failure. Orphaned temp files are never preferred over the committed file automatically.

**Cancellation:** cancel before commit without publication and preserve existing data. After atomic replacement succeeds, return Saved and publish that committed result despite a late canceled token; never report a committed save as canceled. Expected I/O/validation failures are typed results. PinManagerService serializes mutations, passes the last committed revision and publishes only Saved/Unchanged outcomes. Duplicate PinAsync is AlreadyPinned and does not write; no-op moves do not write. Runtime availability/window events never call SaveAsync.

**Never persist:** HWND, PID/thread IDs, RunningWindowId, titles, presence/counts, availability, busy state, pixels, application identity, credentials or telemetry. Pin ID is persistent; application/window identities are separate runtime concepts.

### `recovery.json` *(if recovery helper is validated as needed)*
```json
{
  "schemaVersion": 1,
  "mainProcessId": 12345,
  "taskbarState": "hidden",
  "timestamp": "2026-11-19T00:00:00Z"
}
```

---

## 5. Persistence Requirements

The local settings implementation must:

1. Store data in the appropriate per-user application data location, not beside installed binaries.
2. Use a documented, versioned JSON format.
3. Validate loaded values and provide safe in-memory defaults when unavailable. Pins follow §4.1: corrupt/unsupported originals remain read-only and are never overwritten by defaults.
4. Write changes safely, using a temporary file and replacement strategy to reduce corruption risk.
5. Handle unknown or obsolete settings without crashing the application.
6. Avoid storing passwords, access tokens, or unrelated sensitive information.
7. Keep core taskbar functionality available when optional settings are missing or reset.
8. Never store data in a format that requires a database engine to read.

---

## 6. When to Reconsider a Database

Revisit this decision only when an approved requirement genuinely needs capabilities that local files cannot reasonably provide, such as:

- Large structured datasets requiring reliable querying and relationships.
- Transactional updates across multiple related records.
- Multi-user or cross-device synchronization backed by a service.
- Searchable history or other substantial structured records.

A future feature request alone is not sufficient justification. First document the requirement, data ownership, privacy implications, offline behavior, backup/migration needs, and operational cost. Then update `PRD.md`, `SCOPE.md`, and `ARCHITECTURE.md` before choosing a database.

---

## 7. Security and Privacy

- Keep user configuration local by default.
- Do not add telemetry or remote synchronization as an implicit side effect.
- Do not store credentials in plain-text configuration files.
- Restrict configuration files to the current user's application data area and use normal Windows user-profile protections.

---

## 8. Related Documents

- `PRD.md` — product requirements and privacy principles.
- `SCOPE.md` — confirms that core functionality does not require a database or online service.
- `ARCHITECTURE.md` — defines local versioned JSON persistence.

---

## 9. Decision Summary

| Item | Decision |
|---|---|
| Relational database | Not required |
| Remote/cloud database | Not required |
| Backend service | Not required |
| SQLite or embedded DB engine | Not required — explicitly forbidden |
| SQL schema or migrations | None |
| Local preferences and pins | Versioned JSON files |
| Reconsideration | Only after an approved requirement demonstrates a real need |
