using System;
using System.Collections.Generic;

namespace ZKModMenu.Core {
    public sealed partial class ZkMenu {

        public const float ScreenW = 1920f;
        public const float ScreenH = 1080f;

        // One centred column, like the mockup.
        private const float ColX = 200f;
        private const float ColW = 1520f;
        private const float ColR = ColX + ColW;
        private const float ViewBottom = 984f;

        // Text scales, relative to the game's font at full size.
        private const float STitle = 0.95f;
        private const float SName = 0.8f;
        private const float SBody = 0.5f;
        private const float SValue = 0.42f;
        private const float SMeta = 0.36f;
        private const float SHead = 0.34f;

        private struct Placed {
            public float Y, H;
            public string Section;
            public bool SectionStar;
            public IZkMod Mod;
            public int Index;
            public ZkRow Row;
            public List<string> Help;
        }

        private readonly List<Placed> placed = new List<Placed>();

        /// <summary>Draw the menu. The canvas is 1920x1080.</summary>
        public void Render(IZkCanvas c) {
            if (Suspended)
                return;

            ZkTheme t = Theme;
            c.Image(t.Background, 0f, 0f, 1f, 1f, 0f, 0f, ScreenW, ScreenH);

            float e = 1f - enter;
            float slide = e * e * e * 40f;

            if (screen == Screen.List)
                RenderList(c, t, slide);
            else
                RenderPage(c, t, slide);

            RenderHints(c, t);
            RenderToast(c, t);
        }

        // Repaint the background above and below the scrolling area, hiding rows that scrolled out.
        private static void Mask(IZkCanvas c, ZkTheme t, float top, float bottom) {
            c.Image(t.Background, 0f, 0f, 1f, top / ScreenH, 0f, 0f, ScreenW, top);
            c.Image(t.Background, 0f, bottom / ScreenH, 1f, (ScreenH - bottom) / ScreenH, 0f, bottom, ScreenW, ScreenH - bottom);
        }

        private static float ScrollFor(float current, float y, float h, float viewH, float total, float margin) {
            float target = current;
            if (float.IsNaN(target))
                target = 0f;
            if (y - margin < target)
                target = y - margin;
            if (y + h + margin > target + viewH)
                target = y + h + margin - viewH;
            return Math.Max(0f, Math.Min(Math.Max(0f, total - viewH), target));
        }

        // ------------------------------------------------------------------ mod list

