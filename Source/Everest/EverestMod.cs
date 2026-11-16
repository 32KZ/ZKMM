using System;
using System.Collections.Generic;
using System.Reflection;
using Celeste;
using Celeste.Mod;
using Celeste.Mod.Helpers;
using FMOD.Studio;
using Monocle;
using ZKModMenu.Core;

namespace ZKModMenu {
    /// <summary>
    /// One installed mod.
    ///
    /// Its settings are the very same menu items the mod makes for Everest's Mod Options screen:
    /// ZKMM asks the mod to fill a hidden menu, then shows each item as a row of its own.
    /// That way every mod works without knowing about ZKMM, including mods with hand-made options.
    /// </summary>
    internal sealed class EverestMod : IZkMod, IZkScreenWorld {
        private static readonly List<EverestMod> Opened = new List<EverestMod>();

        /// <summary>A menu the mod opened from its page, shown as a ZKMM page of its own.</summary>
        private sealed class SubScreen {
            public TextMenu Menu;
            public string Title;
            // How the mod left its menu. Put back when ZKMM lets go of it: some mods keep a menu and show it again later.
            public bool WasVisible, WasActive;
            public readonly List<ZkRow> Rows = new List<ZkRow>();
            public readonly List<TextMenu.Item> BuiltFrom = new List<TextMenu.Item>();
        }

        private readonly List<EverestModule> modules = new List<EverestModule>();
        private readonly IZkOptions options;
        private readonly string id;
        private readonly string version;
        private string name;

        private readonly List<ZkRow> rows = new List<ZkRow>();
        private readonly List<HostMenu> hosts = new List<HostMenu>();
        private readonly HashSet<TextMenu> arrived = new HashSet<TextMenu>();
        private bool open;

        // Who is in charge while the page is open: the page, or a screen the mod opened from it.
        private readonly ZkScreens screens = new ZkScreens();
        private readonly List<SubScreen> subs = new List<SubScreen>();
        // What was on screen while the page was last in charge. Anything else is something the mod opened.
        private readonly HashSet<Entity> known = new HashSet<Entity>();
        private Level level;
        private float busyTime;
        private bool trackFailed;
        private int settingCount = -1;

        private int infoVersion = -1;
        private string author;
        private string description;

        private EverestMod(EverestModule first, IZkOptions options) {
            this.options = options;
            id = first.Metadata.Name;
            version = first.Metadata.VersionString ?? "";
        }

        // ------------------------------------------------------------------ finding the mods

        /// <summary>Every installed mod, whether it has settings or not.</summary>
        public static List<IZkMod> FindAll(IZkOptions options) {
            List<IZkMod> list = new List<IZkMod>();
            Dictionary<string, EverestMod> byName = new Dictionary<string, EverestMod>(StringComparer.OrdinalIgnoreCase);

            foreach (EverestModule module in Everest.Modules) {
                if (module == null || module is ZKModMenuModule)
                    continue;
                EverestModuleMetadata meta = module.Metadata;
                if (meta == null || string.IsNullOrEmpty(meta.Name))
                    continue;
                // Everest lists the game itself as a module. It is not a mod.
                if (meta.Name == "Celeste")
                    continue;

                // A mod can come as several modules; they share one page.
                if (!byName.TryGetValue(meta.Name, out EverestMod mod)) {
                    mod = new EverestMod(module, options);
                    byName[meta.Name] = mod;
                    list.Add(mod);
                }
                mod.modules.Add(module);
            }

            foreach (EverestMod mod in byName.Values)
                mod.name = mod.FindName();
            return list;
        }

