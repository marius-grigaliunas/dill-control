using System;
using System.Collections.Concurrent;
using DillControl.Core;
using System.Threading;
using Godot;

namespace DillControl.Voxel;

/// <summary>
/// The threads voxel work runs on: re-meshing, structural checks, splits.
///
/// DECISION -- threads of their own below normal priority, not the .NET pool.
/// This is a four-core laptop with the editor, a browser and Windows on it.
/// Pool threads run at the main thread's priority, and with two of them busy
/// re-meshing a collapse, the main thread lost the core mid-frame: a step
/// that takes 0.1 ms measured 3-4 ms, in no fixed place. Below normal, the
/// work waits for idle time instead, and only the frame it finishes in is
/// late, by a frame or two, which nothing shows.
///
/// The threads end when the assembly unloads (the editor reloading the code
/// after a build), as Jobs' do: one still waiting on the queue holds the
/// assembly and the reload fails.
/// </summary>
public static class VoxelWorkers
{
    private const int Count = 2;

    private static readonly BlockingCollection<Action> Queue = new();
    private static int _started;
    private static Thread[] _threads;

    public static void Run(Action work)
    {
        if (Interlocked.Exchange(ref _started, 1) == 0)
        {
            _threads = new Thread[Count];
            for (int i = 0; i < Count; i++)
            {
                _threads[i] = new Thread(Loop)
                {
                    Name = $"Voxel worker {i}",
                    IsBackground = true,
                    Priority = ThreadPriority.BelowNormal,
                };
                _threads[i].Start();
            }
            CodeUnload.Hook("voxel threads", Stop);
        }
        Queue.Add(work);
    }

    // What is queued still runs; nothing is taken after.
    private static void Stop()
    {
        Queue.CompleteAdding();
        foreach (Thread thread in _threads)
        {
            thread.Join(1000);
        }
    }

    private static void Loop()
    {
        foreach (Action work in Queue.GetConsumingEnumerable())
        {
            try
            {
                work();
            }
            catch (Exception exception)
            {
                GD.PushError($"VoxelWorkers: {exception}");
            }
        }
    }
}
