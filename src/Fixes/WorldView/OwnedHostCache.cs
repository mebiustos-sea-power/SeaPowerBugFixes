using System;
using System.Runtime.CompilerServices;

namespace MebiusTosBugFixes.Fixes.WorldView;

// A battle model owns its root and host. Cache entries expire with the model;
// replacing a root must invalidate the old host even when discovery then fails.
internal sealed class OwnedHostCache<TModel, TRoot, THost>
    where TModel : class where TRoot : class where THost : class
{
    private sealed class Entry
    {
        internal TRoot? Root;
        internal THost? Host;
        internal bool Reported;
    }
    private readonly ConditionalWeakTable<TModel, Entry> entries = new();
    private readonly Func<TModel, TRoot?> getRoot;
    private readonly Func<TRoot, THost> findHost;
    private readonly Action<Exception> report;

    internal OwnedHostCache(Func<TModel, TRoot?> getRoot, Func<TRoot, THost> findHost, Action<Exception> report)
    { this.getRoot = getRoot; this.findHost = findHost; this.report = report; }

    internal bool TryResolve(TModel model, out THost? host)
    {
        Entry? entry = null;
        host = null;
        try
        {
            entry = entries.GetValue(model, _ => new Entry());
            var root = getRoot(model);
            if (!ReferenceEquals(entry.Root, root))
            { entry.Root = root; entry.Host = null; entry.Reported = false; }
            if (root == null) throw new InvalidOperationException("The battle UI root is unavailable.");
            if (entry.Host == null)
                entry.Host = findHost(root) ?? throw new InvalidOperationException("The battle UI has no window host.");
            entry.Reported = false;
            host = entry.Host;
            return true;
        }
        catch (Exception error)
        {
            if (entry == null || !entry.Reported)
            {
                if (entry != null) entry.Reported = true;
                ErrorReporting.Report(report, error);
            }
            // A null host makes the original method skip this synchronization.
            // No exception or registration in an unrelated host escapes the mod.
            return false;
        }
    }
}
