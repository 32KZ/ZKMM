using System;
using System.Collections.Generic;

namespace ZKModMenu.Core {
    /// <summary>
    /// The whole ZK Mod Menu: the mod list, the mod pages and ZKMM's own settings page.
    /// It knows nothing about Celeste. The game side feeds it actions and a canvas.
    /// </summary>
    public sealed partial class ZkMenu {

        private enum Screen {
            List,
            Mod,
            Settings
        }

        private readonly IZkHost host;
        private Screen screen = Screen.List;
        private float time;

        // ---- mod list
        private int listPos;   // 0 = top row (search and settings button), 1.. = mods
        private int topCol;    // 0 = search, 1 = settings button
        private string query = "";
        private bool typing;
        private bool listDirty = true;
        private readonly List<IZkMod> favs = new List<IZkMod>();
        private readonly List<IZkMod> rest = new List<IZkMod>();
        private readonly List<IZkMod> all = new List<IZkMod>();
        private float listScroll, listScrollTarget;

        // ---- mod page / settings page
        private IZkMod mod;
        private readonly List<ZkRow> pageRows = new List<ZkRow>();
        private readonly List<ZkRow> navRows = new List<ZkRow>();
        private ZkRow selected;
        private int selIndex;
        private readonly ZkRow favRow;
        private readonly ZkRow resetAllRow;
        private readonly ZkRow noSettingsRow = new ZkTextRow(ZkRowKind.Note, "This mod has no settings.");
        private readonly ZkRow emptyScreenRow = new ZkTextRow(ZkRowKind.Note, "There is nothing to change on this screen.");
        // The row list on show, and where the selection was on the lists shown before it.
        private IReadOnlyList<ZkRow> shownRows;
        private readonly Dictionary<IReadOnlyList<ZkRow>, ZkRow> remembered = new Dictionary<IReadOnlyList<ZkRow>, ZkRow>();
        private List<ZkRow> settingsRows;
        private float pageScroll, pageScrollTarget;
        private ZkKeybindRow listening;

        // ---- feedback
        private float enter = 1f;
        private string toast;
        private float toastTime;
        private ZkRow armed;
        private float armedTime;

        public ZkMenu(IZkHost host) {
            this.host = host;
            favRow = new FavouriteRow(this);
            resetAllRow = new ResetAllRow(this);
        }

        /// <summary>True while the search box is taking text.</summary>
        public bool Typing => typing;

        /// <summary>The keybind row waiting for a key press, if any.</summary>
        public ZkKeybindRow Listening => listening;

        /// <summary>True while the open mod is showing a screen of its own. The menu hides and waits.</summary>
        public bool Suspended => screen == Screen.Mod && mod != null && mod.Suspended;

        private ZkTheme Theme => ZkTheme.Get(host.Options.Theme);

        // ------------------------------------------------------------------ list

        private static bool Contains(string text, string lowerQuery) {
            return text != null && text.ToLowerInvariant().Contains(lowerQuery);
        }

        private void RebuildList() {
            favs.Clear();
            rest.Clear();
            all.Clear();

            string q = query.Trim().ToLowerInvariant();
            IZkOptions options = host.Options;
            List<IZkMod> match = new List<IZkMod>();
            foreach (IZkMod m in host.Mods) {
                if (q.Length > 0) {
                    if (!Contains(m.Name, q) && !Contains(m.Id, q) && !Contains(m.Author, q))
                        continue;
                } else if (!options.ShowEmptyMods && !options.IsFavourite(m.Id) && m.SettingCount == 0) {
                    continue;
                }
                match.Add(m);
            }
            match.Sort((a, b) => {
                int result = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                return result != 0 ? result : string.Compare(a.Id, b.Id, StringComparison.Ordinal);
            });

            foreach (IZkMod m in match)
                (options.IsFavourite(m.Id) ? favs : rest).Add(m);
            all.AddRange(favs);
            all.AddRange(rest);

            listDirty = false;
            if (listPos > all.Count)
                listPos = all.Count;
        }

        private void EnsureList() {
            if (listDirty)
                RebuildList();
        }

        private void ToggleFavourite(IZkMod m) {
            IZkOptions options = host.Options;
            bool now = !options.IsFavourite(m.Id);
            options.SetFavourite(m.Id, now);
            host.SaveOptions();
            host.Play(ZkSound.Favourite);
            Toast(now ? "Added to favourites, pinned to the top" : "Removed from favourites");
            listDirty = true;
        }

