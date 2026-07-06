namespace HMoniker;

using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Configuration;

public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 2;

    public bool Enabled = true;

    public List<MonikerCharacterConfig> Characters = new();

    public bool TryGetCharacterConfig(string name, string world, out MonikerCharacterConfig? config)
    {
        config = Characters.FirstOrDefault(c =>
            string.Equals(c.CharacterName, name, StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrEmpty(c.World) || string.Equals(c.World, world, StringComparison.OrdinalIgnoreCase)));
        return config != null;
    }
}
