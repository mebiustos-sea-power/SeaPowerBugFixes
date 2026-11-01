using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MebiusTosBugFixes.Fixes.Combat;
using SeaPower;
using SeapowerUI.ViewModels;
using WeaponStatus = SeaPower.ObjectBase.WeaponStatus;

internal static class CeaseFireGameFixture
{
    public sealed class Unit
    {
        public WeaponStatus Status;
        public bool Following { get; set; }
        public bool IsLeader { get; set; }
        public bool AcceptsOrders { get; set; } = true;
        public Formation Formation { get; set; } = null!;
        public int Stops, PendingAttacks = 2;
        public bool[] StopArguments = Array.Empty<bool>();
        public void Stop(bool a, bool b, bool c, bool d, bool e, bool f)
        { Stops++; PendingAttacks = 0; StopArguments = new[] { a, b, c, d, e, f }; }
        public void SetStatus(WeaponStatus status, bool formationCheck) => Setter(this, status, formationCheck);
    }

    public sealed class Formation
    {
        public UnitFormation.ControlMode Mode { get; set; }
        public Collection<Station> Stations { get; } = new();
    }

    public sealed class Station { public Unit? Unit { get; set; } }
    public sealed class View { public Unit Unit = null!; }

    internal static Action<Unit, WeaponStatus, bool> Setter = null!;
    internal static MethodInfo Command => CeaseFirePreservesWeaponsHoldFix.FindCommand(InstructionReader.Read);

    public static void Store(Unit unit, WeaponStatus status) =>
        unit.Status = CeaseFireWeaponStatus.Resolve(unit.Status, status);

    public static void Apply(Unit unit, WeaponStatus status, bool formationCheck) =>
        CeaseFireWeaponStatus.Execute(() => Setter(unit, status, formationCheck),
            error => throw new Exception("Unexpected fixture setter failure", error));

    public static bool Equal(Unit? left, Unit? right) => ReferenceEquals(left, right);
    public static void Log(string message, SeaPower.Log.Section section, string file, int line, string member) { }

    internal static Action<Unit, WeaponStatus, bool> ReadSetter(bool repaired) =>
        (Action<Unit, WeaponStatus, bool>)Compile(CeaseFirePreservesWeaponsHoldFix.Setter,
            repaired ? CeaseFirePreservesWeaponsHoldFix.RewriteSetter : null,
            new[] { typeof(Unit), typeof(WeaponStatus), typeof(bool) }, typeof(Action<Unit, WeaponStatus, bool>));

    internal static Action<View, object?> ReadCommand(bool repaired) =>
        (Action<View, object?>)Compile(Command,
            repaired ? CeaseFirePreservesWeaponsHoldFix.RewriteCommand : null,
            new[] { typeof(View), typeof(object) }, typeof(Action<View, object?>));

    private static Delegate Compile(MethodInfo method, Func<IEnumerable<CodeInstruction>, IEnumerable<CodeInstruction>>? rewrite,
        Type[] arguments, Type delegateType)
    {
        var code = InstructionReader.Read(method);
        if (rewrite != null) code = rewrite(code).ToList();
        var generated = new DynamicMethod("CeaseFireFixture_" + method.Name, typeof(void), arguments,
            typeof(CeaseFireGameFixture).Module, true);
        var il = generated.GetILGenerator();
        foreach (var local in method.GetMethodBody()!.LocalVariables) il.DeclareLocal(MapType(local.LocalType));
        var labels = new Dictionary<Label, Label>();
        Label LabelFor(Label label)
        { if (!labels.TryGetValue(label, out var mapped)) labels[label] = mapped = il.DefineLabel(); return mapped; }

        foreach (var instruction in code)
        {
            var blocks = InstructionReader.Blocks(instruction).Cast<ExceptionBlock>().ToArray();
            foreach (var block in blocks)
            {
                if (block.blockType == ExceptionBlockType.BeginExceptionBlock) il.BeginExceptionBlock();
                else if (block.blockType == ExceptionBlockType.BeginFinallyBlock) il.BeginFinallyBlock();
                else if (block.blockType != ExceptionBlockType.EndExceptionBlock)
                    throw new InvalidOperationException("Unexpected native cease-fire exception boundary.");
            }
            foreach (Label label in InstructionReader.Labels(instruction)) il.MarkLabel(LabelFor(label));
            if (instruction.opcode != OpCodes.Endfinally) Emit(il, instruction, LabelFor);
            foreach (var block in blocks)
                if (block.blockType == ExceptionBlockType.EndExceptionBlock) il.EndExceptionBlock();
        }
        return generated.CreateDelegate(delegateType);
    }