        private void StartTyping() {
            if (typing)
                return;
            typing = true;
            listPos = 0;
            topCol = 0;
            host.Play(ZkSound.ToggleOn);
            host.BeginTyping();
        }

        private void StopTyping(bool jumpToResults) {
            if (!typing)
                return;
            typing = false;
            host.Play(ZkSound.ToggleOff);
            host.EndTyping();
            EnsureList();
            if (jumpToResults && all.Count > 0)
                listPos = 1;
        }

        /// <summary>A printable character typed into the search box.</summary>
        public void TypeChar(char c) {
            if (!typing || char.IsControl(c))
                return;
            if (query.Length >= 30) {
                host.Play(ZkSound.Invalid);
                return;
            }
            query += c;
            listDirty = true;
            host.Play(ZkSound.Type);
        }

        /// <summary>A special key pressed while typing.</summary>
        public void TypingKey(ZkTypingKey key) {
            if (!typing)
                return;
            switch (key) {
                case ZkTypingKey.Enter:
                case ZkTypingKey.Down:
                    StopTyping(true);
                    break;
                case ZkTypingKey.Escape:
                    StopTyping(false);
                    break;
                case ZkTypingKey.Backspace:
                    if (query.Length > 0) {
                        query = query.Substring(0, query.Length - 1);
                        listDirty = true;
                        host.Play(ZkSound.Erase);
                    } else {
                        StopTyping(false);
                    }
                    break;
            }
        }

        private void ActList(ZkAction action) {
            EnsureList();
            int pageSize = host.Options.Compact ? 10 : 7;

            switch (action) {
                case ZkAction.Up:
                    MoveList(-1);
                    break;
                case ZkAction.Down:
                    MoveList(1);
                    break;
                case ZkAction.PageUp:
                    MoveList(listPos <= 1 ? -1 : -Math.Min(pageSize, listPos - 1));
                    break;
                case ZkAction.PageDown:
                    MoveList(pageSize);
                    break;

                case ZkAction.Left:
                case ZkAction.Right:
                    if (listPos == 0) {
                        int col = action == ZkAction.Right ? 1 : 0;
                        if (col != topCol) {
                            topCol = col;
                            host.Play(col == 1 ? ZkSound.MoveDown : ZkSound.MoveUp);
                        }
                    }
                    break;

                case ZkAction.Search:
                    StartTyping();
                    break;

                case ZkAction.Confirm:
                    if (listPos == 0) {
                        if (topCol == 0)
                            StartTyping();
                        else
                            OpenSettings();
                    } else if (listPos <= all.Count) {
                        OpenMod(all[listPos - 1]);
                    }
                    break;

                case ZkAction.Alt:
                    if (listPos == 0) {
                        if (query.Length > 0) {
                            query = "";
                            listDirty = true;
                            host.Play(ZkSound.Erase);
                            Toast("Search cleared");
                        }
                    } else if (listPos <= all.Count) {
                        IZkMod m = all[listPos - 1];
                        ToggleFavourite(m);
                        RebuildList();
                        int index = all.IndexOf(m);
                        listPos = index >= 0 ? index + 1 : Math.Min(listPos, all.Count);
                    }
                    break;

                case ZkAction.Back:
                    host.Play(ZkSound.Back);
                    host.RequestClose();
                    break;
            }
        }

        private void MoveList(int delta) {
            int target = Math.Max(0, Math.Min(all.Count, listPos + delta));
            if (target == listPos)
                return;
            host.Play(target > listPos ? ZkSound.MoveDown : ZkSound.MoveUp);
            listPos = target;
        }

        // ------------------------------------------------------------------ pages

        private void OpenMod(IZkMod m) {
            mod = m;
            screen = Screen.Mod;
            host.Play(ZkSound.Select);
            m.Open();
            EnterPage();
        }

        private void OpenSettings() {
            mod = null;
            screen = Screen.Settings;
            host.Play(ZkSound.Select);
            settingsRows = BuildSettingsRows();
            EnterPage();
        }

        private void EnterPage() {
            selected = null;
            selIndex = 0;
            shownRows = null;
            remembered.Clear();
            pageScroll = pageScrollTarget = 0f;
            armed = null;
            listening = null;
            enter = 0f;
            RefreshRows();
        }

