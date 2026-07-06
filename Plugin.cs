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
