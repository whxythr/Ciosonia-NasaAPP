using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// What the robot's camera shows as its visor degrades (<see cref="SpaceSim.Exploration.RobotCondition.Visor"/>): a lens
    /// vignette always; with damage, cracks that spread from fixed impact points, animated sensor noise, darkening and, near
    /// the end, flicker. At 0 % the view is black — drawn in front of every other panel except the robot status
    /// (<see cref="RobotStatusPanel"/>), so nothing but the status remains. The sprint-strain tunnel of the old helmet
    /// overlay is kept. Visual only.
    /// </summary>
    public sealed class RobotVisorOverlay : MonoBehaviour
    {
        /// <summary>GUI depth of the blackout: in front of every HUD panel (the lowest elsewhere is −50)…</summary>
        public const int BlackoutDepth = -60;
        /// <summary>…and of the status panel, which stays in front of the blackout.</summary>
        public const int StatusDepth = -70;

        const int CrackWidth = 480, CrackHeight = 270, MaxCracks = 16, CrackSeed = 7919;

        Texture2D rim, noise, cracks;
        int crackCount = -1;
        float flickerUntil;

        public bool Visible { get; set; }
        /// <summary>Visor condition: 1 = clear, 0 = blind.</summary>
        public float Visor { get; set; } = 1f;
        /// <summary>Draw the lens vignette (first person; the view from behind has none).</summary>
        public bool Rim { get; set; } = true;
        /// <summary>0 = rested, 1 = exhausted (the edge of the view closes in).</summary>
        public float Strain { get; set; }

        /// <summary>The view is completely black.</summary>
        public bool Blackout => Visible && Visor <= 0f;
        /// <summary>Darkening of the view in [0, 1] for a visor condition (1 at 0 %).</summary>
        public static float Darkening(float visor) => visor <= 0f ? 1f : 0.75f * Mathf.Pow(Mathf.Clamp01(1f - visor), 1.6f);
        /// <summary>Opacity of the sensor noise for a visor condition.</summary>
        public static float NoiseAlpha(float visor) => visor <= 0f ? 0f : 0.55f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.85f, 0.05f, visor));
        /// <summary>How many cracks run across the visor.</summary>
        public static int CrackCount(float visor) => Mathf.Clamp(Mathf.FloorToInt((1f - Mathf.Clamp01(visor)) * (MaxCracks + 2) - 1f), 0, MaxCracks);

        void OnDestroy()
        {
            if (rim != null) Destroy(rim);
            if (noise != null) Destroy(noise);
            if (cracks != null) Destroy(cracks);
        }

        void OnGUI()
        {
            if (!Visible || FlightInput.PauseMenuOpen || FlightInput.MapOpen) return;
            bool black = Visor <= 0f;
            GUI.depth = black ? BlackoutDepth : 10; // otherwise behind the instrument panels (lower depth is drawn on top)
            if (Event.current.type != EventType.Repaint) return;
            Matrix4x4 old = GUI.matrix;
            Color oldColor = GUI.color;
            GUI.matrix = Matrix4x4.identity;
            var screen = new Rect(0f, 0f, Screen.width, Screen.height);
            if (black)
            {
                GUI.color = Color.black;
                GUI.DrawTexture(screen, Texture2D.whiteTexture);
                GUI.color = oldColor;
                GUI.matrix = old;
                return;
            }

            if (rim == null) rim = BuildRim(192, 128);
            if (Rim) GUI.DrawTexture(screen, rim);
            if (Rim && Strain > 0.01f)
            {
                GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(Strain));
                GUI.DrawTexture(screen, rim);
                GUI.DrawTexture(screen, rim);
            }

            float v = Mathf.Clamp01(Visor);
            int n = CrackCount(v);
            if (n > 0)
            {
                if (n != crackCount) BuildCracks(n);
                GUI.color = Color.white;
                GUI.DrawTexture(screen, cracks);
            }

            float na = NoiseAlpha(v);
            if (na > 0.001f)
            {
                if (noise == null) noise = BuildNoise(128);
                float tiles = Screen.width / 256f;
                var uv = new Rect(Random.value, Random.value, tiles, tiles * Screen.height / Mathf.Max(1f, Screen.width));
                GUI.color = new Color(1f, 1f, 1f, na);
                GUI.DrawTextureWithTexCoords(screen, noise, uv);
            }

            float dark = Darkening(v);
            // A failing sensor drops frames: short black flickers below 25 %.
            if (v < 0.25f && Time.unscaledTime > flickerUntil && Random.value < (0.25f - v) * 0.2f) flickerUntil = Time.unscaledTime + Random.Range(0.04f, 0.12f);
            if (Time.unscaledTime < flickerUntil) dark = Mathf.Max(dark, 0.92f);
            if (dark > 0.001f)
            {
                GUI.color = new Color(0f, 0f, 0f, dark);
                GUI.DrawTexture(screen, Texture2D.whiteTexture);
            }
            GUI.color = oldColor;
            GUI.matrix = old;
        }

        /// <summary>Elliptical rim: clear in the middle, a soft dark edge in the corners.</summary>
        static Texture2D BuildRim(int width, int height)
        {
            var t = new Texture2D(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < height; y++)
            {
                float v = (y + 0.5f) / height * 2f - 1f;
                for (int x = 0; x < width; x++)
                {
                    float u = (x + 0.5f) / width * 2f - 1f;
                    float r = Mathf.Sqrt(u * u * 0.85f + v * v * 1.15f);
                    float edge = Mathf.Clamp01((r - 0.82f) / 0.5f);
                    edge = edge * edge * (3f - 2f * edge);
                    t.SetPixel(x, y, new Color(0.01f, 0.012f, 0.016f, 0.8f * edge));
                }
            }
            t.Apply();
            return t;
        }

        /// <summary>Grey sensor noise, tiled and shifted every frame.</summary>
        static Texture2D BuildNoise(int size)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
            var rng = new System.Random(12345);
            var px = new Color32[size * size];
            for (int i = 0; i < px.Length; i++)
            {
                byte g = (byte)rng.Next(256);
                px[i] = new Color32(g, g, g, (byte)(rng.Next(2) == 0 ? 0 : 255));
            }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        /// <summary>
        /// The first <paramref name="count"/> cracks of a fixed sequence (same seed every time), so the damage grows on the
        /// glass instead of reshuffling: each crack is a jagged path from an impact point with a side branch.
        /// </summary>
        void BuildCracks(int count)
        {
            if (cracks == null) cracks = new Texture2D(CrackWidth, CrackHeight, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[CrackWidth * CrackHeight];
            var rng = new System.Random(CrackSeed);
            for (int c = 0; c < count; c++)
            {
                float x = (float)rng.NextDouble() * CrackWidth, y = (float)rng.NextDouble() * CrackHeight;
                int arms = 3 + rng.Next(3);
                for (int a = 0; a < arms; a++)
                {
                    float angle = (float)(rng.NextDouble() * Mathf.PI * 2.0);
                    float length = 40f + (float)rng.NextDouble() * 120f;
                    Walk(px, rng, x, y, angle, length, 2);
                }
                // A frosted star at the impact point.
                Disc(px, x, y, 3f);
            }
            cracks.SetPixels32(px);
            cracks.Apply();
            crackCount = count;
        }

        static void Walk(Color32[] px, System.Random rng, float x, float y, float angle, float length, int depth)
        {
            float travelled = 0f;
            while (travelled < length)
            {
                float seg = 6f + (float)rng.NextDouble() * 10f;
                angle += (float)(rng.NextDouble() - 0.5) * 0.9f;
                float nx = x + Mathf.Cos(angle) * seg, ny = y + Mathf.Sin(angle) * seg;
                Line(px, x, y, nx, ny, depth >= 2 ? (byte)230 : (byte)170);
                x = nx;
                y = ny;
                travelled += seg;
                if (depth > 0 && rng.NextDouble() < 0.12) Walk(px, rng, x, y, angle + (rng.NextDouble() < 0.5 ? 0.8f : -0.8f), length * 0.35f, depth - 1);
                if (x < 0 || y < 0 || x >= CrackWidth || y >= CrackHeight) break;
            }
        }

        static void Line(Color32[] px, float x0, float y0, float x1, float y1, byte alpha)
        {
            int steps = Mathf.CeilToInt(Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0)));
            for (int i = 0; i <= steps; i++)
            {
                float t = steps == 0 ? 0f : (float)i / steps;
                Plot(px, Mathf.RoundToInt(Mathf.Lerp(x0, x1, t)), Mathf.RoundToInt(Mathf.Lerp(y0, y1, t)), alpha);
            }
        }

        static void Disc(Color32[] px, float cx, float cy, float r)
        {
            for (int y = Mathf.FloorToInt(cy - r); y <= Mathf.CeilToInt(cy + r); y++)
                for (int x = Mathf.FloorToInt(cx - r); x <= Mathf.CeilToInt(cx + r); x++)
                    if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r) Plot(px, x, y, 200);
        }

        static void Plot(Color32[] px, int x, int y, byte alpha)
        {
            if (x < 0 || y < 0 || x >= CrackWidth || y >= CrackHeight) return;
            int i = y * CrackWidth + x;
            if (px[i].a < alpha) px[i] = new Color32(225, 235, 240, alpha); // pale glass edge
            // A dark shadow line beside the bright edge reads as a crack on both sky and ground.
            int j = i + 1;
            if (x + 1 < CrackWidth && px[j].a == 0) px[j] = new Color32(10, 12, 14, (byte)(alpha / 2));
        }
    }
}
