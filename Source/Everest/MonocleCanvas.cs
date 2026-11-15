using System.Collections.Generic;
using Celeste;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;
using ZKModMenu.Core;

namespace ZKModMenu {
    /// <summary>
    /// Draws the menu with the game's own sprite batch and font.
    /// Must only be used while the HUD is being rendered.
    /// </summary>
    internal sealed class MonocleCanvas : IZkCanvas {
        private static readonly List<Texture2D> Textures = new List<Texture2D>();
        private static readonly List<ZkImage> Images = new List<ZkImage>();

        // Width of a text at scale 1. Measuring is slow enough to be worth remembering.
        private readonly Dictionary<string, float> widths = new Dictionary<string, float>();

        private sealed class Uploaded {
            public Texture2D Texture;
            public int Version;
        }

        // The HUD is drawn with premultiplied alpha.
        private static Color ToColor(ZkColor c) {
            if (c.A == 255)
                return new Color((int) c.R, (int) c.G, (int) c.B, 255);
            int a = c.A;
            return new Color((c.R * a + 127) / 255, (c.G * a + 127) / 255, (c.B * a + 127) / 255, a);
        }

        public void Rect(float x, float y, float w, float h, ZkColor color) {
            if (color.A == 0 || w < 1f || h < 1f)
                return;
            Draw.Rect(x, y, w, h, ToColor(color));
        }

        public void Text(string text, float x, float y, float justifyX, float justifyY, float scale, ZkColor color) {
            if (string.IsNullOrEmpty(text) || color.A == 0)
                return;
            ActiveFont.Draw(text, new Vector2(x, y), new Vector2(justifyX, justifyY), new Vector2(scale, scale), ToColor(color));
        }

        public float TextWidth(string text, float scale) {
            if (string.IsNullOrEmpty(text))
                return 0f;
            if (!widths.TryGetValue(text, out float width)) {
                if (widths.Count > 4000)
                    widths.Clear();
                width = ActiveFont.Measure(text).X;
                widths[text] = width;
            }
            return width * scale;
        }

        public float LineHeight(float scale) {
            return ActiveFont.LineHeight * scale;
        }

        public void Image(ZkImage image, float srcX, float srcY, float srcW, float srcH, float x, float y, float w, float h) {
            if (image == null || w <= 0f || h <= 0f || srcW <= 0f || srcH <= 0f)
                return;

            Uploaded uploaded = image.Handle as Uploaded;
            if (uploaded == null || uploaded.Texture == null || uploaded.Texture.IsDisposed || uploaded.Version != image.Version) {
                if (uploaded != null && uploaded.Texture != null && !uploaded.Texture.IsDisposed) {
                    Textures.Remove(uploaded.Texture);
                    uploaded.Texture.Dispose();
                }

                Color[] data = new Color[image.Pixels.Length];
                for (int i = 0; i < data.Length; i++)
                    data[i] = ToColor(image.Pixels[i]);
                Texture2D texture = new Texture2D(Engine.Graphics.GraphicsDevice, image.Width, image.Height, false, SurfaceFormat.Color);
                texture.SetData(data);

                uploaded = new Uploaded { Texture = texture, Version = image.Version };
                image.Handle = uploaded;
                Textures.Add(texture);
                if (!Images.Contains(image))
                    Images.Add(image);
            }

            int sx = (int) System.Math.Round(srcX * image.Width);
            int sy = (int) System.Math.Round(srcY * image.Height);
            int sw = System.Math.Max(1, (int) System.Math.Round(srcW * image.Width));
            int sh = System.Math.Max(1, (int) System.Math.Round(srcH * image.Height));
            Rectangle source = new Rectangle(sx, sy, sw, sh);
            Rectangle target = new Rectangle((int) x, (int) y, (int) w, (int) h);
            Draw.SpriteBatch.Draw(uploaded.Texture, target, source, Color.White);
        }

        /// <summary>Free the textures made for images. They are made again when needed.</summary>
        public static void DisposeTextures() {
            foreach (Texture2D texture in Textures) {
                if (!texture.IsDisposed)
                    texture.Dispose();
            }
            Textures.Clear();
            foreach (ZkImage image in Images)
                image.Handle = null;
            Images.Clear();
        }
    }
}
