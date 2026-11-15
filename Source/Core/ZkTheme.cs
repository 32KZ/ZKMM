using System;

namespace ZKModMenu.Core {
    public enum ZkThemeKind {
        Dark,
        Light,
        Colourful
    }

    /// <summary>
    /// The colour roles of one ZKMM theme. Values come straight from the approved mockup.
    /// </summary>
    public sealed class ZkTheme {
        public ZkThemeKind Kind;
        public ZkColor Page, Glow1, Glow2;
        public ZkColor Surface, Surface2, Line;
        public ZkColor Fg, Muted;
        public ZkColor Accent, AccentFg;
        public ZkColor On, OnFg, Off, Star;
        /// <summary>Right-hand colour of the selected list row. Same as Accent unless the theme uses a gradient.</summary>
        public ZkColor AccentEnd;

        private ZkImage background;

        public static readonly ZkTheme Dark = new ZkTheme {
            Kind = ZkThemeKind.Dark,
            Page = ZkColor.Hex("#0f1219"),
            Glow1 = ZkColor.Hex("#1f2a44"),
            Glow2 = ZkColor.Hex("#2a1f33"),
            Surface = ZkColor.Hex("#181c27"),
            Surface2 = ZkColor.Hex("#212737"),
            Line = ZkColor.Hex("#2b3245"),
            Fg = ZkColor.Hex("#e8ebf4"),
            Muted = ZkColor.Hex("#8a93ab"),
            Accent = ZkColor.Hex("#ff8a5c"),
            AccentEnd = ZkColor.Hex("#ff8a5c"),
            AccentFg = ZkColor.Hex("#1a0e08"),
            On = ZkColor.Hex("#4fd6a8"),
            OnFg = ZkColor.Hex("#07291d"),
            Off = ZkColor.Hex("#5a6278"),
            Star = ZkColor.Hex("#ffc94d")
        };

        public static readonly ZkTheme Light = new ZkTheme {
            Kind = ZkThemeKind.Light,
            Page = ZkColor.Hex("#e9edf4"),
            Glow1 = ZkColor.Hex("#d4def0"),
            Glow2 = ZkColor.Hex("#f3dfd6"),
            Surface = ZkColor.Hex("#ffffff"),
            Surface2 = ZkColor.Hex("#f2f4f9"),
            Line = ZkColor.Hex("#d9dee9"),
            Fg = ZkColor.Hex("#18202f"),
            Muted = ZkColor.Hex("#5f6a80"),
            Accent = ZkColor.Hex("#e2562b"),
            AccentEnd = ZkColor.Hex("#e2562b"),
            AccentFg = ZkColor.Hex("#ffffff"),
            On = ZkColor.Hex("#14976c"),
            OnFg = ZkColor.Hex("#ffffff"),
            Off = ZkColor.Hex("#aab2c3"),
            Star = ZkColor.Hex("#d99a00")
        };

        public static readonly ZkTheme Colourful = new ZkTheme {
            Kind = ZkThemeKind.Colourful,
            Page = ZkColor.Hex("#1c0a3c"),
            Glow1 = ZkColor.Hex("#ff3d8b"),
            Glow2 = ZkColor.Hex("#00c2c7"),
            Surface = ZkColor.Hex("#ffffff", 0.08f),
            Surface2 = ZkColor.Hex("#ffffff", 0.16f),
            Line = ZkColor.Hex("#ffffff", 0.18f),
            Fg = ZkColor.Hex("#fff6fb"),
            Muted = ZkColor.Hex("#d3bde6"),
            Accent = ZkColor.Hex("#ffd23f"),
            AccentEnd = ZkColor.Hex("#ff8a3d"),
            AccentFg = ZkColor.Hex("#2a0d45"),
            On = ZkColor.Hex("#5cffc4"),
            OnFg = ZkColor.Hex("#07291d"),
            Off = ZkColor.Hex("#8d72ad"),
            Star = ZkColor.Hex("#ffd23f")
        };

        public static ZkTheme Get(ZkThemeKind kind) {
            switch (kind) {
                case ZkThemeKind.Light:
                    return Light;
                case ZkThemeKind.Colourful:
                    return Colourful;
                default:
                    return Dark;
            }
        }

        /// <summary>
        /// The page background: the page colour with two soft glows, as in the mockup.
        /// It is a quarter-size image that the canvas stretches smoothly over the whole screen.
        /// </summary>
        public ZkImage Background {
            get {
                if (background != null)
                    return background;

                const int w = 480, h = 270;
                ZkImage img = new ZkImage(w, h);
                for (int y = 0; y < h; y++) {
                    for (int x = 0; x < w; x++) {
                        float u = (x + 0.5f) / w;
                        float v = (y + 0.5f) / h;
                        ZkColor c = Page;
                        c = ZkColor.Over(c, Glow1.Fade(0.55f * Falloff(u, v, 0.15f, 0.10f, 0.60f, 0.50f)));
                        c = ZkColor.Over(c, Glow2.Fade(0.50f * Falloff(u, v, 0.90f, 0.90f, 0.50f, 0.50f)));
                        img.Pixels[y * w + x] = c;
                    }
                }
                img.Version = 1;
                return background = img;
            }
        }

        // Elliptical glow that fades to nothing at 70% of its radius.
        private static float Falloff(float u, float v, float cx, float cy, float rx, float ry) {
            float dx = (u - cx) / rx;
            float dy = (v - cy) / ry;
            float d = (float) Math.Sqrt(dx * dx + dy * dy) / 0.7f;
            if (d >= 1f)
                return 0f;
            float t = 1f - d;
            return t * t * (3f - 2f * t);
        }
    }
}
