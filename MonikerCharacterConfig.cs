namespace HMoniker;

using System.Linq;

// Per-character nameplate name. Identity (CharacterName + World) is the match key,
// captured on add. The five slots are the display; First/Last are seeded from the
// real name and are all editable and may be blank.
public class MonikerCharacterConfig
{
    public string CharacterName = string.Empty;
    public string World = string.Empty;

    public string Prefix = string.Empty;
    public string FirstName = string.Empty;
    public string MiddleName = string.Empty;
    public string LastName = string.Empty;
    public string Suffix = string.Empty;

    public bool HideFcTag;

    // Assemble the nameplate name from the non-empty slots.
    public string Compose()
    {
        var parts = new[] { Prefix, FirstName, MiddleName, LastName, Suffix };
        return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()));
    }
}
