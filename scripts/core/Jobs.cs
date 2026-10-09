using System;
using System.Threading;

namespace DillControl.Core;

/// <summary>
/// Runs a loop of independent items on the game's own worker threads and the
/// calling thread together, and returns when every item is done. For the
/// per-frame work that has to finish inside the frame (the horde's passes).
///
/// DECISION -- threads of its own, not <c>Parallel.For</c> on the .NET pool.
/// The pool's threads run at the game's base priority and are shared with
/// whatever else queues work (the flow field's rebuild, the runtime's own);
/// with the machine in use, a partition whose thread lost its core held the
/// frame for up to a scheduler slice: horde stages measured 5-19 ms against
/// 1 ms. These threads run above normal, one fewer than the logical cores (the
/// renderer, audio and Windows keep one), and items are handed out one at a
/// time: a thread that is late to wake finds nothing left and costs nothing.
/// The background voxel work stays below normal (VoxelWorkers).
/// </summary>
public static class Jobs
{
    private sealed class Batch
    {
        public Action<int> Body;
        public int Count;
        public int Next;
        public int Done;
    }

    private static readonly SemaphoreSlim Wake = new(0);
    private static Batch _current;
    private static Thread[] _threads;

    public static int Workers => _threads?.Length ?? 0;

    // A/B switch for measuring (DILL_JOBS=pool): the .NET pool instead.
    private static readonly bool UsePool = Environment.GetEnvironmentVariable("DILL_JOBS") == "pool";

    private static void Start()
    {
        int workers = Math.Clamp(Environment.ProcessorCount - 2, 1, 15);
        _threads = new Thread[workers];
        for (int i = 0; i < workers; i++)
        {
            _threads[i] = new Thread(Loop)
            {
                Name = $"Job worker {i}",
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal,
            };
            _threads[i].Start();
        }
    }

    /// <summary>Runs <paramref name="body"/> for 0 to <paramref name="count"/>-1,
    /// in any order and on any of the job threads and this one.</summary>
    public static void For(int count, Action<int> body)
    {
        if (count <= 0)
        {
            return;
        }
        if (_threads == null)
        {
            Start();
        }
        if (count == 1)
        {
            body(0);
            return;
        }
        if (UsePool)
        {
            System.Threading.Tasks.Parallel.For(0, count, body);
            return;
        }

        // A new batch each time: a worker still holding the last one can only
        // find it finished.
        var batch = new Batch { Body = body, Count = count };
        Volatile.Write(ref _current, batch);
        Wake.Release(Math.Min(count - 1, _threads.Length));
        Drain(batch);

        // What is left is items other threads have taken and not finished.
        var spin = new SpinWait();
        while (Volatile.Read(ref batch.Done) < count)
        {
            spin.SpinOnce(-1);
        }
    }

    private static void Drain(Batch batch)
    {
        while (true)
        {
            int item = Interlocked.Increment(ref batch.Next) - 1;
            if (item >= batch.Count)
            {
                return;
            }
            try
            {
                batch.Body(item);
            }
            finally
            {
                Interlocked.Increment(ref batch.Done);
            }
        }
    }

    private static void Loop()
    {
        while (true)
        {
            Wake.Wait();
            Batch batch = Volatile.Read(ref _current);
            if (batch == null)
            {
                continue;
            }
            try
            {
                Drain(batch);
            }
            catch (Exception exception)
            {
                // Logged, not thrown: an exception would end the thread, and
                // the game with it.
                Godot.GD.PushError($"Jobs: {exception}");
            }
        }
    }
}
