using System;
using System.Collections.Generic;

namespace ZKModMenu.Core {

    /// <summary>The buttons the menu understands. The game side maps real input to these.</summary>
    public enum ZkAction {
        Up,
        Down,
        Left,
        Right,
        Confirm,
        Alt,
        Back,
        PageUp,
        PageDown,
        Search
    }

    public enum ZkTypingKey {
        Enter,
        Backspace,
        Escape,
        Down
    }

    public enum ZkSound {
        MoveUp,
        MoveDown,
        Select,
        Back,
        ToggleOn,
        ToggleOff,
        Invalid,
        Type,
        Erase,
        Favourite
    }

    public enum ZkRowKind {
        /// <summary>Non-selectable group title.</summary>
        Header,
        /// <summary>Non-selectable line of text.</summary>
        Note,
        Toggle,
        Slider,
        Choice,
        Keybind,
        Button,
        Favourite,
        ResetAll
    }

    /// <summary>
    /// One line on a mod page. The menu only talks to rows through this class;
    /// in game each row wraps a real Everest menu item.
    /// </summary>
    public abstract class ZkRow {
        public abstract ZkRowKind Kind { get; }
        public virtual string Label => "";
        /// <summary>Extra help text, shown while the row is selected.</summary>
        public virtual string Description => null;
        public virtual bool Visible => true;
        public virtual bool Disabled => false;
        /// <summary>Nesting level, for settings inside a sub-menu.</summary>
        public virtual int Indent => 0;

        public bool Selectable => Kind != ZkRowKind.Header && Kind != ZkRowKind.Note;

        /// <summary>Toggle state.</summary>
        public virtual bool On => false;
        /// <summary>Slider position in 0..1.</summary>
        public virtual float Fraction => 0f;
        /// <summary>Text of the current value (sliders and choices).</summary>
        public virtual string ValueText => "";
        public virtual bool CanLeft => true;
        public virtual bool CanRight => true;
        /// <summary>Key caps of a keybind row.</summary>
        public virtual IReadOnlyList<string> Caps => Array.Empty<string>();

        /// <summary>True when the default value of this row is known.</summary>
        public virtual bool HasDefault => false;
        /// <summary>True when the value differs from the known default.</summary>
        public virtual bool Changed => false;

        public virtual void Left() { }
        public virtual void Right() { }
        public virtual void Confirm() { }
        /// <summary>Put the row back to its default. Returns false when nothing changed.</summary>
        public virtual bool Reset() => false;

        public virtual void Selected() { }
        public virtual void Deselected() { }
    }

    /// <summary>A keybind row. Capturing the actual key press is done by the game side.</summary>
    public abstract class ZkKeybindRow : ZkRow {
        public override ZkRowKind Kind => ZkRowKind.Keybind;
    }

    /// <summary>One installed mod, as the menu sees it.</summary>
    public interface IZkMod {
        /// <summary>Stable id (the everest.yaml name). Used for favourites.</summary>
        string Id { get; }
        string Name { get; }
        string Version { get; }
        /// <summary>May be null when unknown.</summary>
        string Author { get; }
        /// <summary>May be null when unknown.</summary>
        string Description { get; }
        /// <summary>Number of settings the mod has. Worked out on first use.</summary>
        int SettingCount { get; }
        /// <summary>The rows of the mod page. Only valid between Open and Close.</summary>
        IReadOnlyList<ZkRow> Rows { get; }
        /// <summary>True while the mod shows a screen of its own (for example a key config screen).</summary>
        bool Suspended { get; }
        void Open();
        void Close();
        /// <summary>Put every setting with a known default back to it and save. Returns how many changed.</summary>
        int ResetAllToDefault();
    }

    /// <summary>ZKMM's own saved options.</summary>
    public interface IZkOptions {
        ZkThemeKind Theme { get; set; }
        bool Compact { get; set; }
        bool Animations { get; set; }
        bool ShowMeta { get; set; }
        bool ShowEmptyMods { get; set; }
        bool DetectDefaults { get; set; }
        bool OnlineInfo { get; set; }
        bool HideModOptionsButton { get; set; }

