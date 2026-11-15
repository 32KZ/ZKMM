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
    internal sealed class EverestMod : IZkMod {
        private static readonly List<EverestMod> Opened = new List<EverestMod>();

        private readonly List<EverestModule> modules = new List<EverestModule>();
        private readonly IZkOptions options;
        private readonly string id;
        private readonly string version;
        private string name;

        private readonly List<ZkRow> rows = new List<ZkRow>();
        private readonly List<TextMenu> hosts = new List<TextMenu>();
        private readonly HashSet<TextMenu> arrived = new HashSet<TextMenu>();
        private bool open;
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

        public IReadOnlyList<ZkRow> Rows => rows;

        /// <summary>
        /// A mod's button can open a screen of its own (Everest's key config does). Such screens switch
        /// the menu they came from off and back on, which is how ZKMM knows to step aside meanwhile.
        /// </summary>
        public bool Suspended {
            get {
                if (!open)
                    return false;
                foreach (TextMenu host in hosts) {
                    if (!host.Focused)
                        return true;
                }
                return false;
            }
        }

        // ------------------------------------------------------------------ building

        private static TextMenu NewHost() {
            // Never drawn and never updated by the game: ZKMM drives the items itself.
            return new TextMenu {
                Visible = false,
                Active = false,
                AutoScroll = false
            };
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
        private static TextMenu BuildDefaults(EverestModule module) {
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

            TextMenu host = NewHost();
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
        private static TextMenu BuildRows(EverestModule module, bool withDefaults, List<ZkRow> into) {
            // Defaults first: mods that remember their menu items should end up remembering the real ones.
            TextMenu defaults = withDefaults ? BuildDefaults(module) : null;

            TextMenu host = NewHost();
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
            bool withDefaults = options.DetectDefaults;

            foreach (EverestModule module in modules) {
                TextMenu host = BuildRows(module, withDefaults, rows);
                hosts.Add(host);
                // In the scene, so mods that ask their menu for the level get an answer.
                scene?.Add(host);
            }

            settingCount = CountSelectable(rows);
            open = true;
            Opened.Add(this);
        }

        public void Close() {
            if (!open)
                return;
            open = false;
            Opened.Remove(this);

            foreach (TextMenu host in hosts) {
                try {
                    if (host.Scene != null)
                        host.RemoveSelf();
                    else
                        Engine.Scene?.Entities.ToAdd.Remove(host); // Opened and closed within one frame: it never arrived.
                } catch (Exception e) {
                    Logger.Log(LogLevel.Verbose, ZKModMenuModule.LogTag, "Could not remove a hidden menu: " + e.Message);
                }
            }
            hosts.Clear();
            arrived.Clear();
            rows.Clear();

            Save();
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

        // ------------------------------------------------------------------ per frame

        /// <summary>Give the items of open mod pages their per-frame update, as their own menu would.</summary>
        public static void UpdateOpen() {
            foreach (EverestMod mod in Opened) {
                foreach (TextMenu host in mod.hosts) {
                    if (host.Scene != null)
                        mod.arrived.Add(host);
                    List<TextMenu.Item> items = host.Items;
                    for (int i = 0; i < items.Count; i++) {
                        TextMenu.Item item = items[i];
                        if (item == null)
                            continue;
                        try {
                            item.OnUpdate?.Invoke();
                            item.Update();
                        } catch (Exception e) {
                            Logger.Log(LogLevel.Verbose, ZKModMenuModule.LogTag, "A setting of " + mod.id + " failed to update: " + e.Message);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// True when a mod removed the hidden menu its settings live in. Mods do that when one of their buttons
        /// swaps the options menu for a menu of their own; that menu then runs the show and ZKMM must leave.
        /// </summary>
        public static bool TakenOver {
            get {
                foreach (EverestMod mod in Opened) {
                    foreach (TextMenu host in mod.hosts) {
                        if (host.Scene == null && mod.arrived.Contains(host))
                            return true;
                    }
                }
                return false;
            }
        }

        /// <summary>Close every open page. Used when the menu goes away for any reason.</summary>
        public static void CloseAll() {
            foreach (EverestMod mod in Opened.ToArray())
                mod.Close();
        }
    }
}