        // The same name Everest's Mod Options shows.
        private string FindName() {
            foreach (EverestModule module in modules) {
                try {
                    Type type = module.SettingsType;
                    if (type == null)
                        continue;
                    string typeName = type.Name.ToLowerInvariant();
                    if (typeName.EndsWith("settings"))
                        typeName = typeName.Substring(0, typeName.Length - 8);
                    string key = type.GetCustomAttribute<SettingNameAttribute>()?.Name ?? "modoptions_" + typeName + "_title";
                    string title = key.DialogCleanOrNull();
                    if (!string.IsNullOrWhiteSpace(title))
                        return title;
                } catch {
                    // Fall back to the name from everest.yaml.
                }
            }
            try {
                return EverestName(id) ?? id;
            } catch {
                return id;
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static string EverestName(string id) {
            return ModUpdaterHelper.FormatModName(id);
        }

        /// <summary>Count this mod's settings now, so the list does not have to do it while drawing.</summary>
        public void Warm() {
            int ignored = SettingCount;
        }

        // ------------------------------------------------------------------ IZkMod: facts

        public string Id => id;
        public string Name => name ?? id;
        public string Version => version;

        public string Author {
            get {
                RefreshInfo();
                return author;
            }
        }

        public string Description {
            get {
                RefreshInfo();
                return description;
            }
        }

        private void RefreshInfo() {
            if (infoVersion == ModInfoDatabase.Version)
                return;
            infoVersion = ModInfoDatabase.Version;

            if (id == "Everest") {
                author = "Everest Team";
                description = "The mod loader itself. These are its own options.";
            } else if (ModInfoDatabase.TryGet(id, out ModInfoDatabase.Info info)) {
                author = string.IsNullOrWhiteSpace(info.Author) ? null : info.Author;
                description = string.IsNullOrWhiteSpace(info.Description) ? null : info.Description;
            } else {
                author = null;
                description = null;
            }
        }

        public int SettingCount {
            get {
                if (settingCount < 0)
                    settingCount = CountSettings();
                return settingCount;
            }
        }

        private SubScreen CurrentSub {
            get {
                object menu = screens.CurrentSub;
                if (menu == null)
                    return null;
                foreach (SubScreen sub in subs) {
                    if (sub.Menu == menu)
                        return sub;
                }
                return null;
            }
        }

        /// <summary>The rows on show: the mod's settings, or those of a screen the mod opened from them.</summary>
        public IReadOnlyList<ZkRow> Rows => CurrentSub?.Rows ?? rows;

        public string SubPage => open ? CurrentSub?.Title : null;

        /// <summary>
        /// True while something of the mod's own is on screen that ZKMM cannot show as a page
        /// (Everest's key config, for example). ZKMM hides and waits for it to finish.
        /// </summary>
        public bool Suspended => open && screens.State != ZkScreenState.Page;

        /// <summary>Back on a screen the mod opened: do what that screen does when it is backed out of.</summary>
        public void LeaveSubPage() {
            TextMenu menu = CurrentSub?.Menu;
            if (menu == null)
                return;
            screens.NoteBack();
            Action back = menu.OnCancel ?? menu.OnESC;
            if (back != null) {
                ItemRows.Guard(back);
            } else {
                // It has no way out of its own. Without it the page comes back.
                Audio.Play(SFX.ui_main_button_back);
                menu.RemoveSelf();
            }
        }

        // ------------------------------------------------------------------ building

        private static HostMenu NewHost() {
            // Never drawn and never updated by the game: ZKMM drives the items itself.
            return new HostMenu();
        }

        // The sound snapshot the pause menu holds; mods may use it for their options. Read loosely so that
        // an Everest version that names it differently only costs us the snapshot, not the menu.
        private static EventInstance PauseSnapshot {
            get {
                try {
                    const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                    PropertyInfo property = typeof(Level).GetProperty("_PauseSnapshot", flags);
                    if (property != null)
                        return property.GetValue(null) as EventInstance;
                    FieldInfo snapshotField = typeof(Level).GetField("PauseSnapshot", flags);
                    return snapshotField?.GetValue(null) as EventInstance;
                } catch {
                    return null;
                }
            }
        }

        private static bool Fill(EverestModule module, TextMenu host) {
            try {
                module.CreateModMenuSection(host, true, PauseSnapshot);
                return true;
            } catch (Exception e) {
                Logger.Log(LogLevel.Warn, ZKModMenuModule.LogTag, "Could not build the settings of " + module.Metadata?.Name + ":");
                Logger.LogDetailed(e, ZKModMenuModule.LogTag);
                return false;
            }
        }

        /// <summary>
        /// The mod's menu as it would look with untouched settings, used to learn default values.
        /// The mod's real settings are swapped out only while this menu is being made.
        /// </summary>
        private static HostMenu BuildDefaults(EverestModule module) {
            Type type = module.SettingsType;
            EverestModuleSettings real = module._Settings;
            if (type == null || real == null)
                return null;

            EverestModuleSettings fresh;
            try {
                fresh = Activator.CreateInstance(type) as EverestModuleSettings;
            } catch {
                return null;
            }
            if (fresh == null)
                return null;

            HostMenu host = NewHost();
            try {
                module._Settings = fresh;
                module.CreateModMenuSection(host, true, PauseSnapshot);
                return host;
            } catch (Exception e) {
                Logger.Log(LogLevel.Verbose, ZKModMenuModule.LogTag, "No default values for " + module.Metadata?.Name + ": " + e.Message);
                return null;
            } finally {
                module._Settings = real;
            }
        }

        private static HashSet<string> KeyConfigLabels() {
            HashSet<string> labels = new HashSet<string>();
            labels.Add(Dialog.Clean("options_keyconfig"));
            labels.Add(Dialog.Clean("options_btnconfig"));
            return labels;
        }

        /// <summary>Make the rows of one module. Returns the menu that holds the real items.</summary>
        private static HostMenu BuildRows(EverestModule module, bool withDefaults, List<ZkRow> into) {
            // Defaults first: mods that remember their menu items should end up remembering the real ones.
            HostMenu defaults = withDefaults ? BuildDefaults(module) : null;

            HostMenu host = NewHost();
            if (!Fill(module, host)) {
                into.Add(new ZkTextRow(ZkRowKind.Note, "The settings of this mod could not be loaded. See log.txt for details."));
                return host;
            }

            List<BindingRow> bindings;
            try {
                bindings = BindingRow.Find(module);
            } catch {
                bindings = new List<BindingRow>();
            }

            try {
                // With the bindings shown right on the page, the buttons to Everest's key config screens are not needed.
                ItemRows.Build(into, host.Items, defaults?.Items, 0, null, true, bindings.Count > 0 ? KeyConfigLabels() : null);
            } catch (Exception e) {
                Logger.Log(LogLevel.Warn, ZKModMenuModule.LogTag, "Could not read the settings of " + module.Metadata?.Name + ":");
                Logger.LogDetailed(e, ZKModMenuModule.LogTag);
            }

            if (bindings.Count > 0) {
                into.Add(new ZkTextRow(ZkRowKind.Header, "Key bindings"));
                into.AddRange(bindings);
            }
            return host;
        }

        private static int CountSelectable(List<ZkRow> list) {
            int count = 0;
            foreach (ZkRow row in list) {
                if (row.Selectable)
                    count++;
            }
            return count;
        }

        private int CountSettings() {
            List<ZkRow> temp = new List<ZkRow>();
            foreach (EverestModule module in modules) {
                try {
                    BuildRows(module, false, temp);
                } catch (Exception e) {
                    Logger.Log(LogLevel.Warn, ZKModMenuModule.LogTag, "Could not count the settings of " + id + ": " + e.Message);
                }
            }
            return CountSelectable(temp);
        }

        // ------------------------------------------------------------------ IZkMod: open and close

        public void Open() {
            if (open)
                Close();

            rows.Clear();
            hosts.Clear();
            Scene scene = Engine.Scene;
            level = scene as Level;
            bool withDefaults = options.DetectDefaults;

            foreach (EverestModule module in modules) {
                HostMenu host = BuildRows(module, withDefaults, rows);
                host.Orphaned = false;
                hosts.Add(host);
                // In the scene, so mods that ask their menu for the level get an answer.
                scene?.Add(host);
            }

            settingCount = CountSelectable(rows);
            screens.Reset(hosts);
            busyTime = 0f;
            trackFailed = false;
            Remember();
            open = true;
            Opened.Add(this);
        }

        public void Close() {
            if (!open)
                return;
            open = false;
            Opened.Remove(this);

            // Screens of the mod that were shown as pages go with the page.
            foreach (SubScreen sub in subs) {
                TakeOffScreen(sub.Menu);
                LetGo(sub);
            }
            subs.Clear();

            foreach (HostMenu host in hosts) {
                // The mod may still hold this menu and put it back on screen later. It then knows to get out of the way.
                host.Orphaned = true;
                TakeOffScreen(host);
            }
            hosts.Clear();
            arrived.Clear();
            rows.Clear();
            known.Clear();
            screens.Reset(null);
            level = null;

            Save();
        }

        // A menu that is still on its way to the screen is left to arrive. One of ours then removes itself
        // (see HostMenu); one of the mod's is the mod's to show.
        private static void TakeOffScreen(TextMenu menu) {
            try {
                if (menu.Scene != null)
                    menu.RemoveSelf();
            } catch (Exception e) {
                Logger.Log(LogLevel.Verbose, ZKModMenuModule.LogTag, "Could not remove a hidden menu: " + e.Message);
            }
        }

        private void Save() {
            foreach (EverestModule module in modules) {
                try {
                    if (module.SettingsType != null && module._Settings != null)
                        module.SaveSettings();
                } catch (Exception e) {
                    Logger.Log(LogLevel.Warn, ZKModMenuModule.LogTag, "Could not save the settings of " + id + ": " + e.Message);
                }
            }
        }

        public int ResetAllToDefault() {
            if (open) {
                int changed = ZkMenu.ResetRows(rows);
                if (changed > 0)
                    Save();
                return changed;
            }

            int count = 0;
            foreach (EverestModule module in modules) {
                if (module.SettingsType == null || module._Settings == null)
                    continue;
                try {
                    List<ZkRow> temp = new List<ZkRow>();
                    BuildRows(module, true, temp);
                    count += ZkMenu.ResetRows(temp);
                } catch (Exception e) {
                    Logger.Log(LogLevel.Warn, ZKModMenuModule.LogTag, "Could not reset the settings of " + id + ": " + e.Message);
                }
            }
            if (count > 0)
                Save();
            return count;
        }

        // ------------------------------------------------------------------ the mod's own screens

        private static void Note(string message) {
            Logger.Log(LogLevel.Info, ZKModMenuModule.LogTag, message);
        }

        // Only things that keep running while the game is paused can be a screen.
        private static bool CouldBeScreen(Entity entity) {
            return entity != null && !(entity is ZkMenuEntity) && !(entity is HostMenu) && entity.TagCheck((int) Tags.PauseUpdate);
        }

        /// <summary>Note what is on screen now. Whatever turns up later is something the mod opened.</summary>
        private void Remember() {
            known.Clear();
            if (level == null)
                return;
            EntityList entities = level.Entities;
            for (int i = 0; i < entities.Count; i++) {
                if (CouldBeScreen(entities[i]))
                    known.Add(entities[i]);
            }
            foreach (Entity entity in entities.ToAdd) {
                if (CouldBeScreen(entity))
                    known.Add(entity);
            }
        }

        bool IZkScreenWorld.OnScreen(object menu) {
            TextMenu textMenu = (TextMenu) menu;
            // A page that was only just opened is on its way to the screen. That counts.
            return textMenu.Scene != null || (textMenu is HostMenu && !arrived.Contains(textMenu));
        }

        bool IZkScreenWorld.HasFocus(object menu) {
            return ((TextMenu) menu).Focused;
        }

        bool IZkScreenWorld.PauseMenuOpen => level != null && level.PauseMainMenuOpen;

        // A screen that ends by fading out leaves nothing on screen for a moment. That is not being stuck.
        // Only believed for a few seconds, in case a fade never reports that it finished.
        bool IZkScreenWorld.Busy => level?.Wipe != null && !level.Wipe.Completed && busyTime < 4f;

        void IZkScreenWorld.FindNewcomers(List<ZkNewcomer> into) {
            if (level == null)
                return;
            EntityList entities = level.Entities;
            for (int i = 0; i < entities.Count; i++)
                Consider(entities[i], true, into);
            foreach (Entity entity in entities.ToAdd)
                Consider(entity, false, into);
        }

        private void Consider(Entity entity, bool arrivedOnScreen, List<ZkNewcomer> into) {
            if (!CouldBeScreen(entity) || known.Contains(entity))
                return;
            into.Add(new ZkNewcomer {
                Thing = entity,
                // Only a plain menu can be shown as a page. Menus with code of their own (key config screens) do their own thing.
                IsMenu = entity.GetType() == typeof(TextMenu),
                Arrived = arrivedOnScreen
            });
        }

        void IZkScreenWorld.Adopted(object menu) {
            SubScreen sub = new SubScreen { Menu = (TextMenu) menu };
            sub.WasVisible = sub.Menu.Visible;
            sub.WasActive = sub.Menu.Active;
            sub.Menu.Visible = false;
            sub.Menu.Active = false;
            BuildSub(sub);
            subs.Add(sub);
            Note(id + " opened a screen of its own (\"" + sub.Title + "\"). Showing it as a page.");
        }

        void IZkScreenWorld.Dropped(object menu) {
            foreach (SubScreen sub in subs) {
                if (sub.Menu == menu)
                    LetGo(sub);
            }
            subs.RemoveAll(sub => sub.Menu == menu);
        }

        /// <summary>Give a mod's menu back the way it was found.</summary>
        private static void LetGo(SubScreen sub) {
            TextMenu menu = sub.Menu;
            bool visible = sub.WasVisible, active = sub.WasActive;
            Scene scene = menu.Scene;
            if (scene == null) {
                menu.Visible = visible;
                menu.Active = active;
                return;
            }

            // It is being removed but stays on screen until the frame is over. Switched back on now,
            // it would be drawn and read the controls once more. Wait until it is really gone.
            int tries = 0;
            Action later = null;
            later = () => {
                if (menu.Scene == null || ++tries > 3) {
                    menu.Visible = visible;
                    menu.Active = active;
                } else {
                    scene.OnEndOfFrame += later;
                }
            };
            scene.OnEndOfFrame += later;
        }

        private static void BuildSub(SubScreen sub) {
            sub.Rows.Clear();
            sub.BuiltFrom.Clear();
            sub.BuiltFrom.AddRange(sub.Menu.Items);

            // The menu's big title becomes the title of the page.
            string title = null;
            List<TextMenu.Item> items = new List<TextMenu.Item>();
            foreach (TextMenu.Item item in sub.Menu.Items) {
                if (title == null && item is TextMenu.Header header && !string.IsNullOrWhiteSpace(header.Title)) {
                    title = ItemRows.OneLine(header.Title);
                    continue;
                }
                items.Add(item);
            }
            sub.Title = title ?? "More options";

            try {
                ItemRows.Build(sub.Rows, items, null, 0, null, false, null);
            } catch (Exception e) {
                Logger.Log(LogLevel.Warn, ZKModMenuModule.LogTag, "Could not read a screen of a mod:");
                Logger.LogDetailed(e, ZKModMenuModule.LogTag);
            }
        }

        private static bool SameItems(List<TextMenu.Item> a, List<TextMenu.Item> b) {
            if (a.Count != b.Count)
                return false;
            for (int i = 0; i < a.Count; i++) {
                if (!ReferenceEquals(a[i], b[i]))
                    return false;
            }
            return true;
        }

        /// <summary>Draw a menu the way the game does, for a setting that took the controls inside it.</summary>
        private static void ShowAsItIs(TextMenu menu) {
            menu.Visible = true;
            try {
                // What the menu does for itself when the game runs it: keep the selected item in view.
                menu.Position.Y = menu.Height > menu.ScrollableMinSize ? menu.ScrollTargetY : Engine.Height / 2f;
            } catch (Exception e) {
                Logger.Log(LogLevel.Verbose, ZKModMenuModule.LogTag, "Could not place a menu: " + e.Message);
            }
        }

        /// <summary>See who is in charge this frame. False when ZKMM has to leave.</summary>
        private bool Track(Level scene, float dt, bool restyle) {
            level = scene;
            foreach (HostMenu host in hosts) {
                if (host.Scene != null)
                    arrived.Add(host);
            }
            bool fading = level?.Wipe != null && !level.Wipe.Completed;
            busyTime = fading ? busyTime + dt : 0f;

            ZkScreenState before = screens.State;
            ZkScreenState state = screens.Step(dt, restyle, this);
            if (state != before) {
                switch (state) {
                    case ZkScreenState.Waiting:
                        Note(id + " is showing something of its own. Waiting for it to finish.");
                        break;
                    case ZkScreenState.Inline:
                        Note("A setting of " + id + " took the controls. Showing its menu as the game draws it.");
                        break;
                    case ZkScreenState.PauseMenu:
                        Note(id + " went back to the pause menu. Closing.");
                        break;
                    case ZkScreenState.Stranded:
                        Note(id + " closed its screen without handing the controls back. Loading its page again.");
                        break;
                }
            }

            if (state == ZkScreenState.PauseMenu)
                return false;
            if (state == ZkScreenState.Stranded) {
                // Whatever the mod meant to return to is not coming. A fresh page always works.
                Close();
                Open();
                return true;
            }
            if (state == ZkScreenState.Page)
                Remember();

            // Our menus stay hidden and are never run by the game, whatever a mod did to them in the meantime.
            // The one exception: a setting that took the controls inside its menu can only be seen in that menu.
            bool inline = state == ZkScreenState.Inline;
            foreach (HostMenu host in hosts) {
                host.Active = false;
                host.Visible = false;
                if (inline && screens.PageIsCurrent && !host.Focused)
                    ShowAsItIs(host);
            }
            foreach (SubScreen sub in subs) {
                sub.Menu.Active = false;
                sub.Menu.Visible = false;
                if (inline && screens.CurrentSub == sub.Menu)
                    ShowAsItIs(sub.Menu);
            }
            return true;
        }

        /// <summary>Do for a hidden menu what the game does for one on screen, apart from reading the controls.</summary>
        private void Drive(TextMenu menu) {
            // Off screen the game would not run it either.
            if (menu.Scene == null && arrived.Contains(menu))
                return;

            try {
                // Timers and routines the menu carries, such as the one behind "save, then close".
                ComponentList components = menu.Components;
                for (int i = 0; i < components.Count; i++) {
                    Component component = components[i];
                    if (component != null && component.Active)
                        component.Update();
                }
                menu.OnUpdate?.Invoke();
            } catch (Exception e) {
                Logger.Log(LogLevel.Verbose, ZKModMenuModule.LogTag, "A menu of " + id + " failed to update: " + e.Message);
            }

            List<TextMenu.Item> items = menu.Items;
            for (int i = 0; i < items.Count; i++) {
                TextMenu.Item item = items[i];
                if (item == null)
                    continue;
                try {
                    item.OnUpdate?.Invoke();
                    item.Update();
                } catch (Exception e) {
                    Logger.Log(LogLevel.Verbose, ZKModMenuModule.LogTag, "A setting of " + id + " failed to update: " + e.Message);
                }
            }
        }

        // ------------------------------------------------------------------ per frame

        /// <summary>
        /// Once per frame while a mod page is open: see whether the mod opened or closed a screen of its own,
        /// and give the hidden menus their per-frame update.
        /// </summary>
        /// <returns>False when the pause menu came back and ZKMM has to leave.</returns>
        public static bool UpdateOpen(Level level, float dt, bool restyle) {
            foreach (EverestMod mod in Opened.ToArray()) {
                try {
                    if (!mod.Track(level, dt, restyle))
                        return false;
                } catch (Exception e) {
                    if (!mod.trackFailed) {
                        mod.trackFailed = true;
                        Logger.Log(LogLevel.Warn, ZKModMenuModule.LogTag, "Could not follow the screens of " + mod.id + ":");
                        Logger.LogDetailed(e, ZKModMenuModule.LogTag);
                    }
                }
                if (!mod.open)
                    continue;

                foreach (HostMenu host in mod.hosts)
                    mod.Drive(host);
                foreach (SubScreen sub in mod.subs.ToArray()) {
                    if (sub.Menu.Scene == null)
                        continue;
                    mod.arrived.Add(sub.Menu);
                    // Menus that rebuild themselves (after a "reset" button, say) get fresh rows.
                    if (!SameItems(sub.BuiltFrom, sub.Menu.Items))
                        BuildSub(sub);
                    mod.Drive(sub.Menu);
                }
            }
            return true;
        }

        /// <summary>
        /// The player resumed the game from a screen a mod opened. Let that screen do what it does on resume
        /// (mods save their settings there).
        /// </summary>
        public static void ResumeFromOpen() {
            foreach (EverestMod mod in Opened.ToArray()) {
                Action resume = mod.open && mod.screens.State == ZkScreenState.Page ? mod.CurrentSub?.Menu.OnPause : null;
                if (resume != null)
                    ItemRows.Guard(resume);
            }
        }

        /// <summary>Close every open page. Used when the menu goes away for any reason.</summary>
        public static void CloseAll() {
            foreach (EverestMod mod in Opened.ToArray())
                mod.Close();
        }
    }
}
