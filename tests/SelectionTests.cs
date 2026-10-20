using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using MainGameViewModel = SeapowerUI.ViewModels.MainGameViewModel;
using System.Windows.Input;
using HarmonyLib;
using MebiusTosBugFixes.Fixes.Selection;
using static Program;
using static SelectionGameFixture;

internal static class SelectionTests
{
    internal static void Run()
    {
        SameFriendlyUnitCanBeReselected();
        CommandAvailabilityAndEventsArePreserved();
        FailuresStayInsideTheCommand();
    }

    private static void SameFriendlyUnitCanBeReselected()
    {
        var native = ReadNativeCommand();
        var ship = new Unit(); var other = new Unit(); var unknown = new object();
        var scene = new Scene(ship); Current = scene;
        var original = new Command { Run = native };
        scene.Panel = scene.MapSelection = unknown;
        original.Execute(ship);
        Require(ReferenceEquals(scene.Panel, unknown) && ReferenceEquals(scene.Camera, ship) && scene.CameraChanges == 0,
            "The original game callback and guard did not reproduce the unknown-contact panel remaining selected.");

        var errors = new List<Exception>();
        var repaired = new ReselectionCommand(scene, original, scene.Select, errors.Add);
        repaired.Execute(ship);
        Require(ReferenceEquals(scene.Panel, ship) && ReferenceEquals(scene.MapSelection, ship) &&
            ReferenceEquals(scene.Camera, ship) && scene.CameraChanges == 0 && scene.SelectionChanges == 1,
            "Reselecting the camera's current ship did not restore selection without moving the camera.");
        repaired.Execute(ship);
        Require(scene.SelectionChanges == 1, "Repeating an already synchronized selection caused another selection transaction.");
        repaired.Execute(other);
        Require(ReferenceEquals(scene.Panel, other) && ReferenceEquals(scene.Camera, other) && scene.CameraChanges == 1 &&
            scene.Arguments == (false, false, false), "Ordinary native camera switching or its arguments were changed.");
        repaired.Execute(ship);
        repaired.Execute(null);
        Require(ReferenceEquals(scene.Panel, ship) && errors.Count == 0, "Returning to the original ship or a native null request failed.");

        var second = new Scene(ship); Current = second; second.Panel = second.MapSelection = unknown;
        new ReselectionCommand(second, original, second.Select, errors.Add).Execute(ship);
        Require(ReferenceEquals(second.Panel, ship) && ReferenceEquals(scene.Panel, ship), "A second battle synchronized the previous battle.");
        Console.WriteLine("PASS formation selection: native game IL reproduces the stale panel; reselection restores it without camera movement, duplicates or cross-battle state");
    }

    private static void CommandAvailabilityAndEventsArePreserved()
    {
        var parameter = new object(); object? executed = null, synchronized = null;
        var original = new Command { Run = p => executed = p, Allowed = false };
        var command = new ReselectionCommand(new object(), original, p => synchronized = p, _ => throw new Exception("Unexpected error"));
        Require(!command.CanExecute(parameter) && ReferenceEquals(original.AvailabilityParameter, parameter), "Native availability or parameter forwarding was lost.");
        int changes = 0; EventHandler handler = (_, _) => changes++;
        command.CanExecuteChanged += handler;
        original.Allowed = true; original.Raise();
        Require(changes == 1 && command.CanExecute(parameter), "Native availability changes were not forwarded.");
        command.CanExecuteChanged -= handler; original.Raise();
        Require(changes == 1, "Removing a command availability listener leaked its subscription.");
        command.Execute(parameter);
        Require(ReferenceEquals(executed, parameter) && ReferenceEquals(synchronized, parameter), "The original command or synchronization lost its parameter.");
        Console.WriteLine("PASS formation command: availability, notification subscription/removal and original parameters are preserved");
    }

