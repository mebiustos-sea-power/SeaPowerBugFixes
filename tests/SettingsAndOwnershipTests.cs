using System;
using System.Collections.Generic;
using System.IO;
using MebiusTosBugFixes;
using MebiusTosBugFixes.Fixes.WorldView;
using static Program;

internal static class SettingsAndOwnershipTests
{
    internal static void Run()
    {
        HostsBelongToTheirBattle();
        MissingOwnersStayVisibleAsErrors();
        SettingsRespectDisablingAndRejectInvalidFiles();
        ModCheckboxIsAuthoritative();
    }

    private sealed class Root { internal object? Host = new object(); }
    private sealed class Model { internal Root? Root = new(); }

    private static void HostsBelongToTheirBattle()
    {
        int searches = 0;
        var errors = new List<Exception>();
        var cache = new OwnedHostCache<Model, Root, object>(model => model.Root, root =>
        { searches++; return root.Host!; }, errors.Add);
        var first = new Model();
        var second = new Model();
        Require(cache.TryResolve(first, out object? firstHost) && ReferenceEquals(firstHost, first.Root!.Host), "First battle's owner was lost.");
        Require(cache.TryResolve(second, out object? secondHost) && ReferenceEquals(secondHost, second.Root!.Host), "A second battle reused the first battle's host.");
        for (int i = 0; i < 10; i++) cache.TryResolve(first, out _);
        Require(searches == 2, "Host discovery repeated on unchanged battle roots.");
        first.Root = new Root();
        Require(cache.TryResolve(first, out object? replacement) && ReferenceEquals(replacement, first.Root.Host) && searches == 3,
            "A replacement battle root reused the old host.");
        Require(errors.Count == 0, "Healthy root changes emitted errors.");
        Console.WriteLine("PASS host ownership: separate battles, cached discovery and replacement roots");
    }

    private static void MissingOwnersStayVisibleAsErrors()
    {
        var errors = new List<Exception>();
        var cache = new OwnedHostCache<Model, Root, object>(model => model.Root, root => root.Host!, errors.Add);
        var model = new Model();
        cache.TryResolve(model, out _);
        model.Root = new Root { Host = null };
        Require(!cache.TryResolve(model, out object? missing) && missing == null, "Missing owner did not skip synchronization.");
        Require(!cache.TryResolve(model, out _), "Repeated missing owner escaped containment.");
        Require(errors.Count == 1, "A missing owner was hidden or flooded the log each frame.");
        model.Root.Host = new object();
        Require(cache.TryResolve(model, out object? recovered) && ReferenceEquals(recovered, model.Root.Host), "Host discovery could not recover after UI attachment.");
        model.Root = null;
        Require(!cache.TryResolve(model, out _), "Missing root did not skip synchronization.");
        Require(errors.Count == 2, "Missing root did not surface as a separate failure.");
        var failingReporter = new OwnedHostCache<Model, Root, object>(_ => throw new InvalidOperationException("UI discovery failure"),
            root => root.Host!, _ => throw new InvalidOperationException("log sink failure"));
        Require(!failingReporter.TryResolve(new Model(), out _), "An exception in discovery/error reporting escaped the mod.");
        Console.WriteLine("PASS host failures: skipped synchronization, no exceptions to callers, one report, logging failure containment and recovery");
    }

    private static void SettingsRespectDisablingAndRejectInvalidFiles()
    {
        string directory = Path.Combine(Path.GetTempPath(), "MebiusTosBugFixes-settings-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "fixes.ini");
        string[] ids = { "WorldViewWindowHost", "FormationUnitReselection" };
        Require(FixSettings.Load(path, ids).IsEnabled(ids[0]) && !Directory.Exists(directory),
            "Absent optional settings should enable fixes without creating a file.");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(path, "# Example\n[Fixes]\nWorldViewWindowHost = false\n");
            var settings = FixSettings.Load(path, ids);
            Require(!settings.IsEnabled(ids[0]) && settings.IsEnabled(ids[1]), "Individual disable or new-fix defaults were lost.");
            foreach (string invalid in new[] { "[Fixes]\nWorldViewWindowHost = invalid", "[Fixes]\nUnknownFix = false",
                "[Fixes]\nWorldViewWindowHost = true\nWorldViewWindowHost = false", "[WrongSection]\nWorldViewWindowHost = true" })
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
        Console.WriteLine("PASS configuration: per-fix opt-out, additive defaults and invalid input reported unchanged");
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
