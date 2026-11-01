using System;

namespace MebiusTosBugFixes.Fixes.Input;

// A menu-close notification schedules one refresh after UI dismissal; it does not
// poll during gameplay or assume that closing a menu means the pointer left all UI.
internal sealed class DeferredHoverRefresh : IDisposable
{
    private readonly Action<Action, Action<Exception>> post;
    private readonly Func<bool> eligible;
    private readonly Action refresh;
    private readonly Action<Exception> report;
    private bool pending;
    private bool disposed;

    internal DeferredHoverRefresh(Action<Action, Action<Exception>> post, Func<bool> eligible,
        Action refresh, Action<Exception> report)
    { this.post = post; this.eligible = eligible; this.refresh = refresh; this.report = report; }

    internal void Request()
    {
        if (disposed || pending) return;
        pending = true;
        try { post(Run, Failed); }
        catch (Exception error) { Failed(error); }
    }

    private void Run()
    {
        pending = false;
        if (disposed) return;
        try { if (eligible()) refresh(); }
        catch (Exception error) { ErrorReporting.Report(report, error); }
    }

    private void Failed(Exception error)
    {
        pending = false;
        ErrorReporting.Report(report, error);
    }

    public void Dispose() { disposed = true; }
}
