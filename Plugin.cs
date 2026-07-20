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

    public IObjectTable Objects { get; }
    public IPluginLog Log { get; }

    public Configuration Config { get; }
    public IpcProvider? Ipc { get; private set; }

    private readonly NameplateService nameplateService;
    private readonly WindowSystem windowSystem = new("HMoniker");
    private readonly ConfigWindow configWindow;

    // External local-name override (e.g. HOutfits applying an NPC name). We snapshot
    // the user's own prior config so a later ClearLocalName is non-destructive.
    private MonikerCharacterConfig? extNameBackup;
    private bool extNameCreatedConfig;
    private bool extNameActive;

    private const string CommandName = "/hmoniker";

    public Plugin(
        IDalamudPluginInterface pi,
        ICommandManager commandManager,
        IObjectTable objects,
        INamePlateGui namePlateGui,
        IPluginLog log)
    {
        this.pi = pi;
        this.commandManager = commandManager;
        this.namePlateGui = namePlateGui;
        Objects = objects;
        Log = log;

        Config = pi.GetPluginConfig() as Configuration ?? new Configuration();

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

        Ipc.NotifyReady();
    }

    private void OnCommand(string command, string args) => ToggleWindow();
    private void ToggleWindow() => configWindow.Toggle();

    public void SaveConfig() => pi.SavePluginConfig(Config);

    // Force all nameplates to redraw next frame so changes apply instantly instead
    // of waiting for the game's next organic nameplate update.
    public void RequestNameplateRedraw() => namePlateGui.RequestRedraw();

    // IPC-assigned override wins; otherwise the character's own config composes.
    public bool TryGetActiveName(IPlayerCharacter pc, out string name, out bool hideFcTag)
    {
        name = string.Empty;
        hideFcTag = false;

        // The local player's own nameplate is always locally authoritative: driven
        // solely by local config, never by an IPC assignment. This stops any courier
        // (e.g. HMS) from stomping the host's own name (including via a reused object
        // index) and means a stale self-entry is simply ignored here rather than
        // sticking until a plugin reload.
        var local = Objects.LocalPlayer;
        var isLocal = local != null && pc.EntityId == local.EntityId;

        if (!isLocal && IpcProvider.IpcAssignedNames.TryGetValue(pc.EntityId, out var data))
        {
            name = data.Name ?? string.Empty;
            hideFcTag = data.HideFcTag;
            return true;
        }

        var realName = pc.Name.TextValue;
        var world = pc.HomeWorld.ValueNullable?.Name.ToString() ?? string.Empty;
        if (!Config.TryGetCharacterConfig(realName, world, out var cc) || cc == null) return false;

        name = cc.Compose();
        hideFcTag = cc.HideFcTag;
        return true;
    }

    // Set the LOCAL player's own displayed name via the config path (not the peer-
    // override dictionary, which the host-invariant guard ignores for the local player).
    // Used by cooperating local plugins such as HOutfits applying an NPC name. The raw
    // string is split into slots the SAME way a real name is seeded, so an externally-
    // applied name and a manually-entered one slot identically. Returns false only if the
    // local player is unavailable.
    public bool SetLocalName(string name)
    {
        var local = Objects.LocalPlayer;
        if (local == null) return false;

        var realName = local.Name.TextValue;
        var world = local.HomeWorld.ValueNullable?.Name.ToString() ?? string.Empty;

        if (!Config.TryGetCharacterConfig(realName, world, out var cc) || cc == null)
        {
            cc = new MonikerCharacterConfig { CharacterName = realName, World = world };
            Config.Characters.Add(cc);
            if (!extNameActive)
            {
                extNameCreatedConfig = true;
                extNameBackup = null;
            }
        }
        else if (!extNameActive)
        {
            // First external override on top of a user's own config: snapshot it so a
            // later Clear restores the user's name rather than destroying it.
            extNameCreatedConfig = false;
            extNameBackup = Clone(cc);
        }

        var trimmed = (name ?? string.Empty).Trim();
        var idx = trimmed.IndexOf(' ');
        cc.Prefix = string.Empty;
        cc.MiddleName = string.Empty;
        cc.Suffix = string.Empty;
        cc.FirstName = idx < 0 ? trimmed : trimmed[..idx];
        cc.LastName = idx < 0 ? string.Empty : trimmed[(idx + 1)..];
        // HideFcTag deliberately left untouched: a newly created config defaults to false
        // (tag shown); an existing config keeps the user's own setting.

        extNameActive = true;
        SaveConfig();
        Ipc?.ReportLocalChanged();
        RequestNameplateRedraw();
        return true;
    }

    // Revert an external local-name override: restore the user's own prior name, or
    // remove the entry entirely if the override created it -> back to the real name.
    public void ClearLocalName()
    {
        var local = Objects.LocalPlayer;
        if (local != null)
        {
            var realName = local.Name.TextValue;
            var world = local.HomeWorld.ValueNullable?.Name.ToString() ?? string.Empty;
            if (Config.TryGetCharacterConfig(realName, world, out var cc) && cc != null)
            {
                if (extNameCreatedConfig)
                    Config.Characters.Remove(cc);
                else if (extNameBackup != null)
                    RestoreInto(cc, extNameBackup);
            }
        }

        extNameActive = false;
        extNameCreatedConfig = false;
        extNameBackup = null;
        SaveConfig();
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
    };

    private static void RestoreInto(MonikerCharacterConfig target, MonikerCharacterConfig src)
    {
        target.Prefix = src.Prefix;
        target.FirstName = src.FirstName;
        target.MiddleName = src.MiddleName;
        target.LastName = src.LastName;
        target.Suffix = src.Suffix;
        target.HideFcTag = src.HideFcTag;
    }

    public void Dispose()
    {
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
