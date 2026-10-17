using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace MebiusTosBugFixes.Fixes.WorldView;

internal static class WorldViewHostTranspiler
{
    internal static List<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions,
        MethodInfo exists, MethodInfo instance, MethodInfo currentHost, MethodInfo ownedHost)
    {
        var code = instructions.Select(i => new CodeInstruction(i)).ToList();
        int start = 0;
        while (start < code.Count && code[start].opcode == OpCodes.Nop) start++;
        // Remove only the global-manager guard and host lookup. All of the
        // game's display decisions, ordering, commands and error handling stay.
        if (code.Count < start + 7 || code[start].opcode != OpCodes.Ldc_I4_0 ||
            !code[start + 1].Calls(exists) ||
            (code[start + 2].opcode != OpCodes.Brtrue && code[start + 2].opcode != OpCodes.Brtrue_S) ||
            code[start + 3].opcode != OpCodes.Ret || !code[start + 4].Calls(instance) ||
            !code[start + 5].Calls(currentHost) || !IsStoreLocal(code[start + 6]) ||
            code[start + 2].operand == null || !Labels(code[start + 4]).Contains(code[start + 2].operand))
            throw new InvalidOperationException("Unsupported SyncWorldViewWindow host lookup; the game or another mod changed this method.");

        var removed = code.Skip(start).Take(6).ToArray();
        if (removed.Any(i => i.blocks.Count != 0))
            throw new InvalidOperationException("The host lookup crosses an exception boundary.");
        var interior = new HashSet<object>(removed.Skip(1).SelectMany(i => Labels(i).Cast<object>()));
        if (code.Where((_, i) => i < start || i >= start + 6).Any(i => Targets(i, interior)))
            throw new InvalidOperationException("Another control-flow path enters the host lookup.");

        var loadOwner = new CodeInstruction(OpCodes.Ldarg_0);
        foreach (CodeInstruction instruction in removed) instruction.MoveLabelsTo(loadOwner);
        code.RemoveRange(start, 6);
        code.InsertRange(start, new[] { loadOwner, new CodeInstruction(OpCodes.Call, ownedHost) });
        return code;
    }

    private static bool IsStoreLocal(CodeInstruction instruction) =>
        instruction.opcode == OpCodes.Stloc || instruction.opcode == OpCodes.Stloc_S ||
        instruction.opcode == OpCodes.Stloc_0 || instruction.opcode == OpCodes.Stloc_1 ||
        instruction.opcode == OpCodes.Stloc_2 || instruction.opcode == OpCodes.Stloc_3;
    // The installed Harmony was built against Mono's mscorlib Label, which the
    // netstandard reference facade cannot expose directly. Treat labels as
    // opaque values; Harmony still receives its original labels unchanged.
    internal static IList Labels(CodeInstruction instruction) =>
        (IList)(typeof(CodeInstruction).GetField("labels")?.GetValue(instruction)
            ?? throw new MissingFieldException(typeof(CodeInstruction).FullName, "labels"));
    private static bool Targets(CodeInstruction instruction, HashSet<object> labels) =>
        instruction.operand != null && (labels.Contains(instruction.operand) ||
            instruction.opcode == OpCodes.Switch && instruction.operand is Array targets && targets.Cast<object>().Any(labels.Contains));
}
