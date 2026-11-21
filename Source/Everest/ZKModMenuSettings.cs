using System;
using System.Collections.Generic;
using Celeste.Mod;
using Microsoft.Xna.Framework.Input;
using ZKModMenu.Core;

namespace ZKModMenu {
    /// <summary>
    /// ZKMM's saved options. Everest stores this as modsettings-ZKModMenu.celeste in the Saves folder.
    /// </summary>
    [SettingName("modoptions_zkmodmenu_title")]
    public class ZKModMenuSettings : EverestModuleSettings, IZkOptions {

        public ZkThemeKind Theme { get; set; } = ZkThemeKind.Dark;

        public bool CompactList { get; set; } = false;

        public bool Animations { get; set; } = true;

        public bool ShowAuthorAndVersion { get; set; } = true;

        public bool ShowModsWithoutSettings { get; set; } = true;

        public bool DetectDefaultValues { get; set; } = true;

        public bool LookUpModInfoOnline { get; set; } = true;

        public bool ShowModImages { get; set; } = true;

        public bool HideModOptionsButton { get; set; } = false;

        /// <summary>Show menus that a mod opens from its page as ZKMM pages, instead of in Celeste's own menu style.</summary>
        public bool RestyleModScreens { get; set; } = true;

        /// <summary>The "other action" button: favourite a mod in the list, reset a setting on a mod page.</summary>
        [DefaultButtonBinding(Buttons.X, Keys.X)]
        public ButtonBinding OtherActionButton { get; set; }

        /// <summary>Ids of the favourite mods. Edited from inside ZK Mod Menu.</summary>
        [SettingIgnore]
        public List<string> Favourites { get; set; } = new List<string>();

        // ---- IZkOptions. Explicit, so these are not written to the settings file a second time.

        bool IZkOptions.Compact {
            get => CompactList;
            set => CompactList = value;
        }

        bool IZkOptions.ShowMeta {
            get => ShowAuthorAndVersion;
            set => ShowAuthorAndVersion = value;
        }

        bool IZkOptions.ShowEmptyMods {
            get => ShowModsWithoutSettings;
            set => ShowModsWithoutSettings = value;
        }

        bool IZkOptions.DetectDefaults {
            get => DetectDefaultValues;
            set => DetectDefaultValues = value;
        }

        bool IZkOptions.OnlineInfo {
            get => LookUpModInfoOnline;
            set => LookUpModInfoOnline = value;
        }

        bool IZkOptions.ShowImages {
            get => ShowModImages;
            set => ShowModImages = value;
        }

        bool IZkOptions.IsFavourite(string id) {
            if (id == null || Favourites == null)
                return false;
            foreach (string favourite in Favourites) {
                if (string.Equals(favourite, id, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        void IZkOptions.SetFavourite(string id, bool favourite) {
            if (id == null)
                return;
            if (Favourites == null)
                Favourites = new List<string>();
            Favourites.RemoveAll(f => string.Equals(f, id, StringComparison.OrdinalIgnoreCase));
            if (favourite)
                Favourites.Add(id);
        }

        int IZkOptions.FavouriteCount => Favourites?.Count ?? 0;

        void IZkOptions.ClearFavourites() {
            Favourites?.Clear();
        }
    }
}