        bool IsFavourite(string id);
        void SetFavourite(string id, bool favourite);
        int FavouriteCount { get; }
        void ClearFavourites();
    }

    /// <summary>What the menu needs from the game around it.</summary>
    public interface IZkHost {
        IReadOnlyList<IZkMod> Mods { get; }
        IZkOptions Options { get; }
        /// <summary>ZKMM's own version, for its settings page.</summary>
        string Version { get; }
        /// <summary>Extra rows for the ZKMM settings page that only the game side can provide (key bindings).</summary>
        IReadOnlyList<ZkRow> ExtraSettingsRows { get; }

        void SaveOptions();
        void Play(ZkSound sound);
        /// <summary>Short name of the key or button for an action, for the hint bar.</summary>
        string Glyph(ZkAction action);
        void BeginTyping();
        void EndTyping();
        /// <summary>Called when a keybind row was confirmed; the host captures the next key press.</summary>
        void BeginListening(ZkKeybindRow row);
        /// <summary>Leave the menu and go back to the pause menu.</summary>
        void RequestClose();
        /// <summary>Called when options that change the mod pages were edited.</summary>
        void OptionsChanged();
    }

    /// <summary>A row of the ZKMM settings page, driven by delegates.</summary>
    public sealed class ZkOptionRow : ZkRow {
        private readonly ZkRowKind kind;
        private readonly string label;
        private readonly string description;
        private readonly string[] choices;
        private readonly Func<int> get;
        private readonly Action<int> set;
        private readonly int defaultValue;
        private readonly Action press;

        /// <summary>A toggle (values 0 and 1) or a choice between <paramref name="choices"/>.</summary>
        public ZkOptionRow(string label, string[] choices, Func<int> get, Action<int> set, int defaultValue, string description = null) {
            kind = choices == null ? ZkRowKind.Toggle : ZkRowKind.Choice;
            this.label = label;
            this.choices = choices;
            this.get = get;
            this.set = set;
            this.defaultValue = defaultValue;
            this.description = description;
        }

        /// <summary>A button.</summary>
        public ZkOptionRow(string label, Action press, string description = null) {
            kind = ZkRowKind.Button;
            this.label = label;
            this.press = press;
            this.description = description;
        }

        public override ZkRowKind Kind => kind;
        public override string Label => label;
        public override string Description => description;
        public override bool On => get != null && get() != 0;
        public override string ValueText => choices != null ? choices[Math.Max(0, Math.Min(choices.Length - 1, get()))] : "";
        public override bool CanLeft => choices == null || get() > 0;
        public override bool CanRight => choices == null || get() < choices.Length - 1;
        public override bool HasDefault => kind != ZkRowKind.Button;
        public override bool Changed => kind != ZkRowKind.Button && get() != defaultValue;

        public override void Left() {
            if (kind == ZkRowKind.Toggle)
                set(get() != 0 ? 0 : 1);
            else if (kind == ZkRowKind.Choice && get() > 0)
                set(get() - 1);
        }

        public override void Right() {
            if (kind == ZkRowKind.Toggle)
                set(get() != 0 ? 0 : 1);
            else if (kind == ZkRowKind.Choice && get() < choices.Length - 1)
                set(get() + 1);
        }

        public override void Confirm() {
            if (kind == ZkRowKind.Toggle)
                set(get() != 0 ? 0 : 1);
            else if (kind == ZkRowKind.Choice)
                set((get() + 1) % choices.Length);
            else
                press?.Invoke();
        }

        public override bool Reset() {
            if (kind == ZkRowKind.Button || get() == defaultValue)
                return false;
            set(defaultValue);
            return true;
        }
    }

    /// <summary>A plain header or note row.</summary>
    public sealed class ZkTextRow : ZkRow {
        private readonly ZkRowKind kind;
        private readonly string label;
        private readonly int indent;

        public ZkTextRow(ZkRowKind kind, string label, int indent = 0) {
            this.kind = kind;
            this.label = label;
            this.indent = indent;
        }

        public override ZkRowKind Kind => kind;
        public override string Label => label;
        public override int Indent => indent;
    }
}
