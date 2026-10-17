using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

internal static class InstructionReader
{
    private static readonly Dictionary<ushort, OpCode> Codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!).ToDictionary(code => unchecked((ushort)code.Value));

    public static List<CodeInstruction> Read(MethodBase method)
    {
        var body = method.GetMethodBody() ?? throw new InvalidDataException("The original method has no body.");
        var module = method.Module;
        var typeArguments = method.DeclaringType?.GetGenericArguments();
        var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
        var records = new List<(int Offset, CodeInstruction Instruction)>();
        var targets = new HashSet<int>();
        using var stream = new MemoryStream(body.GetILAsByteArray()!);
        using var reader = new BinaryReader(stream);
        while (stream.Position < stream.Length)
        {
            var offset = checked((int)stream.Position);
            ushort number = reader.ReadByte();
            if (number == 0xfe) number = (ushort)(0xfe00 | reader.ReadByte());
            if (!Codes.TryGetValue(number, out var code)) throw new InvalidDataException("Unknown original opcode.");
            object? operand;
            switch (code.OperandType)
            {
                case OperandType.InlineNone: operand = null; break;
                case OperandType.ShortInlineI: operand = reader.ReadSByte(); break;
                case OperandType.InlineI: operand = reader.ReadInt32(); break;
                case OperandType.InlineI8: operand = reader.ReadInt64(); break;
                case OperandType.ShortInlineR: operand = reader.ReadSingle(); break;
                case OperandType.InlineR: operand = reader.ReadDouble(); break;
                case OperandType.InlineString: operand = module.ResolveString(reader.ReadInt32()); break;
                case OperandType.InlineField: operand = module.ResolveField(reader.ReadInt32(), typeArguments, methodArguments); break;
                case OperandType.InlineMethod: operand = module.ResolveMethod(reader.ReadInt32(), typeArguments, methodArguments); break;
                case OperandType.InlineType: operand = module.ResolveType(reader.ReadInt32(), typeArguments, methodArguments); break;
                case OperandType.InlineTok: operand = module.ResolveMember(reader.ReadInt32(), typeArguments, methodArguments); break;
                case OperandType.InlineSig: operand = module.ResolveSignature(reader.ReadInt32()); break;
                case OperandType.ShortInlineVar: operand = (int)reader.ReadByte(); break;
                case OperandType.InlineVar: operand = (int)reader.ReadUInt16(); break;
                case OperandType.ShortInlineBrTarget:
                {
                    var delta = reader.ReadSByte();
                    var target = checked((int)stream.Position + delta);
                    targets.Add(target); operand = target; break;
                }
                case OperandType.InlineBrTarget:
                {
                    var delta = reader.ReadInt32();
                    var target = checked((int)stream.Position + delta);
                    targets.Add(target); operand = target; break;
                }
                case OperandType.InlineSwitch:
                {
                    var count = reader.ReadInt32();
                    var after = checked((int)stream.Position + count * 4);
                    var branches = new int[count];
                    for (var index = 0; index < count; index++) { branches[index] = checked(after + reader.ReadInt32()); targets.Add(branches[index]); }
                    operand = branches; break;
                }
                default: throw new InvalidDataException("Unsupported original operand: " + code.OperandType);
            }
            records.Add((offset, new CodeInstruction(code, operand)));
        }
        var generator = new DynamicMethod("ReadInstructionLabels", typeof(void), Type.EmptyTypes).GetILGenerator();
        var labels = targets.ToDictionary(offset => offset, _ => generator.DefineLabel());
        var byOffset = records.ToDictionary(record => record.Offset, record => record.Instruction);
        foreach (var record in records)
        {
            var instruction = record.Instruction;
            if (labels.TryGetValue(record.Offset, out var label)) Metadata(instruction, "labels").Add(label);
            if (instruction.opcode.OperandType is OperandType.InlineBrTarget or OperandType.ShortInlineBrTarget)
                instruction.operand = labels[(int)instruction.operand];
            else if (instruction.opcode.OperandType == OperandType.InlineSwitch)
                instruction.operand = ((int[])instruction.operand).Select(offset => labels[offset]).ToArray();
        }
        foreach (var clause in body.ExceptionHandlingClauses)
        {
            Metadata(byOffset[clause.TryOffset], "blocks").Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
            var kind = clause.Flags switch
            {
                ExceptionHandlingClauseOptions.Clause => ExceptionBlockType.BeginCatchBlock,
                ExceptionHandlingClauseOptions.Finally => ExceptionBlockType.BeginFinallyBlock,
                ExceptionHandlingClauseOptions.Fault => ExceptionBlockType.BeginFaultBlock,
                ExceptionHandlingClauseOptions.Filter => ExceptionBlockType.BeginExceptFilterBlock,
                _ => throw new InvalidDataException("Unknown exception handler.")
            };
            var handlerOffset = clause.Flags == ExceptionHandlingClauseOptions.Filter ? clause.FilterOffset : clause.HandlerOffset;
            Metadata(byOffset[handlerOffset], "blocks").Add(new ExceptionBlock(kind, clause.Flags == ExceptionHandlingClauseOptions.Clause ? clause.CatchType : null));
            var lastOffset = records.Last(record => record.Offset < clause.HandlerOffset + clause.HandlerLength).Offset;
            Metadata(byOffset[lastOffset], "blocks").Add(new ExceptionBlock(ExceptionBlockType.EndExceptionBlock));
        }
        return records.Select(record => record.Instruction).ToList();
    }

    private static IList Metadata(CodeInstruction instruction, string name) => (IList)typeof(CodeInstruction).GetField(name)!.GetValue(instruction)!;
}
