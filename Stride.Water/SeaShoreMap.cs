using System;

namespace Stride.Water;

/// <summary>
/// Where a sea's shore is, on a square grid over its floor: for each place, how far it is (in metres) to the nearest
/// land, how deep the still water is there, and which way the shore is (a unit direction along x and z), for its
/// breaking waves to run in towards it. Land is where the floor is at or above the sea's level.
/// </summary>
public sealed class SeaShoreMap
{
    private SeaShoreMap(int size, float cell, float originX, float originZ)
    {
        Size = size;
        Cell = cell;
        OriginX = originX;
        OriginZ = originZ;
        Distance = new float[size * size];
        Depth = new float[size * size];
        DirectionX = new float[size * size];
        DirectionZ = new float[size * size];
    }

    /// <summary>How many places across it is, and how far apart they are, in metres.</summary>
    public int Size { get; }

    public float Cell { get; }

    /// <summary>Where its first place is, in the world (along x and z).</summary>
    public float OriginX { get; }

    public float OriginZ { get; }

    public float[] Distance { get; }

    public float[] Depth { get; }

    public float[] DirectionX { get; }

    public float[] DirectionZ { get; }

    /// <summary>
    /// Builds it over a square (from <paramref name="originX"/>, <paramref name="originZ"/>, <paramref name="width"/>
    /// metres across) of <paramref name="size"/> places each way, from the floor's height at each (NaN where there is
    /// none: deep water) and the sea's level.
    /// </summary>
    public static SeaShoreMap Build(int size, float originX, float originZ, float width, float level, Func<float, float, float> floor)
    {
        var cell = width / size;
        var map = new SeaShoreMap(size, cell, originX, originZ);
        const float far = 1e6f;
        var distance = map.Distance;
        for (var z = 0; z < size; z++)
        {
            for (var x = 0; x < size; x++)
            {
                var height = floor(originX + (x + 0.5f) * cell, originZ + (z + 0.5f) * cell);
                var depth = float.IsNaN(height) ? 1000f : level - height;
                map.Depth[z * size + x] = depth;
                distance[z * size + x] = depth <= 0f ? 0f : far;
            }
        }

        // Distance to land: two sweeps over the grid, each place taking the nearest of its neighbours' plus the step to
        // them (straight or diagonal), forwards then back.
        var straight = cell;
        var diagonal = cell * MathF.Sqrt(2f);
        for (var z = 0; z < size; z++)
        {
            for (var x = 0; x < size; x++)
            {
                var i = z * size + x;
                var d = distance[i];
                if (x > 0) d = MathF.Min(d, distance[i - 1] + straight);
                if (z > 0)
                {
                    d = MathF.Min(d, distance[i - size] + straight);
                    if (x > 0) d = MathF.Min(d, distance[i - size - 1] + diagonal);
                    if (x < size - 1) d = MathF.Min(d, distance[i - size + 1] + diagonal);
                }
                distance[i] = d;
            }
        }
        for (var z = size - 1; z >= 0; z--)
        {
            for (var x = size - 1; x >= 0; x--)
            {
                var i = z * size + x;
                var d = distance[i];
                if (x < size - 1) d = MathF.Min(d, distance[i + 1] + straight);
                if (z < size - 1)
                {
                    d = MathF.Min(d, distance[i + size] + straight);
                    if (x < size - 1) d = MathF.Min(d, distance[i + size + 1] + diagonal);
                    if (x > 0) d = MathF.Min(d, distance[i + size - 1] + diagonal);
                }
                distance[i] = d;
            }
        }

        // Which way the shore is: down the distance's slope.
        for (var z = 0; z < size; z++)
        {
            for (var x = 0; x < size; x++)
            {
                var i = z * size + x;
                var gx = distance[z * size + Math.Min(x + 1, size - 1)] - distance[z * size + Math.Max(x - 1, 0)];
                var gz = distance[Math.Min(z + 1, size - 1) * size + x] - distance[Math.Max(z - 1, 0) * size + x];
                var length = MathF.Sqrt(gx * gx + gz * gz);
                (map.DirectionX[i], map.DirectionZ[i]) = length > 1e-6f ? (-gx / length, -gz / length) : (0f, 0f);
            }
        }
        return map;
    }
}
