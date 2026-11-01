using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using SeaPower;
using SeapowerUI.ViewModels;
using WeaponStatus = SeaPower.ObjectBase.WeaponStatus;

namespace MebiusTosBugFixes.Fixes.Combat;

internal sealed class CeaseFirePreservesWeaponsHoldFix : IBugFix
{
    public string Id => "CeaseFirePreservesWeaponsHold";

    internal static MethodInfo Setter => typeof(ObjectBase).GetMethod(nameof(ObjectBase.SetWeaponStatus),
        new[] { typeof(WeaponStatus), typeof(bool) })
        ?? throw new MissingMethodException(typeof(ObjectBase).FullName, nameof(ObjectBase.SetWeaponStatus));

    private static FieldInfo Status => typeof(ObjectBase).GetField(nameof(ObjectBase._weaponStatus))
        ?? throw new MissingFieldException(typeof(ObjectBase).FullName, nameof(ObjectBase._weaponStatus));

    internal static MethodInfo Command => FindCommand(ReadOriginal);

    internal static MethodInfo FindCommand(Func<MethodBase, List<CodeInstruction>> read)
    {
        var menu = typeof(ObjectBaseViewModel).GetMethod("GetContextMenuItems",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingMethodException(typeof(ObjectBaseViewModel).FullName, "GetContextMenuItems");
        var candidates = read(menu).Where(i => i.opcode == OpCodes.Ldftn)
            .Select(i => i.operand).OfType<MethodInfo>().Distinct()
            .Where(method => read(method).Any(i => i.operand is MethodInfo call &&
                call.DeclaringType == typeof(ObjectBase) && call.Name == nameof(ObjectBase.CeaseFire)))
            .Where(method => StatusCall(read(method)) >= 0).ToArray();
        if (candidates.Length != 1)
            throw new InvalidOperationException("The unit Cease Fire menu command is missing or ambiguous.");
        return candidates[0];
    }

    public void Install(Harmony patches)
    {
        var command = Command;
        // Validate both boundaries before installing either patch. Formation routing
        // stays in the native setter; only its final writes receive the policy.
        RewriteCommand(ReadOriginal(command)).ToList();
        RewriteSetter(ReadOriginal(Setter)).ToList();
        patches.Patch(Setter, transpiler: new HarmonyMethod(typeof(CeaseFirePreservesWeaponsHoldFix), nameof(RewriteSetter)));
        patches.Patch(command, transpiler: new HarmonyMethod(typeof(CeaseFirePreservesWeaponsHoldFix), nameof(RewriteCommand)));
    }

    internal static IEnumerable<CodeInstruction> RewriteCommand(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.Select(i => new CodeInstruction(i)).ToList();
        int call = StatusCall(code);
        if (call < 0)
            throw new InvalidOperationException("Unsupported Cease Fire weapon-status change; the game or another mod changed this command.");
        code[call].opcode = OpCodes.Call;
        code[call].operand = typeof(CeaseFirePreservesWeaponsHoldFix).GetMethod(nameof(Apply), BindingFlags.NonPublic | BindingFlags.Static)!;
        return code;
    }

    internal static IEnumerable<CodeInstruction> RewriteSetter(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.Select(i => new CodeInstruction(i)).ToList();
        var status = Status;
        var stores = code.Where(i => i.opcode == OpCodes.Stfld && Equals(i.operand, status)).ToArray();
        if (stores.Length != 2)
            throw new InvalidOperationException("Unsupported weapon-status writes; the game or another mod changed the setter.");
        foreach (var store in stores)
        {
            store.opcode = OpCodes.Call;
            store.operand = typeof(CeaseFirePreservesWeaponsHoldFix).GetMethod(nameof(Store), BindingFlags.NonPublic | BindingFlags.Static)!;
        }
        return code;
    }

    internal static void Apply(ObjectBase unit, WeaponStatus status, bool formationCheck) =>
        CeaseFireWeaponStatus.Execute(() => unit.SetWeaponStatus(status, formationCheck),
            error => ModLog.Error("CeaseFirePreservesWeaponsHold: could not apply the cease-fire weapon status", error));

    private static void Store(ObjectBase unit, WeaponStatus status) =>
        unit._weaponStatus = CeaseFireWeaponStatus.Resolve(unit._weaponStatus, status);

    private static int StatusCall(IList<CodeInstruction> code)
    {
        int found = -1;
        var setter = Setter;
        for (int i = 2; i < code.Count; i++)
        {
            if (!code[i].Calls(setter)) continue;
            if (found >= 0 || code[i - 2].opcode != OpCodes.Ldc_I4_0 || code[i - 1].opcode != OpCodes.Ldc_I4_1)
                return -1;
            found = i;
        }
        return found;
    }

    private static List<CodeInstruction> ReadOriginal(MethodBase method)
    {
        // Use reflection at Harmony's Mono ILGenerator boundary; the plugin's
        // netstandard reference facade cannot name that mscorlib type directly.
        var reader = typeof(PatchProcessor).GetMethods().Single(m => m.Name == "GetOriginalInstructions" &&
            m.GetParameters().Length == 2 && !m.GetParameters()[1].ParameterType.IsByRef);
        return (List<CodeInstruction>)reader.Invoke(null, new object?[] { method, null })!;
    }
}
