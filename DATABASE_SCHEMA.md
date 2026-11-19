# DATABASE_SCHEMA.md

**Project:** IslandTaskbar  
**Document status:** Draft v0.2  
**Last updated:** 2026-11-19  
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
- Validate loaded values and fall back to safe defaults when files are missing or corrupted.
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

> These are the expected JSON files. Exact field names and types will evolve — update this section when the schema is finalized. The agent must follow this structure and must not invent additional files.

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
      "id": "unique-guid",
      "displayName": "App Name",
      "launchReference": "path-or-identity",
      "iconSource": "resolved-icon-path-or-null",
      "order": 0
    }
  ]
}
```

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
3. Validate loaded values and provide safe defaults when files are missing or invalid.
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
