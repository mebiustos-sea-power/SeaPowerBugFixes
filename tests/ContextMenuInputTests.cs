using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using HarmonyLib;
using MebiusTosBugFixes.Fixes.Input;
using MainGameViewModel = SeapowerUI.ViewModels.MainGameViewModel;
using static Program;

internal static class ContextMenuInputTests
{
    internal static void Run()
    {
        RefreshRunsAfterDismissalUsingCurrentPointer();
        UiAndViewLifetimesKeepTheirInputRestrictions();
        DuplicateNotificationsDoNotCreatePolling();
        FailuresStayInsideTheModAndCanRecover();
    }

    private static void RefreshRunsAfterDismissalUsingCurrentPointer()
    {
        var frame = new Queue<Action>();
        bool pointerOverUi = true, inputBlocked = true;
        int nativeRefreshes = 0;
        var errors = new List<Exception>();
        var refresh = new DeferredHoverRefresh((run, _) => frame.Enqueue(run), () => true,
            () => { nativeRefreshes++; inputBlocked = pointerOverUi; }, errors.Add);

        // The closing item is still under the pointer in the original button-up
        // event. Dismissal removes it before the next rendered frame.
        refresh.Request();
        Require(inputBlocked && nativeRefreshes == 0, "Hover was refreshed before the closing menu disappeared.");
        pointerOverUi = false;
        frame.Dequeue()();
        Require(!inputBlocked && nativeRefreshes == 1 && errors.Count == 0,
            "The stale menu hit still blocks camera input after the deferred native refresh.");

        // Use the pointer's position when the deferred callback runs.
        refresh.Request();
        pointerOverUi = true;
        frame.Dequeue()();
        Require(inputBlocked && nativeRefreshes == 2, "Closing a menu incorrectly enabled input over another UI element.");
        Console.WriteLine("PASS menu dismissal: stale UI input gate clears after dismissal; actual current UI hit still blocks camera input");
    }

    private static void UiAndViewLifetimesKeepTheirInputRestrictions()
    {
        foreach (string situation in new[] { "menu reopened", "view hidden", "battle replaced" })
        {
            var frame = new Queue<Action>();
            bool current = true; int calls = 0;
            var refresh = new DeferredHoverRefresh((run, _) => frame.Enqueue(run), () => current, () => calls++, _ => { });
            refresh.Request();
            current = false;
            frame.Dequeue()();
            Require(calls == 0, "A queued refresh modified input after " + situation + ".");
            current = true;
            refresh.Request(); frame.Dequeue()();
            Require(calls == 1, "A later valid menu close did not recover after " + situation + ".");
        }
        var queue = new Queue<Action>(); int touchedDisposedUi = 0;
        var disposed = new DeferredHoverRefresh((run, _) => queue.Enqueue(run),
            () => { touchedDisposedUi++; return true; }, () => touchedDisposedUi++, _ => { });
        disposed.Request(); disposed.Dispose(); queue.Dequeue()(); disposed.Request();
        Require(touchedDisposedUi == 0 && queue.Count == 0, "Disposed view callbacks accessed UI or scheduled more work.");
        Console.WriteLine("PASS menu lifecycle: reopened menus, hidden/replaced views and destroyed bindings are left alone");
    }

    private static void DuplicateNotificationsDoNotCreatePolling()
    {
        var frame = new Queue<Action>(); int calls = 0;
        var refresh = new DeferredHoverRefresh((run, _) => frame.Enqueue(run), () => true, () => calls++, _ => { });
        refresh.Request(); refresh.Request(); refresh.Request();
        Require(frame.Count == 1, "Duplicate close notifications scheduled redundant refreshes.");
        frame.Dequeue()();
        Require(calls == 1 && frame.Count == 0, "Menu recovery scheduled continuing work.");
        refresh.Request(); frame.Dequeue()();
        Require(calls == 2 && frame.Count == 0, "A subsequent close was lost or created a permanent loop.");
        Console.WriteLine("PASS menu scheduling: one bounded refresh per dismissal, coalesced notifications, no recurring work");
    }

    private static void FailuresStayInsideTheModAndCanRecover()
    {
        var errors = new List<Exception>(); var queue = new Queue<Action>();
        bool failPost = true, failNative = false, failEligibility = false; int calls = 0;
        Action<Exception>? asynchronousFailure = null;
        var refresh = new DeferredHoverRefresh((run, failed) =>
        {
            if (failPost) throw new InvalidOperationException("Scheduling failed");
            asynchronousFailure = failed; queue.Enqueue(run);
        }, () => failEligibility ? throw new InvalidOperationException("UI access failed") : true,
            () => { if (failNative) throw new InvalidOperationException("Native refresh failed"); calls++; }, errors.Add);
        refresh.Request();
        Require(errors.Count == 1, "A synchronous scheduler failure escaped or was hidden.");
        failPost = false; refresh.Request(); failNative = true; queue.Dequeue()();
        Require(errors.Count == 2, "A native hover failure escaped or was hidden.");
        failNative = false; failEligibility = true; refresh.Request(); queue.Dequeue()();
        Require(errors.Count == 3, "An eligibility failure escaped or was hidden.");
        failEligibility = false; refresh.Request();
        queue.Clear(); asynchronousFailure!(new InvalidOperationException("Frame scheduler failed"));
        Require(errors.Count == 4, "An asynchronous scheduler failure escaped or was hidden.");
        refresh.Request(); queue.Dequeue()();
        Require(calls == 1 && errors.Count == 4, "A later menu close did not recover after failures.");
        var badLogger = new DeferredHoverRefresh((run, _) => run(), () => true,
            () => throw new Exception("Refresh failed"), _ => throw new Exception("Logging failed"));
        badLogger.Request();
        Console.WriteLine("PASS menu failures: scheduling, UI/native access and logger exceptions stay inside the mod; later closes can recover");
    }

    internal static void InstallRootSetterOnGameMono()
    {
        var patches = new Harmony("mebiustos.seapower.bugfixes.tests.menu");
        try
        {
            // Exercise the real late-binding boundary without instantiating UI or
            // forcing a custom Noesis behavior's static initializer.
            patches.Patch(ContextMenuInputRecoveryFix.RootSetter,
                postfix: new HarmonyMethod(typeof(ContextMenuInputRecoveryFix), "RootAssigned"));
#pragma warning disable SYSLIB0050
            var model = (MainGameViewModel)FormatterServices.GetUninitializedObject(typeof(MainGameViewModel));
#pragma warning restore SYSLIB0050
            var caller = new DynamicMethod("AssignNativeBattleRoot", typeof(void),
                new[] { typeof(MainGameViewModel), typeof(Noesis.FrameworkElement) }, typeof(ContextMenuInputTests).Module, true);
            var il = caller.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, ContextMenuInputRecoveryFix.RootSetter); il.Emit(OpCodes.Ret);
            var assign = (Action<MainGameViewModel, Noesis.FrameworkElement?>)caller.CreateDelegate(
                typeof(Action<MainGameViewModel, Noesis.FrameworkElement>));
            assign(model, null);
            Require(model.RootControl == null, "The root-assignment observer changed the game's value.");
            Require(Harmony.GetPatchInfo(ContextMenuInputRecoveryFix.RootSetter)!.Owners.Contains(patches.Id),
                "The native root-assignment hook was not installed.");
            Console.WriteLine("PASS game Mono: real root setter hook installs and accepts cleared UI without native Noesis initialization");
        }
        finally { patches.UnpatchSelf(); }
    }
}
