using System;
using System.Runtime.InteropServices;

// The separate Mono process has no Unity engine to create profiler markers.
// Only these profiling initializers are inert; game methods under test are unchanged.
internal static class UnityProfilerBoundary
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr CreateMarker(IntPtr name, ushort category, ushort flags, int metadataCount);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SetMetadata(IntPtr marker, int index, IntPtr name, byte type, byte units);

    private static readonly CreateMarker create = (_, _, _, _) => IntPtr.Zero;
    private static readonly SetMetadata metadata = (_, _, _, _, _) => { };

    [DllImport("mono-2.0-bdwgc.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "mono_add_internal_call")]
    private static extern void AddInternalCall(string name, IntPtr method);

    internal static void Register()
    {
        AddInternalCall("Unity.Profiling.LowLevel.Unsafe.ProfilerUnsafeUtility::CreateMarker_Injected",
            Marshal.GetFunctionPointerForDelegate(create));
        AddInternalCall("Unity.Profiling.LowLevel.Unsafe.ProfilerUnsafeUtility::SetMarkerMetadata_Injected",
            Marshal.GetFunctionPointerForDelegate(metadata));
    }
}
