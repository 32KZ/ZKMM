using System;

namespace ZKModMenu.Core {
    /// <summary>
    /// A small CPU-side image the canvas stretches with smooth filtering. Used for the page background
    /// and for the mods' pictures.
    /// </summary>
    public sealed class ZkImage {
        public readonly int Width;
        public readonly int Height;
        public readonly ZkColor[] Pixels;

        /// <summary>Bump after changing <see cref="Pixels"/> so the canvas re-uploads it.</summary>
        public int Version;

        /// <summary>Slot for the canvas to keep its own texture in.</summary>
        public object Handle;

        public ZkImage(int width, int height) {
            Width = width;
            Height = height;
            Pixels = new ZkColor[width * height];
        }

        /// <summary>
        /// Scale a picture (8-bit RGBA rows, straight alpha) to fill <paramref name="outW"/> x <paramref name="outH"/>,
        /// cutting off what sticks out on either side, and round its corners.
        /// <paramref name="corner"/> is the corner radius as a fraction of the shorter side.
        /// </summary>
        public static ZkImage Cover(byte[] rgba, int width, int height, int outW, int outH, float corner) {
            if (rgba == null || width <= 0 || height <= 0 || rgba.Length < width * height * 4)
                return null;
            ZkImage image = new ZkImage(outW, outH);

            // The part of the source that is used, with the output's shape, centred.
            float scale = Math.Max(outW / (float) width, outH / (float) height);
            float srcW = outW / scale, srcH = outH / scale;
            float srcX = (width - srcW) / 2f, srcY = (height - srcH) / 2f;
            float step = 1f / scale;

            for (int y = 0; y < outH; y++) {
                float y0 = srcY + y * step, y1 = y0 + step;
                int ya = Math.Max(0, (int) y0), yb = Math.Min(height, Math.Max(ya + 1, (int) Math.Ceiling(y1)));
                for (int x = 0; x < outW; x++) {
                    float x0 = srcX + x * step, x1 = x0 + step;
                    int xa = Math.Max(0, (int) x0), xb = Math.Min(width, Math.Max(xa + 1, (int) Math.Ceiling(x1)));
                    // Box filter: the average of every source pixel under this one.
                    long r = 0, g = 0, b = 0, a = 0, n = 0;
                    for (int sy = ya; sy < yb; sy++) {
                        int i = (sy * width + xa) * 4;
                        for (int sx = xa; sx < xb; sx++, i += 4) {
                            int alpha = rgba[i + 3];
                            r += rgba[i] * alpha;
                            g += rgba[i + 1] * alpha;
                            b += rgba[i + 2] * alpha;
                            a += alpha;
                            n++;
                        }
                    }
                    ZkColor c = a == 0 ? ZkColor.Transparent
                        : new ZkColor((byte) (r / a), (byte) (g / a), (byte) (b / a), (byte) (a / n));
                    image.Pixels[y * outW + x] = c;
                }
            }

            RoundCorners(image, corner * Math.Min(outW, outH));
            return image;
        }

        /// <summary>Fade out the pixels outside rounded corners of the given radius, smoothing the edge.</summary>
        public static void RoundCorners(ZkImage image, float radius) {
            if (radius < 1f)
                return;
            int w = image.Width, h = image.Height;
            int reach = Math.Min((int) Math.Ceiling(radius), Math.Min(w, h) / 2);
            for (int y = 0; y < reach; y++) {
                for (int x = 0; x < reach; x++) {
                    float dx = radius - (x + 0.5f), dy = radius - (y + 0.5f);
                    if (dx <= 0f || dy <= 0f)
                        continue;
                    float cover = radius - (float) Math.Sqrt(dx * dx + dy * dy) + 0.5f;
                    if (cover >= 1f)
                        continue;
                    cover = Math.Max(0f, cover);
                    Fade(image, x, y, cover);
                    Fade(image, w - 1 - x, y, cover);
                    Fade(image, x, h - 1 - y, cover);
                    Fade(image, w - 1 - x, h - 1 - y, cover);
                }
            }
        }

        private static void Fade(ZkImage image, int x, int y, float cover) {
            int i = y * image.Width + x;
            image.Pixels[i] = image.Pixels[i].Fade(cover);
        }
    }

    /// <summary>
    /// Everything the menu needs to draw itself, in a 1920x1080 space.
    /// In game this is backed by Monocle's Draw and ActiveFont.
    /// </summary>
    public interface IZkCanvas {
        /// <summary>Fill an axis-aligned rectangle.</summary>
        void Rect(float x, float y, float w, float h, ZkColor color);

        /// <summary>Draw text. justifyX / justifyY of 0.5 centre the text on the point.</summary>
        void Text(string text, float x, float y, float justifyX, float justifyY, float scale, ZkColor color);

        /// <summary>Width of the text at the given scale.</summary>
        float TextWidth(string text, float scale);

        /// <summary>Height of one line of text at the given scale.</summary>
        float LineHeight(float scale);

        /// <summary>Draw a part of an image (source in 0..1 fractions) stretched over the destination.</summary>
        void Image(ZkImage image, float srcX, float srcY, float srcW, float srcH, float x, float y, float w, float h);
    }
}
