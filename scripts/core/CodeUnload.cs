using System;
using System.Runtime.Loader;
using Godot;

namespace DillControl.Core;

/// <summary>
/// What runs when the editor unloads the project's code to load a new build.
///
/// Every handler on the assembly's Unloading event is one delegate: the first
/// to throw ends the rest. By then the editor has already torn down the C#
/// side of every node and object (the plugins, the bridge's logger), so a
/// handler that touches one throws; the handlers after it (the worker threads'
/// stops) then never ran, a thread still in our code held the assembly, the
/// unload failed and the editor gave up reloading for the session, with every
/// tool plugin dead. So each piece of unload work runs here, alone, and what
/// it throws is reported and goes no further.
/// </summary>
public static class CodeUnload
{
    /// <summary>Run <paramref name="work"/> when the code unloads.</summary>
    public static void Hook(string what, Action work)
    {
        AssemblyLoadContext.GetLoadContext(typeof(CodeUnload).Assembly)!.Unloading += _ => Safely(what, work);
    }

    /// <summary>Run <paramref name="work"/> now; if it throws, report it and carry on.</summary>
    public static void Safely(string what, Action work)
    {
        try
        {
            work();
        }
        catch (Exception exception)
        {
            try
            {
                GD.PushWarning($"[unload] {what}: {exception.GetType().Name}: {exception.Message}");
            }
            catch
            {
                // Nothing left to report through.
            }
        }
    }
}