        private void RenderList(IZkCanvas c, ZkTheme t, float slide) {
            EnsureList();
            IZkOptions o = host.Options;
            bool compact = o.Compact;
            const float viewTop = 252f;
            float viewH = ViewBottom - viewTop;
            float rowH = compact ? 56f : 82f;
            float gap = compact ? 6f : 8f;
            const float sectionH = 52f;
            bool searching = query.Trim().Length > 0;

            // Lay the list out.
            placed.Clear();
            float y = 0f;
            int index = 0;
            if (favs.Count > 0) {
                placed.Add(new Placed { Y = y, H = sectionH, Section = "FAVOURITES", SectionStar = true, Index = favs.Count });
                y += sectionH;
                foreach (IZkMod m in favs) {
                    placed.Add(new Placed { Y = y, H = rowH, Mod = m, Index = ++index });
                    y += rowH + gap;
                }
                y += 6f;
            }
            if (rest.Count > 0) {
                placed.Add(new Placed { Y = y, H = sectionH, Section = searching ? "RESULTS" : "ALL MODS", Index = rest.Count });
                y += sectionH;
                foreach (IZkMod m in rest) {
                    placed.Add(new Placed { Y = y, H = rowH, Mod = m, Index = ++index });
                    y += rowH + gap;
                }
            }
            float total = y;

            // Keep the selected row in view.
            float target = 0f;
            if (listPos > 1) {
                foreach (Placed p in placed) {
                    if (p.Mod != null && p.Index == listPos) {
                        target = ScrollFor(listScrollTarget, p.Y, p.H, viewH, total, 76f);
                        break;
                    }
                }
            }
            listScrollTarget = target;
            if (float.IsNaN(listScroll))
                listScroll = target;

            // Rows.
            float top = viewTop - (float) Math.Round(listScroll);
            foreach (Placed p in placed) {
                float py = top + p.Y;
                if (py + p.H < viewTop - 2f || py > ViewBottom + 2f)
                    continue;
                if (p.Section != null)
                    DrawSection(c, t, ColX + slide, py, ColW, p.H, p.Section, p.Index, p.SectionStar);
                else
                    DrawModRow(c, t, p.Mod, ColX + slide, py, ColW, p.H, listPos == p.Index && !typing, compact, o.ShowMeta);
            }
            if (all.Count == 0) {
                string message = searching ? "No mods match \"" + query.Trim() + "\"." : "No mods to show.";
                c.Text(message, ScreenW / 2f, viewTop + 110f, 0.5f, 0.5f, SBody, t.Muted);
                if (searching)
                    c.Text("Select the search box to change the search, or press " + host.Glyph(ZkAction.Alt) + " on it to clear it.",
                        ScreenW / 2f, viewTop + 160f, 0.5f, 0.5f, SMeta, t.Muted);
            }

            Mask(c, t, viewTop, ViewBottom);

            // Fixed header.
            DrawCrumbs(c, t, "PAUSE", "ZK MOD MENU", null);

            float titleY = 122f;
            c.Text("ZK", ColX + slide, titleY, 0f, 0.5f, STitle, t.Fg);
            c.Text("Mod Menu", ColX + slide + c.TextWidth("ZK ", STitle), titleY, 0f, 0.5f, STitle, t.Accent);
            int installed = host.Mods.Count;
            string count = searching || all.Count != installed
                ? all.Count + " of " + installed + " mods"
                : installed + (installed == 1 ? " mod installed" : " mods installed");
            c.Text(count, ColR, titleY + 10f, 1f, 0.5f, SMeta, t.Muted);

            // Search box and settings button.
            const float boxY = 170f, boxH = 66f;
            const string gearText = "ZKMM Settings";
            float gearW = 26f + 34f + 14f + c.TextWidth(gearText, SValue) + 28f;
            float gearX = ColR - gearW;
            float searchW = gearX - 16f - ColX;

            bool searchSel = listPos == 0 && topCol == 0;
            DrawBox(c, t, ColX, boxY, searchW, boxH, searchSel, typing);
            ZkShapes.Icon(c, ZkIcon.Search, ColX + 38f, boxY + boxH / 2f, 30f, typing ? t.Accent : t.Muted);
            float textX = ColX + 68f;
            float cy = boxY + boxH / 2f;
            if (query.Length > 0) {
                string shown = ZkShapes.Ellipsize(c, query, SBody, searchW - 110f);
                c.Text(shown, textX, cy, 0f, 0.5f, SBody, t.Fg);
                textX += c.TextWidth(shown, SBody);
            } else if (!typing) {
                c.Text("Search " + installed + (installed == 1 ? " mod" : " mods"), textX, cy, 0f, 0.5f, SBody, t.Muted);
            }
            if (typing && time % 1f < 0.55f)
                c.Rect(textX + 4f, cy - 17f, 3f, 34f, t.Accent);

            bool gearSel = listPos == 0 && topCol == 1;
            DrawBox(c, t, gearX, boxY, gearW, boxH, gearSel, false);
            ZkShapes.Icon(c, ZkIcon.Gear, gearX + 26f + 17f, cy, 34f, gearSel ? t.Accent : t.Fg);
            c.Text(gearText, gearX + 26f + 34f + 14f, cy, 0f, 0.5f, SValue, t.Fg);
        }

        private static void DrawBox(IZkCanvas c, ZkTheme t, float x, float y, float w, float h, bool selected, bool active) {
            if (selected && !active)
                ZkShapes.RoundRectOutline(c, x - 5f, y - 5f, w + 10f, h + 10f, 21f, 5f, t.Accent.Fade(0.3f));
            ZkShapes.RoundRect(c, x, y, w, h, 16f, t.Surface);
            ZkShapes.RoundRectOutline(c, x, y, w, h, 16f, 3f, selected || active ? t.Accent : t.Line);
        }

        private void DrawSection(IZkCanvas c, ZkTheme t, float x, float y, float w, float h, string text, int count, bool star) {
            float cy = y + h - 20f;
            float tx = x + 8f;
            if (star) {
                ZkShapes.Icon(c, ZkIcon.Star, tx + 10f, cy - 1f, 22f, t.Star);
                tx += 30f;
            }
            c.Text(text, tx, cy, 0f, 0.5f, SHead, t.Muted);
            tx += c.TextWidth(text, SHead) + 14f;
            ZkShapes.Circle(c, tx, cy, 3f, t.Muted);
            tx += 14f;
            string n = count.ToString();
            c.Text(n, tx, cy, 0f, 0.5f, SHead, t.Muted);
            tx += c.TextWidth(n, SHead) + 16f;
            if (x + w - tx > 4f)
                c.Rect(tx, (float) Math.Round(cy) - 1f, x + w - tx, 2f, t.Line);
        }

