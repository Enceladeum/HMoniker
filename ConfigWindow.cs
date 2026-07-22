namespace HMoniker;

using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

// Single-character editor. HMoniker only ever governs the character you are logged in as,
// so the window shows exactly that character: five free-text slots
// [Prefix][First][Middle][Last][Suffix] that wrap onto new lines as the window narrows,
// then [Hide FC tag] and [Hide name]. Edits are staged in a draft and applied on Apply;
// Reset returns the slots to the real character name. To edit another character, log in to
// it: its own file loads automatically.
public sealed class ConfigWindow : Window
{
    private readonly Plugin plugin;

    // Editing buffer for the current character; committed only on Apply/Reset. Reloaded
    // whenever the logged-in character changes (tracked by the plugin's epoch).
    private int draftEpoch = -1;
    private readonly MonikerCharacterConfig draft = new();

    public ConfigWindow(Plugin plugin) : base("HMoniker###HMonikerConfig")
    {
        this.plugin = plugin;
        Size = new Vector2(480, 230);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(220, 150),
            MaximumSize = new Vector2(2000, 2000),
        };
    }

    public override void Draw()
    {
        var cfg = plugin.CurrentConfig;
        if (cfg == null)
        {
            ImGui.TextWrapped("Log in to a character to set its display name.");
            return;
        }

        if (plugin.CurrentEpoch != draftEpoch)
        {
            LoadDraft(cfg);
            draftEpoch = plugin.CurrentEpoch;
        }

        ImGui.TextWrapped(string.IsNullOrEmpty(cfg.World) ? cfg.CharacterName : $"{cfg.CharacterName}  -  {cfg.World}");
        ImGui.Separator();
        ImGui.Spacing();

        DrawSlotsWrapped();

        ImGui.Spacing();
        DrawTogglesWrapped();

        ImGui.Spacing();
        var composed = draft.Compose();
        var preview = draft.HideName
            ? "(hidden)"
            : string.IsNullOrWhiteSpace(composed) ? "(unchanged)" : composed;
        ImGui.TextWrapped($"Preview:  {preview}");

        ImGui.Spacing();
        if (ImGui.Button("Apply")) ApplyDraft(cfg);
        ImGui.SameLine();
        if (ImGui.Button("Reset"))
        {
            ResetDraftToRealName();
            ApplyDraft(cfg);
        }
    }

    // Lay the five name slots out left to right, dropping to a new line whenever the next
    // field would run past the window's content edge. This is what makes the row reflow as
    // the window is narrowed instead of overflowing off the side.
    private void DrawSlotsWrapped()
    {
        var style = ImGui.GetStyle();
        var visX2 = ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X;

        void Slot(string id, string hint, ref string val, float w, bool first)
        {
            if (!first)
            {
                var nextX2 = ImGui.GetItemRectMax().X + style.ItemSpacing.X + w;
                if (nextX2 < visX2) ImGui.SameLine();
            }
            ImGui.SetNextItemWidth(w);
            ImGui.InputTextWithHint(id, hint, ref val, 32);
        }

        Slot("##prefix", "Prefix", ref draft.Prefix, 68, true);
        Slot("##first", "First name", ref draft.FirstName, 104, false);
        Slot("##middle", "Middle", ref draft.MiddleName, 68, false);
        Slot("##last", "Last name", ref draft.LastName, 104, false);
        Slot("##suffix", "Suffix", ref draft.Suffix, 68, false);
    }

    // "Hide name" blanks the plate name entirely and wins over the slots above, so the
    // preview reads "(hidden)" when it is on. Both toggles wrap the same way the slots do.
    private void DrawTogglesWrapped()
    {
        var style = ImGui.GetStyle();
        var visX2 = ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X;

        ImGui.Checkbox("Hide FC tag", ref draft.HideFcTag);

        var nameW = ImGui.GetFrameHeight() + style.ItemInnerSpacing.X + ImGui.CalcTextSize("Hide name").X;
        var nextX2 = ImGui.GetItemRectMax().X + style.ItemSpacing.X + nameW;
        if (nextX2 < visX2) ImGui.SameLine();
        ImGui.Checkbox("Hide name", ref draft.HideName);
    }

    private void ApplyDraft(MonikerCharacterConfig c)
    {
        CommitDraft(c);
        plugin.SaveCurrentConfig();
        plugin.Ipc?.ReportLocalChanged();
        plugin.RequestNameplateRedraw();
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
}
