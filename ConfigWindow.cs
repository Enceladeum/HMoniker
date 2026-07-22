namespace HMoniker;

using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

// Row per selected character: [Prefix] [First] [Middle] [Last] [Suffix] on one line,
// then [Hide FC tag] [Hide name] toggles below, all free text. Edits are staged in a
// draft and only take effect on Apply; Reset returns the fields to the real character
// name. First/Last seeded from the real name.
public sealed class ConfigWindow : Window
{
    private readonly Plugin plugin;
    private int selected = -1;

    // Editing buffer for the selected character; committed only on Apply/Reset.
    private int draftFor = -1;
    private readonly MonikerCharacterConfig draft = new();

    public ConfigWindow(Plugin plugin) : base("HMoniker###HMonikerConfig")
    {
        this.plugin = plugin;
        Size = new Vector2(780, 300);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        var config = plugin.Config;

        if (ImGui.Checkbox("Enabled", ref config.Enabled))
        {
            plugin.SaveConfig();
            plugin.Ipc?.ReportLocalChanged();
            plugin.RequestNameplateRedraw();
        }
        ImGui.Separator();

        // Left: character list
        ImGui.BeginChild("##chars", new Vector2(180, 0), true);
        if (ImGui.Button("Add current character")) AddLocal();
        ImGui.Separator();
        for (var i = 0; i < config.Characters.Count; i++)
        {
            var c = config.Characters[i];
            var label = string.IsNullOrEmpty(c.CharacterName) ? "(unnamed)" : c.CharacterName;
            if (ImGui.Selectable($"{label}##c{i}", selected == i)) selected = i;
        }
        ImGui.EndChild();

        ImGui.SameLine();

        // Right: slot editor
        ImGui.BeginChild("##editor", new Vector2(0, 0), true);
        if (selected >= 0 && selected < config.Characters.Count)
            DrawCharacter(config.Characters[selected]);
        else
            ImGui.TextDisabled("Add your current character to begin.");
        ImGui.EndChild();
    }

    private void DrawCharacter(MonikerCharacterConfig c)
    {
        if (draftFor != selected)
        {
            LoadDraft(c);
            draftFor = selected;
        }

        ImGui.TextDisabled(string.IsNullOrEmpty(c.World) ? c.CharacterName : $"{c.CharacterName}  -  {c.World}");
        ImGui.Spacing();

        // Row 1: [Prefix] [First] [Middle] [Last] [Suffix]
        ImGui.SetNextItemWidth(68);
        ImGui.InputTextWithHint("##prefix", "Prefix", ref draft.Prefix, 32);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        ImGui.InputTextWithHint("##first", "First name", ref draft.FirstName, 32);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(68);
        ImGui.InputTextWithHint("##middle", "Middle", ref draft.MiddleName, 32);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        ImGui.InputTextWithHint("##last", "Last name", ref draft.LastName, 32);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(68);
        ImGui.InputTextWithHint("##suffix", "Suffix", ref draft.Suffix, 32);

        // Row 2: toggles. "Hide name" blanks the plate name entirely; it wins over the
        // slots above, so the preview shows "(hidden)" when it is on.
        ImGui.Spacing();
        ImGui.Checkbox("Hide FC tag", ref draft.HideFcTag);
        ImGui.SameLine();
        ImGui.Checkbox("Hide name", ref draft.HideName);

        ImGui.Spacing();
        var composed = draft.Compose();
        var preview = draft.HideName
            ? "(hidden)"
            : string.IsNullOrWhiteSpace(composed) ? "(unchanged)" : composed;
        ImGui.TextDisabled($"Preview:  {preview}");

        ImGui.Spacing();
        if (ImGui.Button("Apply"))
        {
            CommitDraft(c);
            plugin.SaveConfig();
            plugin.Ipc?.ReportLocalChanged();
            plugin.RequestNameplateRedraw();
        }
        ImGui.SameLine();
        if (ImGui.Button("Reset"))
        {
            ResetDraftToRealName();
            CommitDraft(c);
            plugin.SaveConfig();
            plugin.Ipc?.ReportLocalChanged();
            plugin.RequestNameplateRedraw();
        }

        ImGui.Spacing();
        ImGui.Separator();
        if (ImGui.Button("Remove character"))
        {
            plugin.Config.Characters.RemoveAt(selected);
            selected = -1;
            draftFor = -1;
            plugin.SaveConfig();
            plugin.RequestNameplateRedraw();
        }
    }

    private void LoadDraft(MonikerCharacterConfig c)
    {
        draft.CharacterName = c.CharacterName;
        draft.World = c.World;
        draft.Prefix = c.Prefix;
        draft.FirstName = c.FirstName;
        draft.MiddleName = c.MiddleName;
        draft.LastName = c.LastName;
        draft.Suffix = c.Suffix;
        draft.HideFcTag = c.HideFcTag;
        draft.HideName = c.HideName;
    }

    private void CommitDraft(MonikerCharacterConfig c)
    {
        c.Prefix = draft.Prefix;
        c.FirstName = draft.FirstName;
        c.MiddleName = draft.MiddleName;
        c.LastName = draft.LastName;
        c.Suffix = draft.Suffix;
        c.HideFcTag = draft.HideFcTag;
        c.HideName = draft.HideName;
    }

    private void ResetDraftToRealName()
    {
        var full = draft.CharacterName;
        var idx = full.IndexOf(' ');
        draft.Prefix = string.Empty;
        draft.MiddleName = string.Empty;
        draft.Suffix = string.Empty;
        draft.FirstName = idx < 0 ? full : full[..idx];
        draft.LastName = idx < 0 ? string.Empty : full[(idx + 1)..];
        draft.HideFcTag = false;
        draft.HideName = false;
    }

    private void AddLocal()
    {
        var lp = plugin.Objects.LocalPlayer;
        if (lp == null) return;

        var full = lp.Name.TextValue;
        var world = lp.HomeWorld.ValueNullable?.Name.ToString() ?? string.Empty;

        for (var i = 0; i < plugin.Config.Characters.Count; i++)
        {
            if (string.Equals(plugin.Config.Characters[i].CharacterName, full, StringComparison.OrdinalIgnoreCase))
            {
                selected = i;
                draftFor = -1;
                return;
            }
        }

        var idx = full.IndexOf(' ');
        plugin.Config.Characters.Add(new MonikerCharacterConfig
        {
            CharacterName = full,
            World = world,
            FirstName = idx < 0 ? full : full[..idx],
            LastName = idx < 0 ? string.Empty : full[(idx + 1)..],
        });
        selected = plugin.Config.Characters.Count - 1;
        draftFor = -1;
        plugin.SaveConfig();
    }
}