        private void DrawModRow(IZkCanvas c, ZkTheme t, IZkMod m, float x, float y, float w, float h, bool sel, bool compact, bool showMeta) {
            float sx = x + (sel ? 10f : 0f);
            float rw = w - 10f;
            float cy = y + h / 2f;

            if (sel)
                ZkShapes.RoundRect(c, sx, y, rw, h, 16f, t.Accent, t.AccentEnd);
            else
                ZkShapes.RoundRect(c, sx, y, rw, h, 16f, t.Surface);

            ZkColor fg = sel ? t.AccentFg : t.Fg;
            ZkColor muted = sel ? t.AccentFg.Fade(0.85f) : t.Muted;

            float badge = compact ? 38f : 56f;
            float bx = sx + 18f;
            ZkImage thumb = m.Thumb;
            if (thumb != null)
                c.Image(thumb, 0f, 0f, 1f, 1f, bx, cy - badge / 2f, badge, badge);
            else
                DrawBadge(c, m.Name, bx, cy - badge / 2f, badge, compact ? 10f : 15f, compact ? 0.3f : 0.42f);
            float tx = bx + badge + 20f;

            float rx = sx + rw - 24f;
            if (host.Options.IsFavourite(m.Id)) {
                ZkShapes.Icon(c, ZkIcon.Star, rx - 13f, cy - 1f, 28f, sel ? t.AccentFg : t.Star);
                rx -= 40f;
            }
            int n = m.SettingCount;
            string right = n < 0 ? "" : n == 0 ? "No settings" : n == 1 ? "1 setting" : n + " settings";
            c.Text(right, rx, cy, 1f, 0.5f, SMeta, muted);
            float maxText = rx - c.TextWidth(right, SMeta) - 28f - tx;

            string name = ZkShapes.Ellipsize(c, m.Name, SBody, maxText);
            if (showMeta && !compact) {
                c.Text(name, tx, cy - 14f, 0f, 0.5f, SBody, fg);
                float mx = tx;
                if (!string.IsNullOrEmpty(m.Author)) {
                    string author = ZkShapes.Ellipsize(c, m.Author, SMeta, maxText - 140f);
                    c.Text(author, mx, cy + 20f, 0f, 0.5f, SMeta, muted);
                    mx += c.TextWidth(author, SMeta) + 12f;
                    ZkShapes.Circle(c, mx, cy + 20f, 3f, muted);
                    mx += 12f;
                }
                c.Text("v" + m.Version, mx, cy + 20f, 0f, 0.5f, SMeta, muted);
            } else {
                c.Text(name, tx, cy, 0f, 0.5f, SBody, fg);
            }
        }

        private static void DrawBadge(IZkCanvas c, string name, float x, float y, float size, float radius, float scale) {
            int hue = 0;
            foreach (char ch in name)
                hue = (hue * 31 + ch) % 360;
            ZkShapes.RoundRect(c, x, y, size, size, radius, ZkColor.Hsl(hue, 0.58f, 0.46f));
            c.Text(Initials(name), x + size / 2f, y + size / 2f, 0.5f, 0.5f, scale, ZkColor.White);
        }

        private static string Initials(string name) {
            string result = "";
            bool wordStart = true;
            foreach (char ch in name) {
                if (char.IsWhiteSpace(ch) || ch == '-' || ch == '_') {
                    wordStart = true;
                    continue;
                }
                if (wordStart && char.IsLetterOrDigit(ch)) {
                    result += char.ToUpperInvariant(ch);
                    if (result.Length == 2)
                        break;
                }
                wordStart = false;
            }
            return result.Length > 0 ? result : "?";
        }

        // ------------------------------------------------------------------ mod page and settings page

