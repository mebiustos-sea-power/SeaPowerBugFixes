using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Noesis;
using SeaPower;
using SeapowerUI;
using SeapowerUI.ViewModels;

namespace MebiusTosBugFixes.Fixes.WorldView;

internal sealed class WorldViewWindowHostFix : IBugFix
{
    public string Id => "WorldViewWindowHost";
    private static readonly OwnedHostCache<MainGameViewModel, FrameworkElement, WindowHost> Hosts =
        new(model => model.RootControl, FindHost,
            error => ModLog.Error("WorldViewWindowHost: synchronization skipped; battle host resolution failed", error));
    internal static bool Applied { get; private set; }
    private static bool rewriteFailureReported;

    internal static MethodInfo Target => typeof(MainGameViewModel).GetMethod("SyncWorldViewWindow",
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null)
        ?? throw new MissingMethodException(typeof(MainGameViewModel).FullName, "SyncWorldViewWindow()");
    internal static MethodInfo Exists => typeof(Singleton<WindowHostManager>).GetMethod("InstanceExists", BindingFlags.Public | BindingFlags.Static,
        null, new[] { typeof(bool) }, null) ?? throw new MissingMethodException("WindowHostManager.InstanceExists(Boolean)");
    internal static MethodInfo Instance => typeof(Singleton<WindowHostManager>).GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetGetMethod()
        ?? throw new MissingMethodException("WindowHostManager.Instance");
    internal static MethodInfo CurrentHost => typeof(WindowHostManager).GetProperty("CurrentHost")?.GetGetMethod()
        ?? throw new MissingMethodException("WindowHostManager.CurrentHost");
    internal static MethodInfo Resolver => typeof(WorldViewWindowHostFix).GetMethod(nameof(ResolveHost), BindingFlags.Static | BindingFlags.NonPublic)!;

    public void Install(Harmony patches)
    {
        if (Target.IsStatic || Target.ReturnType != typeof(void))
            throw new InvalidOperationException("SyncWorldViewWindow has an unsupported signature.");
        Applied = false;
        patches.Patch(Target, transpiler: new HarmonyMethod(typeof(WorldViewWindowHostFix), nameof(Transpile)) { priority = Priority.Last });
        if (!Applied) throw new InvalidOperationException("World-view patch was not applied; see the compatibility error above.");
    }

    internal static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
    {
        try
        {
            var replacement = WorldViewHostTranspiler.Rewrite(instructions, Exists, Instance, CurrentHost, Resolver);
            Applied = true;
            rewriteFailureReported = false;
            return replacement;
        }
        catch (Exception error)
        {
            Applied = false;
            if (!rewriteFailureReported)
            {
                rewriteFailureReported = true;
                ModLog.Error("WorldViewWindowHost: patch skipped for this method rebuild; incoming game instructions preserved", error);
            }
            // Harmony can rebuild this method later for another mod. Never
            // propagate our compatibility failure into that mod's patch call.
            return instructions;
        }
    }
    private static WindowHost? ResolveHost(MainGameViewModel model)
    {
        Hosts.TryResolve(model, out WindowHost? host);
        return host;
    }

    private static WindowHost FindHost(FrameworkElement root)
    {
        var views = new List<MainGameView>();
        FindViews(root, views);
        if (views.Count != 1)
            throw new InvalidOperationException("Expected one MainGameView in the battle root, found " + views.Count + ".");
        MainGameView view = views[0];
        // The behavior belongs to this particular battle view. A shared global
        // host can belong to an already-closed pause menu instead.
        var hosts = Noesis.Interactivity.Interaction.GetBehaviors(view).OfType<WindowHost>().ToArray();
        if (hosts.Length != 1 || hosts[0].AssociatedObject != view)
            throw new InvalidOperationException("MainGameView does not own exactly one attached WindowHost.");
        return hosts[0];
    }

    private static void FindViews(DependencyObject node, List<MainGameView> result)
    {
        if (node is MainGameView view) { result.Add(view); return; }
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            FindViews(VisualTreeHelper.GetChild(node, i), result);
    }
}
