using System.Collections.Generic;
using System.Text;

namespace CrestPicker.Pure;

/// <summary>
/// Builds the dialogue text the chapel maid speaks before the crest offer, free of game
/// and Unity types. One config line becomes one dialogue page.
/// </summary>
internal static class OfferLines
{
    /// <summary>Tag the game splits NPC dialogue pages on.</summary>
    private const string PageTag = "<page>";

    /// <summary>
    /// Joins the configured lines into one dialogue text. Each line is trimmed, blank and
    /// null lines are dropped, and lines holding '&lt;' or '&gt;' are dropped because the
    /// game's dialogue parser cuts the text on an unclosed tag. Pages are joined with
    /// <c>&lt;page&gt;</c>; no lines left gives <see cref="string.Empty"/>.
    /// </summary>
    /// <param name="lines">Config lines, in the order they are shown.</param>
    internal static string Compose(IReadOnlyList<string?> lines)
    {
        StringBuilder? text = null;
        for (int i = 0; i < lines.Count; i++)
        {
            string? line = lines[i];
            if (line == null)
            {
                continue;
            }

            line = line.Trim();
            if (line.Length == 0 || line.IndexOf('<') >= 0 || line.IndexOf('>') >= 0)
            {
                continue;
            }

            if (text == null)
            {
                text = new StringBuilder(line);
            }
            else
            {
                text.Append(PageTag).Append(line);
            }
        }

        return text?.ToString() ?? string.Empty;
    }

    /// <summary>True when the composed text has no page to show.</summary>
    /// <param name="composed">Text returned by <see cref="Compose"/>.</param>
    internal static bool IsEmpty(string? composed)
    {
        return string.IsNullOrEmpty(composed);
    }
}
