namespace HMoniker;

using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Newtonsoft.Json;

// Carried over IPC: the composed name string + FC-tag flag + hide-name flag. A courier
// (HMS relay) computes a peer's name and applies it via SetCharacterName; the receiver
// renders. HideName is an additive field (IPC 2.2): older peers that deserialize this
// JSON simply ignore it, and JSON from an older sender leaves it false. So a peer who
// hides their own name has it blanked for everyone running a build new enough to read it.
public class NameData
{
    public string Name = string.Empty;
    public bool HideFcTag;
    public bool HideName;
}

public class IpcProvider : IDisposable
{
    public const uint MajorVersion = 2;
    public const uint MinorVersion = 2;

    // Wire name kept as "Moniker" on purpose: it is the cross-plugin IPC contract
    // HMapSync already calls. Renaming it to match the plugin would silently break that
    // integration, so this namespace string stays even though the plugin is HMoniker.
    public const string NameSpace = "Moniker";

    public static readonly Dictionary<uint, NameData> IpcAssignedNames = new();

    private readonly Plugin plugin;

    private ICallGateProvider<(uint, uint)>? apiVersion;
    private ICallGateProvider<string>? getLocalCharacterName;
    private ICallGateProvider<int, string, object>? setCharacterName;
    private ICallGateProvider<int, object>? clearCharacterName;
    private ICallGateProvider<string, object>? localCharacterNameChanged;
    private ICallGateProvider<object>? ready;
    private ICallGateProvider<object>? disposing;
    private ICallGateProvider<string, bool>? setLocalName;
    private ICallGateProvider<object>? clearLocalName;

    public IpcProvider(IDalamudPluginInterface pi, Plugin plugin)
    {
        this.plugin = plugin;

        apiVersion = pi.GetIpcProvider<(uint, uint)>($"{NameSpace}.ApiVersion");
        apiVersion.RegisterFunc(() => (MajorVersion, MinorVersion));

        getLocalCharacterName = pi.GetIpcProvider<string>($"{NameSpace}.GetLocalCharacterName");
        getLocalCharacterName.RegisterFunc(LocalNameJson);

        setCharacterName = pi.GetIpcProvider<int, string, object>($"{NameSpace}.SetCharacterName");
        setCharacterName.RegisterAction((index, json) =>
        {
            try
            {
                var obj = index >= 0 && index < plugin.Objects.Length ? plugin.Objects[index] : null;
                if (obj is not IPlayerCharacter pc) return;

                // Never accept an IPC name for the local player's own object. A courier
                // must not override the host's own nameplate; if a reused index resolves
                // to the local player, drop it and clear any stale self-entry.
                var local = plugin.Objects.LocalPlayer;
                if (local != null && pc.EntityId == local.EntityId)
                {
                    IpcAssignedNames.Remove(pc.EntityId);
                    plugin.Log.Warning("HMoniker: rejected IPC name targeting the local player (courier index-reuse guard).");
                    return;
                }

                IpcAssignedNames.Remove(pc.EntityId);
                if (string.IsNullOrEmpty(json)) return;
                var data = JsonConvert.DeserializeObject<NameData>(json);
                if (data == null) return;
                IpcAssignedNames[pc.EntityId] = data;
            }
            catch (Exception ex)
            {
                plugin.Log.Error(ex, "Error handling SetCharacterName IPC.");
            }
        });

        clearCharacterName = pi.GetIpcProvider<int, object>($"{NameSpace}.ClearCharacterName");
        clearCharacterName.RegisterAction(index =>
        {
            var obj = index >= 0 && index < plugin.Objects.Length ? plugin.Objects[index] : null;
            if (obj is IPlayerCharacter pc) IpcAssignedNames.Remove(pc.EntityId);
        });

        // Local-player name set by a cooperating local plugin (e.g. HOutfits applying an
        // NPC name to the user themselves). Writes the local player's own config, NOT the
        // IpcAssignedNames dictionary (which is ignored for the local player by the
        // host-invariant guard). Legitimate because it is the user's own explicit action.
        setLocalName = pi.GetIpcProvider<string, bool>($"{NameSpace}.SetLocalName");
        setLocalName.RegisterFunc(name => plugin.SetLocalName(name));

        clearLocalName = pi.GetIpcProvider<object>($"{NameSpace}.ClearLocalName");
        clearLocalName.RegisterAction(() => plugin.ClearLocalName());

        localCharacterNameChanged = pi.GetIpcProvider<string, object>($"{NameSpace}.LocalCharacterNameChanged");
        ready = pi.GetIpcProvider<object>($"{NameSpace}.Ready");
        disposing = pi.GetIpcProvider<object>($"{NameSpace}.Disposing");
    }

    private string LocalNameJson()
    {
        var local = plugin.Objects.LocalPlayer;
        if (local == null) return string.Empty;
        if (!plugin.TryGetActiveName(local, out var name, out var hideFc, out var hideName)) return string.Empty;
        if (string.IsNullOrWhiteSpace(name) && !hideFc && !hideName) return string.Empty;
        return JsonConvert.SerializeObject(new NameData { Name = name, HideFcTag = hideFc, HideName = hideName });
    }

    private string lastReported = string.Empty;

    public void ReportLocalChanged()
    {
        var json = LocalNameJson();
        if (json == lastReported) return;
        lastReported = json;
        localCharacterNameChanged?.SendMessage(json);
    }

    public void NotifyReady() => ready?.SendMessage();

    public void Dispose()
    {
        disposing?.SendMessage();
        apiVersion?.UnregisterFunc();
        getLocalCharacterName?.UnregisterFunc();
        setCharacterName?.UnregisterAction();
        clearCharacterName?.UnregisterAction();
        setLocalName?.UnregisterFunc();
        clearLocalName?.UnregisterAction();
        IpcAssignedNames.Clear();
    }
}
