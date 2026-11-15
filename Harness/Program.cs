using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkiaSharp;
using ZKModMenu.Core;

namespace ZKModMenu.Harness {

    // ---------------------------------------------------------------- canvas

    sealed class SkiaCanvas : IZkCanvas, IDisposable {
        public readonly SKBitmap Bitmap = new SKBitmap(1920, 1080, SKColorType.Rgba8888, SKAlphaType.Premul);
        readonly SKCanvas canvas;
        readonly SKTypeface face;
        readonly SKPaint paint = new SKPaint { IsAntialias = false };
        public int Rects, Texts;

        public SkiaCanvas() {
            canvas = new SKCanvas(Bitmap);
            face = SKTypeface.FromFile("/usr/share/fonts/opentype/inter/Inter-Bold.otf") ?? SKTypeface.Default;
        }

        static SKColor C(ZkColor c) => new SKColor(c.R, c.G, c.B, c.A);

        // Rough stand-in for the game's font (Renogare at 64px, which is fairly wide).
        const float FontPx = 56f, Line = 66f, Stretch = 1.1f;

        SKPaint TextPaint(float scale, ZkColor color) => new SKPaint {
            Typeface = face, TextSize = FontPx * scale, IsAntialias = true, Color = C(color), TextScaleX = Stretch
        };

        public void Rect(float x, float y, float w, float h, ZkColor color) {
            Rects++;
            // Same truncation as Monocle's Draw.Rect.
            paint.Color = C(color);
            canvas.DrawRect(new SKRect((int) x, (int) y, (int) x + (int) w, (int) y + (int) h), paint);
        }

        public void Text(string text, float x, float y, float jx, float jy, float scale, ZkColor color) {
            if (string.IsNullOrEmpty(text)) return;
            Texts++;
            using SKPaint p = TextPaint(scale, color);
            float w = p.MeasureText(text);
            float lh = Line * scale;
            float top = y - lh * jy;
            float baseline = top + lh * 0.5f + p.TextSize * 0.36f;
            canvas.DrawText(text, x - w * jx, baseline, p);
        }

        public float TextWidth(string text, float scale) {
            if (string.IsNullOrEmpty(text)) return 0f;
            using SKPaint p = TextPaint(scale, ZkColor.White);
            return p.MeasureText(text);
        }

        public float LineHeight(float scale) => Line * scale;

        public void Image(ZkImage image, float sx, float sy, float sw, float sh, float x, float y, float w, float h) {
            if (!(image.Handle is SKBitmap bmp)) {
                bmp = new SKBitmap(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
                for (int py = 0; py < image.Height; py++)
                    for (int px = 0; px < image.Width; px++)
                        bmp.SetPixel(px, py, C(image.Pixels[py * image.Width + px]));
                image.Handle = bmp;
            }
            using SKPaint p = new SKPaint { FilterQuality = SKFilterQuality.Medium };
            canvas.DrawBitmap(bmp, new SKRect(sx * image.Width, sy * image.Height, (sx + sw) * image.Width, (sy + sh) * image.Height), new SKRect(x, y, x + w, y + h), p);
        }

        public void Save(string path) {
            canvas.Flush();
            using SKData data = Bitmap.Encode(SKEncodedImageFormat.Png, 90);
            File.WriteAllBytes(path, data.ToArray());
        }

        public void Dispose() { canvas.Dispose(); Bitmap.Dispose(); }
    }

    // ---------------------------------------------------------------- fake data

