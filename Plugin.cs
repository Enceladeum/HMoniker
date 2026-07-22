namespace HMoniker;

using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

public sealed class Plugin : IDalamudPlugin
{
    private readonly IDalamudPluginInterface pi;
    private readonly ICommandManager commandManager;
    private readonly INamePlateGui namePlateGui;
    private readonly IFramework framework;

    public IObjectTable Objects { get; }
    public IPluginLog Log { get; }

    public IpcProvider? Ipc { get; private set; }

    private readonly CharacterStore store;
    private readonly NameplateService nameplateService;
    private readonly WindowSystem windowSystem = new("HMoniker");
    private readonly ConfigWindow configWindow;

    // The one character this client is logged in as: its config, its identity key, and an
    // epoch bumped whenever either is (re)assigned so the window knows to reload its draft.
    // Refreshed every frame from the local player; null when not logged in.
    private MonikerCharacterConfig? current;
    private string? currentKey;
    public int CurrentEpoch { get; private set; }

    public MonikerCharacterConfig? CurrentConfig => current;

    // External local-name override (e.g. HOutfits applying an NPC name). We snapshot the
    // user's own current config so a later ClearLocalName restores it rather than destroying it.
    private MonikerCharacterConfig? extNameBackup;
    private bool extNameActive;

    private const string CommandName = "/hmoniker";

