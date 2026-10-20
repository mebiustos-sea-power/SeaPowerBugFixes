using System;
using System.IO;
using System.Linq;
using AnchorChain;
using MebiusTosBugFixes.Fixes.WorldView;
using MebiusTosBugFixes.Fixes.Selection;

namespace MebiusTosBugFixes;

[ACPlugin(ModInfo.Id, ModInfo.Name, ModInfo.Version)]
public sealed class Plugin : IAnchorChainMod
{
    private static bool initialized;

    public void TriggerEntryPoint()
    {
        if (initialized) return;
        initialized = true;
        try
        {
            var game = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Seapower-Scripts");
            string package = Path.GetDirectoryName(typeof(Plugin).Assembly.Location)
                ?? throw new InvalidOperationException("The mod package directory is unavailable.");
            // Anchor Chain discovers DLLs in unchecked packages as well. The game's
            // checkbox is authoritative; changing it requires a game restart.
            if (!ModActivation.IsEnabled(game.GetType("SeaPower.FileManager", true)!, package))
            { ModLog.Info("Disabled in the mod manager; no fixes installed."); return; }

            IBugFix[] fixes = { new WorldViewWindowHostFix(), new FormationUnitReselectionFix() };
            var settings = FixSettings.Load(Path.Combine(package, "fixes.ini"), fixes.Select(f => f.Id));
            BugFixes.Install(fixes, settings);
        }
        catch (Exception error)
        {
            ModLog.Error("Bug-fix initialization failed; no further fixes will be installed", error);
        }
    }
}
