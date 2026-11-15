using System;
using System.Globalization;

namespace ZKModMenu.Core {
    /// <summary>
    /// A plain RGBA colour with straight (non-premultiplied) alpha.
    /// The core UI never touches XNA types so it can be rendered outside the game too.
    /// </summary>
    public readonly struct ZkColor {
        public readonly byte R, G, B, A;

        public ZkColor(byte r, byte g, byte b, byte a = 255) {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        public static readonly ZkColor White = new ZkColor(255, 255, 255);
        public static readonly ZkColor Black = new ZkColor(0, 0, 0);
        public static readonly ZkColor Transparent = new ZkColor(0, 0, 0, 0);

        /// <summary>Parse "#rrggbb" (or "rrggbb").</summary>
        public static ZkColor Hex(string hex, float alpha = 1f) {
            if (hex.StartsWith("#"))
                hex = hex.Substring(1);
            byte r = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte g = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte b = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return new ZkColor(r, g, b, ToByte(alpha * 255f));
        }

        /// <summary>Multiply the alpha.</summary>
        public ZkColor Fade(float alpha) => new ZkColor(R, G, B, ToByte(A * Clamp01(alpha)));

        public static ZkColor Lerp(ZkColor a, ZkColor b, float t) {
            t = Clamp01(t);
            return new ZkColor(
                ToByte(a.R + (b.R - a.R) * t),
                ToByte(a.G + (b.G - a.G) * t),
                ToByte(a.B + (b.B - a.B) * t),
                ToByte(a.A + (b.A - a.A) * t)
            );
        }

        /// <summary>Paint <paramref name="top"/> over an opaque <paramref name="bottom"/>.</summary>
        public static ZkColor Over(ZkColor bottom, ZkColor top) {
            float a = top.A / 255f;
            return new ZkColor(
                ToByte(bottom.R + (top.R - bottom.R) * a),
                ToByte(bottom.G + (top.G - bottom.G) * a),
                ToByte(bottom.B + (top.B - bottom.B) * a)
            );
        }

        /// <summary>h in degrees, s and l in 0..1.</summary>
        public static ZkColor Hsl(float h, float s, float l) {
            h = ((h % 360f) + 360f) % 360f / 360f;
            float q = l < 0.5f ? l * (1f + s) : l + s - l * s;
            float p = 2f * l - q;
            return new ZkColor(
                ToByte(HueToRgb(p, q, h + 1f / 3f) * 255f),
                ToByte(HueToRgb(p, q, h) * 255f),
                ToByte(HueToRgb(p, q, h - 1f / 3f) * 255f)
            );
        }

        private static float HueToRgb(float p, float q, float t) {
            if (t < 0f)
                t += 1f;
            if (t > 1f)
                t -= 1f;
            if (t < 1f / 6f)
                return p + (q - p) * 6f * t;
            if (t < 0.5f)
                return q;
            if (t < 2f / 3f)
                return p + (q - p) * (2f / 3f - t) * 6f;
            return p;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

        private static byte ToByte(float v) => (byte) Math.Max(0, Math.Min(255, (int) Math.Round(v)));
    }
}
