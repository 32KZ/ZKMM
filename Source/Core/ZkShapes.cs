using System;
using System.Collections.Generic;
using System.Text;

namespace ZKModMenu.Core {
    public enum ZkIcon {
        Star,
        StarOutline,
        Search,
        Gear,
        ArrowUp,
        ArrowDown,
        ArrowLeft,
        ArrowRight,
        ChevronLeft,
        ChevronRight
    }

    /// <summary>
    /// Rounded rectangles, icons and text helpers built on top of <see cref="IZkCanvas.Rect"/>.
    /// The game only gives us filled rectangles and text, so curves are drawn as short
    /// horizontal spans with soft (partially transparent) ends.
    /// </summary>
    public static class ZkShapes {

        // ------------------------------------------------------------------ rounded rectangles

        /// <summary>A horizontal span with anti-aliased ends.</summary>
        private static void Span(IZkCanvas c, float x1, float x2, float y, float h, ZkColor color) {
            if (x2 <= x1)
                return;
            float l = (float) Math.Ceiling(x1);
            float r = (float) Math.Floor(x2);
            if (r < l) {
                c.Rect((float) Math.Floor(x1), y, 1f, h, color.Fade(x2 - x1));
                return;
            }
            if (r > l)
                c.Rect(l, y, r - l, h, color);
            float fl = l - x1;
            if (fl > 0.03f)
                c.Rect(l - 1f, y, 1f, h, color.Fade(fl));
            float fr = x2 - r;
            if (fr > 0.03f)
                c.Rect(r, y, 1f, h, color.Fade(fr));
        }

        private static float Inset(float radius, float row) {
            float dy = radius - row - 0.5f;
            return radius - (float) Math.Sqrt(Math.Max(0f, radius * radius - dy * dy));
        }

        public static void RoundRect(IZkCanvas c, float x, float y, float w, float h, float radius, ZkColor color) {
            RoundRect(c, x, y, w, h, radius, color, color);
        }

        /// <summary>Rounded rectangle with a left-to-right gradient.</summary>
        public static void RoundRect(IZkCanvas c, float x, float y, float w, float h, float radius, ZkColor left, ZkColor right) {
            x = (float) Math.Round(x);
            y = (float) Math.Round(y);
            w = (float) Math.Round(w);
            h = (float) Math.Round(h);
            if (w <= 0f || h <= 0f)
                return;
            int r = (int) Math.Max(0f, Math.Min(radius, Math.Min(w, h) / 2f));

            // Middle part: full height, optionally sliced for the gradient.
            float mw = w - 2 * r;
            if (mw > 0f) {
                bool same = left.R == right.R && left.G == right.G && left.B == right.B && left.A == right.A;
                if (same) {
                    c.Rect(x + r, y, mw, h, left);
                } else {
                    int slices = Math.Max(1, Math.Min(48, (int) (mw / 24f)));
                    for (int i = 0; i < slices; i++) {
                        float sx = (float) Math.Round(x + r + mw * i / slices);
                        float ex = (float) Math.Round(x + r + mw * (i + 1) / slices);
                        float t = (sx + ex) / 2f - x;
                        c.Rect(sx, y, ex - sx, h, ZkColor.Lerp(left, right, t / w));
                    }
                }
            }

            if (r <= 0)
                return;

            // Left and right caps.
            if (h - 2 * r > 0f) {
                c.Rect(x, y + r, r, h - 2 * r, left);
                c.Rect(x + w - r, y + r, r, h - 2 * r, right);
            }
            for (int i = 0; i < r; i++) {
                float inset = Inset(r, i);
                Span(c, x + inset, x + r, y + i, 1f, left);
                Span(c, x + inset, x + r, y + h - 1 - i, 1f, left);
                Span(c, x + w - r, x + w - inset, y + i, 1f, right);
                Span(c, x + w - r, x + w - inset, y + h - 1 - i, 1f, right);
            }
        }

        /// <summary>The border of a rounded rectangle, drawn inside its bounds.</summary>
        public static void RoundRectOutline(IZkCanvas c, float x, float y, float w, float h, float radius, float thickness, ZkColor color) {
            x = (float) Math.Round(x);
            y = (float) Math.Round(y);
            w = (float) Math.Round(w);
            h = (float) Math.Round(h);
            float t = (float) Math.Round(thickness);
            if (w <= 0f || h <= 0f || t <= 0f)
                return;
            int r = (int) Math.Max(0f, Math.Min(radius, Math.Min(w, h) / 2f));
            if (r < t)
                r = (int) t;
            if (r * 2 > Math.Min(w, h)) {
                // Too small for corners: plain frame.
                c.Rect(x, y, w, t, color);
                c.Rect(x, y + h - t, w, t, color);
                c.Rect(x, y + t, t, h - 2 * t, color);
                c.Rect(x + w - t, y + t, t, h - 2 * t, color);
                return;
            }

            c.Rect(x + r, y, w - 2 * r, t, color);
            c.Rect(x + r, y + h - t, w - 2 * r, t, color);
            c.Rect(x, y + r, t, h - 2 * r, color);
            c.Rect(x + w - t, y + r, t, h - 2 * r, color);

            float inner = r - t;
            for (int i = 0; i < r; i++) {
                float outerInset = Inset(r, i);
                float dy = r - i - 0.5f;
                float innerInset = r;
                if (inner > 0f && dy < inner)
                    innerInset = r - (float) Math.Sqrt(inner * inner - dy * dy);
                Span(c, x + outerInset, x + innerInset, y + i, 1f, color);
                Span(c, x + outerInset, x + innerInset, y + h - 1 - i, 1f, color);
                Span(c, x + w - innerInset, x + w - outerInset, y + i, 1f, color);
                Span(c, x + w - innerInset, x + w - outerInset, y + h - 1 - i, 1f, color);
            }
        }

