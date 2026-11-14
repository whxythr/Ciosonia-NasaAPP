using System;
using System.Collections.Generic;
using SpaceSim.Vehicles;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Simple line-art icons of the hangar menu, drawn in code (no art assets): one per catalog category and one per propellant.
    /// White shapes on transparent background, tinted with GUI.color when drawn. Built once, anti-aliased with signed distances.
    /// </summary>
    public static class HangarIcons
    {
        const int Size = 48;

        static readonly Dictionary<ModuleCategory, Texture2D> categories = new Dictionary<ModuleCategory, Texture2D>();
        static readonly Dictionary<PropellantType, Texture2D> propellants = new Dictionary<PropellantType, Texture2D>();

        /// <summary>Icon of a catalog category.</summary>
        public static Texture2D Category(ModuleCategory c)
        {
            if (categories.TryGetValue(c, out Texture2D t) && t != null) return t;
            t = Paint(CategoryShape(c));
            categories[c] = t;
            return t;
        }

        /// <summary>Icon of a propellant (null for <see cref="PropellantType.None"/>).</summary>
        public static Texture2D Propellant(PropellantType p)
        {
            if (p == PropellantType.None) return null;
            if (propellants.TryGetValue(p, out Texture2D t) && t != null) return t;
            t = Paint(PropellantShape(p));
            propellants[p] = t;
            return t;
        }

        /// <summary>Tint of a propellant icon (HORIZON palette).</summary>
        public static Color PropellantColor(PropellantType p)
        {
            switch (p)
            {
                case PropellantType.Kerolox: return HudStyle.Amber;
                case PropellantType.Hydrolox: return HudStyle.Cyan;
                case PropellantType.Hypergolic: return HudStyle.Violet;
                case PropellantType.Solid: return HudStyle.TextPanel;
                case PropellantType.LiquidHydrogen: return new Color(0.70f, 0.88f, 1f);
                case PropellantType.Xenon: return HudStyle.Green;
                default: return HudStyle.TextMuted;
            }
        }

        // ---- shapes: signed distance in icon units (0..1, y down); negative inside ----

        delegate float Shape(Vector2 p);

        static Shape CategoryShape(ModuleCategory c)
        {
            switch (c)
            {
                case ModuleCategory.Cabin:
                    return p => Subtract(Union(Poly(p, V(0.38f, 0.16f), V(0.62f, 0.16f), V(0.80f, 0.74f), V(0.20f, 0.74f)),
                                               Box(p, 0.17f, 0.78f, 0.83f, 0.86f, 0.03f)),
                                         Circle(p, 0.5f, 0.47f, 0.085f));
                case ModuleCategory.Engine:
                    return p => Union(Box(p, 0.34f, 0.10f, 0.66f, 0.26f, 0.03f),
                                Union(Box(p, 0.43f, 0.25f, 0.57f, 0.40f, 0f),
                                      Poly(p, V(0.43f, 0.38f), V(0.57f, 0.38f), V(0.78f, 0.88f), V(0.22f, 0.88f))));
                case ModuleCategory.FuelTank:
                    return p => Subtract(Subtract(Box(p, 0.29f, 0.08f, 0.71f, 0.92f, 0.13f), Box(p, 0.25f, 0.33f, 0.75f, 0.37f, 0f)),
                                         Box(p, 0.25f, 0.63f, 0.75f, 0.67f, 0f));
                case ModuleCategory.Aerodynamics:
                    return p => Union(Box(p, 0.22f, 0.06f, 0.34f, 0.94f, 0.03f),
                                      Poly(p, V(0.34f, 0.18f), V(0.48f, 0.18f), V(0.86f, 0.74f), V(0.86f, 0.88f), V(0.34f, 0.88f)));
                case ModuleCategory.Science:
                    return p => Union(Box(p, 0.41f, 0.10f, 0.59f, 0.42f, 0.02f),
                                Union(Box(p, 0.35f, 0.08f, 0.65f, 0.14f, 0.02f),
                                      Poly(p, V(0.41f, 0.40f), V(0.59f, 0.40f), V(0.84f, 0.88f), V(0.16f, 0.88f))));
                case ModuleCategory.Auxiliary:
                    return p =>
                    {
                        float dome = Math.Max(Circle(p, 0.5f, 0.50f, 0.36f), p.y - 0.50f);
                        float lines = Math.Min(Segment(p, V(0.16f, 0.50f), V(0.47f, 0.80f), 0.025f),
                                      Math.Min(Segment(p, V(0.84f, 0.50f), V(0.53f, 0.80f), 0.025f),
                                               Segment(p, V(0.50f, 0.50f), V(0.50f, 0.80f), 0.025f)));
                        return Union(Union(dome, lines), Box(p, 0.42f, 0.79f, 0.58f, 0.92f, 0.02f));
                    };
                case ModuleCategory.Communication:
                    return p =>
                    {
                        float dish = Subtract(Circle(p, 0.42f, 0.42f, 0.32f), Circle(p, 0.56f, 0.28f, 0.33f));
                        float mast = Segment(p, V(0.44f, 0.46f), V(0.62f, 0.82f), 0.035f);
                        float feed = Segment(p, V(0.36f, 0.50f), V(0.64f, 0.22f), 0.022f);
                        return Union(Union(dish, mast), Union(feed, Box(p, 0.46f, 0.80f, 0.82f, 0.88f, 0.02f)));
                    };
                case ModuleCategory.Mechanics:
                    return p =>
                    {
                        float gear = Circle(p, 0.5f, 0.5f, 0.27f);
                        for (int k = 0; k < 8; k++)
                        {
                            float a = k * Mathf.PI / 4f;
                            Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                            gear = Union(gear, Segment(p, V(0.5f, 0.5f) + d * 0.22f, V(0.5f, 0.5f) + d * 0.37f, 0.06f));
                        }
                        return Subtract(gear, Circle(p, 0.5f, 0.5f, 0.10f));
                    };
                case ModuleCategory.Cargo:
                    return p =>
                    {
                        float frame = Subtract(Box(p, 0.16f, 0.22f, 0.84f, 0.84f, 0.03f), Box(p, 0.23f, 0.29f, 0.77f, 0.77f, 0f));
                        float cross = Math.Min(Segment(p, V(0.23f, 0.29f), V(0.77f, 0.77f), 0.03f), Segment(p, V(0.77f, 0.29f), V(0.23f, 0.77f), 0.03f));
                        return Union(Union(frame, cross), Box(p, 0.12f, 0.14f, 0.88f, 0.20f, 0.02f));
                    };
                default:
                    return p => Circle(p, 0.5f, 0.5f, 0.3f);
            }
        }

        static Shape PropellantShape(PropellantType t)
        {
            switch (t)
            {
                case PropellantType.Kerolox:
                    return Drop(0.5f, 0.0f, 1f);
                case PropellantType.Hydrolox:
                    return p => Subtract(Drop(0.5f, 0.0f, 1f)(p), Circle(p, 0.5f, 0.64f, 0.11f));
                case PropellantType.Hypergolic:
                    return p => Union(Drop(0.31f, 0.12f, 0.72f)(p), Drop(0.69f, 0.12f, 0.72f)(p));
                case PropellantType.Solid:
                    return p => Subtract(Box(p, 0.20f, 0.20f, 0.80f, 0.80f, 0.08f), Circle(p, 0.5f, 0.5f, 0.12f));
                case PropellantType.LiquidHydrogen:
                    return p =>
                    {
                        float s = Segment(p, V(0.5f, 0.12f), V(0.5f, 0.88f), 0.05f);
                        s = Union(s, Segment(p, V(0.17f, 0.31f), V(0.83f, 0.69f), 0.05f));
                        return Union(s, Segment(p, V(0.17f, 0.69f), V(0.83f, 0.31f), 0.05f));
                    };
                case PropellantType.Xenon:
                    return p => Union(Circle(p, 0.5f, 0.5f, 0.13f), Math.Abs(Circle(p, 0.5f, 0.5f, 0.34f)) - 0.045f);
                default:
                    return p => Circle(p, 0.5f, 0.5f, 0.3f);
            }
        }

        /// <summary>A drop shape of the given height scale, centred at x, starting at y0.</summary>
        static Shape Drop(float x, float y0, float scale) => p =>
        {
            Vector2 q = new Vector2((p.x - x) / scale + 0.5f, (p.y - y0) / scale);
            float d = Union(Circle(q, 0.5f, 0.62f, 0.27f), Poly(q, V(0.5f, 0.08f), V(0.265f, 0.56f), V(0.735f, 0.56f)));
            return d * scale;
        };

        static Vector2 V(float x, float y) => new Vector2(x, y);
        static float Dot(Vector2 a, Vector2 b) => a.x * b.x + a.y * b.y;
        static float Union(float a, float b) => Math.Min(a, b);
        static float Subtract(float a, float b) => Math.Max(a, -b);

        static float Circle(Vector2 p, float cx, float cy, float r) => Mathf.Sqrt((p.x - cx) * (p.x - cx) + (p.y - cy) * (p.y - cy)) - r;

        static float Box(Vector2 p, float x0, float y0, float x1, float y1, float radius)
        {
            float cx = 0.5f * (x0 + x1), cy = 0.5f * (y0 + y1);
            float hx = 0.5f * (x1 - x0) - radius, hy = 0.5f * (y1 - y0) - radius;
            float qx = Math.Abs(p.x - cx) - hx, qy = Math.Abs(p.y - cy) - hy;
            float outside = Mathf.Sqrt(Math.Max(qx, 0f) * Math.Max(qx, 0f) + Math.Max(qy, 0f) * Math.Max(qy, 0f));
            return outside + Math.Min(Math.Max(qx, qy), 0f) - radius;
        }

        static float Segment(Vector2 p, Vector2 a, Vector2 b, float halfWidth)
        {
            Vector2 pa = p - a, ba = b - a;
            float h = Mathf.Clamp01(Dot(pa, ba) / Math.Max(1e-6f, Dot(ba, ba)));
            Vector2 d = pa - ba * h;
            return Mathf.Sqrt(d.x * d.x + d.y * d.y) - halfWidth;
        }

        /// <summary>Signed distance to a convex or concave polygon.</summary>
        static float Poly(Vector2 p, params Vector2[] v)
        {
            float d = Dot(p - v[0], p - v[0]);
            float s = 1f;
            for (int i = 0, j = v.Length - 1; i < v.Length; j = i, i++)
            {
                Vector2 e = v[j] - v[i], w = p - v[i];
                Vector2 b = w - e * Mathf.Clamp01(Dot(w, e) / Math.Max(1e-6f, Dot(e, e)));
                d = Math.Min(d, Dot(b, b));
                bool c1 = p.y >= v[i].y, c2 = p.y < v[j].y, c3 = e.x * w.y > e.y * w.x;
                if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) s = -s;
            }
            return s * Mathf.Sqrt(d);
        }

        static Texture2D Paint(Shape shape)
        {
            var t = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var px = new Color[Size * Size];
            float pixel = 1f / Size;
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                // Texture rows go bottom → top; the shapes are written with y down.
                var p = new Vector2((x + 0.5f) * pixel, 1f - (y + 0.5f) * pixel);
                float d = shape(p) / pixel;
                px[y * Size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(0.5f - d));
            }
            t.SetPixels(px);
            t.Apply();
            return t;
        }
    }
}
