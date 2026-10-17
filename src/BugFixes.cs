using System;
using System.Collections.Generic;
using HarmonyLib;

namespace MebiusTosBugFixes;

internal interface IBugFix
{
    string Id { get; }
    void Install(Harmony patches);
}

internal static class BugFixes
{
    internal static void Install(IEnumerable<IBugFix> fixes, FixSettings settings)
    {
        foreach (IBugFix fix in fixes)
        {
            if (!settings.IsEnabled(fix.Id)) { ModLog.Info(fix.Id + ": disabled in fixes.ini."); continue; }
            Harmony? patches = null;
            try
            {
                patches = new Harmony(ModInfo.Id + "." + fix.Id);
                fix.Install(patches);
                ModLog.Info(fix.Id + ": installed.");
            }
            catch (Exception failure)
            {
                ModLog.Error(fix.Id + ": installation failed; this fix is unavailable", failure);
                try { patches?.UnpatchSelf(); }
                catch (Exception cleanup) { ModLog.Error(fix.Id + ": patch rollback failed", cleanup); }
            }
        }
    }
}
