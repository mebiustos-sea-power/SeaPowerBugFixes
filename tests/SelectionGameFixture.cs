using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MebiusTosBugFixes.Fixes.Selection;
using SeaPower;
using SeapowerUI.ViewModels;

// Execute the native command callback and camera's original no-op guard.
// Rendering and selection notifications are managed boundaries, not a running Unity scene.
internal static class SelectionGameFixture
{
    internal sealed class Unit { }
    internal sealed class Scene
    {
        public Unit? Camera { get; private set; }
        public bool Attached { get; private set; }
        internal object? Panel, MapSelection;
        internal int CameraChanges, SelectionChanges;
        internal (bool, bool, bool) Arguments;
        internal Scene(Unit camera) { Camera = camera; Attached = true; Panel = MapSelection = camera; }
        public void Switch(Unit unit, bool first, bool second, bool third)
        { Arguments = (first, second, third); cameraGuard(this, unit, first, second, third); }
        public void Attach(Unit unit)
        {
            CameraChanges++;
            bool changed = !ReferenceEquals(Camera, unit);
            Camera = unit; Attached = true;
            if (changed) Panel = MapSelection = unit;
        }
        internal void Select(object? parameter)
        {
            if (!(parameter is Unit unit) || ReferenceEquals(MapSelection, unit)) return;
            SelectionChanges++; Panel = MapSelection = unit;
        }
    }
    public static Scene Current { get; set; } = null!;
    public static bool Equal(Unit? left, Unit? right) => ReferenceEquals(left, right);
    private static Action<Scene, Unit, bool, bool, bool> cameraGuard = null!;

    internal static Action<object?> ReadNativeCommand()
    {
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var onStart = InstructionReader.Read(typeof(MainGameViewModel).GetMethod("OnStart", flags)!);
        int setter = onStart.FindIndex(i => i.Calls(FormationUnitReselectionFix.Target));
        if (setter < 0) throw new InvalidOperationException("The native formation command is no longer assigned in OnStart.");
        var callback = (MethodInfo)onStart.Take(setter).Last(i => i.opcode == OpCodes.Ldftn).operand;

        var original = InstructionReader.Read(typeof(RenderPosition).GetMethod("switchToObject")!);
        int boundary = original.FindIndex(i => i.operand is MethodInfo m && m.Name == "isWeapon") - 1;
        if (boundary <= 0) throw new InvalidOperationException("The game's camera guard cannot be isolated.");
        var guard = new DynamicMethod("NativeCameraNoOpGuard", typeof(void),
            new[] { typeof(Scene), typeof(Unit), typeof(bool), typeof(bool), typeof(bool) }, typeof(SelectionGameFixture).Module, true);
        var generator = guard.GetILGenerator();
        var labels = new Dictionary<Label, Label>();
        Label LabelFor(Label label)
        { if (!labels.TryGetValue(label, out var mapped)) labels[label] = mapped = generator.DefineLabel(); return mapped; }
        foreach (var i in original.Take(boundary)) Emit(generator, i, LabelFor);
        foreach (Label label in InstructionReader.Labels(original[boundary])) generator.MarkLabel(LabelFor(label));
        generator.Emit(OpCodes.Ldarg_0); generator.Emit(OpCodes.Ldarg_1);
        generator.Emit(OpCodes.Callvirt, typeof(Scene).GetMethod(nameof(Scene.Attach))!); generator.Emit(OpCodes.Ret);
        cameraGuard = (Action<Scene, Unit, bool, bool, bool>)guard.CreateDelegate(typeof(Action<Scene, Unit, bool, bool, bool>));

        var command = new DynamicMethod("NativeFormationCommand", typeof(void), new[] { typeof(object), typeof(object) },
            typeof(SelectionGameFixture).Module, true);
        generator = command.GetILGenerator(); labels.Clear();
        foreach (var local in callback.GetMethodBody()!.LocalVariables)
            generator.DeclareLocal(local.LocalType == typeof(ObjectBase) ? typeof(Unit) : local.LocalType);
        foreach (var i in InstructionReader.Read(callback)) Emit(generator, i, LabelFor);
        var invoke = (Action<object?, object?>)command.CreateDelegate(typeof(Action<object?, object?>));
        return parameter => invoke(null, parameter);
    }

    private static void Emit(ILGenerator generator, CodeInstruction instruction, Func<Label, Label> labelFor)
    {
        foreach (Label label in InstructionReader.Labels(instruction)) generator.MarkLabel(labelFor(label));
        var operand = instruction.operand;
        if (operand == null) generator.Emit(instruction.opcode);
        else if (operand is Label label) generator.Emit(instruction.opcode, labelFor(label));
        else if (operand is Type type && type == typeof(ObjectBase)) generator.Emit(instruction.opcode, typeof(Unit));
        else if (operand is MethodInfo method)
        {
            MethodInfo mapped;
            if (method.DeclaringType == typeof(UnityEngine.Object) && method.Name == "op_Equality")
                mapped = typeof(SelectionGameFixture).GetMethod(nameof(Equal))!;
            else if (method.DeclaringType == typeof(RenderPosition) && method.Name == "get_SelectedObject")
                mapped = typeof(Scene).GetProperty(nameof(Scene.Camera))!.GetGetMethod()!;
            else if (method.DeclaringType == typeof(RenderPosition) && method.Name == "get_IsCameraAttachedToTarget")
                mapped = typeof(Scene).GetProperty(nameof(Scene.Attached))!.GetGetMethod()!;
            else if (method.DeclaringType == typeof(RenderPosition) && method.Name == "switchToObject")
                mapped = typeof(Scene).GetMethod(nameof(Scene.Switch))!;
            else if (method.DeclaringType == typeof(Singleton<RenderPosition>) && method.Name == "get_Instance")
                mapped = typeof(SelectionGameFixture).GetProperty(nameof(Current))!.GetGetMethod()!;
            else throw new InvalidOperationException("An unmapped native selection dependency would run: " + method);
            generator.Emit(instruction.opcode, mapped);
        }
        else throw new InvalidOperationException("Unexpected native selection instruction: " + instruction);
    }
}