        private void RenderPage(IZkCanvas c, ZkTheme t, float slide) {
            if (pageRows.Count == 0)
                RefreshRows();

            bool isMod = screen == Screen.Mod && mod != null;
            // A screen the mod opened from its page: same card, with the screen's own title.
            string subPage = isMod ? mod.SubPage : null;
            string name = isMod ? (subPage ?? mod.Name) : "ZKMM Settings";
            string description;
            if (subPage != null)
                description = "More settings of " + mod.Name + ". " + host.Glyph(ZkAction.Back) + " goes back.";
            else if (isMod)
                description = string.IsNullOrWhiteSpace(mod.Description) ? "No description found for this mod." : mod.Description;
            else
                description = "Change how ZK Mod Menu looks and behaves. The theme changes as soon as you pick it.";

            // Header card metrics. A mod with a picture shows it in 16:9 where the letter badge would be.
            const float heroY = 76f, badge = 104f;
            ZkImage banner = isMod && subPage == null ? mod.Banner : null;
            float badgeW = banner != null ? (float) Math.Round(badge * 16f / 9f) : badge;
            float heroX = ColX + slide;
            float tx = heroX + 30f + badgeW + 28f;
            float tw = heroX + ColW - 30f - tx;
            List<string> lines = ZkShapes.Wrap(c, description, SValue, tw, 3);
            float nameH = c.LineHeight(SName);
            float lineH = c.LineHeight(SValue) * 1.1f;
            float textH = nameH + 6f + 38f + 12f + lines.Count * lineH;
            float heroH = Math.Max(badge, textH) + 52f;

            // Snapped to the background image's pixel grid so the scroll mask lines up exactly.
            float viewTop = (float) Math.Ceiling((heroY + heroH + 18f) / 4f) * 4f;
            float viewH = ViewBottom - viewTop;

            // Lay the rows out.
            placed.Clear();
            float y = 0f;
            float selY = 0f, selH = 0f;
            for (int i = 0; i < pageRows.Count; i++) {
                ZkRow row = pageRows[i];
                float rowX = ColX + row.Indent * 36f;
                float rowW = ColW - row.Indent * 36f;
                Placed p = new Placed { Row = row, Y = y };
                switch (row.Kind) {
                    case ZkRowKind.Header:
                        p.H = i == 0 ? 40f : 56f;
                        break;
                    case ZkRowKind.Note:
                        p.H = 110f;
                        break;
                    default:
                        p.H = 66f;
                        if (row == selected && !string.IsNullOrWhiteSpace(row.Description)) {
                            p.Help = ZkShapes.Wrap(c, row.Description, SMeta, rowW - 48f, 3);
                            p.H += p.Help.Count * c.LineHeight(SMeta) * 1.1f + 8f;
                        }
                        break;
                }
                if (row == selected) {
                    selY = y;
                    selH = p.H;
                }
                placed.Add(p);
                y += p.H + 8f;
            }
            float total = y;

            float target = 0f;
            if (selected != null && selIndex > 0)
                target = ScrollFor(pageScrollTarget, selY, selH, viewH, total, 70f);
            pageScrollTarget = target;

            // Rows.
            float top = viewTop - (float) Math.Round(pageScroll);
            foreach (Placed p in placed) {
                float py = top + p.Y;
                if (py + p.H < viewTop - 2f || py > ViewBottom + 2f)
                    continue;
                float rowX = ColX + slide + p.Row.Indent * 36f;
                float rowW = ColW - p.Row.Indent * 36f;
                DrawRow(c, t, p, rowX, py, rowW);
            }

            Mask(c, t, viewTop, ViewBottom);

            // Header card.
            if (subPage != null)
                DrawCrumbs(c, t, "PAUSE", "ZK MOD MENU", mod.Name.ToUpperInvariant(), subPage.ToUpperInvariant());
            else
                DrawCrumbs(c, t, "PAUSE", "ZK MOD MENU", isMod ? name.ToUpperInvariant() : "SETTINGS");
            ZkShapes.RoundRect(c, heroX, heroY, ColW, heroH, 22f, t.Surface);
            ZkShapes.RoundRectOutline(c, heroX, heroY, ColW, heroH, 22f, 2f, t.Line);

            float by = heroY + 26f;
            if (banner != null) {
                c.Image(banner, 0f, 0f, 1f, 1f, heroX + 30f, by, badgeW, badge);
            } else if (isMod) {
                DrawBadge(c, mod.Name, heroX + 30f, by, badge, 24f, 0.72f);
            } else {
                ZkShapes.RoundRect(c, heroX + 30f, by, badge, badge, 24f, t.Accent);
                c.Text("ZK", heroX + 30f + badge / 2f, by + badge / 2f, 0.5f, 0.5f, 0.72f, t.AccentFg);
            }

            float ty = heroY + 26f + Math.Max(0f, (badge - textH) / 2f);
            c.Text(ZkShapes.Ellipsize(c, name, SName, tw), tx, ty + nameH / 2f, 0f, 0.5f, SName, t.Fg);
            ty += nameH + 6f;

            float chipX = tx;
            string version = "v" + (isMod ? mod.Version : host.Version);
            float chipW = c.TextWidth(version, SHead) + 24f;
            ZkShapes.RoundRect(c, chipX, ty, chipW, 38f, 9f, t.Surface2);
            c.Text(version, chipX + chipW / 2f, ty + 19f, 0.5f, 0.5f, SHead, t.Fg);
            chipX += chipW + 14f;
            string author = isMod ? mod.Author : "ZEEK";
            c.Text(string.IsNullOrEmpty(author) ? "Author unknown" : "by " + ZkShapes.Ellipsize(c, author, SMeta, tw - chipW - 80f),
                chipX, ty + 19f, 0f, 0.5f, SMeta, t.Muted);
            ty += 38f + 12f;

            foreach (string line in lines) {
                c.Text(line, tx, ty + lineH / 2f, 0f, 0.5f, SValue, t.Fg);
                ty += lineH;
            }
        }

