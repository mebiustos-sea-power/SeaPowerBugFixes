using System;
using MebiusTosBugFixes;
using WeaponStatus = SeaPower.ObjectBase.WeaponStatus;

namespace MebiusTosBugFixes.Fixes.Combat;

internal static class CeaseFireWeaponStatus
{
    [ThreadStatic] private static bool preservingHold;

    internal static WeaponStatus Resolve(WeaponStatus current, WeaponStatus requested) =>
        preservingHold && current == WeaponStatus.Hold && requested == WeaponStatus.Tight
            ? WeaponStatus.Hold : requested;

    internal static void Execute(Action changeStatus, Action<Exception> report)
    {
        bool previous = preservingHold;
        try
        {
            preservingHold = true;
            changeStatus();
        }
        catch (Exception error) { ErrorReporting.Report(report, error); }
        finally { preservingHold = previous; }
    }
}