        public static void Circle(IZkCanvas c, float cx, float cy, float radius, ZkColor color) {
            RoundRect(c, cx - radius, cy - radius, radius * 2f, radius * 2f, radius, color);
        }

        // ------------------------------------------------------------------ icons

        private struct Run {
            public short X, Y, Length;
            public float Alpha;
        }

        private static readonly Dictionary<int, Run[]> iconCache = new Dictionary<int, Run[]>();

        /// <summary>Draw an icon centred on (cx, cy) inside a size x size box.</summary>
        public static void Icon(IZkCanvas c, ZkIcon icon, float cx, float cy, float size, ZkColor color) {
            int s = Math.Max(4, (int) Math.Round(size));
            int key = ((int) icon << 16) | s;
            Run[] runs;
            lock (iconCache) {
                if (!iconCache.TryGetValue(key, out runs))
                    iconCache[key] = runs = Rasterize(Contours(icon), s);
            }
            float ox = (float) Math.Round(cx - s / 2f);
            float oy = (float) Math.Round(cy - s / 2f);
            foreach (Run run in runs)
                c.Rect(ox + run.X, oy + run.Y, run.Length, 1f, run.Alpha >= 0.999f ? color : color.Fade(run.Alpha));
        }

        // Contours are in a unit square, filled with the even-odd rule.
        private static List<float[]> Contours(ZkIcon icon) {
            List<float[]> list = new List<float[]>();
            switch (icon) {
                case ZkIcon.Star:
                    list.Add(StarPoints(0.5f, 0.53f, 0.52f, 0.235f));
                    break;

                case ZkIcon.StarOutline:
                    list.Add(StarPoints(0.5f, 0.53f, 0.52f, 0.235f));
                    list.Add(StarPoints(0.5f, 0.53f, 0.33f, 0.15f));
                    break;

                case ZkIcon.Search:
                    list.Add(CirclePoints(0.42f, 0.42f, 0.36f));
                    list.Add(CirclePoints(0.42f, 0.42f, 0.24f));
                    list.Add(new float[] { 0.60f, 0.70f, 0.70f, 0.60f, 0.97f, 0.87f, 0.87f, 0.97f });
                    break;

                case ZkIcon.Gear: {
                    const int teeth = 8;
                    float[] pts = new float[teeth * 4 * 2];
                    for (int i = 0; i < teeth; i++) {
                        double a = Math.PI * 2 * i / teeth;
                        double step = Math.PI * 2 / teeth;
                        SetPolar(pts, i * 4 + 0, a - step * 0.30, 0.37f);
                        SetPolar(pts, i * 4 + 1, a - step * 0.17, 0.50f);
                        SetPolar(pts, i * 4 + 2, a + step * 0.17, 0.50f);
                        SetPolar(pts, i * 4 + 3, a + step * 0.30, 0.37f);
                    }
                    list.Add(pts);
                    list.Add(CirclePoints(0.5f, 0.5f, 0.17f));
                    break;
                }

                case ZkIcon.ArrowUp:
                    list.Add(new float[] { 0.5f, 0.14f, 0.92f, 0.82f, 0.08f, 0.82f });
                    break;
                case ZkIcon.ArrowDown:
                    list.Add(new float[] { 0.08f, 0.18f, 0.92f, 0.18f, 0.5f, 0.86f });
                    break;
                case ZkIcon.ArrowLeft:
                    list.Add(new float[] { 0.14f, 0.5f, 0.82f, 0.08f, 0.82f, 0.92f });
                    break;
                case ZkIcon.ArrowRight:
                    list.Add(new float[] { 0.18f, 0.08f, 0.86f, 0.5f, 0.18f, 0.92f });
                    break;

                case ZkIcon.ChevronLeft:
                    list.Add(new float[] { 0.62f, 0.08f, 0.80f, 0.22f, 0.44f, 0.5f, 0.80f, 0.78f, 0.62f, 0.92f, 0.16f, 0.5f });
                    break;
                case ZkIcon.ChevronRight:
                    list.Add(new float[] { 0.38f, 0.08f, 0.84f, 0.5f, 0.38f, 0.92f, 0.20f, 0.78f, 0.56f, 0.5f, 0.20f, 0.22f });
                    break;
            }
            return list;
        }

