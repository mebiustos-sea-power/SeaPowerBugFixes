using System;
using System.Windows.Input;

namespace MebiusTosBugFixes.Fixes.Selection;

// Keeps the native command and its availability/events; repairs selection only after it succeeds.
internal sealed class ReselectionCommand : ICommand
{
    internal object Owner { get; }
    private readonly ICommand original;
    private readonly Action<object?> synchronize;
    private readonly Action<Exception> report;
    private bool executeFailed, availabilityFailed, eventFailed;

    internal ReselectionCommand(object owner, ICommand original, Action<object?> synchronize, Action<Exception> report)
    { Owner = owner; this.original = original; this.synchronize = synchronize; this.report = report; }

    public bool CanExecute(object? parameter)
    {
        try { bool result = original.CanExecute(parameter); availabilityFailed = false; return result; }
        catch (Exception error) { Report(ref availabilityFailed, error); return false; }
    }

    public void Execute(object? parameter)
    {
        try { original.Execute(parameter); synchronize(parameter); executeFailed = false; }
        catch (Exception error) { Report(ref executeFailed, error); }
    }

    public event EventHandler? CanExecuteChanged
    {
        add
        {
            try { original.CanExecuteChanged += value; eventFailed = false; }
            catch (Exception error) { Report(ref eventFailed, error); }
        }
        remove
        {
            try { original.CanExecuteChanged -= value; eventFailed = false; }
            catch (Exception error) { Report(ref eventFailed, error); }
        }
    }

    private void Report(ref bool reported, Exception error)
    {
        if (reported) return;
        reported = true;
        ErrorReporting.Report(report, error);
    }
}