    private static Type MapType(Type type)
    {
        if (type == typeof(ObjectBase)) return typeof(Unit);
        if (type == typeof(UnitFormation)) return typeof(Formation);
        if (type == typeof(SeaPower.Station)) return typeof(Station);
        if (type == typeof(ObjectBaseViewModel)) return typeof(View);
        if (type.IsGenericType)
            return type.GetGenericTypeDefinition().MakeGenericType(type.GetGenericArguments().Select(MapType).ToArray());
        return type;
    }

    private static MethodInfo MapMethod(MethodInfo method)
    {
        MethodInfo Getter(Type type, string property) => type.GetProperty(property)!.GetGetMethod()!;
        if (method.DeclaringType == typeof(UnityEngine.Object) && method.Name == "op_Equality")
            return typeof(CeaseFireGameFixture).GetMethod(nameof(Equal))!;
        if (method.DeclaringType == typeof(ObjectBase))
            return method.Name switch
            {
                "get_FollowingFormation" => Getter(typeof(Unit), nameof(Unit.Following)),
                "get_Formation" => Getter(typeof(Unit), nameof(Unit.Formation)),
                "get_IsFormationLeader" => Getter(typeof(Unit), nameof(Unit.IsLeader)),
                "get_AcceptsOrdersFromPlayer" => Getter(typeof(Unit), nameof(Unit.AcceptsOrders)),
                "CeaseFire" => typeof(Unit).GetMethod(nameof(Unit.Stop))!,
                "SetWeaponStatus" => typeof(Unit).GetMethod(nameof(Unit.SetStatus))!,
                _ => throw new InvalidOperationException("Unmapped unit dependency: " + method)
            };
        if (method.DeclaringType == typeof(UnitFormation))
            return method.Name switch
            {
                "get_SelectedControlMode" => Getter(typeof(Formation), nameof(Formation.Mode)),
                "get_Stations" => Getter(typeof(Formation), nameof(Formation.Stations)),
                _ => throw new InvalidOperationException("Unmapped formation dependency: " + method)
            };
        if (method.DeclaringType == typeof(SeaPower.Station) && method.Name == "get_UnitObject")
            return Getter(typeof(Station), nameof(Station.Unit));
        if (method.DeclaringType == typeof(CeaseFirePreservesWeaponsHoldFix))
            return typeof(CeaseFireGameFixture).GetMethod(method.Name)!;
        if (method.DeclaringType == typeof(SeaPower.Log) && method.Name == "Info")
            return typeof(CeaseFireGameFixture).GetMethod(nameof(Log))!;
        var declaring = MapType(method.DeclaringType!);
        if (declaring != method.DeclaringType)
            return declaring.GetMethods().Single(candidate => candidate.MetadataToken == method.MetadataToken);
        if (method.DeclaringType!.Assembly == typeof(ObjectBase).Assembly ||
            method.DeclaringType.Assembly == typeof(UnityEngine.Object).Assembly)
            throw new InvalidOperationException("An unmapped game dependency would execute: " + method);
        return method;
    }

    private static void Emit(ILGenerator il, CodeInstruction instruction, Func<Label, Label> labelFor)
    {
        var op = instruction.opcode;
        switch (instruction.operand)
        {
            case null: il.Emit(op); break;
            case Label label: il.Emit(op, labelFor(label)); break;
            case string text: il.Emit(op, text); break;
            case sbyte value: il.Emit(op, value); break;
            case int value when op.OperandType == OperandType.ShortInlineVar || op.OperandType == OperandType.InlineVar:
                il.Emit(op, (short)value); break;
            case int value: il.Emit(op, value); break;
            case MethodInfo method: il.Emit(op, MapMethod(method)); break;
            case FieldInfo field when field.DeclaringType == typeof(ObjectBase) && field.Name == "_weaponStatus":
                il.Emit(op, typeof(Unit).GetField(nameof(Unit.Status))!); break;
            case FieldInfo field when field.DeclaringType == typeof(ObjectBaseViewModel) && field.Name == "_objectBase":
                il.Emit(op, typeof(View).GetField(nameof(View.Unit))!); break;
            default: throw new InvalidOperationException("Unexpected cease-fire instruction: " + instruction);
        }
    }
}