        private void DrawRow(IZkCanvas c, ZkTheme t, Placed p, float x, float y, float w) {
            ZkRow row = p.Row;
            bool sel = row == selected;
            const float baseH = 66f;
            float cy = y + baseH / 2f;

            switch (row.Kind) {
                case ZkRowKind.Header: {
                    string title = row.Label.ToUpperInvariant();
                    float hy = y + p.H - 18f;
                    c.Text(ZkShapes.Ellipsize(c, title, SHead, w - 16f), x + 8f, hy, 0f, 0.5f, SHead, t.Muted);
                    return;
                }

                case ZkRowKind.Note:
                    ZkShapes.RoundRect(c, x, y, w, p.H, 14f, t.Surface);
                    c.Text(ZkShapes.Ellipsize(c, row.Label, SBody, w - 60f), x + w / 2f, y + p.H / 2f, 0.5f, 0.5f, SBody, t.Muted);
                    return;

                case ZkRowKind.Favourite: {
                    bool on = row.On;
                    ZkShapes.RoundRect(c, x, y, w, p.H, 14f, sel ? t.Accent : t.Surface, sel ? t.AccentEnd : t.Surface);
                    ZkShapes.Icon(c, on ? ZkIcon.Star : ZkIcon.StarOutline, x + 40f, cy - 1f, 32f, sel ? t.AccentFg : t.Star);
                    string favLabel = on ? "Favourited (press " + host.Glyph(ZkAction.Confirm) + " to remove)" : "Add to favourites";
                    c.Text(favLabel, x + 70f, cy, 0f, 0.5f, SBody, sel ? t.AccentFg : t.Fg);
                    return;
                }

                case ZkRowKind.ResetAll: {
                    ZkShapes.RoundRect(c, x, y, w, p.H, 14f, sel ? t.Surface2 : t.Surface);
                    if (sel)
                        ZkShapes.RoundRectOutline(c, x, y, w, p.H, 14f, 3f, t.Accent);
                    bool isArmed = armed == row;
                    string resetLabel = isArmed ? "Press " + host.Glyph(ZkAction.Confirm) + " again to reset every setting on this page" : row.Label;
                    c.Text(resetLabel, x + w / 2f, cy, 0.5f, 0.5f, SValue, isArmed ? t.Accent : sel ? t.Fg : t.Muted);
                    return;
                }
            }

            // A regular setting row.
            bool disabled = row.Disabled;
            ZkShapes.RoundRect(c, x, y, w, p.H, 14f, sel ? t.Surface2 : t.Surface);
            if (sel)
                ZkShapes.RoundRectOutline(c, x, y, w, p.H, 14f, 3f, t.Accent);

            ZkColor fg = disabled ? t.Muted.Fade(0.6f) : t.Fg;
            float rx = x + w - 24f;
            float controlW = DrawControl(c, t, row, rx, cy, sel, disabled);

            bool isArmedRow = armed == row;
            string text = isArmedRow ? "Press " + host.Glyph(ZkAction.Confirm) + " again to confirm" : row.Label;
            float maxLabel = w - 48f - controlW - 40f;
            text = ZkShapes.Ellipsize(c, text, SBody, maxLabel);
            c.Text(text, x + 24f, cy, 0f, 0.5f, SBody, isArmedRow ? t.Accent : fg);
            if (row.HasDefault && row.Changed && !isArmedRow)
                ZkShapes.Circle(c, x + 24f + c.TextWidth(text, SBody) + 16f, cy, 5f, t.Accent);

            if (p.Help != null) {
                float lineH = c.LineHeight(SMeta) * 1.1f;
                float hy = y + baseH - 6f;
                foreach (string line in p.Help) {
                    c.Text(line, x + 24f, hy + lineH / 2f, 0f, 0.5f, SMeta, t.Muted);
                    hy += lineH;
                }
            }
        }

