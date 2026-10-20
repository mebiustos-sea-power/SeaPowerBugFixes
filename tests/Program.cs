using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 3 || (args[0] != "core" && args[0] != "game"))
                throw new ArgumentException("Supply core/game, the game directory and the Anchor Chain directory.");
            string[] search = { Path.Combine(args[1], "Sea Power_Data", "Managed"), Path.Combine(args[1], "BepInEx", "core"), args[2] };
            AppDomain.CurrentDomain.AssemblyResolve += (_, request) =>
            {
                string name = new AssemblyName(request.Name).Name + ".dll";
                foreach (string directory in search)
                {
                    string path = Path.Combine(directory, name);
                    if (File.Exists(path)) return Assembly.LoadFrom(path);
                }
                return null;
            };
            Run(args[0] == "game");
            Console.WriteLine("PASS " + args[0] + " checks. No Unity or Noesis rendering was initialized.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run(bool game)
    {
        Console.WriteLine("Runtime: " + typeof(object).Assembly.Location);
        SettingsAndOwnershipTests.Run();
        RoutingTests.Run();
        SelectionTests.Run();
        if (game)
        {
            if (Type.GetType("Mono.Runtime") == null)
                throw new PlatformNotSupportedException("The installed game's Harmony requires the game Mono runtime. Use tools/test-mono.ps1.");
            RoutingTests.InstallOnGameMono();
            SelectionTests.InstallOnGameMono();
        }
    }

    internal static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    internal static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
}
