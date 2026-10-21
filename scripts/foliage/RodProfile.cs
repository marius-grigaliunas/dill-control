using System;

namespace DillControl.Foliage;

/// <summary>
/// A plant's <see cref="Rod"/> from its voxels counted layer by layer: each
/// segment's mass, and each joint's section as the thinnest layer near it.
/// <see cref="PlantFlex"/> builds its rod this way, and the vine checks build
/// the same rod from a vine before it is ever placed (<see cref="VineValidator"/>).
/// </summary>
public static class RodProfile
{
    /// <summary>
    /// <paramref name="bins"/> are voxels per layer of <paramref name="binSize"/>
    /// metres from the foot, now; <paramref name="builtBins"/> as built (a layer
    /// that was never solid is not a joint's thinnest).
    /// </summary>
    public static void From(int[] bins, int[] builtBins, float binSize, int segments, float segmentLength,
        float massPerVoxel, float[] mass, float[] section)
    {
        Array.Clear(mass);
        for (int b = 0; b < bins.Length; b++)
        {
            float height = (b + 0.5f) * binSize;
            int s = Math.Clamp((int)(height / segmentLength), 0, segments - 1);
            mass[s] += bins[b] * massPerVoxel;
        }
        for (int j = 0; j < segments; j++)
        {
            // The thinnest layer within half a segment of the joint: a cut a
            // voxel thick is what decides how much is left to bend on.
            float from = (j - 0.5f) * segmentLength, to = (j + 0.5f) * segmentLength;
            int thinnest = int.MaxValue;
            for (int b = Math.Max(0, (int)(from / binSize)); b < bins.Length && b * binSize < to; b++)
            {
                if (builtBins[b] > 0)
                {
                    thinnest = Math.Min(thinnest, bins[b]);
                }
            }
            section[j] = thinnest == int.MaxValue ? 0.0f : thinnest;
        }
    }
}
