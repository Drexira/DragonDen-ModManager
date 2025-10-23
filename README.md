<h1 align="center"><em>Dragon Den Mod Manager</em></h1>

A mod manager for SPT mods built with Avalonia UI and .NET 9.  
It indexes mods from Forge, lets users search/filter, and installs or uninstalls versions that match their SPT server.  
The manager keeps a local cache for fast browsing and provides rich, non-blocking loading UX.

This readme is for developers who want to contribute.

---

## Features
* Avalonia 11 desktop app (Windows only at the moment)
* .NET 9.0
* Fast local cache of Forge data (SQLite)
* Full-text search, author queries (`@author`), category, SPT version, sort, pagination, and page size
* “loading” UX with lightweight overlays
* Mod install queue with 7-Zip extraction
* SPT compatibility awareness (major and full tag matching)
* Single-instance activation with “raise existing window” on subsequent launches
* Custom title bar and chrome

---

## Requirements
* .NET 9 SDK
* Windows 10/11
    * Primary target: Windows 10/11
* A valid Forge API token
* An SPT root folder (to detect SPT version and manage installs)

---

On first launch you’ll be prompted for:
* **Forge Token** (stored in app settings)
* **SPT Root** (folder containing SPT.Server.exe, used to detect version and manage installs)

If either is missing or invalid, first-run dialogs will guide you.

---

## Project Structure (important bits)
```
DragonDen.ModManager/
App.axaml / App.cs                      // App lifetime, DI-lite wiring, cache warmup, single-instance activation
Views/
  MainWindow.axaml / .cs
  BrowseModsPage.axaml / .cs            // Search UI, filters, paging; loading overlay; install/uninstall
  ChromeTitleBar.axaml / .cs            // Custom title bar and window chrome
  ...
Services/
  ForgeClient.cs                        // Forge API helpers (summary)
  CacheDb.cs (Storage & Services)       // Cache DB facade (summaries, categories, SPT tags)
  SevenZip.cs                           // 7z wrapper
  InstallQueue.cs                       // Download + extraction queue
  Toasts.cs                             // Simple toast system
  Paths.cs                              
  SemverUtil.cs
  Spt.cs                                // Detect server version
Storage/
  CacheDb.cs                            // SQLite or equivalent storage for cached Forge data
  Db.cs                                 // Installed mods DB
ViewModels/
  SearchResultRow.cs                    // Item model for the results list
Assets/
  icon.png
```

Notes:
* There are two “CacheDb” namespaces: a storage‐level one (`DragonDen.ModManager.Storage.CacheDb`) and a services-level cache (`DragonDen.ModManager.Services.CacheDb`). The manager wires the latter as `App.Cache` and the DB for installed mods as `App.Db`.

---

## How Things Work
### App startup
* `App.OnFrameworkInitializationCompleted()`:
    * Ensures cache and mod directories.
    * Loads `Config`.
    * Initializes `Db` (installed mods), `SevenZip`, `InstallQueue`, `Toasts`.
    * Initializes the services cache db (`App.Cache`) and kicks off a background warmup (`WarmCacheOnLaunch`).
    * Creates the `MainWindow`, subscribes to exit events, and sets up single-instance activation handling.

### Single instance
* **Program.cs** creates a named `Mutex`. If a second instance is launched, it signals a named `EventWaitHandle` and exits.
* **App.cs** owns and waits on the same `EventWaitHandle`. When signaled, it brings the existing window to the front, restoring from minimized if needed.

### Browse & search
* `BrowseModsPage` drives searches with:
    * Query text or `@author`
    * Category, SPT version tag (major or full), sort, page size, page nav
    * Hide toggles (featured/ads/AI)
* Debounced input and selection changes call `PerformSearch(resetPage)`; results are paged and displayed with a wrapping layout.
* A subtle top progress bar and a centered overlay show “loading” with cycling fun tips to avoid flash and layout jumps.

### Indexing / refreshing
* `Refresh` triggers `CacheDb.RefreshModsIncrementalAsync` using progress to update the modal. After refresh, filters are reloaded and a search is run.

### Install / uninstall

* Install:
    * Validates SPT compatibility (major vs full match depending on current filter).
    * Enqueues the download via `InstallQueue` with 7-Zip extraction.
* Uninstall:
    * Marks removal in `App.Db` and refreshes UI.

### SPT compatibility
* The app detects the SPT server version (AB or full tag) from `SPT.Server.exe` file version.
* Version displays are scored so the SPT-matching versions sort to the top.
* Latest mod version “badge” shows for items targeting the current latest SPT known in cache.

---

## Configuration & Paths
Settings are saved to an app-specific settings file (see `Paths.AppSettingsPath`). Key values:
* `Config.Forge.Token` – Forge API bearer token
* `Config.Paths.SptRoot` – SPT root folder
* `Config.UI.SearchSort`, `Config.UI.SearchPageSize` – last used UI prefs

On disk folders:
* `Paths.CacheDir` – cached Forge JSON and images, DBs
* `Paths.ModsDir` – local mod installations
* `Paths.CacheDbPath` – services cache database
* `Paths.ModsDbPath` – installed mods database
* `Paths.SevenZipPath` – 7-Zip binary used by `SevenZip`

---

## UI/UX patterns used
* Non-blocking operations with `Dispatcher.UIThread.Post` for UI updates.
* Debounced search input to limit DB hits.
* Subtle top progress bar during short tasks; modal only for full reindexing.
* Overlay with cycling tips for filter/sort re-queries to avoid content flash.

---

## Contributing

I welcome any and all contributions to this project!

---

## Disclaimer
This is my first time making a mod manager, things might not be up to various standards.