        /// <summary>Draw the value of a row, right-aligned at rx. Returns the width used.</summary>
        private float DrawControl(IZkCanvas c, ZkTheme t, ZkRow row, float rx, float cy, bool sel, bool disabled) {
            float dim = disabled ? 0.5f : 1f;

            switch (row.Kind) {
                case ZkRowKind.Toggle: {
                    const float w = 92f, h = 40f;
                    bool on = row.On;
                    ZkShapes.RoundRect(c, rx - w, cy - h / 2f, w, h, h / 2f, (on ? t.On : t.Off).Fade(dim));
                    c.Text(on ? "ON" : "OFF", rx - w / 2f, cy, 0.5f, 0.5f, SMeta, (on ? t.OnFg : ZkColor.White).Fade(dim));
                    return w;
                }

                case ZkRowKind.Slider: {
                    const float valueW = 120f, trackW = 260f, trackH = 14f;
                    c.Text(row.ValueText, rx, cy, 1f, 0.5f, SValue, t.Fg.Fade(dim));
                    float trackX = rx - valueW - 16f - trackW;
                    ZkShapes.RoundRect(c, trackX, cy - trackH / 2f, trackW, trackH, trackH / 2f, sel ? t.Line : t.Surface2);
                    ZkShapes.RoundRectOutline(c, trackX, cy - trackH / 2f, trackW, trackH, trackH / 2f, 1f, t.Line);
                    float fraction = Math.Max(0f, Math.Min(1f, row.Fraction));
                    float fill = (float) Math.Round(trackW * fraction);
                    if (fill >= 2f)
                        ZkShapes.RoundRect(c, trackX, cy - trackH / 2f, Math.Max(trackH, fill), trackH, trackH / 2f, t.Accent.Fade(dim));
                    return valueW + 16f + trackW;
                }

                case ZkRowKind.Choice: {
                    string value = ZkShapes.Ellipsize(c, row.ValueText, SValue, 520f);
                    float valueW = Math.Max(190f, c.TextWidth(value, SValue) + 28f);
                    ZkColor arrow = sel ? t.Accent : t.Muted;
                    ZkShapes.Icon(c, ZkIcon.ChevronRight, rx - 12f, cy, 24f, arrow.Fade(row.CanRight ? dim : 0.25f));
                    c.Text(value, rx - 28f - valueW / 2f, cy, 0.5f, 0.5f, SValue, t.Fg.Fade(dim));
                    ZkShapes.Icon(c, ZkIcon.ChevronLeft, rx - 28f - valueW - 16f, cy, 24f, arrow.Fade(row.CanLeft ? dim : 0.25f));
                    return valueW + 60f;
                }

                case ZkRowKind.Keybind: {
                    if (listening == row) {
                        if (time % 1f < 0.6f)
                            return DrawCap(c, t, "Press a key or button", rx, cy, 42f, SMeta, t.Accent, true);
                        return CapWidth(c, "Press a key or button", SMeta);
                    }
                    IReadOnlyList<string> caps = row.Caps;
                    if (caps.Count == 0) {
                        c.Text("Not bound", rx, cy, 1f, 0.5f, SMeta, t.Muted);
                        return c.TextWidth("Not bound", SMeta);
                    }
                    float used = 0f;
                    int shown = Math.Min(4, caps.Count);
                    for (int i = shown - 1; i >= 0; i--) {
                        float cw = DrawCap(c, t, caps[i], rx - used, cy, 42f, SMeta, t.Fg.Fade(dim), false);
                        used += cw + 8f;
                    }
                    return used;
                }

                case ZkRowKind.Text: {
                    bool editing = row == editRow;
                    string value = editing ? draft : (row as IZkTextEdit)?.Text ?? row.ValueText ?? "";
                    bool isColour = ZkColor.TryParseHex(value, out ZkColor colour);
                    const float boxH = 46f;
                    float swatch = isColour ? boxH - 14f : 0f;
                    string shown = value.Length == 0 && !editing ? "Empty" : ZkShapes.Ellipsize(c, value, SValue, 480f);
                    float textW = c.TextWidth(shown, SValue);
                    float boxW = Math.Max(editing ? 300f : 200f, 32f + (isColour ? swatch + 14f : 0f) + textW + (editing ? 10f : 0f));
                    float bx = rx - boxW;
                    ZkShapes.RoundRect(c, bx, cy - boxH / 2f, boxW, boxH, 11f, editing ? t.Page : t.Surface2.Fade(dim));
                    ZkShapes.RoundRectOutline(c, bx, cy - boxH / 2f, boxW, boxH, 11f, editing ? 3f : 1f, editing ? t.Accent : t.Line);
                    float textX = bx + 16f;
                    if (isColour) {
                        float sy = cy - swatch / 2f;
                        if (colour.A < 255) {
                            // A see-through colour: show it over a checkerboard.
                            ZkShapes.RoundRect(c, textX, sy, swatch, swatch, 7f, ZkColor.White);
                            c.Rect(textX + swatch / 2f, sy, swatch / 2f - 3f, swatch / 2f, new ZkColor(190, 190, 190));
                            c.Rect(textX + 3f, sy + swatch / 2f, swatch / 2f - 3f, swatch / 2f - 3f, new ZkColor(190, 190, 190));
                        }
                        ZkShapes.RoundRect(c, textX, sy, swatch, swatch, 7f, colour);
                        ZkShapes.RoundRectOutline(c, textX, sy, swatch, swatch, 7f, 2f, t.Line);
                        textX += swatch + 14f;
                    }
                    c.Text(shown, textX, cy, 0f, 0.5f, SValue, (value.Length == 0 && !editing ? t.Muted : t.Fg).Fade(dim));
                    if (editing && time % 1f < 0.55f)
                        c.Rect(textX + textW + 4f, cy - 15f, 3f, 30f, t.Accent);
                    return boxW;
                }

                case ZkRowKind.Button: {
                    string value = row.ValueText;
                    string hint = disabled ? "Not available here" : !string.IsNullOrEmpty(value) ? value : "Press " + host.Glyph(ZkAction.Confirm);
                    c.Text(hint, rx, cy, 1f, 0.5f, SMeta, t.Muted.Fade(disabled ? 0.6f : 1f));
                    return c.TextWidth(hint, SMeta);
                }
            }
            return 0f;
        }

