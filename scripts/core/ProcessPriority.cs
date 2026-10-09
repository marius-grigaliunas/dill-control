using System;
using System.Diagnostics;
using Godot;

namespace DillControl.Core;

/// <summary>
/// Asks the operating system to run the game ahead of ordinary programs.
///
/// DECISION -- above normal, not left at normal. The requirement holds with
/// the machine in use (docs/decisions/0002, 0015). At normal priority the
/// editor, a browser or an update shares the four cores with the frame on
/// equal terms, and the main thread lost its core mid-frame: a step that
/// takes 0.2 ms measured 6-9 ms, in no fixed place. Above normal, Windows
/// runs the frame first and the rest in what is left. Not high: that can
/// starve the input and audio services the game itself needs. The game's own
/// background work drops below normal on its threads (VoxelWorkers).
/// </summary>
public static class ProcessPriority
{
    private static bool _raised;

    public static void Raise()
    {
        if (_raised || OS.GetName() != "Windows")
        {
            return;
        }
        _raised = true;
        try
        {
            using Process self = Process.GetCurrentProcess();
            self.PriorityClass = ProcessPriorityClass.AboveNormal;
        }
        catch (Exception exception)
        {
            GD.PushWarning($"ProcessPriority: could not raise the game's priority: {exception.Message}");
        }
    }
}
