using System;
using System.Diagnostics;
using BepInEx.Logging;

namespace MebiusTosBugFixes;

internal static class ModLog
{
    private static ManualLogSource? source;
    internal static void Info(string message) => Write(LogLevel.Info, () => message);
    internal static void Error(string context, Exception error) => Write(LogLevel.Error, () => context + ": " + error);

    private static void Write(LogLevel level, Func<string> message)
    {
        try { (source ??= Logger.CreateLogSource(ModInfo.Name)).Log(level, message()); }
        catch (Exception loggingError) { ErrorReporting.TraceFailure(loggingError); }
    }
}

internal static class ErrorReporting
{
    internal static void Report(Action<Exception> report, Exception error)
    {
        try { report(error); }
        catch (Exception loggingError)
        { TraceFailure(new AggregateException("The fix and its error reporter failed.", error, loggingError)); }
    }

    internal static void TraceFailure(Exception error)
    {
        // A failing log listener must not turn a contained fix failure into an
        // exception in Anchor Chain, another Harmony patch, or the game loop.
        try { Trace.TraceError("Bug Fixes by MebiusTos: " + error); }
        catch (Exception) { }
    }
}
