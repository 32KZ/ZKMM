using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework.Input;
using Monocle;
using ZKModMenu.Core;

namespace ZKModMenu {
    /// <summary>Short names for keys and controller buttons, for key caps.</summary>
    internal static class KeyNames {
        public static string Of(Keys key) {
            switch (key) {
                case Keys.Up: return "↑";
                case Keys.Down: return "↓";
                case Keys.Left: return "←";
                case Keys.Right: return "→";
                case Keys.Back: return "Bksp";
                case Keys.Enter: return "Enter";
                case Keys.Escape: return "Esc";
                case Keys.Space: return "Space";
                case Keys.Tab: return "Tab";
                case Keys.Delete: return "Del";
                case Keys.Insert: return "Ins";
                case Keys.PageUp: return "PgUp";
                case Keys.PageDown: return "PgDn";
                case Keys.CapsLock: return "Caps";
                case Keys.LeftShift: return "LShift";
                case Keys.RightShift: return "RShift";
                case Keys.LeftControl: return "LCtrl";
                case Keys.RightControl: return "RCtrl";
                case Keys.LeftAlt: return "LAlt";
                case Keys.RightAlt: return "RAlt";
                case Keys.OemComma: return ",";
                case Keys.OemPeriod: return ".";
                case Keys.OemMinus: return "-";
                case Keys.OemPlus: return "=";
                case Keys.OemQuestion: return "/";
                case Keys.OemSemicolon: return ";";
                case Keys.OemQuotes: return "'";
                case Keys.OemOpenBrackets: return "[";
                case Keys.OemCloseBrackets: return "]";
                case Keys.OemPipe: return "\\";
                case Keys.OemBackslash: return "\\";
                case Keys.OemTilde: return "`";
                case Keys.Add: return "Num+";
                case Keys.Subtract: return "Num-";
                case Keys.Multiply: return "Num*";
                case Keys.Divide: return "Num/";
                case Keys.Decimal: return "Num.";
            }
            if (key >= Keys.D0 && key <= Keys.D9)
                return ((int) key - (int) Keys.D0).ToString();
            if (key >= Keys.NumPad0 && key <= Keys.NumPad9)
                return "Num" + ((int) key - (int) Keys.NumPad0);
            return key.ToString();
        }

        public static string Of(Buttons button) {
            switch (button) {
                case Buttons.LeftShoulder: return "LB";
                case Buttons.RightShoulder: return "RB";
                case Buttons.LeftTrigger: return "LT";
                case Buttons.RightTrigger: return "RT";
                case Buttons.LeftStick: return "LS";
                case Buttons.RightStick: return "RS";
                case Buttons.Back: return "Select";
                case Buttons.DPadUp: return "D-pad ↑";
                case Buttons.DPadDown: return "D-pad ↓";
                case Buttons.DPadLeft: return "D-pad ←";
                case Buttons.DPadRight: return "D-pad →";
                case Buttons.LeftThumbstickUp: return "LS ↑";
                case Buttons.LeftThumbstickDown: return "LS ↓";
                case Buttons.LeftThumbstickLeft: return "LS ←";
                case Buttons.LeftThumbstickRight: return "LS →";
                case Buttons.RightThumbstickUp: return "RS ↑";
                case Buttons.RightThumbstickDown: return "RS ↓";
                case Buttons.RightThumbstickLeft: return "RS ←";
                case Buttons.RightThumbstickRight: return "RS →";
            }
            return button.ToString();
        }
    }

    /// <summary>
    /// One of a mod's key bindings, shown right on the mod page.
    /// Select it, then press a key or a controller button to bind it.
    /// </summary>
    internal sealed class BindingRow : ZkKeybindRow {
        private readonly string label;
        private readonly ButtonBinding binding;
        private readonly Keys[] defaultKeys;
        private readonly Buttons[] defaultButtons;
        private readonly List<string> caps = new List<string>();

        public BindingRow(string label, ButtonBinding binding, DefaultButtonBindingAttribute defaults) {
            this.label = label;
            this.binding = binding;

            List<Keys> keys = new List<Keys>();
            List<Buttons> buttons = new List<Buttons>();
            if (defaults != null) {
                if (defaults.Key != 0)
                    keys.Add(defaults.Key);
                if (defaults.Keys != null)
                    keys.AddRange(defaults.Keys.Where(k => k != 0));
                if (defaults.Button != 0)
                    buttons.Add(defaults.Button);
                if (defaults.Buttons != null)
                    buttons.AddRange(defaults.Buttons.Where(b => b != 0));
            }
            defaultKeys = keys.Distinct().ToArray();
            defaultButtons = buttons.Distinct().ToArray();
        }

        private Binding Target => binding.Binding;

        public override string Label => label;