        private void LeavePage() {
            host.Play(ZkSound.Back);
            selected?.Deselected();
            selected = null;
            listening = null;
            armed = null;
            shownRows = null;
            remembered.Clear();

            IZkMod left = mod;
            if (screen == Screen.Mod && left != null)
                left.Close();
            else
                host.SaveOptions();

            bool wasSettings = screen == Screen.Settings;
            mod = null;
            screen = Screen.List;
            enter = 0f;
            RebuildList();
            if (wasSettings) {
                listPos = 0;
                topCol = 1;
            } else {
                int index = all.IndexOf(left);
                listPos = index >= 0 ? index + 1 : 0;
                if (index < 0)
                    topCol = 0;
            }
            // Jump straight to where the selection is; the list did not move while the page was open.
            listScroll = float.NaN;
        }

        /// <summary>Work out which rows are visible and keep the selection valid.</summary>
        private void RefreshRows() {
            pageRows.Clear();
            navRows.Clear();

            if (screen == Screen.Mod && mod != null) {
                // A mod can swap its page for a screen of its own and back. Each has its own rows and selection.
                IReadOnlyList<ZkRow> rows = mod.Rows;
                bool subPage = mod.SubPage != null;
                if (!ReferenceEquals(rows, shownRows)) {
                    if (shownRows != null) {
                        if (selected != null)
                            remembered[shownRows] = selected;
                        Select(null);
                        enter = 0f;
                    }
                    shownRows = rows;
                    remembered.TryGetValue(rows, out selected);
                    selected?.Selected();
                    selIndex = 0;
                    armed = null;
                    listening = null;
                    pageScroll = pageScrollTarget = 0f;
                }

                if (!subPage)
                    pageRows.Add(favRow);
                bool any = false, resettable = false;
                foreach (ZkRow row in rows) {
                    if (!row.Visible)
                        continue;
                    pageRows.Add(row);
                    any |= row.Selectable;
                    resettable |= row.HasDefault;
                }
                if (!any)
                    pageRows.Add(subPage ? emptyScreenRow : noSettingsRow);
                if (resettable)
                    pageRows.Add(resetAllRow);
            } else if (screen == Screen.Settings && settingsRows != null) {
                foreach (ZkRow row in settingsRows) {
                    if (row.Visible)
                        pageRows.Add(row);
                }
            }

            foreach (ZkRow row in pageRows) {
                if (row.Selectable && !row.Disabled)
                    navRows.Add(row);
            }

            if (navRows.Count == 0) {
                Select(null);
                selIndex = 0;
                return;
            }
            int index = selected != null ? navRows.IndexOf(selected) : -1;
            if (index < 0)
                index = Math.Max(0, Math.Min(navRows.Count - 1, selIndex));
            selIndex = index;
            Select(navRows[index]);
        }

        private void Select(ZkRow row) {
            if (row == selected)
                return;
            selected?.Deselected();
            selected = row;
            armed = null;
            row?.Selected();
        }

        private void MovePage(int delta) {
            if (navRows.Count == 0)
                return;
            int target = Math.Max(0, Math.Min(navRows.Count - 1, selIndex + delta));
            if (target == selIndex)
                return;
            host.Play(target > selIndex ? ZkSound.MoveDown : ZkSound.MoveUp);
            selIndex = target;
            Select(navRows[target]);
        }

        private void ActPage(ZkAction action) {
            RefreshRows();

            switch (action) {
                case ZkAction.Up:
                    MovePage(-1);
                    return;
                case ZkAction.Down:
                    MovePage(1);
                    return;
                case ZkAction.PageUp:
                    MovePage(-6);
                    return;
                case ZkAction.PageDown:
                    MovePage(6);
                    return;
                case ZkAction.Back:
                    if (screen == Screen.Mod && mod != null && mod.SubPage != null) {
                        // The mod's own screen closes itself, with its own sound; the page comes back once it has.
                        mod.LeaveSubPage();
                    } else {
                        LeavePage();
                    }
                    return;
            }

            ZkRow row = selected;
            if (row == null)
                return;
            bool special = row.Kind == ZkRowKind.Favourite || row.Kind == ZkRowKind.ResetAll;

            switch (action) {
                case ZkAction.Alt:
                    if (special || row.Kind == ZkRowKind.Button)
                        break;
                    if (!row.HasDefault) {
                        host.Play(ZkSound.Invalid);
                        Toast("The default for this setting is not known");
                    } else if (row.Reset()) {
                        Toast(row.Label + " reset to default");
                    }
                    break;

                case ZkAction.Left:
                    if (!special && row.Kind != ZkRowKind.Keybind)
                        row.Left();
                    break;

                case ZkAction.Right:
                    if (!special && row.Kind != ZkRowKind.Keybind)
                        row.Right();
                    break;

                case ZkAction.Confirm:
                    if (row is ZkKeybindRow keybind) {
                        listening = keybind;
                        host.Play(ZkSound.Select);
                        host.BeginListening(keybind);
                    } else {
                        row.Confirm();
                    }
                    break;
            }

            // A row may have shown or hidden other rows.
            if (screen != Screen.List)
                RefreshRows();
        }

