using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using System.Threading;
using HarmonyLib;
using MebiusTosBugFixes.Fixes.Combat;
using ObjectBase = SeaPower.ObjectBase;
using UnitFormation = SeaPower.UnitFormation;
using WeaponStatus = SeaPower.ObjectBase.WeaponStatus;
using static Program;
using static CeaseFireGameFixture;

internal static class CeaseFireTests
{
    internal static void Run()
    {
        CommandsPreserveEachUnitsHold();
        ManualStatusChangesRemainAvailable();
        ProtectionIsScopedAndFailuresAreContained();
        UnsupportedGameChangesAreRejected();
    }

    private static void CommandsPreserveEachUnitsHold()
    {
        var nativeSetter = ReadSetter(false);
        var repairedSetter = ReadSetter(true);
        var nativeCommand = ReadCommand(false);
        var repairedCommand = ReadCommand(true);
        int cases = 0;
        foreach (UnitFormation.ControlMode mode in Enum.GetValues(typeof(UnitFormation.ControlMode)))
        foreach (bool following in new[] { false, true })
        foreach (bool leader in new[] { false, true })
        foreach (WeaponStatus selected in Enum.GetValues(typeof(WeaponStatus)))
        {
            Unit[] Scene()
            {
                var formation = new Formation { Mode = mode };
                var units = new[] { new Unit { Status = selected, Following = following, IsLeader = leader },
                    new Unit { Status = WeaponStatus.Free }, new Unit { Status = WeaponStatus.Tight },
                    new Unit { Status = WeaponStatus.Hold } };
                foreach (var unit in units) { unit.Formation = formation; formation.Stations.Add(new Station { Unit = unit }); }
                formation.Stations.Add(new Station());
                return units;
            }
            var before = Scene(); var native = Scene(); var repaired = Scene();
            Setter = nativeSetter; nativeCommand(new View { Unit = native[0] }, null);
            Require(native[0].Status == WeaponStatus.Tight, "The original menu did not reproduce its unconditional Weapons Tight request.");
            Setter = repairedSetter; repairedCommand(new View { Unit = repaired[0] }, null);
            for (int i = 0; i < before.Length; i++)
            {
                var expected = before[i].Status == WeaponStatus.Hold ? WeaponStatus.Hold : native[i].Status;
                Require(repaired[i].Status == expected, "Cease Fire loosened a unit's Hold or changed native formation routing.");
            }
            Require(repaired[0].Stops == 1 && repaired[0].PendingAttacks == 0 &&
                repaired[0].StopArguments.Length == 6 && repaired[0].StopArguments.All(value => value),
                "The native stop action or its arguments were changed.");
            repairedCommand(new View { Unit = repaired[0] }, null);
            Require(repaired[0].Stops == 2 && repaired[0].Status == (selected == WeaponStatus.Hold ? WeaponStatus.Hold : WeaponStatus.Tight),
                "Repeated Cease Fire did not preserve its behavior.");
            cases++;
        }
        var excluded = new Unit { Status = WeaponStatus.Hold, AcceptsOrders = false };
        Setter = repairedSetter; repairedCommand(new View { Unit = excluded }, null);
        Require(excluded.Stops == 0 && excluded.Status == WeaponStatus.Hold, "A unit that rejects player orders was changed.");
        Console.WriteLine("PASS cease fire: native menu/setter IL, " + cases + " formation/control cases, mixed Hold states, repeated commands and native stop arguments");
    }

    private static void ManualStatusChangesRemainAvailable()
    {
        var setter = ReadSetter(true);
        foreach (WeaponStatus requested in Enum.GetValues(typeof(WeaponStatus)))
        {
            var unit = new Unit { Status = WeaponStatus.Hold };
            setter(unit, requested, false);
            Require(unit.Status == requested, "An ordinary manual weapon-status change was blocked by the cease-fire fix.");
        }
        Console.WriteLine("PASS manual ROE: Hold can still be changed explicitly to Tight or Free outside Cease Fire");
    }

