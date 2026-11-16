using System.Collections.Generic;

namespace ZKModMenu.Core {

    /// <summary>What an open mod page is doing about screens the mod opens on its own.</summary>
    public enum ZkScreenState {
        /// <summary>A menu is in charge and ZKMM shows it as a page: the mod's settings, or a screen of the mod's.</summary>
        Page,
        /// <summary>Something of the mod's own is on screen. ZKMM hides and waits for it.</summary>
        Waiting,
        /// <summary>A setting took the controls inside its own menu. That menu is shown the way the game draws it.</summary>
        Inline,
        /// <summary>The pause menu came back. ZKMM has to leave.</summary>
        PauseMenu,
        /// <summary>Everything closed and nothing took over. The page has to be made again, or the game is stuck.</summary>
        Stranded
    }

    /// <summary>Something that turned up on screen while a mod page was open.</summary>
    public struct ZkNewcomer {
        public object Thing;
        /// <summary>True for a plain menu, the kind ZKMM can show as a page.</summary>
        public bool IsMenu;
        /// <summary>False while it is still waiting to be put on screen.</summary>
        public bool Arrived;
    }

    /// <summary>What <see cref="ZkScreens"/> needs to know about the game.</summary>
    public interface IZkScreenWorld {
        bool OnScreen(object menu);
        bool HasFocus(object menu);
        /// <summary>True while the game's own pause menu is the open menu.</summary>
        bool PauseMenuOpen { get; }
        /// <summary>True while the game is doing something that needs no menu, such as fading the screen out.</summary>
        bool Busy { get; }
        /// <summary>List what is on screen now, or about to be, that was not there when a page was last in charge.</summary>
        void FindNewcomers(List<ZkNewcomer> into);
        /// <summary>A menu of the mod's will be shown as a page from now on.</summary>
        void Adopted(object menu);
        /// <summary>An adopted menu has closed.</summary>
        void Dropped(object menu);
    }

    /// <summary>
    /// Keeps track of who is in charge while a mod page is open.
    ///
    /// A mod's button can open a screen of the mod's own. Mods do that in a few ways: take the options menu off
    /// screen and put it back later, switch it off and on again, or go back to the pause menu when done. They all
    /// expect Everest's Mod Options underneath. This class watches the menus and says what ZKMM should do, so that
    /// leaving such a screen always ends up somewhere the player can use.
    /// </summary>
    public sealed class ZkScreens {
        /// <summary>How long nothing may be in charge before the page is made again.</summary>
        public const float StrandedAfter = 0.6f;

        // The hidden menus that hold the settings of the mod page. Mods with several parts have several.
        private readonly List<object> page = new List<object>();
        // Menus the mod opened that ZKMM shows as pages, the newest last.
        private readonly List<object> subs = new List<object>();
        // Menus that gave the controls to something else. Until they are in charge again they are not shown as they are.
        private readonly HashSet<object> handedOff = new HashSet<object>();
        private readonly List<ZkNewcomer> newcomers = new List<ZkNewcomer>();
        private readonly object pageKey = new object();

        private object current;
        private float stranded;

        public ZkScreenState State { get; private set; } = ZkScreenState.Page;

        /// <summary>The adopted menu that is in charge, or null when it is the mod page (or nothing).</summary>
        public object CurrentSub => current != null && current != pageKey ? current : null;

        /// <summary>True when the mod page itself is the innermost menu still on screen.</summary>
        public bool PageIsCurrent => current == pageKey;

        public IReadOnlyList<object> Subs => subs;

        /// <summary>Start again with these menus as the mod page.</summary>
        public void Reset(IEnumerable<object> pageMenus) {
            page.Clear();
            subs.Clear();
            handedOff.Clear();
            if (pageMenus != null)
                page.AddRange(pageMenus);
            current = pageKey;
            stranded = 0f;
            State = ZkScreenState.Page;
        }

        /// <summary>Back was passed on to the menu in charge. While it closes it must not be shown as it is.</summary>
        public void NoteBack() {
            if (current != null)
                handedOff.Add(current);
        }

        private bool IsOurs(object thing) {
            return page.Contains(thing) || subs.Contains(thing);
        }

        private bool PageOnScreen(IZkScreenWorld world) {
            if (page.Count == 0)
                return false;
            foreach (object menu in page) {
                if (!world.OnScreen(menu))
                    return false;
            }
            return true;
        }

        private bool Focused(object key, IZkScreenWorld world) {
            if (key != pageKey)
                return world.HasFocus(key);
            foreach (object menu in page) {
                if (!world.HasFocus(menu))
                    return false;
            }
            return true;
        }

        /// <summary>Look at the game once per frame and decide what ZKMM should do.</summary>
        /// <param name="adopt">Show plain menus the mod opens as ZKMM pages. When false they are left as they are.</param>
        public ZkScreenState Step(float dt, bool adopt, IZkScreenWorld world) {
            // Whoever was in charge until now is the one that opened anything new.
            object opener = current ?? (subs.Count > 0 ? subs[subs.Count - 1] : pageKey);

            newcomers.Clear();
            world.FindNewcomers(newcomers);
            bool other = false;
            foreach (ZkNewcomer newcomer in newcomers) {
                if (newcomer.Thing == null || IsOurs(newcomer.Thing))
                    continue;
                if (newcomer.IsMenu && world.PauseMenuOpen)
                    return State = ZkScreenState.PauseMenu;
                if (newcomer.IsMenu && adopt && newcomer.Arrived) {
                    subs.Add(newcomer.Thing);
                    handedOff.Add(opener);
                    world.Adopted(newcomer.Thing);
                } else {
                    other = true;
                }
            }

            // In charge is the innermost menu that is still on screen.
            const int none = -2, thePage = -1;
            int index = none;
            for (int i = subs.Count - 1; i >= 0; i--) {
                if (world.OnScreen(subs[i])) {
                    index = i;
                    break;
                }
            }
            if (index == none && PageOnScreen(world))
                index = thePage;

            // Menus opened from it have closed. While nothing is on screen they are kept: one of them may come back.
            if (index != none) {
                for (int i = subs.Count - 1; i > index; i--) {
                    object gone = subs[i];
                    subs.RemoveAt(i);
                    handedOff.Remove(gone);
                    world.Dropped(gone);
                }
            }

            current = index == none ? null : index == thePage ? pageKey : subs[index];

            if (current != null && Focused(current, world)) {
                handedOff.Remove(current);
                stranded = 0f;
                return State = ZkScreenState.Page;
            }

            // Nothing of ours has the controls.
            if (other) {
                handedOff.Add(current ?? opener);
                stranded = 0f;
                return State = ZkScreenState.Waiting;
            }
            if (world.Busy) {
                stranded = 0f;
                return State = ZkScreenState.Waiting;
            }
            // Still on screen, switched off, and nothing was opened: one of its own settings took the controls.
            if (current != null && !handedOff.Contains(current)) {
                stranded = 0f;
                return State = ZkScreenState.Inline;
            }

            stranded += dt;
            return State = stranded >= StrandedAfter ? ZkScreenState.Stranded : ZkScreenState.Waiting;
        }
    }
}
