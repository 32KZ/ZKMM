using System;
using Celeste;
using Celeste.Mod;
using Monocle;

namespace ZKModMenu {
    /// <summary>
    /// The hidden menu a mod fills with its settings. To the mod it is Everest's Mod Options menu.
    ///
    /// The game never runs or draws it: ZKMM presses its items itself and shows them as rows.
    /// Mods keep hold of this menu, though. A button that opens a screen of the mod's own often takes the menu off
    /// screen first and puts it back when that screen closes. If ZKMM has left by then, a hidden menu would be all
    /// that is on screen and the game would be stuck. So a menu that comes back unowned opens the pause menu instead.
    /// </summary>
    internal sealed class HostMenu : TextMenu {
        /// <summary>True while no open mod page uses this menu.</summary>
        public bool Orphaned = true;

        public HostMenu() {
            Visible = false;
            Active = false;
            AutoScroll = false;
        }

        // ZKMM does this menu's work. Should a mod switch it on, the game must still not run it a second time.
        public override void Update() {
        }

        public override void Added(Scene scene) {
            base.Added(scene);
            if (Orphaned && scene != null)
                scene.OnEndOfFrame += () => Rescue(scene);
        }

        private void Rescue(Scene scene) {
            try {
                RemoveSelf();
                if (!(scene is Level level) || Engine.Scene != scene || !level.Paused)
                    return;
                if (ZkMenuEntity.Current != null || AnotherMenuIsUp(level))
                    return;
                Logger.Log(LogLevel.Info, ZKModMenuModule.LogTag,
                    "A mod went back to its settings after ZK Mod Menu had closed. Opening the pause menu instead.");
                level.Pause(0, false, false);
            } catch (Exception e) {
                Logger.Log(LogLevel.Warn, ZKModMenuModule.LogTag, "Could not return to the pause menu:");
                Logger.LogDetailed(e, ZKModMenuModule.LogTag);
            }
        }

        private static bool IsRealMenu(Entity entity) {
            return entity is TextMenu && !(entity is HostMenu);
        }

        private static bool AnotherMenuIsUp(Level level) {
            EntityList entities = level.Entities;
            for (int i = 0; i < entities.Count; i++) {
                if (IsRealMenu(entities[i]))
                    return true;
            }
            foreach (Entity entity in entities.ToAdd) {
                if (IsRealMenu(entity))
                    return true;
            }
            return false;
        }
    }
}