        /// <summary>
        /// Called by the host when a key capture ended, whether a key was bound or not.
        /// </summary>
        public void EndListening(string message) {
            listening = null;
            if (!string.IsNullOrEmpty(message))
                Toast(message);
        }

        /// <summary>
        /// Two-step confirmation for destructive buttons. Returns true on the second press.
        /// </summary>
        private bool ConfirmTwice(ZkRow row) {
            if (armed == row && armedTime > 0f) {
                armed = null;
                return true;
            }
            armed = row;
            armedTime = 4f;
            host.Play(ZkSound.Select);
            return false;
        }

        private List<ZkRow> BuildSettingsRows() {
            IZkOptions o = host.Options;
            List<ZkRow> rows = new List<ZkRow>();

            void Toggle(string label, Func<bool> get, Action<bool> set, bool def, string description = null, bool affectsPages = false) {
                rows.Add(new ZkOptionRow(label, null, () => get() ? 1 : 0, v => {
                    set(v != 0);
                    host.Play(v != 0 ? ZkSound.ToggleOn : ZkSound.ToggleOff);
                    host.SaveOptions();
                    listDirty = true;
                    if (affectsPages)
                        host.OptionsChanged();
                }, def ? 1 : 0, description));
            }

            void Choice(string label, string[] choices, Func<int> get, Action<int> set, int def) {
                rows.Add(new ZkOptionRow(label, choices, get, v => {
                    host.Play(v > get() ? ZkSound.ToggleOn : ZkSound.ToggleOff);
                    set(v);
                    host.SaveOptions();
                }, def));
            }

            rows.Add(new ZkTextRow(ZkRowKind.Header, "Look"));
            Choice("Theme", new[] { "Dark", "Light", "Colourful" }, () => (int) o.Theme, v => o.Theme = (ZkThemeKind) v, 0);
            Choice("List density", new[] { "Comfortable", "Compact" }, () => o.Compact ? 1 : 0, v => o.Compact = v != 0, 0);
            Toggle("Animations", () => o.Animations, v => o.Animations = v, true);

            rows.Add(new ZkTextRow(ZkRowKind.Header, "List"));
            Toggle("Show author and version in list", () => o.ShowMeta, v => o.ShowMeta = v, true);
            Toggle("Show mods without settings", () => o.ShowEmptyMods, v => o.ShowEmptyMods = v, true,
                "Turn off to hide mods that have nothing to set up. Favourites and search results always show.");

            rows.Add(new ZkTextRow(ZkRowKind.Header, "Mod pages"));
            Toggle("Detect default values", () => o.DetectDefaults, v => o.DetectDefaults = v, true,
                "Lets you reset a setting to its default and marks changed settings with a dot. Turn off if a mod's page misbehaves.", true);
            Toggle("Look up author and description online", () => o.OnlineInfo, v => o.OnlineInfo = v, true,
                "Everest does not store authors or descriptions, so ZKMM reads them from the public Celeste mod database.", true);
            Toggle("Restyle mods' own screens", () => o.RestyleModScreens, v => o.RestyleModScreens = v, true,
                "Menus a mod opens from its page are shown as ZKMM pages. Turn off to see them the way the mod made them.");

            rows.Add(new ZkTextRow(ZkRowKind.Header, "Pause menu"));
            Toggle("Hide Everest's Mod Options button", () => o.HideModOptionsButton, v => o.HideModOptionsButton = v, false,
                "Removes the old Mod Options entry from the pause menu. It stays available from the title screen.");

            IReadOnlyList<ZkRow> extra = host.ExtraSettingsRows;
            if (extra != null && extra.Count > 0) {
                rows.Add(new ZkTextRow(ZkRowKind.Header, "Controls"));
                rows.AddRange(extra);
            }

            rows.Add(new ZkTextRow(ZkRowKind.Header, "Reset"));
            ZkOptionRow clearFavourites = null;
            clearFavourites = new ZkOptionRow("Clear all favourites", () => {
                if (!ConfirmTwice(clearFavourites))
                    return;
                o.ClearFavourites();
                host.SaveOptions();
                listDirty = true;
                host.Play(ZkSound.ToggleOff);
                Toast("Favourites cleared");
            });
            rows.Add(clearFavourites);

            ZkOptionRow resetEverything = null;
            resetEverything = new ZkOptionRow("Reset all mod settings", () => {
                if (!ConfirmTwice(resetEverything))
                    return;
                int count = 0;
                foreach (IZkMod m in host.Mods)
                    count += m.ResetAllToDefault();
                host.Play(ZkSound.ToggleOff);
                Toast(count == 1 ? "1 setting reset to default" : count + " settings reset to default");
            }, "Puts every setting with a known default back to that default, for every mod.");
            rows.Add(resetEverything);

            return rows;
        }