        public override IReadOnlyList<string> Caps {
            get {
                caps.Clear();
                Binding target = Target;
                if (target == null)
                    return caps;
                if (target.Keyboard != null) {
                    foreach (Keys key in target.Keyboard)
                        caps.Add(KeyNames.Of(key));
                }
                if (target.Controller != null) {
                    foreach (Buttons button in target.Controller)
                        caps.Add(KeyNames.Of(button));
                }
                return caps;
            }
        }

        public override bool HasDefault => true;

        public override bool Changed {
            get {
                Binding target = Target;
                if (target == null)
                    return false;
                return !Same(target.Keyboard, defaultKeys) || !Same(target.Controller, defaultButtons);
            }
        }

        private static bool Same<T>(List<T> current, T[] wanted) {
            if (current == null)
                return wanted.Length == 0;
            if (current.Count != wanted.Length)
                return false;
            foreach (T value in wanted) {
                if (!current.Contains(value))
                    return false;
            }
            return true;
        }

        public override bool Reset() {
            Binding target = Target;
            if (target == null || !Changed)
                return false;
            target.Keyboard?.Clear();
            target.Controller?.Clear();
            if (defaultKeys.Length > 0)
                target.Add(defaultKeys);
            if (defaultButtons.Length > 0)
                target.Add(defaultButtons);
            Audio.Play(SFX.ui_main_button_toggle_off);
            return true;
        }

        /// <summary>Bind a key, replacing the keys bound before. Returns false when the game refuses the key.</summary>
        public bool Bind(Keys key) {
            Binding target = Target;
            if (target == null)
                return false;
            List<Keys> before = target.Keyboard != null ? new List<Keys>(target.Keyboard) : new List<Keys>();
            target.Keyboard?.Clear();
            if (target.Add(key) || (target.Keyboard != null && target.Keyboard.Contains(key)))
                return true;
            // Refused (the key is reserved for something that must stay separate): put the old keys back.
            target.Keyboard?.Clear();
            if (before.Count > 0)
                target.Add(before.ToArray());
            return false;
        }

        /// <summary>Bind a controller button, replacing the buttons bound before.</summary>
        public bool Bind(Buttons button) {
            Binding target = Target;
            if (target == null)
                return false;
            List<Buttons> before = target.Controller != null ? new List<Buttons>(target.Controller) : new List<Buttons>();
            target.Controller?.Clear();
            if (target.Add(button) || (target.Controller != null && target.Controller.Contains(button)))
                return true;
            target.Controller?.Clear();
            if (before.Count > 0)
                target.Add(before.ToArray());
            return false;
        }

        /// <summary>Remove every key and button.</summary>
        public void Unbind() {
            Binding target = Target;
            if (target == null)
                return;
            target.Keyboard?.Clear();
            target.Controller?.Clear();
        }

        /// <summary>
        /// The key bindings found in a mod's settings, named the way Everest's own key config screen names them.
        /// </summary>
        public static List<BindingRow> Find(EverestModule module) {
            List<BindingRow> rows = new List<BindingRow>();
            Type type = module.SettingsType;
            object settings = module._Settings;
            if (type == null || settings == null)
                return rows;

            string typeName = type.Name.ToLowerInvariant();
            if (typeName.EndsWith("settings"))
                typeName = typeName.Substring(0, typeName.Length - 8);
            string prefix = "modoptions_" + typeName + "_";

            foreach (PropertyInfo prop in type.GetProperties()) {
                try {
                    SettingInGameAttribute inGame = prop.GetCustomAttribute<SettingInGameAttribute>();
                    if (inGame != null && !inGame.InGame)
                        continue;
                    if (prop.GetCustomAttribute<SettingIgnoreAttribute>() != null)
                        continue;
                    if (!prop.CanRead || !prop.CanWrite || prop.GetIndexParameters().Length > 0)
                        continue;
                    if (!typeof(ButtonBinding).IsAssignableFrom(prop.PropertyType))
                        continue;
                    if (!(prop.GetValue(settings) is ButtonBinding binding) || binding.Binding == null)
                        continue;

                    string name = prop.GetCustomAttribute<SettingNameAttribute>()?.Name ?? prefix + prop.Name.ToLowerInvariant();
                    name = name.DialogCleanOrNull()
                        ?? (prop.Name.ToLowerInvariant().StartsWith("button") && prop.Name.Length > 6 ? prop.Name.Substring(6) : prop.Name).SpacedPascalCase();

                    rows.Add(new BindingRow(name, binding, prop.GetCustomAttribute<DefaultButtonBindingAttribute>()));
                } catch (Exception e) {
                    Logger.Log(LogLevel.Warn, ZKModMenuModule.LogTag, "Skipped key binding " + prop.Name + " of " + type.Name + ": " + e.Message);
                }
            }
            return rows;
        }
    }
}
