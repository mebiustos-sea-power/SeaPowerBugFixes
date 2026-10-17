using System;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using MebiusTosBugFixes.Fixes.WorldView;
using static GameMethodFixture;
using static Program;

internal static class RoutingTests
{
    internal static void InstallOnGameMono()
    {
        var patches = new Harmony("mebiustos.seapower.bugfixes.tests");
        try
        {
            new WorldViewWindowHostFix().Install(patches);
            Require(Harmony.GetPatchInfo(WorldViewWindowHostFix.Target)!.Owners.Contains(patches.Id), "Mono did not install the patch.");
            Console.WriteLine("PASS game Mono: Harmony generated and installed the actual game-method patch");
        }
        finally { patches.UnpatchSelf(); }
    }

    internal static void Run()
    {
        var instructions = Read();
        Action<Model> original = Compile(instructions);
        Action<Model> repaired = Compile(WorldViewWindowHostFix.Transpile(instructions).ToList());
        ReproducesOriginalPauseMenuBug(original);
        RoutesToTheBattleAndKeepsOtherWindows(repaired);
        ClosedViewDoesNotCreateAWindow(repaired);
        DoesNotDependOnGlobalManager(repaired);
        MissingOwnerSkipsOnlyTheCurrentSync(repaired);
        UnsupportedBodiesAreRejected(instructions);
        HarmonyCallbackContainsCompatibilityFailures(instructions);
    }

    private static void ReproducesOriginalPauseMenuBug(Action<Model> original)
    {
        var model = new Model { Visible = true };
        model.Owner.Windows.Add(model.Map);
        var pause = new Host();
        Manager.Available = true;
        Manager.Instance.CurrentHost = pause;
        original(model);
        Require(model.World != null && pause.Windows.Contains(model.World) && !model.Owner.Windows.Contains(model.World),
            "The installed game's original method did not reproduce the wrong-host registration.");
        Require(Errors.Count == 0, "Original routing fixture failed with an unrelated error.");
        Console.WriteLine("PASS reproduction: actual original game IL adds the world view to the closed pause menu's host");
    }

    private static void RoutesToTheBattleAndKeepsOtherWindows(Action<Model> repaired)
    {
        var model = new Model { Visible = true };
        var other = new object();
        model.Owner.Windows.Add(model.Map); model.Owner.Windows.Add(other);
        var pause = new Host();
        var pauseItem = new object(); pause.Windows.Add(pauseItem);
        Manager.Instance.CurrentHost = pause;
        int before = Updates;
        repaired(model);
        Require(model.World != null && ReferenceEquals(model.World.Main, model), "World view lost its battle data context.");
        Require(model.Owner.Windows.SequenceEqual(new[] { model.Map, model.World!, other }), "Battle window ordering changed.");
        Require(pause.Windows.Count == 1 && ReferenceEquals(pause.Windows[0], pauseItem) && ReferenceEquals(Manager.Instance.CurrentHost, pause),
            "Fix changed the pause menu's windows or the global host.");
        for (int i = 0; i < 3; i++) { model.Visible = false; repaired(model); model.Visible = true; repaired(model); }
        Require(model.Owner.Windows.Count == 3 && Updates == before + 1, "Repeated open/close duplicated the window or command updates.");
        Require(Errors.Count == 0, "Repaired routing emitted unexpected game errors.");
        Console.WriteLine("PASS repaired game IL: battle-owned registration, original ordering, commands, repeated toggles and unchanged global/menu state");
    }

    private static void ClosedViewDoesNotCreateAWindow(Action<Model> repaired)
    {
        var model = new Model { Visible = false };
        model.Owner.Windows.Add(model.Map);
        repaired(model);
        Require(model.Owner.Windows.Count == 1, "A hidden/detached world view was inserted despite the game's visibility decision.");
        model.Visible = true; repaired(model);
        Require(model.Owner.Windows.Count == 2, "A previously hidden world view could not be opened.");
        Console.WriteLine("PASS original display decision: no insertion while hidden, subsequent opening works");
    }

    private static void DoesNotDependOnGlobalManager(Action<Model> repaired)
    {
        var model = new Model { Visible = true };
        model.Owner.Windows.Add(model.Map);
        Manager.Available = false;
        try { repaired(model); Require(model.Owner.Windows.Count == 2, "A missing global manager blocked the battle's own host."); }
        finally { Manager.Available = true; }
        Console.WriteLine("PASS owned-host routing is independent of the unrelated global-manager guard");
    }

    private static void UnsupportedBodiesAreRejected(System.Collections.Generic.List<CodeInstruction> instructions)
    {
        var changed = instructions.Select(i => new CodeInstruction(i)).ToList();
        changed[1].operand = WorldViewWindowHostFix.Resolver;
        Throws<InvalidOperationException>(() => Rewrite(changed));
        changed = instructions.Select(i => new CodeInstruction(i)).ToList();
        changed.Insert(0, new CodeInstruction(OpCodes.Ldc_I4_1));
        Throws<InvalidOperationException>(() => Rewrite(changed));
        Require(Errors.Count == 0, "Compatibility failures invoked game logging instead of rejecting installation.");
        Console.WriteLine("PASS compatibility: unsupported game/control-flow changes reject the patch explicitly");
    }

    private static void MissingOwnerSkipsOnlyTheCurrentSync(Action<Model> repaired)
    {
        var model = new Model { Visible = true, OwnerUnavailable = true };
        model.Owner.Windows.Add(model.Map);
        var pause = new Host();
        Manager.Instance.CurrentHost = pause;
        int before = Updates;
        repaired(model);
        Require(model.World == null && model.Owner.Windows.Count == 1 && pause.Windows.Count == 0 && Updates == before,
            "A failed host lookup created a window, changed commands or fell back to the pause menu.");
        model.OwnerUnavailable = false;
        repaired(model);
        Require(model.World != null && model.Owner.Windows.Contains(model.World) && pause.Windows.Count == 0,
            "A skipped synchronization did not recover when its host became available.");
        Require(Errors.Count == 0, "A contained host failure reached the game's exception logger.");
        Console.WriteLine("PASS missing owner: original game null guard skips synchronization without side effects and the next sync recovers");
    }

    private static void HarmonyCallbackContainsCompatibilityFailures(System.Collections.Generic.List<CodeInstruction> instructions)
    {
        var changed = instructions.Select(i => new CodeInstruction(i)).ToList();
        changed[1].operand = WorldViewWindowHostFix.Resolver;
        Require(WorldViewWindowHostFix.Transpile(changed).SequenceEqual(changed) && !WorldViewWindowHostFix.Applied,
            "A failed Harmony callback changed the caller's instructions or reported success.");
        Require(WorldViewWindowHostFix.Transpile(instructions).Any() && WorldViewWindowHostFix.Applied,
            "The callback did not recover when supplied a compatible method.");
        Console.WriteLine("PASS Harmony callback: compatibility exceptions contained, incoming IL preserved and compatible rebuild recovers");
    }
}