    private static void ProtectionIsScopedAndFailuresAreContained()
    {
        var errors = new List<Exception>();
        CeaseFireWeaponStatus.Execute(() =>
        {
            Require(CeaseFireWeaponStatus.Resolve(WeaponStatus.Hold, WeaponStatus.Tight) == WeaponStatus.Hold, "Cease-fire protection was missing.");
            CeaseFireWeaponStatus.Execute(() => throw new InvalidOperationException("Inner setter failed"), errors.Add);
            Require(CeaseFireWeaponStatus.Resolve(WeaponStatus.Hold, WeaponStatus.Tight) == WeaponStatus.Hold, "A nested failure ended the outer protection.");
            WeaponStatus concurrent = WeaponStatus.Hold;
            var thread = new Thread(() => concurrent = CeaseFireWeaponStatus.Resolve(WeaponStatus.Hold, WeaponStatus.Tight));
            thread.Start(); thread.Join();
            Require(concurrent == WeaponStatus.Tight, "A different thread's ordinary change inherited cease-fire protection.");
            throw new InvalidOperationException("Outer setter failed");
        }, errors.Add);
        Require(errors.Count == 2 && CeaseFireWeaponStatus.Resolve(WeaponStatus.Hold, WeaponStatus.Tight) == WeaponStatus.Tight,
            "Setter failures escaped, were hidden, or leaked protection into subsequent commands.");
        CeaseFireWeaponStatus.Execute(() => throw new Exception("Setter failed"), _ => throw new Exception("Logger failed"));
        Require(CeaseFireWeaponStatus.Resolve(WeaponStatus.Hold, WeaponStatus.Tight) == WeaponStatus.Tight,
            "A logging failure left protection active.");
        Console.WriteLine("PASS cease-fire failures: nesting, thread isolation, restored state, error reporting and failing logger containment");
    }

    private static void UnsupportedGameChangesAreRejected()
    {
        var original = InstructionReader.Read(CeaseFirePreservesWeaponsHoldFix.Setter);
        var missing = original.Where(i => i.opcode != OpCodes.Stfld).ToList();
        Throws<InvalidOperationException>(() => CeaseFirePreservesWeaponsHoldFix.RewriteSetter(missing).ToList());
        Require(original.Count(i => i.opcode == OpCodes.Stfld) == 2, "A rejected patch mutated incoming native instructions.");
        var command = InstructionReader.Read(CeaseFireGameFixture.Command);
        var changed = command.Where(i => !i.Calls(CeaseFirePreservesWeaponsHoldFix.Setter)).ToList();
        Throws<InvalidOperationException>(() => CeaseFirePreservesWeaponsHoldFix.RewriteCommand(changed).ToList());
        Console.WriteLine("PASS cease-fire compatibility: changed command/setter rejected before modification");
    }

    internal static void InstallOnGameMono()
    {
        UnityProfilerBoundary.Register();
        var patches = new Harmony("mebiustos.seapower.bugfixes.tests.ceasefire");
        try
        {
            new CeaseFirePreservesWeaponsHoldFix().Install(patches);
            Require(Harmony.GetPatchInfo(CeaseFirePreservesWeaponsHoldFix.Setter)!.Owners.Contains(patches.Id) &&
                Harmony.GetPatchInfo(CeaseFirePreservesWeaponsHoldFix.Command)!.Owners.Contains(patches.Id),
                "The cease-fire patches did not both install.");
#pragma warning disable SYSLIB0050
            var unit = (ObjectBase)FormatterServices.GetUninitializedObject(typeof(SeaPower.Vessel));
#pragma warning restore SYSLIB0050
            unit._weaponStatus = WeaponStatus.Hold;
            CeaseFirePreservesWeaponsHoldFix.Apply(unit, WeaponStatus.Tight, false);
            Require(unit._weaponStatus == WeaponStatus.Hold, "The actual patched setter did not preserve Hold during Cease Fire.");
            unit.SetWeaponStatus(WeaponStatus.Free, false);
            Require(unit._weaponStatus == WeaponStatus.Free, "An actual manual change was blocked after Cease Fire.");
            CeaseFirePreservesWeaponsHoldFix.Apply(unit, WeaponStatus.Tight, false);
            Require(unit._weaponStatus == WeaponStatus.Tight, "The actual patched setter did not lower Free to Tight.");
            Console.WriteLine("PASS game Mono: actual cease-fire command/setter patch installation and actual status-write interception");
        }
        finally { patches.UnpatchSelf(); }
    }
}
