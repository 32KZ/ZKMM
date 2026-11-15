namespace ZKModMenu.Core {
    /// <summary>
    /// A tiny CPU-side image the canvas stretches with smooth filtering. Used for the page background.
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
