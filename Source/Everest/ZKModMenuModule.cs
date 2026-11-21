using System;
using System.Collections.Generic;
using System.Linq;
using Celeste;
using Celeste.Mod;
using Monocle;

namespace ZKModMenu {
    /// <summary>
    /// ZK Mod Menu (ZKMM): a pause menu entry that gives every installed mod a page of its own.
    /// </summary>
    public class ZKModMenuModule : EverestModule {
        internal const string LogTag = "ZKModMenu";

        public static ZKModMenuModule Instance { get; private set; }

        public override Type SettingsType => typeof(ZKModMenuSettings);
        public static ZKModMenuSettings Settings => (ZKModMenuSettings) Instance._Settings;

        internal static string VersionText => Instance?.Metadata?.VersionString ?? "1.1.0";

        private static string PauseLabel => Dialog.Clean("zkmodmenu_pause_button");

        public ZKModMenuModule() {
            Instance = this;
        }

        public override void Load() {
            Everest.Events.Level.OnCreatePauseMenuButtons += OnCreatePauseMenuButtons;
        }

        public override void Unload() {
            Everest.Events.Level.OnCreatePauseMenuButtons -= OnCreatePauseMenuButtons;
            ZkMenuEntity.Current?.ForceClose(true);
            MonocleCanvas.DisposeTextures();
        }

        private static int FindButton(TextMenu menu, string label) {
            return menu.Items.FindIndex(item => item is TextMenu.Button button && button.Label == label);
        }

        // Runs each time the pause menu is made. Everest has added its Mod Options button by now.
        private void OnCreatePauseMenuButtons(Level level, TextMenu menu, bool minimal) {
            try {
                List<TextMenu.Item> items = menu.Items;

                int modOptions = FindButton(menu, Dialog.Clean("menu_pause_modoptions"));
                TextMenu.Item modOptionsItem = modOptions >= 0 ? items[modOptions] : null;

                // Right above Everest's Mod Options; without it, below Options; failing that, at the end.
                int index = modOptions;
                if (index < 0) {
                    int options = FindButton(menu, Dialog.Clean("menu_pause_options"));
                    index = options >= 0 ? options + 1 : items.Count;
                }

                TextMenu.Button button = new TextMenu.Button(PauseLabel);
                button.Pressed(() => {
                    int returnIndex = menu.IndexOf(button);
                    menu.RemoveSelf();
                    // A copy left waiting behind a mod's own screen must not come back to life.
                    ZkMenuEntity.Current?.ForceClose(false);

                    // Stay paused, with ZKMM in place of the pause menu.
                    level.PauseMainMenuOpen = false;
                    level.Paused = true;
                    level.Add(new ZkMenuEntity(level, returnIndex, minimal));
                });
                menu.Insert(index, button);

                if (Settings.HideModOptionsButton && modOptionsItem != null)
                    menu.Remove(modOptionsItem);
            } catch (Exception e) {
                Logger.Log(LogLevel.Error, LogTag, "Could not add ZK Mod Menu to the pause menu:");
                Logger.LogDetailed(e, LogTag);
            }
        }

        /// <summary>Bring the pause menu back with the ZK Mod Menu entry selected.</summary>
        internal static void ReturnToPauseMenu(Level level, int returnIndex, bool minimal) {
            level.Pause(returnIndex, minimal, false);

            // The entry may have moved (for example when the Mod Options button was just hidden or shown).
            try {
                string label = PauseLabel;
                foreach (Entity entity in level.Entities.ToAdd) {
                    if (!(entity is TextMenu pause))
                        continue;
                    int index = FindButton(pause, label);
                    if (index >= 0) {
                        pause.Selection = index;
                        break;
                    }
                }
            } catch (Exception e) {
                Logger.Log(LogLevel.Verbose, LogTag, "Could not reselect the pause menu entry: " + e.Message);
            }
        }
    }
}