    sealed class FakeRow : ZkRow {
        public ZkRowKind K; public string L, Desc; public int Value, Def, Min, Max, Step = 1; public string Unit = ""; public string[] Opts;
        public List<string> Keys = new List<string>(); public string DefKey; public bool NoDefault, Off; public int Ind; public Action OnPress;
        public override ZkRowKind Kind => K;
        public override string Label => L;
        public override string Description => Desc;
        public override bool Disabled => Off;
        public override int Indent => Ind;
        public override bool On => Value != 0;
        public override float Fraction => Max > Min ? (Value - Min) / (float) (Max - Min) : 0f;
        public override string ValueText => K == ZkRowKind.Choice ? Opts[Value] : K == ZkRowKind.Slider ? Value + Unit : "";
        public override bool CanLeft => K != ZkRowKind.Choice || Value > 0;
        public override bool CanRight => K != ZkRowKind.Choice || Value < Opts.Length - 1;
        public override IReadOnlyList<string> Caps => Keys;
        public override bool HasDefault => !NoDefault && K != ZkRowKind.Button && K != ZkRowKind.Header && K != ZkRowKind.Note;
        public override bool Changed => K == ZkRowKind.Keybind ? !(Keys.Count == 1 && Keys[0] == DefKey) : Value != Def;
        public override void Left() { if (K == ZkRowKind.Toggle) Value ^= 1; else if (K == ZkRowKind.Slider) Value = Math.Max(Min, Value - Step); else if (K == ZkRowKind.Choice && Value > 0) Value--; }
        public override void Right() { if (K == ZkRowKind.Toggle) Value ^= 1; else if (K == ZkRowKind.Slider) Value = Math.Min(Max, Value + Step); else if (K == ZkRowKind.Choice && Value < Opts.Length - 1) Value++; }
        public override void Confirm() { if (K == ZkRowKind.Toggle) Value ^= 1; else if (K == ZkRowKind.Choice) Value = (Value + 1) % Opts.Length; else if (K == ZkRowKind.Button) OnPress?.Invoke(); }
        public override bool Reset() { if (!Changed) return false; if (K == ZkRowKind.Keybind) { Keys.Clear(); Keys.Add(DefKey); } else Value = Def; return true; }
    }

    sealed class FakeKey : ZkKeybindRow {
        public string L; public List<string> Keys = new List<string>(); public string Def;
        public override string Label => L;
        public override IReadOnlyList<string> Caps => Keys;
        public override bool HasDefault => true;
        public override bool Changed => !(Keys.Count == 1 && Keys[0] == Def);
        public override bool Reset() { if (!Changed) return false; Keys.Clear(); Keys.Add(Def); return true; }
    }

    sealed class FakeMod : IZkMod {
        public string N, A, V, D; public List<ZkRow> R = new List<ZkRow>(); public bool Opened; public int Opens, Closes;
        public string Id => N.Replace(" ", "");
        public string Name => N;
        public string Version => V;
        public string Author => A;
        public string Description => D;
        public int SettingCount => R.Count(r => r.Selectable);
        public IReadOnlyList<ZkRow> Rows => R;
        public bool Suspended => false;
        public void Open() { Opened = true; Opens++; }
        public void Close() { Opened = false; Closes++; }
        public int ResetAllToDefault() => ZkMenu.ResetRows(R);
    }

    sealed class FakeOptions : IZkOptions {
        public ZkThemeKind Theme { get; set; }
        public bool Compact { get; set; }
        public bool Animations { get; set; } = false;
        public bool ShowMeta { get; set; } = true;
        public bool ShowEmptyMods { get; set; } = true;
        public bool DetectDefaults { get; set; } = true;
        public bool OnlineInfo { get; set; } = true;
        public bool HideModOptionsButton { get; set; }
        public HashSet<string> Favs = new HashSet<string>();
        public bool IsFavourite(string id) => Favs.Contains(id);
        public void SetFavourite(string id, bool f) { if (f) Favs.Add(id); else Favs.Remove(id); }
        public int FavouriteCount => Favs.Count;
        public void ClearFavourites() => Favs.Clear();
    }

    sealed class FakeHost : IZkHost {
        public List<IZkMod> ModList = new List<IZkMod>();
        public FakeOptions Opt = new FakeOptions();
        public bool Closed, TypingOn; public int Saves; public ZkKeybindRow ListenRow;
        public List<ZkRow> Extra = new List<ZkRow> { new FakeKey { L = "Other action button", Keys = { "X" }, Def = "X" } };
        public IReadOnlyList<IZkMod> Mods => ModList;
        public IZkOptions Options => Opt;
        public string Version => "1.0.0";
        public IReadOnlyList<ZkRow> ExtraSettingsRows => Extra;
        public void SaveOptions() => Saves++;
        public void Play(ZkSound sound) { }
        public string Glyph(ZkAction a) => a == ZkAction.Confirm ? "C" : a == ZkAction.Alt ? "X" : a == ZkAction.Back ? "Bksp" : "?";
        public void BeginTyping() => TypingOn = true;
        public void EndTyping() => TypingOn = false;
        public void BeginListening(ZkKeybindRow row) => ListenRow = row;
        public void RequestClose() => Closed = true;
        public void OptionsChanged() { }
    }

