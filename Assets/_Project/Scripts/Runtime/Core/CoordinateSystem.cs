using SpaceSim.Mathematics;
using SpaceSim.Procedural;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// The only place where physics coordinates become Unity coordinates.
    ///
    /// Physics: right-handed, double precision, SI metres, Z = ecliptic north.
    /// Unity:   left-handed, float, Y up. Mapping: unity = (x, z, y) — swapping two axes is a reflection,
    /// which converts handedness correctly (east stays to the right of north when looking down).
    /// Positions must additionally be made origin-relative through <see cref="FloatingOrigin"/> first.
    /// </summary>
    public static class CoordinateSystem
    {
        public static Vector3 ToUnityVector(Vector3d v) => new Vector3((float)v.x, (float)v.z, (float)v.y);

        public static Vector3d FromUnityVector(Vector3 v) => new Vector3d(v.x, v.z, v.y);

        /// <summary>Rotation R in physics axes → S·R·S in Unity axes, i.e. quaternion (−x, −z, −y, w).</summary>
        public static Quaternion ToUnityRotation(QuaternionD q) => new Quaternion((float)-q.x, (float)-q.z, (float)-q.y, (float)q.w);

        public static QuaternionD FromUnityRotation(Quaternion q) => new QuaternionD(-q.x, -q.z, -q.y, q.w);

        /// <summary>Core colours are sRGB-encoded; Unity vertex colours are used as-is by our shaders (they linearise).</summary>
        public static Color ToColor(ColorRGB c, float a = 1f) => new Color(c.r, c.g, c.b, a);

        /// <summary>
        /// Copies engine-independent chunk geometry into a Unity mesh: swaps axes and reverses triangle winding
        /// (the reflection flips orientation; Unity front faces are clockwise).
        /// </summary>
        /// <summary>Period [m] of the procedural ground detail (must match SPACESIM_DETAIL_PERIOD in SpaceSimDetail.hlsl).</summary>
        public const double DetailPeriod = 4096.0;

        static double Wrap(double v) => v - DetailPeriod * System.Math.Floor(v / DetailPeriod);

        public static void FillMesh(ChunkMeshData data, Mesh mesh)
        {
            int n = data.VertexCount;
            var vertices = new Vector3[n];
            var normals = new Vector3[n];
            var colors = new Color[n];
            for (int i = 0; i < n; i++)
            {
                vertices[i] = new Vector3(data.Positions[i * 3], data.Positions[i * 3 + 2], data.Positions[i * 3 + 1]);
                normals[i] = new Vector3(data.Normals[i * 3], data.Normals[i * 3 + 2], data.Normals[i * 3 + 1]);
                colors[i] = new Color(data.Colors[i * 4], data.Colors[i * 4 + 1], data.Colors[i * 4 + 2], data.Colors[i * 4 + 3]);
            }
            var tris = new int[data.Triangles.Length];
            for (int t = 0; t < tris.Length; t += 3)
            {
                tris[t] = data.Triangles[t];
                tris[t + 1] = data.Triangles[t + 2];
                tris[t + 2] = data.Triangles[t + 1];
            }
            // Surface detail coordinates: the chunk centre modulo DetailPeriod (planet-fixed, Unity axes), constant per chunk.
            // The shader adds the vertex position and evaluates noise with exactly that period, so the pattern is continuous
            // across chunks, never swims with the floating origin or the planet's rotation, and keeps float precision.
            var off = new Vector3((float)Wrap(data.Center.x), (float)Wrap(data.Center.z), (float)Wrap(data.Center.y));
            float edge = (float)data.EdgeLength;
            var uv0 = new Vector3[n];
            var uv1 = new Vector2[n];
            var uv2 = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                uv0[i] = new Vector3(off.x, off.y, edge);
                uv1[i] = new Vector2(off.z, 0f);
            }
            FillParentMorph(data, uv1, uv2);
            mesh.Clear();
            mesh.indexFormat = n > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.colors = colors;
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.SetUVs(2, uv2);
            mesh.triangles = tris;
            float b = (float)data.BoundingRadius;
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(2f * b, 2f * b, 2f * b));
        }

        /// <summary>
        /// What the parent chunk has at each vertex, for the shader's LOD transition (uv1.y: radial offset [m]; uv2: packed normal, packed colour).
        /// A parent has only the even grid vertices; between them it interpolates along its edges and along the diagonal its triangles split on.
        /// </summary>
        static void FillParentMorph(ChunkMeshData data, Vector2[] uv1, Vector2[] uv2)
        {
            int res = data.GridResolution, side = res + 1;
            if (res < 2) return;
            float[] pos = data.Positions, nor = data.Normals, col = data.Colors;
            Vector3d center = data.Center;
            for (int j = 0; j < side; j++)
            {
                for (int i = 0; i < side; i++)
                {
                    int k = j * side + i;
                    bool oddI = (i & 1) == 1, oddJ = (j & 1) == 1;
                    int a = k, b = k;
                    if (oddI && oddJ)
                    {
                        a = (j - 1) * side + i + 1; // the parent's quad is split along this diagonal
                        b = (j + 1) * side + i - 1;
                    }
                    else if (oddI)
                    {
                        a = k - 1;
                        b = k + 1;
                    }
                    else if (oddJ)
                    {
                        a = k - side;
                        b = k + side;
                    }

                    if (a != k)
                    {
                        double x = pos[k * 3], y = pos[k * 3 + 1], z = pos[k * 3 + 2];
                        double mx = 0.5 * (pos[a * 3] + pos[b * 3]) - x, my = 0.5 * (pos[a * 3 + 1] + pos[b * 3 + 1]) - y, mz = 0.5 * (pos[a * 3 + 2] + pos[b * 3 + 2]) - z;
                        Vector3d radial = (center + new Vector3d(x, y, z)).Normalized;
                        uv1[k].y = (float)(mx * radial.x + my * radial.y + mz * radial.z);
                    }
                    float nx = nor[a * 3] + nor[b * 3], ny = nor[a * 3 + 1] + nor[b * 3 + 1], nz = nor[a * 3 + 2] + nor[b * 3 + 2];
                    float r = 0.5f * (col[a * 4] + col[b * 4]), g = 0.5f * (col[a * 4 + 1] + col[b * 4 + 1]), bl = 0.5f * (col[a * 4 + 2] + col[b * 4 + 2]);
                    uv2[k] = new Vector2(PackNormal(nx, nz, ny), PackColor(r, g, bl)); // normal in Unity axes
                }
            }
            // Skirts follow the border vertex they hang from, in the same order as ChunkMeshBuilder.BorderLoop.
            int s = side * side, n = data.VertexCount;
            for (int b = 0; b < n - s; b++)
            {
                int src = BorderVertex(b, res);
                uv1[s + b].y = uv1[src].y;
                uv2[s + b] = uv2[src];
            }
        }

        static int BorderVertex(int b, int n)
        {
            int side = n + 1;
            if (b < n) return b;
            b -= n;
            if (b < n) return b * side + n;
            b -= n;
            if (b < n) return n * side + (n - b);
            b -= n;
            return (n - b) * side;
        }

        /// <summary>Y-major octahedral normal in two 12-bit fields of one float (a float holds 24-bit integers exactly).</summary>
        static float PackNormal(float x, float y, float z)
        {
            float s = Mathf.Abs(x) + Mathf.Abs(y) + Mathf.Abs(z);
            if (s < 1e-8f) return 2047f * 4096f + 2047f;
            float u = x / s, v = z / s;
            if (y < 0f)
            {
                float ou = u;
                u = (1f - Mathf.Abs(v)) * (ou >= 0f ? 1f : -1f);
                v = (1f - Mathf.Abs(ou)) * (v >= 0f ? 1f : -1f);
            }
            return Mathf.Round((u * 0.5f + 0.5f) * 4095f) * 4096f + Mathf.Round((v * 0.5f + 0.5f) * 4095f);
        }

        static float PackColor(float r, float g, float b) =>
            Mathf.Round(Mathf.Clamp01(r) * 255f) * 65536f + Mathf.Round(Mathf.Clamp01(g) * 255f) * 256f + Mathf.Round(Mathf.Clamp01(b) * 255f);
    }
}
