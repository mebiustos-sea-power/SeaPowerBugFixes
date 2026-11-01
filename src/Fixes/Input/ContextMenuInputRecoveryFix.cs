using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Noesis;
using Noesis.Interactivity;
using SeapowerUI.ViewModels;
using UniRx;

namespace MebiusTosBugFixes.Fixes.Input;

internal sealed class ContextMenuInputRecoveryFix : IBugFix
{
    private static readonly ConditionalWeakTable<MainGameViewModel, Binding> bindings = new();
    private const string HoverBehaviorName = "SeapowerUI.SceneEdit.Behaviours.MouseOverManager";
    public string Id => "ContextMenuInputRecovery";

    internal static MethodInfo RootSetter => typeof(MainGameViewModel).GetProperty(nameof(MainGameViewModel.RootControl))!
        .GetSetMethod(true) ?? throw new MissingMethodException(typeof(MainGameViewModel).FullName, "set_RootControl");
    internal static MethodInfo Destroy => typeof(MainGameViewModel).GetMethod("OnDestroy",
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new MissingMethodException(typeof(MainGameViewModel).FullName, "OnDestroy");

    public void Install(Harmony patches)
    {
        var setter = RootSetter;
        var destroy = Destroy;
        // These are ordinary view-model methods. Do not patch or initialize a
        // custom Noesis behavior before the game registers its native type.
        patches.Patch(setter, postfix: new HarmonyMethod(typeof(ContextMenuInputRecoveryFix), nameof(RootAssigned)));
        patches.Patch(destroy, prefix: new HarmonyMethod(typeof(ContextMenuInputRecoveryFix), nameof(ViewDestroyed)));
    }

    private static void RootAssigned(MainGameViewModel __instance, FrameworkElement? __0)
    {
        try
        {
            if (bindings.TryGetValue(__instance, out var old))
            {
                if (old.Root == __0) return;
                bindings.Remove(__instance);
                old.Dispose();
            }
            if (__0 == null) return;
            var canvas = __0.FindName("ContextMenuCanvas") as Canvas
                ?? throw new InvalidOperationException("The battle context-menu canvas is unavailable.");
            var menu = canvas.ContextMenu
                ?? throw new InvalidOperationException("The battle context menu is unavailable.");
            var refresh = FindNativeRefresh(canvas);
            var binding = new Binding(__instance, __0, canvas, menu, refresh);
            bindings.Add(__instance, binding);
            try { binding.Attach(); }
            catch (Exception)
            {
                bindings.Remove(__instance);
                try { binding.Dispose(); }
                catch (Exception cleanup) { ModLog.Error("ContextMenuInputRecovery: event-binding rollback failed", cleanup); }
                throw;
            }
        }
        catch (Exception error) { ModLog.Error("ContextMenuInputRecovery: could not bind the battle context menu", error); }
    }

    private static void ViewDestroyed(MainGameViewModel __instance)
    {
        try
        {
            if (!bindings.TryGetValue(__instance, out var binding)) return;
            bindings.Remove(__instance);
            binding.Dispose();
        }
        catch (Exception error) { ModLog.Error("ContextMenuInputRecovery: could not detach the battle context menu", error); }
    }

    private static Action FindNativeRefresh(DependencyObject menuOwner)
    {
        // Discover only behavior instances already created by the game's XAML.
        // The nearest ancestor handles this menu's native mouse routing.
        for (DependencyObject? element = menuOwner; element != null; element = VisualTreeHelper.GetParent(element))
        {
            var behaviors = Interaction.GetBehaviors(element);
            if (behaviors == null) continue;
            var matches = behaviors.Cast<Behavior>().Where(behavior => behavior.GetType().Assembly == typeof(MainGameViewModel).Assembly &&
                behavior.GetType().FullName == HoverBehaviorName).ToArray();
            if (matches.Length == 0) continue;
            if (matches.Length != 1) throw new InvalidOperationException("The native UI-hover behavior is ambiguous.");
            var existing = matches[0];
            var method = existing.GetType().GetMethod("UpdateCursor",
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null)
                ?? throw new MissingMethodException(existing.GetType().FullName, "UpdateCursor");
            if (method.ReturnType != typeof(void)) throw new InvalidOperationException("Unsupported native UI-hover refresh signature.");
            return (Action)Delegate.CreateDelegate(typeof(Action), existing, method);
        }
        throw new InvalidOperationException("No existing native UI-hover behavior owns the battle context menu.");
    }

    private sealed class Binding : IDisposable
    {
        internal FrameworkElement Root { get; }
        private readonly MainGameViewModel model;
        private readonly Canvas canvas;
        private readonly ContextMenu menu;
        private readonly DeferredHoverRefresh refresh;
        private readonly RoutedEventHandler closed;
        private bool attached;

        internal Binding(MainGameViewModel model, FrameworkElement root, Canvas canvas, ContextMenu menu, Action nativeRefresh)
        {
            this.model = model; Root = root; this.canvas = canvas; this.menu = menu;
            refresh = new DeferredHoverRefresh(
                (run, failed) => Observable.TimerFrame(1, FrameCountType.EndOfFrame).Subscribe(_ => run(), failed),
                Eligible, nativeRefresh,
                error => ModLog.Error("ContextMenuInputRecovery: UI-hover refresh failed", error));
            closed = (_, _) => refresh.Request();
        }

        internal void Attach() { menu.Closed += closed; attached = true; }

        private bool Eligible()
        {
            if (NoesisTeardown.InProgress || model == null || !ReferenceEquals(SeaPower.Globals._mainGameViewModel, model)) return false;
            return ReferenceEquals(model.RootControl, Root) && model.IsHudVisible && Root.View != null &&
                canvas.ContextMenu == menu && !menu.IsOpen;
        }

        public void Dispose()
        {
            refresh.Dispose();
            if (!attached) return;
            attached = false;
            if (!NoesisTeardown.InProgress) menu.Closed -= closed;
        }
    }
}
