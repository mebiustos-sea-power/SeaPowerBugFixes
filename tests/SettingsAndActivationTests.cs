using System;
using System.Collections.Generic;
using System.IO;
using MebiusTosBugFixes;
using static Program;

internal static class SettingsAndActivationTests
{
    internal static void Run()
    {
        SettingsRespectDisablingAndRejectInvalidFiles();
        UnknownSettingsDoNotBlockActiveFixes();
        InvalidValuesWarnWithoutChangingValidSettings();
        ModCheckboxIsAuthoritative();
    }

    private static void SettingsRespectDisablingAndRejectInvalidFiles()
    {
        string directory = Path.Combine(Path.GetTempPath(), "MebiusTosBugFixes-settings-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "fixes.ini");
        string[] ids = { "FormationUnitReselection", "FutureFix" };
        Require(FixSettings.Load(path, ids).IsEnabled(ids[0]) && !Directory.Exists(directory),
            "Absent optional settings should enable fixes without creating a file.");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(path, "# Example\n[Fixes]\nFormationUnitReselection = false\n");
            var settings = FixSettings.Load(path, ids);
            Require(!settings.IsEnabled(ids[0]) && settings.IsEnabled(ids[1]), "Individual disable or new-fix defaults were lost.");
            foreach (string invalid in new[] {
                "[Fixes]\nFormationUnitReselection = true\nFormationUnitReselection = false",
                "[WrongSection]\nFormationUnitReselection = true" })
            {
                File.WriteAllText(path, invalid);
                Throws<FormatException>(() => FixSettings.Load(path, ids));
                Require(File.ReadAllText(path) == invalid, "Invalid user settings were overwritten.");
            }
            File.Delete(path);
            Directory.CreateDirectory(path);
            Throws<UnauthorizedAccessException>(() => FixSettings.Load(path, ids));
        }
        finally { Directory.Delete(directory, true); }
        Console.WriteLine("PASS configuration: per-fix opt-out, additive defaults and malformed/duplicate settings reported unchanged");
    }

    private static void UnknownSettingsDoNotBlockActiveFixes()
    {
        string directory = Path.Combine(Path.GetTempPath(), "MebiusTosBugFixes-unknown-settings-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "fixes.ini");
        string[] ids = { "FormationUnitReselection" };
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var item in new[] { (Entry: "WorldViewWindowHost = true", Warnings: 1),
                (Entry: "WorldViewWindowHost = false", Warnings: 1), (Entry: "FutureFix = not-a-Boolean", Warnings: 1),
                (Entry: "OtherFix = true\nOtherFix = false", Warnings: 2) })
            {
                string input = "[Fixes]\n" + item.Entry + "\nFormationUnitReselection = false\n";
                File.WriteAllText(path, input);
                var settings = FixSettings.Load(path, ids);
                Require(!settings.IsEnabled(ids[0]), "An unknown entry blocked or changed the active fix's disabled state.");
                Require(settings.Warnings.Count == item.Warnings, "Ignored unknown entries did not produce one warning per entry.");
                Require(File.ReadAllText(path) == input, "Unknown user settings were overwritten.");
            }
            File.WriteAllText(path, "[Fixes]\nWorldViewWindowHost = true\n");
            Require(FixSettings.Load(path, ids).IsEnabled(ids[0]), "A removed fix's setting changed the active fix's default.");
        }
        finally { Directory.Delete(directory, true); }
        Console.WriteLine("PASS unknown settings: removed/future IDs warn and are ignored, active choices and defaults preserved, input unchanged");
    }

    private static void InvalidValuesWarnWithoutChangingValidSettings()
    {
        string directory = Path.Combine(Path.GetTempPath(), "MebiusTosBugFixes-invalid-values-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "fixes.ini");
        string[] ids = { "FormationUnitReselection", "FutureFix" };
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var item in new[] {
                (Input: "[Fixes]\nFormationUnitReselection = invalid\nFutureFix = false\n", Enabled: true),
                (Input: "[Fixes]\nFormationUnitReselection = false\nFormationUnitReselection = invalid\nFutureFix = false\n", Enabled: false),
                (Input: "[Fixes]\nFormationUnitReselection = invalid\nFormationUnitReselection = false\nFutureFix = false\n", Enabled: false) })
            {
                File.WriteAllText(path, item.Input);
                var settings = FixSettings.Load(path, ids);
                Require(settings.IsEnabled(ids[0]) == item.Enabled && !settings.IsEnabled(ids[1]),
                    "An invalid Boolean changed a default/valid setting or prevented another fix from loading.");
                Require(settings.Warnings.Count == 1 && settings.Warnings[0].Contains(ids[0]),
                    "An invalid Boolean was not reported with the affected fix ID.");
                Require(File.ReadAllText(path) == item.Input, "Invalid user values were overwritten.");
            }
        }
        finally { Directory.Delete(directory, true); }
        Console.WriteLine("PASS invalid values: warning identifies the fix; defaults/valid values and other fixes survive; input unchanged");
    }

    public sealed class DirectoryEntry
    {
        public DirectoryInfo DirectoryInfo { get; set; } = null!;
        public bool IsChecked { get; set; }
    }
    public sealed class FileManagerFixture
    {
        public static FileManagerFixture Instance { get; } = new();
        public List<DirectoryEntry> Directories { get; } = new();
    }
    private static void ModCheckboxIsAuthoritative()
    {
        string path = Path.Combine(Path.GetTempPath(), "MebiusTosBugFixes-activation");
        var entry = new DirectoryEntry { DirectoryInfo = new DirectoryInfo(path), IsChecked = false };
        FileManagerFixture.Instance.Directories.Add(entry);
        Require(!ModActivation.IsEnabled(typeof(FileManagerFixture), path), "Unchecked mod was enabled.");
        entry.IsChecked = true;
        Require(ModActivation.IsEnabled(typeof(FileManagerFixture), path + Path.DirectorySeparatorChar), "Checked package path was not recognized.");
        Throws<InvalidOperationException>(() => ModActivation.IsEnabled(typeof(FileManagerFixture), path + "-other"));
        FileManagerFixture.Instance.Directories.Clear();
        Console.WriteLine("PASS activation: mod checkbox, normalized package path and missing registration errors");
    }
}
