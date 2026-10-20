using System;
using System.Linq;
using System.Reflection;
using System.Windows.Input;
using HarmonyLib;
using SeaPower;
using SeapowerUI.ViewModels;

namespace MebiusTosBugFixes.Fixes.Selection;

internal sealed class FormationUnitReselectionFix : IBugFix
{
    public string Id => "FormationUnitReselection";
    private static bool bindingFailureReported;

    internal static MethodInfo Target => typeof(MainGameViewModel).GetProperty("SelectUnitCommand")?.GetSetMethod(true)
        ?? throw new MissingMethodException(typeof(MainGameViewModel).FullName, "set_SelectUnitCommand");

    public void Install(Harmony patches)
    {
        var target = Target;
        if (target.IsStatic || target.ReturnType != typeof(void) ||
            !target.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(ICommand) }))
            throw new InvalidOperationException("The formation selection command has an unsupported setter.");
        // Wrap before the command reaches XAML bindings; its setter does not notify existing bindings.
        patches.Patch(target, prefix: new HarmonyMethod(typeof(FormationUnitReselectionFix), nameof(Bind)) { priority = Priority.Last });
    }

    internal static void Bind(MainGameViewModel __instance, ref ICommand __0)
    {
        try
        {
            if (__0 == null || (__0 is ReselectionCommand existing && ReferenceEquals(existing.Owner, __instance))) return;
            var owner = __instance;
            __0 = new ReselectionCommand(owner, __0, parameter => Synchronize(owner, parameter),
                error => ModLog.Error("FormationUnitReselection: selection synchronization failed", error));
            bindingFailureReported = false;
        }
        catch (Exception error)
        {
            if (!bindingFailureReported)
            {
                bindingFailureReported = true;
                ModLog.Error("FormationUnitReselection: could not bind the selection command", error);
            }
        }
    }

    private static void Synchronize(MainGameViewModel model, object? parameter)
    {
        if (!(parameter is ObjectBase unit) || unit == null || !unit.IsPlayerObject) return;
        var map = model.Map ?? throw new InvalidOperationException("The battle map is unavailable.");
        var entry = map.MapKnownUnits.AllUnits.SingleOrDefault(item => ReferenceEquals(item.Unit.Object, unit))
            ?? throw new InvalidOperationException("The selected friendly unit is missing from the current battle map.");
        var current = map.SelectedObject.Value;
        if (ReferenceEquals(current, entry.Unit) || ReferenceEquals(current, unit)) return;
        // The native selection transaction updates highlights, interaction state and the bottom panel together.
        entry.Selected = true;
    }
}
