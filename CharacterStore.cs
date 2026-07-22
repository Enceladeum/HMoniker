namespace HMoniker;

using System;
using System.Collections.Generic;
using System.IO;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Newtonsoft.Json;

// Persists one MonikerCharacterConfig per character as its own JSON file under the plugin
// config directory (%AppData%\XIVLauncher\pluginConfigs\HMoniker\), keyed by "name@world".
//
// Why this exists: pre-1.3 kept every character in a single shared file written wholesale
// on every save. Two game clients launched from the same XIVLauncher folder therefore
// shared one list and clobbered each other (last writer wins), so names bled across
// separate accounts and HMS sync was flaky. Here each character owns its file and writes
// only that file. A character can only be logged in on one client at a time, so no two
// clients ever write the same file: the save race is gone by construction.
public sealed class CharacterStore
{
    private readonly DirectoryInfo dir;
    private readonly IPluginLog log;

    public CharacterStore(IDalamudPluginInterface pi, IPluginLog log)
    {
        this.log = log;
        dir = pi.ConfigDirectory;
    }

    // Identity key. name+world uniquely identifies a character; this is the same value the
    // window header shows and the file name is derived from.
    public static string KeyFor(string name, string world) => $"{name}@{world}";

    private string PathFor(string name, string world)
    {
        var file = $"char-{name}@{world}.json";
        foreach (var ch in Path.GetInvalidFileNameChars())
            file = file.Replace(ch, '_');
        return Path.Combine(dir.FullName, file);
    }

    public bool Exists(string name, string world) => File.Exists(PathFor(name, world));

    // Load a character's own file, or null if it has none. Falls back to a name-only file
    // (empty world) so legacy entries that were saved before a home world resolved still
    // migrate: they are rewritten under the correct key on the next save.
    public MonikerCharacterConfig? Load(string name, string world)
    {
        try
        {
            var path = PathFor(name, world);
            if (!File.Exists(path) && !string.IsNullOrEmpty(world))
                path = PathFor(name, string.Empty);
            if (!File.Exists(path)) return null;

            return JsonConvert.DeserializeObject<MonikerCharacterConfig>(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            log.Error(ex, $"HMoniker: failed to load config for {KeyFor(name, world)}.");
            return null;
        }
    }

    public void Save(MonikerCharacterConfig cfg)
    {
        try
        {
            Directory.CreateDirectory(dir.FullName);
            File.WriteAllText(PathFor(cfg.CharacterName, cfg.World),
                JsonConvert.SerializeObject(cfg, Formatting.Indented));
        }
        catch (Exception ex)
        {
            log.Error(ex, $"HMoniker: failed to save config for {KeyFor(cfg.CharacterName, cfg.World)}.");
        }
    }

    // One-time split of the pre-1.3 shared list into per-character files. Idempotent and
    // race-safe: a character that already has its own file is skipped (never clobbering
    // newer data), and the shared file is never written back, so two clients migrating at
    // once only ever write identical bytes. The legacy file is left in place as a passive
    // backup; runtime never reads it again.
    public void MigrateLegacy(IEnumerable<MonikerCharacterConfig> legacy)
    {
        foreach (var c in legacy)
        {
            if (string.IsNullOrEmpty(c.CharacterName)) continue;
            if (Exists(c.CharacterName, c.World)) continue;
            Save(c);
            log.Information($"HMoniker: migrated {KeyFor(c.CharacterName, c.World)} to its own config file.");
        }
    }
}