        private static void SetPolar(float[] pts, int index, double angle, float radius) {
            pts[index * 2] = 0.5f + (float) Math.Cos(angle) * radius;
            pts[index * 2 + 1] = 0.5f + (float) Math.Sin(angle) * radius;
        }

        private static float[] StarPoints(float cx, float cy, float outer, float inner) {
            float[] pts = new float[20];
            for (int i = 0; i < 10; i++) {
                double a = -Math.PI / 2 + Math.PI * i / 5;
                float r = (i % 2 == 0) ? outer : inner;
                pts[i * 2] = cx + (float) Math.Cos(a) * r;
                pts[i * 2 + 1] = cy + (float) Math.Sin(a) * r;
            }
            return pts;
        }

        private static float[] CirclePoints(float cx, float cy, float radius) {
            const int n = 28;
            float[] pts = new float[n * 2];
            for (int i = 0; i < n; i++) {
                double a = Math.PI * 2 * i / n;
                pts[i * 2] = cx + (float) Math.Cos(a) * radius;
                pts[i * 2 + 1] = cy + (float) Math.Sin(a) * radius;
            }
            return pts;
        }

        private static Run[] Rasterize(List<float[]> contours, int size) {
            const int sub = 4;
            float[] coverage = new float[size * size];
            List<float> xs = new List<float>();

            for (int py = 0; py < size; py++) {
                for (int s = 0; s < sub; s++) {
                    float sy = (py + (s + 0.5f) / sub) / size;
                    xs.Clear();
                    foreach (float[] pts in contours) {
                        int n = pts.Length / 2;
                        for (int i = 0; i < n; i++) {
                            float x1 = pts[i * 2], y1 = pts[i * 2 + 1];
                            int j = (i + 1) % n;
                            float x2 = pts[j * 2], y2 = pts[j * 2 + 1];
                            if ((y1 <= sy && y2 > sy) || (y2 <= sy && y1 > sy))
                                xs.Add((x1 + (sy - y1) / (y2 - y1) * (x2 - x1)) * size);
                        }
                    }
                    xs.Sort();
                    for (int i = 0; i + 1 < xs.Count; i += 2) {
                        float a = Math.Max(0f, xs[i]);
                        float b = Math.Min(size, xs[i + 1]);
                        for (int px = (int) a; px < size && px < b; px++) {
                            float overlap = Math.Min(b, px + 1) - Math.Max(a, px);
                            if (overlap > 0f)
                                coverage[py * size + px] += overlap / sub;
                        }
                    }
                }
            }

            List<Run> runs = new List<Run>();
            for (int py = 0; py < size; py++) {
                int px = 0;
                while (px < size) {
                    float a = coverage[py * size + px];
                    if (a < 0.04f) {
                        px++;
                        continue;
                    }
                    if (a >= 0.96f) {
                        int start = px;
                        while (px < size && coverage[py * size + px] >= 0.96f)
                            px++;
                        runs.Add(new Run { X = (short) start, Y = (short) py, Length = (short) (px - start), Alpha = 1f });
                    } else {
                        runs.Add(new Run { X = (short) px, Y = (short) py, Length = 1, Alpha = a });
                        px++;
                    }
                }
            }
            return runs.ToArray();
        }

        // ------------------------------------------------------------------ text

        /// <summary>Shorten text with "..." so it fits in maxWidth.</summary>
        public static string Ellipsize(IZkCanvas c, string text, float scale, float maxWidth) {
            if (string.IsNullOrEmpty(text) || c.TextWidth(text, scale) <= maxWidth)
                return text ?? "";
            const string dots = "...";
            int lo = 0, hi = text.Length;
            while (lo < hi) {
                int mid = (lo + hi + 1) / 2;
                if (c.TextWidth(text.Substring(0, mid).TrimEnd() + dots, scale) <= maxWidth)
                    lo = mid;
                else
                    hi = mid - 1;
            }
            return lo <= 0 ? dots : text.Substring(0, lo).TrimEnd() + dots;
        }

        /// <summary>Word-wrap text into at most maxLines lines; the last line is shortened if needed.</summary>
        public static List<string> Wrap(IZkCanvas c, string text, float scale, float maxWidth, int maxLines) {
            List<string> lines = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
                return lines;

            string[] words = text.Replace("\r", "").Replace('\n', ' ').Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            StringBuilder line = new StringBuilder();
            for (int i = 0; i < words.Length; i++) {
                string candidate = line.Length == 0 ? words[i] : line + " " + words[i];
                if (line.Length > 0 && c.TextWidth(candidate, scale) > maxWidth) {
                    if (lines.Count == maxLines - 1) {
                        // Out of lines: put the rest on this one and cut it.
                        string rest = line + " " + string.Join(" ", words, i, words.Length - i);
                        lines.Add(Ellipsize(c, rest, scale, maxWidth));
                        return lines;
                    }
                    lines.Add(line.ToString());
                    line.Clear();
                    line.Append(words[i]);
                } else {
                    line.Clear();
                    line.Append(candidate);
                }
            }
            if (line.Length > 0)
                lines.Add(Ellipsize(c, line.ToString(), scale, maxWidth));
            return lines;
        }
    }
}