    private static void FailuresStayInsideTheCommand()
    {
        var errors = new List<Exception>(); int synchronized = 0;
        var original = new Command { ThrowAvailability = true, Run = _ => throw new InvalidOperationException("Native command failed") };
        var command = new ReselectionCommand(new object(), original, _ => synchronized++, errors.Add);
        Require(!command.CanExecute(null) && !command.CanExecute(null) && errors.Count == 1, "Availability failures escaped or repeatedly logged.");
        command.Execute(null); command.Execute(null);
        Require(synchronized == 0 && errors.Count == 2, "A failed native command still changed selection or repeatedly logged.");
        original.ThrowAvailability = false; original.Run = _ => { };
        Require(command.CanExecute(null), "Availability did not recover."); command.Execute(null);
        Require(synchronized == 1, "Execution did not recover after the native command recovered.");
        original.Run = _ => throw new InvalidOperationException("Native command failed again"); command.Execute(null);
        Require(errors.Count == 3, "A new failure after recovery was hidden.");
        original.Run = _ => { };
        var badSynchronization = new ReselectionCommand(new object(), original, _ => throw new Exception("Selection failed"), errors.Add);
        badSynchronization.Execute(null);
        Require(errors.Count == 4, "A selection synchronization failure was hidden.");
        var badReporter = new ReselectionCommand(new object(), original, _ => throw new Exception("Selection failed"), _ => throw new Exception("Logger failed"));
        badReporter.Execute(null);
        Console.WriteLine("PASS formation failures: no exceptions reach callers, failed native actions do not synchronize, repeated failures are suppressed and recovery is possible");
    }

    internal static void InstallOnGameMono()
    {
        var patches = new Harmony("mebiustos.seapower.bugfixes.tests.selection");
        try
        {
            new FormationUnitReselectionFix().Install(patches);
            Require(Harmony.GetPatchInfo(FormationUnitReselectionFix.Target)!.Owners.Contains(patches.Id), "The formation command patch did not install on game Mono.");
            // Calling the actual setter through newly generated IL also checks that the
            // tiny native setter is intercepted when its caller is JIT-compiled.
#pragma warning disable SYSLIB0050
            var model = (MainGameViewModel)FormatterServices.GetUninitializedObject(typeof(MainGameViewModel));
            var second = (MainGameViewModel)FormatterServices.GetUninitializedObject(typeof(MainGameViewModel));
#pragma warning restore SYSLIB0050
            var caller = new DynamicMethod("AssignNativeFormationCommand", typeof(void),
                new[] { typeof(MainGameViewModel), typeof(ICommand) }, typeof(SelectionTests).Module, true);
            var il = caller.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, FormationUnitReselectionFix.Target); il.Emit(OpCodes.Ret);
            var assign = (Action<MainGameViewModel, ICommand>)caller.CreateDelegate(typeof(Action<MainGameViewModel, ICommand>));
            var native = new Command();
            assign(model, native);
            Require(model.SelectUnitCommand is ReselectionCommand first && ReferenceEquals(first.Owner, model),
                "The actual command setter did not receive the repair before UI bindings.");
            var bound = model.SelectUnitCommand;
            assign(model, bound);
            Require(ReferenceEquals(model.SelectUnitCommand, bound), "Reassigning the bound command wrapped it twice.");
            assign(second, native);
            Require(second.SelectUnitCommand is ReselectionCommand next && ReferenceEquals(next.Owner, second) &&
                !ReferenceEquals(second.SelectUnitCommand, bound), "Command binding reused the previous battle owner.");
            Console.WriteLine("PASS game Mono: actual setter binds each battle, intercepts a JIT-compiled caller and avoids double wrapping");
        }
        finally { patches.UnpatchSelf(); }
    }

    private sealed class Command : ICommand
    {
        internal Action<object?> Run = _ => { };
        internal bool Allowed = true, ThrowAvailability;
        internal object? AvailabilityParameter;
        public bool CanExecute(object? parameter)
        {
            AvailabilityParameter = parameter;
            if (ThrowAvailability) throw new Exception("Availability failed");
            return Allowed;
        }
        public void Execute(object? parameter) => Run(parameter);
        public event EventHandler? CanExecuteChanged;
        internal void Raise() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
