using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Godot;

namespace DillControl.Core;

/// <summary>
/// Compiles every method of the game's assembly on a background thread, once
/// per run, so nothing is compiled the first time it is called in play.
///
/// DECISION -- warmed ahead rather than left to the JIT. .NET compiles a method
/// the first time it runs, on the thread that runs it: the first tower to fall
/// paid 5-8 ms of compiling in the frame it fell, the first crush as much
/// again, and every system has a first time of its own. Done while a level
/// loads, on one worker, it costs that worker a few seconds and play nothing.
/// Hot methods are still re-compiled optimised later, but the runtime does
/// that on a thread of its own.
/// </summary>
public static class JitWarmup
{
    private static int _started;

    public static void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }
        Assembly assembly = typeof(JitWarmup).Assembly;
        // Below normal: it must not take a core from the frame while it runs.
        new Thread(() =>
        {
            ulong start = Time.GetTicksMsec();
            int prepared = 0;
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                types = Array.FindAll(exception.Types, t => t != null);
            }
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                     BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            foreach (Type type in types)
            {
                if (type.ContainsGenericParameters)
                {
                    continue;
                }
                foreach (MethodBase method in type.GetMethods(all))
                {
                    prepared += Prepare(method);
                }
                foreach (MethodBase constructor in type.GetConstructors(all))
                {
                    prepared += Prepare(constructor);
                }
            }
            GD.Print($"[jit] warmed {prepared} methods in {Time.GetTicksMsec() - start} ms");
        })
        {
            Name = "JIT warm-up",
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
        }.Start();
    }

    private static bool _classesWarmed;

    /// <summary>
    /// Makes and frees one of every Godot class the game defines, on the main
    /// thread, once per run. The first object of a C# Godot class costs
    /// milliseconds that later ones do not (Godot binding the script class):
    /// the first DamageInfo of a session took 2.4-3.1 ms, inside the blast
    /// that made it, and the first VoxelChunk 3.7 ms. Nothing here enters the
    /// tree, so no _EnterTree or _Ready runs; editor-only classes are skipped.
    /// </summary>
    public static void WarmGodotClasses()
    {
        if (_classesWarmed)
        {
            return;
        }
        _classesWarmed = true;
        ulong start = Time.GetTicksMsec();
        int made = 0;
        foreach (Type type in typeof(JitWarmup).Assembly.GetTypes())
        {
            if (type.IsAbstract || type.ContainsGenericParameters || !typeof(GodotObject).IsAssignableFrom(type)
                || type.GetConstructor(Type.EmptyTypes) == null || IsEditorOnly(type))
            {
                continue;
            }
            try
            {
                var instance = (GodotObject)Activator.CreateInstance(type);
                if (instance is RefCounted)
                {
                    instance.Dispose();
                }
                else
                {
                    instance.Free();
                }
                made++;
            }
            catch (Exception exception)
            {
                GD.PushWarning($"JitWarmup: could not make a {type.Name}: {exception.Message}");
            }
        }
        GD.Print($"[jit] made {made} Godot classes once in {Time.GetTicksMsec() - start} ms");
    }

    private static bool IsEditorOnly(Type type)
    {
        for (Type t = type; t != null; t = t.BaseType)
        {
            if (t.Name.StartsWith("Editor", StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static int Prepare(MethodBase method)
    {
        if (method.IsAbstract || method.ContainsGenericParameters || method.GetMethodBody() == null)
        {
            return 0;
        }
        try
        {
            RuntimeHelpers.PrepareMethod(method.MethodHandle);
            return 1;
        }
        catch (Exception)
        {
            // Some cannot be compiled ahead (open generics through their
            // class, interop stubs). They compile when called, as before.
            return 0;
        }
    }
}
