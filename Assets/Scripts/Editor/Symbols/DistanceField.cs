using System;

namespace LogicSim.EditorTools.Symbols
{
    /// <summary>
    /// Exact Euclidean signed distance of a coverage mask (Felzenszwalb and Huttenlocher's separable transform).
    /// Positive inside the shape, negative outside, in source pixels; the edge lies halfway between an inside and an
    /// outside pixel.
    /// </summary>
    public sealed class DistanceField
    {
        private const float Far = 1e20f;

        public readonly int Width, Height;
        private readonly float[] _distance;

        private DistanceField(int width, int height, float[] distance)
        {
            Width = width;
            Height = height;
            _distance = distance;
        }

        /// <param name="inside">Row-major mask, true where the shape covers the pixel.</param>
        public static DistanceField FromMask(bool[] inside, int width, int height)
        {
            var toInside = SquaredDistances(inside, width, height, true);
            var toOutside = SquaredDistances(inside, width, height, false);
            var distance = new float[width * height];
            for (int i = 0; i < distance.Length; i++)
            {
                distance[i] = inside[i]
                    ? (float)Math.Sqrt(toOutside[i]) - 0.5f
                    : 0.5f - (float)Math.Sqrt(toInside[i]);
            }
            return new DistanceField(width, height, distance);
        }

        /// <summary>Signed distance at a pixel position (pixel centers at +0.5), bilinear, clamped at the borders.</summary>
        public float Sample(float x, float y)
        {
            x -= 0.5f;
            y -= 0.5f;
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            float fx = x - x0, fy = y - y0;
            float a = At(x0, y0), b = At(x0 + 1, y0), c = At(x0, y0 + 1), d = At(x0 + 1, y0 + 1);
            return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy;
        }

        public float At(int x, int y)
        {
            x = x < 0 ? 0 : x >= Width ? Width - 1 : x;
            y = y < 0 ? 0 : y >= Height ? Height - 1 : y;
            return _distance[y * Width + x];
        }

        /// <summary>Squared distance from every pixel to the nearest pixel whose mask value equals <paramref name="target"/>.</summary>
        private static float[] SquaredDistances(bool[] mask, int width, int height, bool target)
        {
            var grid = new float[width * height];
            for (int i = 0; i < grid.Length; i++)
                grid[i] = mask[i] == target ? 0f : Far;

            int n = Math.Max(width, height);
            var f = new float[n];
            var d = new float[n];
            var v = new int[n];
            var z = new float[n + 1];

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                    f[y] = grid[y * width + x];
                Transform(f, height, d, v, z);
                for (int y = 0; y < height; y++)
                    grid[y * width + x] = d[y];
            }
            for (int y = 0; y < height; y++)
            {
                Array.Copy(grid, y * width, f, 0, width);
                Transform(f, width, d, v, z);
                Array.Copy(d, 0, grid, y * width, width);
            }
            return grid;
        }

        /// <summary>1D squared distance transform of the sampled function f (lower envelope of parabolas).</summary>
        private static void Transform(float[] f, int n, float[] d, int[] v, float[] z)
        {
            int k = 0;
            v[0] = 0;
            z[0] = -Far;
            z[1] = Far;
            for (int q = 1; q < n; q++)
            {
                float s;
                while (true)
                {
                    int p = v[k];
                    s = ((f[q] + (float)q * q) - (f[p] + (float)p * p)) / (2f * q - 2f * p);
                    if (s > z[k] || k == 0)
                        break;
                    k--;
                }
                if (s <= z[k])
                {
                    // k == 0 and the new parabola dominates everywhere.
                    v[0] = q;
                    z[0] = -Far;
                    z[1] = Far;
                    continue;
                }
                k++;
                v[k] = q;
                z[k] = s;
                z[k + 1] = Far;
            }
            k = 0;
            for (int q = 0; q < n; q++)
            {
                while (z[k + 1] < q)
                    k++;
                float dq = q - v[k];
                d[q] = dq * dq + f[v[k]];
            }
        }
    }
}