        // ------------------------------------------------------------------ key caps, crumbs, hints, toast

        private static bool IsArrow(char ch, out ZkIcon icon) {
            switch (ch) {
                case '↑':
                    icon = ZkIcon.ArrowUp;
                    return true;
                case '↓':
                    icon = ZkIcon.ArrowDown;
                    return true;
                case '←':
                    icon = ZkIcon.ArrowLeft;
                    return true;
                case '→':
                    icon = ZkIcon.ArrowRight;
                    return true;
            }
            icon = ZkIcon.ArrowUp;
            return false;
        }

        // Cap text may hold arrow characters, which the game's font lacks; those are drawn as icons.
        private static float CapContentWidth(IZkCanvas c, string text, float scale) {
            float arrow = c.LineHeight(scale) * 0.62f;
            float w = 0f;
            int start = 0;
            for (int i = 0; i <= text.Length; i++) {
                bool end = i == text.Length;
                if (end || IsArrow(text[i], out _)) {
                    if (i > start)
                        w += c.TextWidth(text.Substring(start, i - start), scale);
                    if (!end)
                        w += arrow + (i + 1 < text.Length ? 4f : 0f);
                    start = i + 1;
                }
            }
            return w;
        }

        private static void DrawCapContent(IZkCanvas c, string text, float x, float cy, float scale, ZkColor color) {
            float arrow = c.LineHeight(scale) * 0.62f;
            int start = 0;
            for (int i = 0; i <= text.Length; i++) {
                bool end = i == text.Length;
                ZkIcon icon = ZkIcon.ArrowUp;
                if (end || IsArrow(text[i], out icon)) {
                    if (i > start) {
                        string part = text.Substring(start, i - start);
                        c.Text(part, x, cy, 0f, 0.5f, scale, color);
                        x += c.TextWidth(part, scale);
                    }
                    if (!end) {
                        ZkShapes.Icon(c, icon, x + arrow / 2f, cy, arrow, color);
                        x += arrow + 4f;
                    }
                    start = i + 1;
                }
            }
        }

        private static float CapWidth(IZkCanvas c, string text, float scale) {
            return Math.Max(44f, CapContentWidth(c, text, scale) + 28f);
        }

        /// <summary>Draw a key cap whose right edge is at rx. Returns its width.</summary>
        private static float DrawCap(IZkCanvas c, ZkTheme t, string text, float rx, float cy, float h, float scale, ZkColor color, bool accent) {
            float content = CapContentWidth(c, text, scale);
            float w = Math.Max(h + 2f, content + 28f);
            float x = rx - w;
            float y = cy - h / 2f;
            ZkShapes.RoundRect(c, x, y, w, h, 9f, accent ? t.Accent : t.Line);
            ZkShapes.RoundRect(c, x + 2f, y + 2f, w - 4f, h - 7f, 7f, ZkColor.Over(t.Page, t.Surface2));
            DrawCapContent(c, text, x + (w - content) / 2f, cy - 2f, scale, color);
            return w;
        }

        private void DrawCrumbs(IZkCanvas c, ZkTheme t, params string[] crumbs) {
            const float y = 42f;
            float x = ColX;
            List<string> parts = new List<string>();
            foreach (string crumb in crumbs) {
                if (crumb != null)
                    parts.Add(crumb);
            }
            for (int i = 0; i < parts.Count; i++) {
                bool final = i == parts.Count - 1;
                // The fixed parts are short. The names after them are cut to fit on one line.
                float room = parts.Count > 3 ? (final ? 560f : 460f) : 900f;
                string text = i < 2 ? parts[i] : ZkShapes.Ellipsize(c, parts[i], SHead, room);
                c.Text(text, x, y, 0f, 0.5f, SHead, final ? t.Fg : t.Muted);
                x += c.TextWidth(text, SHead) + 14f;
                if (!final) {
                    c.Text("/", x, y, 0f, 0.5f, SHead, t.Muted.Fade(0.5f));
                    x += c.TextWidth("/", SHead) + 14f;
                }
            }
        }

