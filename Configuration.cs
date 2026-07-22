namespace HMoniker;

using System.Collections.Generic;
using Dalamud.Configuration;

// Legacy shared config, retained only as a one-time, read-only migration SOURCE.
//
// Pre-1.3 stored every character in this single shared file, which is why two game clients
// launched from the same XIVLauncher folder clobbered each other's names. 1.3+ stores each
// character in its own file under the plugin config directory and never writes this shared
// file again; on first load its entries are split out per character (see CharacterStore).
// Fields removed from older builds (e.g. Enabled) are simply ignored on deserialize.
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 2;

    public List<MonikerCharacterConfig> Characters = new();
}
