namespace HMoniker;

using System;
using System.Collections.Generic;
using Dalamud.Game.Gui.NamePlate;
using Dalamud.Plugin.Services;

public sealed class NameplateService : IDisposable
{
    private readonly Plugin plugin;
    private readonly INamePlateGui namePlateGui;

    public NameplateService(Plugin plugin, INamePlateGui namePlateGui)
    {
        this.plugin = plugin;
        this.namePlateGui = namePlateGui;
        this.namePlateGui.OnNamePlateUpdate += OnNamePlateUpdate;
    }

    private void OnNamePlateUpdate(INamePlateUpdateContext context, IReadOnlyList<INamePlateUpdateHandler> handlers)
    {
        foreach (var handler in handlers)
        {
            if (handler.PlayerCharacter is not { } pc) continue;
            if (!plugin.TryGetActiveName(pc, out var name, out var hideFc, out var hideName)) continue;

            // Hide name wins over any composed override: blank the plate name outright.
            if (hideName)
            {
                handler.Name = string.Empty;
            }
            else if (!string.IsNullOrWhiteSpace(name))
            {
                var real = pc.Name.TextValue;
                if (name != real) handler.Name = name;
            }

            if (hideFc) handler.FreeCompanyTag = string.Empty;
        }
    }

    public void Dispose() => namePlateGui.OnNamePlateUpdate -= OnNamePlateUpdate;
}