    static class Data {
        static ZkRow H(string l) => new ZkTextRow(ZkRowKind.Header, l);
        static ZkRow T(string l, bool d = true, string desc = null) => new FakeRow { K = ZkRowKind.Toggle, L = l, Value = d ? 1 : 0, Def = d ? 1 : 0, Desc = desc };
        static ZkRow S(string l, int min, int max, int step, int d, string unit = "") => new FakeRow { K = ZkRowKind.Slider, L = l, Min = min, Max = max, Step = step, Value = d, Def = d, Unit = unit };
        static ZkRow C(string l, string[] o, int d = 0) => new FakeRow { K = ZkRowKind.Choice, L = l, Opts = o, Value = d, Def = d };
        static ZkRow K(string l, string k) => new FakeKey { L = l, Keys = { k }, Def = k };
        static ZkRow B(string l) => new FakeRow { K = ZkRowKind.Button, L = l };
        static readonly string[] POS = { "Top left", "Top right", "Bottom left", "Bottom right" };

        static FakeMod M(string n, string a, string v, string d, params ZkRow[] rows) => new FakeMod { N = n, A = a, V = v, D = d, R = rows.ToList() };

        public static List<IZkMod> Mods() => new List<IZkMod> {
            M("Afterimage Dash", "Nimbus", "2.3.1", "Leaves a fading silhouette behind Madeline every time she dashes.", H("Trail"), T("Enabled"), S("Trail length", 1, 20, 1, 6), C("Trail colour", new[] { "Match hair", "White", "Rainbow", "Custom" }), S("Opacity", 0, 100, 10, 70, "%"), H("Performance"), T("Shorten trail on low FPS")),
            M("Assist Presets", "Calla Moss", "1.4.0", "Save assist mode setups and swap between them with a single key.", C("Active preset", new[] { "Off", "Practice", "Chill", "Custom 1", "Custom 2" }), K("Next preset", "Tab"), T("Show preset name on screen"), B("Save current as Custom 1")),
            M("Berry Ledger", "PebbleWorks", "3.0.2", "Tracks every strawberry, golden and moon berry across all of your installed maps.", T("Show totals in chapter select"), C("Sort by", new[] { "Map", "Date collected", "Rarity" }), T("Count goldens separately"), T("Show missing berries in pause menu", false), B("Clear ledger")),
            M("Bounce Pad Pack", "Tess Varga", "1.1.0", "Springy blocks and bounce pads for custom map makers.", T("Show hitboxes in editor", false), S("Bounce strength", 50, 200, 10, 100, "%")),
            M("Camera Tweaks", "Ollie Frost", "2.0.5", "Smooth, offset and zoom the camera however you like.", H("View"), S("Zoom", 50, 200, 10, 100, "%"), S("Smoothing", 0, 10, 1, 4), T("Look ahead when running", true, "Moves the camera a little in the direction you are running so you can see what is coming."), H("Controls"), K("Zoom in", "="), K("Zoom out", "-"), H("Effects"), C("Screen shake", new[] { "Off", "Low", "Normal", "High" }, 2)),
            M("Checkpoint Anywhere", "Rowan Ink", "1.2.3", "Drop a practice checkpoint wherever you are standing and jump back to it.", K("Place checkpoint", "Q"), K("Return to checkpoint", "E"), T("Clear when changing room"), S("Max checkpoints", 1, 9, 1, 3)),
            M("Chroma Hair", "Juniper Lane", "4.1.0", "Pick your own hair colours for every dash count, with optional gradients.", C("0 dashes", new[] { "Default", "Red", "Pink", "Teal", "Gold", "White" }, 3), C("1 dash", new[] { "Default", "Red", "Pink", "Teal", "Gold", "White" }, 1), H("Gradient"), T("Animate gradient", false), S("Gradient speed", 1, 10, 1, 5)),
            M("Collab Lobby Helper", "Mira Okafor", "1.6.2", "Quality of life for big collab lobbies: map lists, warp markers and progress at a glance.", T("Show completion %"), T("Show warp markers"), K("Open map list", "M"), C("Lobby music", new[] { "Default", "Quiet", "Off" })),
            M("Controller Glyphs", "Fenwick", "2.2.0", "Shows the correct button icons for whatever controller you are using.", C("Glyph style", new[] { "Auto", "Xbox", "PlayStation", "Switch", "Keyboard" }), T("Use glyphs in dialogue")),
            M("Crystal Heart Radar", "Sol & Sky", "1.0.4", "Gives a gentle ping when a crystal heart is somewhere in the current room.", T("Enabled"), C("Ping type", new[] { "Sound", "Visual", "Both" }, 2), S("Ping volume", 0, 100, 5, 60, "%")),
            M("Death Counter Overlay", "Hazel Quill", "3.3.0", "Shows deaths per room, per chapter and per session in a small overlay.", H("Counters"), T("Room deaths"), T("Chapter deaths"), T("Session deaths", false), H("Display"), C("Position", POS, 1), S("Scale", 50, 200, 10, 100, "%"), K("Reset session count", "F7")),
            M("Dialogue Skipper", "Brisk", "1.3.1", "Fast-forwards through text you have already read.", T("Skip seen dialogue"), S("Text speed", 1, 5, 1, 3), K("Skip key", "LShift")),
            M("Everest Font Fix", null, "1.0.1", null),
            M("Extended Dash Rules", "Kestrel Moss", "5.2.0", "Change how many dashes you get, how far they go and when they refill.", H("Dashes"), S("Dash count", 0, 5, 1, 1), S("Dash speed", 50, 200, 10, 100, "%"), C("Refill on", new[] { "Ground", "Ground or wall", "Never" }), H("Danger zone"), T("Allow in story mode", false), T("Infinite stamina", false)),
            M("Frame Step", "Byte Hollow", "1.5.0", "Pause the game and advance one frame at a time to study tricky movement.", K("Toggle frame step", "F1"), K("Advance one frame", "F2"), T("Show frame counter")),
            M("Ghost Runner", "Ash Kim", "2.4.0", "Race a ghost of your best attempt through every room.", T("Enabled"), C("Compare against", new[] { "Personal best", "Last attempt", "Session best" }), S("Ghost opacity", 10, 100, 10, 50, "%")),
            M("Hitbox Viewer", "Pixel Pals", "3.1.0", "Draws hitboxes for Madeline, hazards and triggers so you can see exactly what touched you.", T("Player hitbox"), T("Hazard hitboxes"), T("Trigger zones", false), K("Toggle all", "F3"), C("Colour scheme", new[] { "Classic", "High contrast", "Colour-blind friendly" })),
            M("Input Display", "Marlowe", "2.8.0", "Shows your button presses on screen. Handy for streams and tutorials.", T("Enabled"), C("Layout", new[] { "Compact", "Full controller", "Arrow cross" }), S("Size", 50, 200, 10, 100, "%")),
            M("Jukebox", "Ren Halley", "1.9.3", "Pick the music for any chapter, including tracks from your custom maps.", C("Music override", new[] { "Off", "Per chapter", "Shuffle all" }), S("Music volume", 0, 100, 5, 80, "%")),
            M("Practice Timer", "Hazel Quill", "4.0.3", "Room, chapter and file timers with optional splits.", H("Timers"), T("Room timer"), T("Chapter timer"), T("File timer", false), H("Format"), C("Time format", new[] { "0.000", "00:00.000", "Frames" }), C("Splits", new[] { "Off", "Per room", "Per checkpoint" }, 1), K("Reset timer", "R")),
            M("Savestates Lite", "Byte Hollow", "2.6.0", "Save and load the game state while practising.", K("Save state", "F8"), K("Load state", "F9"), K("Clear state", "F10"), S("Slots", 1, 5, 1, 1), T("Load state after death", false)),
            M("Zoom Out Map With A Really Long Name That Keeps Going And Going", "Quinn Ard", "1.8.0", "See the whole chapter layout at once. This description is deliberately very long so that the wrapping and the cut-off at three lines can be checked: it keeps going with more words about maps, zooming, rooms, chapters, berries and hearts until it certainly does not fit any more in the space that the header card gives it.", K("Toggle map", "Tab"), S("Map zoom", 25, 100, 5, 50, "%"), T("Show your position")),
        };
    }

