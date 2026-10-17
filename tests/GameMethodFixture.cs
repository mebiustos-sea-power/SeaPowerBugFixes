using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MebiusTosBugFixes.Fixes.WorldView;

// Execute the installed game's actual method IL against managed host/command
// boundaries. No copy of SyncWorldViewWindow's algorithm is maintained here.
internal static class GameMethodFixture
{
    internal sealed class Host { public ObservableCollection<object> Windows { get; } = new(); }
    internal sealed class Model
    {
        public object Map { get; } = new();
        public bool Visible { get; set; }
        public World? World = null; // Assigned by the cloned game IL.
        internal Host Owner = new();
        internal bool OwnerUnavailable;
    }
    internal sealed class World { public Model Main; public World(Model main) => Main = main; }
    internal sealed class Manager
    {
        public static bool Exists(bool _) => Available;
        internal static bool Available = true;
        public static Manager Instance { get; } = new();
        public Host CurrentHost { get; set; } = new();
    }
    internal sealed class Command { public void UpdateCanExecute() => Updates++; }
    internal static readonly List<Exception> Errors = new();
    internal static int Updates;
    public static Command CloseWindowCommand { get; } = new();
    public static bool Equal(Host? first, object? second) => ReferenceEquals(first, second);
    public static Host? ResolveHost(Model model) => model.OwnerUnavailable ? null : model.Owner;
    public static void LogException(Exception error) => Errors.Add(error);

    internal static List<CodeInstruction> Read() => InstructionReader.Read(WorldViewWindowHostFix.Target);
    internal static List<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> original) => WorldViewHostTranspiler.Rewrite(original,
        WorldViewWindowHostFix.Exists, WorldViewWindowHostFix.Instance, WorldViewWindowHostFix.CurrentHost, WorldViewWindowHostFix.Resolver);

    internal static Action<Model> Compile(List<CodeInstruction> code)
    {
        var method = new DynamicMethod("GameWorldViewRoutingFixture", typeof(void), new[] { typeof(Model) }, typeof(GameMethodFixture).Module, true);
        ILGenerator generator = method.GetILGenerator();
        var locals = WorldViewWindowHostFix.Target.GetMethodBody()!.LocalVariables
            .Select(local => generator.DeclareLocal(MapType(local.LocalType), local.IsPinned)).ToArray();
        var labels = new Dictionary<Label, Label>();
        Label MapLabel(Label label)
        {
            if (!labels.TryGetValue(label, out Label mapped)) { mapped = generator.DefineLabel(); labels.Add(label, mapped); }
            return mapped;
        }

        foreach (CodeInstruction instruction in code)
        {
            foreach (ExceptionBlock block in instruction.blocks)
            {
                if (block.blockType == ExceptionBlockType.BeginExceptionBlock) generator.BeginExceptionBlock();
                else if (block.blockType == ExceptionBlockType.BeginCatchBlock) generator.BeginCatchBlock(block.catchType);
                else if (block.blockType != ExceptionBlockType.EndExceptionBlock)
                    throw new NotSupportedException("Unexpected exception block: " + block.blockType);
            }
            foreach (Label label in WorldViewHostTranspiler.Labels(instruction)) generator.MarkLabel(MapLabel(label));
            object? operand = instruction.operand;
            OpCode opcode = instruction.opcode;
            if (operand == null) generator.Emit(opcode);
            else if (operand is Label target) generator.Emit(opcode, MapLabel(target));
            else if (operand is Label[] targets) generator.Emit(opcode, targets.Select(MapLabel).ToArray());
            else if (operand is LocalBuilder local) generator.Emit(opcode, locals[local.LocalIndex]);
            else if (operand is MethodInfo called) generator.Emit(opcode, MapMethod(called));
            else if (operand is ConstructorInfo constructor)
            {
                if (constructor.DeclaringType!.FullName != "SeapowerUI.ViewModels.WorldViewWindowViewModel")
                    throw new NotSupportedException("Unexpected game constructor: " + constructor);
                generator.Emit(opcode, typeof(World).GetConstructor(new[] { typeof(Model) })!);
            }
            else if (operand is FieldInfo field)
            {
                if (field.Name != "_worldViewWindow") throw new NotSupportedException("Unexpected game field: " + field);
                generator.Emit(opcode, typeof(Model).GetField(nameof(Model.World))!);
            }
            else if (operand is int integer) generator.Emit(opcode, integer);
            else if (operand is sbyte small) generator.Emit(opcode, small);
            else if (operand is byte unsigned) generator.Emit(opcode, unsigned);
            else if (operand is string text) generator.Emit(opcode, text);
            else throw new NotSupportedException("Unexpected game IL operand: " + operand);
            foreach (ExceptionBlock block in instruction.blocks)
                if (block.blockType == ExceptionBlockType.EndExceptionBlock) generator.EndExceptionBlock();
        }
        return (Action<Model>)method.CreateDelegate(typeof(Action<Model>));
    }

    private static Type MapType(Type type) => type.FullName == "SeapowerUI.WindowHost" ? typeof(Host) : type;
    private static MethodInfo Getter(Type type, string name) => type.GetProperty(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)!.GetGetMethod()!;
    private static MethodInfo Method(Type type, string name) => type.GetMethod(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)!;
    private static MethodInfo MapMethod(MethodInfo method)
    {
        if (method == WorldViewWindowHostFix.Exists) return Method(typeof(Manager), nameof(Manager.Exists));
        if (method == WorldViewWindowHostFix.Instance) return Getter(typeof(Manager), nameof(Manager.Instance));
        if (method == WorldViewWindowHostFix.CurrentHost) return Getter(typeof(Manager), nameof(Manager.CurrentHost));
        if (method == WorldViewWindowHostFix.Resolver) return Method(typeof(GameMethodFixture), nameof(ResolveHost));
        string type = method.DeclaringType!.FullName!;
        if (type == "SeapowerUI.WindowHost" && method.Name == "get_Windows") return Getter(typeof(Host), nameof(Host.Windows));
        if (type == "Noesis.BaseComponent" && method.Name == "op_Equality") return Method(typeof(GameMethodFixture), nameof(Equal));
        if (type == "SeapowerUI.ViewModels.MainGameViewModel" && method.Name == "get_Map") return Getter(typeof(Model), nameof(Model.Map));
        if (type == "SeapowerUI.ViewModels.MainGameViewModel" && method.Name == "get_ShowPip3D") return Getter(typeof(Model), nameof(Model.Visible));
        if (type == "SeapowerUI.WindowHostCommands" && method.Name == "get_CloseWindowCommand") return Getter(typeof(GameMethodFixture), nameof(CloseWindowCommand));
        if (type == "SeapowerUI.CustomRoutedCommand" && method.Name == "UpdateCanExecute") return Method(typeof(Command), nameof(Command.UpdateCanExecute));
        if (type == "UnityEngine.Debug" && method.Name == "LogException") return Method(typeof(GameMethodFixture), nameof(LogException));
        if (method.DeclaringType.Assembly == typeof(WorldViewWindowHostFix).Assembly ||
            type.StartsWith("SeapowerUI.", StringComparison.Ordinal) || type.StartsWith("SeaPower.", StringComparison.Ordinal) ||
            type.StartsWith("Noesis.", StringComparison.Ordinal) || type.StartsWith("UnityEngine.", StringComparison.Ordinal))
            throw new NotSupportedException("An unmapped game dependency would be invoked: " + method);
        return method;
    }
}
