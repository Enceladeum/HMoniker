# HMoniker

Change your character's **display name** on its nameplate. Build a name from five
free-text slots (prefix, first, middle, last, suffix), optionally hide the free
company tag, or hide the nameplate name entirely.

Local only: the name you set is visible **only to yourself**, outside of HMS
(HMapSync) sessions. Inside an HMS session, the courier can carry your chosen name
to other clients in that session.

Open with `/hmoniker`. The window shows the character you're currently logged in as;
to set up another character, log in to it (its own settings load automatically).

- **Prefix / First / Middle / Last / Suffix** compose the nameplate name. Empty slots
  are skipped, and the fields wrap onto new lines as you narrow the window. First and
  last are seeded from your real name; every slot is free text and may be left blank.
- **Hide FC tag** removes the free company tag from your nameplate.
- **Hide name** blanks the nameplate name entirely. It overrides the slots above, so
  you can hide your name whether or not you've composed a custom one.
- **Apply** commits your edits. **Reset** returns the slots to your real name.

## Installing

### From the custom repo (recommended)

1. In game, open `/xlsettings` → **Experimental** → **Custom Plugin
   Repositories**.
2. Paste this URL into a new row, click the **+**, then **Save**:

   ```
   https://raw.githubusercontent.com/Enceladeum/DalamudPlugins/main/repo.json
   ```

   This one URL is The Enceladeum's shared feed: it lists **every** plugin by this
   author (and any added later), so there's nothing else to track down.
3. Open the plugin installer (`/xlplugins`), search for **HMoniker**, and click
   **Install**.

### Local dev build

1. Build it (see **Building**). The output folder ends up with `HMoniker.dll` and
   a generated `HMoniker.json` manifest.
2. In game, `/xlsettings` → **Experimental** → **Dev Plugin Locations**. Add the
   path to the built `HMoniker.dll` (or its folder), save, and hit the reload/scan
   button.
3. **HMoniker** appears in **Installed Dev Plugins**; enable it.

## How it works

**Nameplates.** `NameplateService` subscribes to Dalamud's `INamePlateGui`
`OnNamePlateUpdate`. For each player handler it composes the active name and, when
one is set, overwrites `handler.Name` (and clears `handler.FreeCompanyTag` when
Hide FC tag is on). Hide name blanks `handler.Name` outright and takes precedence
over any composed name.

**Per-character storage.** Each character reads and writes its **own file** under the
plugin config directory, keyed by name plus home world. Two game clients launched from
the same XIVLauncher folder no longer share one list or race on a single file, so names
never bleed between separate accounts. Your own nameplate is always driven by your local
config, never by an incoming IPC assignment, so a courier cannot stomp your own name
(even through a reused object index). A pre-1.3 shared list is split into per-character
files automatically on first load; the old file is left untouched as a backup.

**Sync surface.** `IpcProvider` exposes a small Dalamud IPC API so a courier such
as HMS (HMapSync) can read your chosen name and apply a peer's name on your client.
The carried payload includes the Hide FC tag and Hide name flags, so those choices
follow you to other clients in a session. Hide name is an additive field (IPC 2.2):
older peers ignore it, and payloads from older senders default it off. For backward
compatibility with that contract, the IPC namespace string stays `Moniker` even
though the plugin is named HMoniker.

## Building

```
dotnet build -c Release
```

Requires the Dalamud dev environment (the `Dalamud.NET.Sdk` resolves the game
references). There is no hand-written manifest `.json`: the SDK generates the
manifest at build time and stamps the API level from the SDK version. Manifest
metadata (`Name`, `Author`, `Punchline`, `Description`, `Tags`, `IconUrl`) lives in
the `.csproj` PropertyGroup. If you rename or change references and get odd load
behavior, delete `bin/` and `obj/` and rebuild.

Load `bin/Release/HMoniker.dll` as a dev plugin.

## Files

- `Plugin.cs`: entry point, DI, window system, `/hmoniker` command, tracks the logged-in character, active-name resolution.
- `NameplateService.cs`: the `INamePlateGui` hook that rewrites nameplates.
- `ConfigWindow.cs`: the single-character slot editor UI.
- `CharacterStore.cs`: per-character file load/save plus the one-time legacy migration.
- `MonikerCharacterConfig.cs`: one character's name slots plus `Compose()`.
- `Configuration.cs`: the legacy shared config, kept only as a read-only migration source.
- `IpcProvider.cs`: the Dalamud IPC surface HMS uses to carry names.
- `HMoniker.csproj`: project and manifest metadata (no separate `.json`).

## License

MIT. See [LICENSE](LICENSE). You may use, modify, and redistribute this freely,
including in closed-source projects.