    // ---------------------------------------------------------------- scenarios

    static class Program {
        static string outDir;
        static int failures;

        static void Check(bool ok, string what) {
            if (!ok) { failures++; Console.WriteLine("FAIL: " + what); }
        }

        static void Shot(ZkMenu menu, string name) {
            // Settle: the scroll target is worked out while rendering, one frame ahead of the scroll itself.
            for (int i = 0; i < 3; i++) {
                using SkiaCanvas warm = new SkiaCanvas();
                menu.Update(0.5f);
                menu.Render(warm);
            }
            using SkiaCanvas c = new SkiaCanvas();
            menu.Update(0.5f);
            menu.Render(c);
            c.Save(Path.Combine(outDir, name + ".png"));
            Console.WriteLine($"{name}: {c.Rects} rects, {c.Texts} texts");
        }

        static void Do(ZkMenu m, params ZkAction[] actions) { foreach (ZkAction a in actions) { m.Act(a); m.Update(0.016f); } }
        static void Times(ZkMenu m, ZkAction a, int n) { for (int i = 0; i < n; i++) { m.Act(a); m.Update(0.016f); } }

        static FakeHost NewHost() {
            FakeHost h = new FakeHost { ModList = Data.Mods() };
            h.Opt.Favs.Add("PracticeTimer"); h.Opt.Favs.Add("HitboxViewer"); h.Opt.Favs.Add("SavestatesLite");
            return h;
        }

