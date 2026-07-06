# HMoniker

Change your character's **display name** on its nameplate. Build a name from five
free-text slots (prefix, first, middle, last, suffix) and optionally hide the free
company tag.

Local only: the name you set is visible **only to yourself**, outside of HMS
(HMapSync) sessions. Inside an HMS session, the courier can carry your chosen name
to other clients in that session.

Open with `/hmoniker`, then:

- **Add current character** captures the character you're logged in as. First and
  last are seeded from your real name; every slot is free text and may be left
  blank.
- **Prefix / First / Middle / Last / Suffix** compose the nameplate name. Empty
  slots are skipped.
- **Hide FC tag** removes the free company tag from your nameplate.
- **Apply** commits your edits. **Reset** returns the slots to your real name.

## Installing

### From the custom repo (recommended)

1. In game, open `/xlsettings` → **Experimental** → **Custom Plugin
   Repositories**.
2. Paste this URL into a new row, click the **+**, then **Save**:

   ```
   https://raw.githubusercontent.com/Enceladeum/HMoniker/main/repo.json
   ```
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
Hide FC tag is on).

**Local authority.** Your own nameplate is always driven by your local config,
never by an incoming IPC assignment, so a courier cannot stomp your own name (even
through a reused object index). Per-character configs are matched by name plus home
world.

**Sync surface.** `IpcProvider` exposes a small Dalamud IPC API so a courier such
as HMS (HMapSync) can read your chosen name and apply a peer's name on your client.
For backward compatibility with that contract, the IPC namespace string stays
`Moniker` even though the plugin is named HMoniker.

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

- `Plugin.cs`: entry point, DI, window system, `/hmoniker` command, active-name resolution.
- `NameplateService.cs`: the `INamePlateGui` hook that rewrites nameplates.
- `ConfigWindow.cs`: the slot editor UI.
- `MonikerCharacterConfig.cs`: one character's name slots plus `Compose()`.
- `Configuration.cs`: persisted settings and per-character lookup.
- `IpcProvider.cs`: the Dalamud IPC surface HMS uses to carry names.
- `HMoniker.csproj`: project and manifest metadata (no separate `.json`).

## License

MIT. See [LICENSE](LICENSE). You may use, modify, and redistribute this freely,
including in closed-source projects.
