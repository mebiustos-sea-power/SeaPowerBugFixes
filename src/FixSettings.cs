using System;
using System.Collections.Generic;
using System.IO;

namespace MebiusTosBugFixes;

internal sealed class FixSettings
{
    private readonly Dictionary<string, bool> enabled;
    private FixSettings(Dictionary<string, bool> values) => enabled = values;
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
            if (!values.ContainsKey(id)) throw new FormatException(path + ":" + number + ": unknown fix: " + id);
            if (!assigned.Add(id)) throw new FormatException(path + ":" + number + ": duplicate fix: " + id);
            if (!bool.TryParse(line.Substring(separator + 1).Trim(), out bool value))
                throw new FormatException(path + ":" + number + ": invalid Boolean for " + id);
            values[id] = value;
        }
        return new FixSettings(values);
    }
}