        private readonly List<KeyValuePair<string, string>> hints = new List<KeyValuePair<string, string>>();

        private void Hint(string key, string label) {
            hints.Add(new KeyValuePair<string, string>(key, label));
        }

        private void BuildHints() {
            hints.Clear();
            string confirm = host.Glyph(ZkAction.Confirm);
            string alt = host.Glyph(ZkAction.Alt);
            string back = host.Glyph(ZkAction.Back);
            const string upDown = "↑↓";
            const string leftRight = "←→";

            if (listening != null) {
                Hint("Any key", "Bind");
                Hint("Del", "Clear");
                Hint("Bksp", "Cancel");
                return;
            }
            if (typing && editRow != null) {
                Hint("Type", "Edit");
                Hint("Enter", "Save");
                Hint("Bksp", "Delete");
                Hint("Esc", "Cancel");
                return;
            }
            if (typing) {
                Hint("Type", "Search");
                Hint("Enter", "Done");
                Hint("Bksp", "Delete");
                return;
            }

            if (screen == Screen.List) {
                if (listPos == 0) {
                    Hint(leftRight, "Search / Settings");
                    Hint(confirm, topCol == 1 ? "Open settings" : "Type to search");
                    if (query.Length > 0)
                        Hint(alt, "Clear search");
                } else {
                    Hint(upDown, "Move");
                    Hint(confirm, "Open mod");
                    bool fav = listPos <= all.Count && host.Options.IsFavourite(all[listPos - 1].Id);
                    Hint(alt, fav ? "Unfavourite" : "Favourite");
                }
                Hint(back, "Back");
                return;
            }

            Hint(upDown, "Move");
            ZkRow row = selected;
            if (row != null) {
                switch (row.Kind) {
                    case ZkRowKind.Favourite:
                        Hint(confirm, row.On ? "Unfavourite" : "Favourite");
                        break;
                    case ZkRowKind.ResetAll:
                        Hint(confirm, "Reset all");
                        break;
                    case ZkRowKind.Toggle:
                        Hint(confirm, "Toggle");
                        break;
                    case ZkRowKind.Slider:
                        Hint(leftRight, "Adjust");
                        break;
                    case ZkRowKind.Choice:
                        Hint(leftRight, "Change");
                        break;
                    case ZkRowKind.Keybind:
                        Hint(confirm, "Rebind");
                        break;
                    case ZkRowKind.Button:
                        Hint(confirm, "Press");
                        break;
                    case ZkRowKind.Text:
                        Hint(confirm, "Type a new value");
                        break;
                }
                if (row.HasDefault && row.Kind != ZkRowKind.Button)
                    Hint(alt, "Reset to default");
            }
            Hint(back, "Back");
        }

        private void RenderHints(IZkCanvas c, ZkTheme t) {
            BuildHints();
            const float y = 1004f, h = 50f, capH = 34f;
            float x = ColR;
            for (int i = hints.Count - 1; i >= 0; i--) {
                string key = hints[i].Key;
                string label = hints[i].Value;
                float capW = Math.Max(capH + 2f, CapContentWidth(c, key, SHead) + 28f);
                float labelW = c.TextWidth(label, SMeta);
                float w = 10f + capW + 12f + labelW + 18f;
                x -= w;
                ZkShapes.RoundRect(c, x, y, w, h, 13f, t.Surface);
                ZkShapes.RoundRectOutline(c, x, y, w, h, 13f, 2f, t.Line);
                DrawCap(c, t, key, x + 10f + capW, y + h / 2f, capH, SHead, t.Fg, false);
                c.Text(label, x + 10f + capW + 12f, y + h / 2f, 0f, 0.5f, SMeta, t.Muted);
                x -= 10f;
            }
        }

        private void RenderToast(IZkCanvas c, ZkTheme t) {
            if (toastTime <= 0f || string.IsNullOrEmpty(toast))
                return;
            float alpha = Math.Min(1f, toastTime / 0.25f);
            float w = c.TextWidth(toast, SValue) + 60f;
            const float h = 60f;
            float x = (ScreenW - w) / 2f;
            float y = 912f + (1f - alpha) * 14f;
            ZkShapes.RoundRect(c, x, y, w, h, 14f, t.Fg.Fade(alpha));
            c.Text(toast, ScreenW / 2f, y + h / 2f, 0.5f, 0.5f, SValue, t.Page.Fade(alpha));
        }
    }
}