        /// <summary>Reset every row with a known default. Returns how many rows changed.</summary>
        public static int ResetRows(IReadOnlyList<ZkRow> rows) {
            int count = 0;
            foreach (ZkRow row in rows) {
                if (row.HasDefault && !row.Disabled && row.Changed && row.Reset())
                    count++;
            }
            return count;
        }

        private sealed class FavouriteRow : ZkRow {
            private readonly ZkMenu menu;

            public FavouriteRow(ZkMenu menu) {
                this.menu = menu;
            }

            public override ZkRowKind Kind => ZkRowKind.Favourite;
            public override bool On => menu.mod != null && menu.host.Options.IsFavourite(menu.mod.Id);
            public override string Label => On ? "Favourited (press again to remove)" : "Add to favourites";

            public override void Confirm() {
                if (menu.mod != null)
                    menu.ToggleFavourite(menu.mod);
            }
        }

        private sealed class ResetAllRow : ZkRow {
            private readonly ZkMenu menu;

            public ResetAllRow(ZkMenu menu) {
                this.menu = menu;
            }

            public override ZkRowKind Kind => ZkRowKind.ResetAll;
            public override string Label => "Reset all to default";

            public override void Confirm() {
                if (menu.mod == null || !menu.ConfirmTwice(this))
                    return;
                int count = ResetRows(menu.mod.Rows);
                menu.host.Play(ZkSound.ToggleOff);
                menu.Toast(count == 0 ? "Everything is already at its default" : menu.mod.Name + " reset to defaults");
            }
        }

        // ------------------------------------------------------------------ shared

        /// <summary>Feed one button press into the menu.</summary>
        public void Act(ZkAction action) {
            if (typing || listening != null || Suspended)
                return;
            if (screen == Screen.List)
                ActList(action);
            else
                ActPage(action);
        }

        public void Toast(string message) {
            toast = message;
            toastTime = 2.2f;
        }

        /// <summary>Advance animations. Call once per frame.</summary>
        public void Update(float dt) {
            time += dt;
            bool animate = host.Options.Animations;

            enter = animate ? Math.Min(1f, enter + dt / 0.2f) : 1f;
            if (toastTime > 0f)
                toastTime -= dt;
            if (armedTime > 0f) {
                armedTime -= dt;
                if (armedTime <= 0f)
                    armed = null;
            }

            if (screen == Screen.List)
                EnsureList();
            else if (!Suspended)
                RefreshRows();

            listScroll = Approach(listScroll, listScrollTarget, dt, animate);
            pageScroll = Approach(pageScroll, pageScrollTarget, dt, animate);
        }

        private static float Approach(float value, float target, float dt, bool animate) {
            if (!animate || float.IsNaN(value) || Math.Abs(target - value) < 0.5f)
                return target;
            return value + (target - value) * (1f - (float) Math.Exp(-dt * 16f));
        }

        /// <summary>
        /// Call when the menu is going away, so an open mod page is closed and its settings are saved.
        /// </summary>
        public void Shutdown() {
            if (typing) {
                typing = false;
                host.EndTyping();
            }
            listening = null;
            if (screen == Screen.Mod && mod != null) {
                selected?.Deselected();
                selected = null;
                mod.Close();
            }
            shownRows = null;
            remembered.Clear();
            mod = null;
            screen = Screen.List;
            host.SaveOptions();
        }
    }
}
