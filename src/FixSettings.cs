using System;
using System.Collections.Generic;
using System.IO;

namespace MebiusTosBugFixes;

internal sealed class FixSettings
{
    private readonly Dictionary<string, bool> enabled;
    internal IReadOnlyList<string> Warnings { get; }
    private FixSettings(Dictionary<string, bool> values, IReadOnlyList<string>? warnings = null)
    { enabled = values; Warnings = warnings ?? Array.Empty<string>(); }
    internal bool IsEnabled(string id) => enabled[id];

    internal static FixSettings Load(string path, IEnumerable<string> fixIds)
    {
        var values = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (string id in fixIds) values.Add(id, true);
        string[] lines;
        try { lines = File.ReadAllLines(path); }
        catch (FileNotFoundException) { return new FixSettings(values); }
        catch (DirectoryNotFoundException) { return new FixSettings(values); }

        var assigned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        bool section = false;
        int number = 0;
        foreach (string raw in lines)
        {
            number++;
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith(";", StringComparison.Ordinal)) continue;
            if (string.Equals(line, "[Fixes]", StringComparison.OrdinalIgnoreCase)) { section = true; continue; }
            int separator = line.IndexOf('=');
            if (!section || separator <= 0)
                throw new FormatException(path + ":" + number + ": expected [Fixes] or FixId = true/false.");
            string id = line.Substring(0, separator).Trim();
            // Unknown entries can remain after a fix is removed or come from another mod version.
            if (!values.ContainsKey(id))
            {
                warnings.Add(path + ":" + number + ": unknown fix ignored: " + id);
                continue;
            }
            if (!bool.TryParse(line.Substring(separator + 1).Trim(), out bool value))
            {
                warnings.Add(path + ":" + number + ": invalid Boolean for " + id + "; entry ignored.");
                continue;
            }
            if (!assigned.Add(id)) throw new FormatException(path + ":" + number + ": duplicate fix: " + id);
            values[id] = value;
        }
        return new FixSettings(values, warnings.AsReadOnly());
    }
}