    public Plugin(
        IDalamudPluginInterface pi,
        ICommandManager commandManager,
        IObjectTable objects,
        INamePlateGui namePlateGui,
        IFramework framework,
        IPluginLog log)
    {
        this.pi = pi;
        this.commandManager = commandManager;
        this.namePlateGui = namePlateGui;
        this.framework = framework;
        Objects = objects;
        Log = log;

        store = new CharacterStore(pi, log);

        // Split any pre-1.3 shared list into per-character files, once. Read-only on the
        // shared file: the legacy config is never written back (see CharacterStore).
        if (pi.GetPluginConfig() is Configuration legacy && legacy.Characters.Count > 0)
            store.MigrateLegacy(legacy.Characters);

        configWindow = new ConfigWindow(this);
        windowSystem.AddWindow(configWindow);

        nameplateService = new NameplateService(this, namePlateGui);
        Ipc = new IpcProvider(pi, this);

        commandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the HMoniker settings window.",
        });

        pi.UiBuilder.Draw += windowSystem.Draw;
        pi.UiBuilder.OpenConfigUi += ToggleWindow;
        pi.UiBuilder.OpenMainUi += ToggleWindow;

        framework.Update += OnFrameworkUpdate;

        Ipc.NotifyReady();
    }

    private void OnCommand(string command, string args) => ToggleWindow();
    private void ToggleWindow() => configWindow.Toggle();

    // Track the logged-in character. When it changes (login, logout, or switching
    // characters), load that character's own file (or seed a fresh config from the real
    // name) and drop the previous character's external-override snapshot.
    private void OnFrameworkUpdate(IFramework _)
    {
        var local = Objects.LocalPlayer;
        if (local == null)
        {
            if (currentKey != null)
            {
                current = null;
                currentKey = null;
                extNameActive = false;
                extNameBackup = null;
                CurrentEpoch++;
            }
            return;
        }

        var name = local.Name.TextValue;
        var world = local.HomeWorld.ValueNullable?.Name.ToString() ?? string.Empty;
        var key = CharacterStore.KeyFor(name, world);
        if (key == currentKey) return;

        currentKey = key;
        current = store.Load(name, world) ?? SeedFromRealName(name, world);
        // Stamp the live identity so a save always writes the canonical name@world file
        // (upgrading the defensive empty-world fallback and any casing drift on first save).
        current.CharacterName = name;
        current.World = world;
        extNameActive = false;
        extNameBackup = null;
        CurrentEpoch++;
        Ipc?.ReportLocalChanged();
    }

    private static MonikerCharacterConfig SeedFromRealName(string name, string world)
    {
        var idx = name.IndexOf(' ');
        return new MonikerCharacterConfig
        {
            CharacterName = name,
            World = world,
            FirstName = idx < 0 ? name : name[..idx],
            LastName = idx < 0 ? string.Empty : name[(idx + 1)..],
        };
    }

    public void SaveCurrentConfig()
    {
        if (current != null) store.Save(current);
    }

    // Force all nameplates to redraw next frame so changes apply instantly instead of
    // waiting for the game's next organic nameplate update.
    public void RequestNameplateRedraw() => namePlateGui.RequestRedraw();

    // Resolve the name to render for a nameplate. Peers are governed only by an explicit
    // IPC assignment (e.g. an HMS courier); the local player is governed solely by its own
    // per-character config and never by an incoming IPC assignment, so a courier cannot
    // stomp the host's own name (even through a reused object index).
    public bool TryGetActiveName(IPlayerCharacter pc, out string name, out bool hideFcTag, out bool hideName)
    {
        name = string.Empty;
        hideFcTag = false;
        hideName = false;

        var local = Objects.LocalPlayer;
        var isLocal = local != null && pc.EntityId == local.EntityId;

        if (!isLocal)
        {
            if (IpcProvider.IpcAssignedNames.TryGetValue(pc.EntityId, out var data))
            {
                name = data.Name ?? string.Empty;
                hideFcTag = data.HideFcTag;
                hideName = data.HideName;
                return true;
            }
            return false;
        }

        // Local player. Guard against a one-frame stale config during a character switch by
        // confirming the loaded config still matches who we are.
        var realName = pc.Name.TextValue;
        if (current == null || !string.Equals(current.CharacterName, realName, System.StringComparison.OrdinalIgnoreCase))
            return false;

        name = current.Compose();
        hideFcTag = current.HideFcTag;
        hideName = current.HideName;

        // Nothing customized (slots blank or still the real name, no hide flags) counts as
        // no override, so we neither rewrite the plate nor broadcast over IPC.
        if (!hideFcTag && !hideName && (string.IsNullOrWhiteSpace(name) || name == realName))
            return false;

        return true;
    }

    // Set the LOCAL player's own displayed name via its per-character config (not the peer
    // dictionary, which the host-invariant guard ignores for the local player). Used by
    // cooperating local plugins such as HOutfits applying an NPC name. The raw string is
    // split into slots the same way a real name is seeded, so an externally-applied name and
    // a manually-entered one slot identically. Returns false only if unavailable.
    public bool SetLocalName(string name)
    {
        if (Objects.LocalPlayer == null || current == null) return false;

        // Snapshot the user's own config once, so a later Clear restores it.
        if (!extNameActive)
        {
            extNameBackup = Clone(current);
            extNameActive = true;
        }

        var trimmed = (name ?? string.Empty).Trim();
        var idx = trimmed.IndexOf(' ');
        current.Prefix = string.Empty;
        current.MiddleName = string.Empty;
        current.Suffix = string.Empty;
        current.FirstName = idx < 0 ? trimmed : trimmed[..idx];
        current.LastName = idx < 0 ? string.Empty : trimmed[(idx + 1)..];
        // HideFcTag/HideName deliberately left as the user's own setting.

        SaveCurrentConfig();
        Ipc?.ReportLocalChanged();
        RequestNameplateRedraw();
        return true;
    }

    // Revert an external local-name override: restore the user's own prior config.
    public void ClearLocalName()
    {
        if (current != null && extNameActive && extNameBackup != null)
            RestoreInto(current, extNameBackup);

        extNameActive = false;
        extNameBackup = null;
        SaveCurrentConfig();
        Ipc?.ReportLocalChanged();
        RequestNameplateRedraw();
    }

    private static MonikerCharacterConfig Clone(MonikerCharacterConfig c) => new()
    {
        CharacterName = c.CharacterName,
        World = c.World,
        Prefix = c.Prefix,
        FirstName = c.FirstName,
        MiddleName = c.MiddleName,
        LastName = c.LastName,
        Suffix = c.Suffix,
        HideFcTag = c.HideFcTag,
        HideName = c.HideName,
    };

    private static void RestoreInto(MonikerCharacterConfig target, MonikerCharacterConfig src)
    {
        target.Prefix = src.Prefix;
        target.FirstName = src.FirstName;
        target.MiddleName = src.MiddleName;
        target.LastName = src.LastName;
        target.Suffix = src.Suffix;
        target.HideFcTag = src.HideFcTag;
        target.HideName = src.HideName;
    }

    public void Dispose()
    {
        framework.Update -= OnFrameworkUpdate;

        pi.UiBuilder.Draw -= windowSystem.Draw;
        pi.UiBuilder.OpenConfigUi -= ToggleWindow;
        pi.UiBuilder.OpenMainUi -= ToggleWindow;
        windowSystem.RemoveAllWindows();

        commandManager.RemoveHandler(CommandName);

        nameplateService.Dispose();
        Ipc?.Dispose();
        Ipc = null;
    }
}