        static void Main(string[] args) {
            outDir = args.Length > 0 ? args[0] : "out";
            Directory.CreateDirectory(outDir);

            // 1. list, top
            FakeHost h = NewHost();
            ZkMenu m = new ZkMenu(h);
            Shot(m, "01-list-dark");
            Do(m, ZkAction.Down, ZkAction.Down);
            Shot(m, "02-list-selected");
            Times(m, ZkAction.Down, 12);
            Shot(m, "03-list-scrolled");

            // favourite toggling keeps selection on the same mod
            Do(m, ZkAction.Alt);
            Shot(m, "04-list-favourited");
            Check(h.Opt.Favs.Count == 4, "favourite added from list");

            // 2. mod page
            Do(m, ZkAction.Confirm);
            Shot(m, "05-mod-page");
            FakeMod opened = (FakeMod) h.ModList.First(x => ((FakeMod) x).Opened);
            Do(m, ZkAction.Down, ZkAction.Right, ZkAction.Right, ZkAction.Down, ZkAction.Confirm, ZkAction.Down);
            Shot(m, "06-mod-page-changed");
            Do(m, ZkAction.Back);
            Check(!opened.Opened && opened.Closes == 1, "mod closed on back");
            Check(!h.Closed, "back from page does not close the menu");

            // 3. camera tweaks: description + keybinds + listening
            h = NewHost(); m = new ZkMenu(h);
            Do(m, ZkAction.Down); Times(m, ZkAction.Down, 7);
            Do(m, ZkAction.Confirm);
            Times(m, ZkAction.Down, 3);
            Shot(m, "07-mod-page-description");
            Times(m, ZkAction.Down, 1);
            Do(m, ZkAction.Confirm);
            Check(m.Listening != null && h.ListenRow != null, "keybind enters listening");
            Shot(m, "08-mod-page-listening");
            ((FakeKey) m.Listening).Keys[0] = "PageUp";
            m.EndListening("Zoom in bound to PageUp");
            Shot(m, "09-mod-page-bound");
            Do(m, ZkAction.Alt);
            Times(m, ZkAction.Down, 9);
            Do(m, ZkAction.Confirm);
            Shot(m, "10-mod-page-reset-armed");
            Do(m, ZkAction.Back, ZkAction.Back);
            Check(h.Closed, "back from list closes the menu");

            // 4. no settings mod + search
            h = NewHost(); m = new ZkMenu(h);
            Do(m, ZkAction.Confirm);
            Check(m.Typing && h.TypingOn, "confirm on search starts typing");
            foreach (char ch in "font") m.TypeChar(ch);
            Shot(m, "11-search-typing");
            m.TypingKey(ZkTypingKey.Enter);
            Check(!m.Typing && !h.TypingOn, "enter stops typing");
            Shot(m, "12-search-results");
            Do(m, ZkAction.Confirm);
            Shot(m, "13-mod-page-no-settings");
            Do(m, ZkAction.Back, ZkAction.Up, ZkAction.Alt);
            Shot(m, "14-search-cleared");

            // no results
            Do(m, ZkAction.Confirm);
            foreach (char ch in "qqq") m.TypeChar(ch);
            m.TypingKey(ZkTypingKey.Enter);
            Shot(m, "15-search-empty");
            Do(m, ZkAction.Alt);

            // 5. ZKMM settings + themes
            h = NewHost(); m = new ZkMenu(h);
            Do(m, ZkAction.Right);
            Shot(m, "16-list-gear");
            Do(m, ZkAction.Confirm);
            Shot(m, "17-settings");
            Do(m, ZkAction.Right);
            Check(h.Opt.Theme == ZkThemeKind.Light, "theme changes to light");
            Shot(m, "18-settings-light");
            Do(m, ZkAction.Back, ZkAction.Left, ZkAction.Down, ZkAction.Down, ZkAction.Down);
            Shot(m, "19-list-light");
            Do(m, ZkAction.Confirm, ZkAction.Down, ZkAction.Down);
            Shot(m, "20-mod-page-light");
            Do(m, ZkAction.Back);
            h.Opt.Theme = ZkThemeKind.Colourful;
            Shot(m, "21-list-colourful");
            Do(m, ZkAction.Confirm, ZkAction.Down, ZkAction.Down, ZkAction.Down);
            Shot(m, "22-mod-page-colourful");
            Do(m, ZkAction.Back);
            h.Opt.Compact = true;
            Times(m, ZkAction.Down, 9);
            Shot(m, "23-list-compact-colourful");

            // settings page bottom + destructive confirm
            h = NewHost(); m = new ZkMenu(h);
            Do(m, ZkAction.Right, ZkAction.Confirm);
            Times(m, ZkAction.Down, 20);
            Do(m, ZkAction.Up, ZkAction.Confirm);
            Shot(m, "24-settings-bottom-armed");
            Check(h.Opt.Favs.Count == 3, "clear favourites needs a second press");
            Do(m, ZkAction.Confirm);
            Check(h.Opt.Favs.Count == 0, "clear favourites on second press");

            // reset all mod settings
            FakeRow someRow = (FakeRow) h.ModList[0].Rows[1];
            someRow.Value = 0;
            Do(m, ZkAction.Down, ZkAction.Confirm, ZkAction.Confirm);
            Check(someRow.Value == 1, "reset all mod settings restores defaults");

            // navigation bounds
            h = NewHost(); m = new ZkMenu(h);
            Times(m, ZkAction.Up, 3);
            Times(m, ZkAction.Down, 100);
            Shot(m, "25-list-bottom");
            Times(m, ZkAction.PageUp, 2);
            Shot(m, "26-list-pageup");
            Do(m, ZkAction.Search);
            Check(m.Typing, "search action starts typing");
            m.TypingKey(ZkTypingKey.Escape);

            // hide empties
            h = NewHost(); h.Opt.ShowEmptyMods = false; m = new ZkMenu(h);
            Shot(m, "27-list-hide-empty");

            Console.WriteLine(failures == 0 ? "ALL CHECKS PASSED" : failures + " CHECK(S) FAILED");
            Environment.Exit(failures == 0 ? 0 : 1);
        }
    }
}
