using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Celeste;
using Celeste.Mod;
using ZKModMenu.Core;

namespace ZKModMenu {
    /// <summary>
    /// Base of the rows that wrap one of a mod's real menu items.
    /// The item keeps doing its own work (changing the setting, playing its sound);
    /// ZKMM only presses it and reads it back.
    /// </summary>
    internal abstract class ItemRow : ZkRow {
        public readonly TextMenu.Item Item;
        private readonly Func<bool> shown;
        private readonly int indent;
        private string description;

        protected ItemRow(TextMenu.Item item, int indent, Func<bool> shown) {
            Item = item;
            this.indent = indent;
            this.shown = shown;
        }

        public override bool Visible => Item.Visible && (shown == null || shown());
        public override bool Disabled => Item.Disabled;
        public override int Indent => indent;
        public override string Description => description;

        public void AddDescription(string text) {
            if (string.IsNullOrWhiteSpace(text))
                return;
            text = ItemRows.OneLine(text);
            description = string.IsNullOrEmpty(description) ? text : description + " " + text;
        }

        /// <summary>Learn the default value from the matching item of a menu built with default settings.</summary>
        public virtual void LearnDefault(TextMenu.Item fromDefaults) {
        }

        public override void Left() {
            if (!Item.Disabled)
                ItemRows.Guard(Item.LeftPressed);
        }

        public override void Right() {
            if (!Item.Disabled)
                ItemRows.Guard(Item.RightPressed);
        }

        public override void Confirm() {
            if (Item.Disabled)
                return;
            ItemRows.Guard(() => {
                Item.ConfirmPressed();
                Item.OnPressed?.Invoke();
            });
        }

        public override void Selected() {
            ItemRows.Guard(() => {
                // Some mods ask their menu which item is selected.
                TextMenu container = Item.Container;
                int index = container != null ? container.Items.IndexOf(Item) : -1;
                if (index >= 0)
                    container.Selection = index;
                Item.OnEnter?.Invoke();
            });
        }

        public override void Deselected() {
            ItemRows.Guard(() => Item.OnLeave?.Invoke());
        }
    }

    /// <summary>A header or a line of text that follows a real menu item.</summary>
    internal sealed class ItemTextRow : ZkRow {
        private readonly ZkRowKind kind;
        private readonly TextMenu.Item item;
        private readonly Func<string> text;
        private readonly Func<bool> shown;
        private readonly int indent;

        public ItemTextRow(ZkRowKind kind, TextMenu.Item item, Func<string> text, int indent, Func<bool> shown) {
            this.kind = kind;
            this.item = item;
            this.text = text;
            this.indent = indent;
            this.shown = shown;
        }

        public override ZkRowKind Kind => kind;
        public override string Label => ItemRows.OneLine(text() ?? "");
        public override int Indent => indent;
        public override bool Visible => (item == null || item.Visible) && (shown == null || shown());
    }

    /// <summary>On/off switches, sliders and choices. All of them are a TextMenu.Option underneath.</summary>
    internal sealed class OptionRow<T> : ItemRow {
        private readonly TextMenu.Option<T> option;
        private readonly ZkRowKind kind;
        private int defaultIndex = -1;

        public OptionRow(TextMenu.Option<T> option, int indent, Func<bool> shown) : base(option, indent, shown) {
            this.option = option;
            kind = Classify(option);
        }

        private static ZkRowKind Classify(TextMenu.Option<T> option) {
            int count = option.Values?.Count ?? 0;
            if (count == 2 && typeof(T) == typeof(bool))
                return ZkRowKind.Toggle;
            if (count >= 3) {
                // A run of numbers reads best as a slider.
                bool numeric = true;
                foreach (Tuple<string, T> value in option.Values) {
                    if (!double.TryParse((value.Item1 ?? "").Trim().TrimEnd('%', 'x'), NumberStyles.Float, CultureInfo.InvariantCulture, out _)) {
                        numeric = false;
                        break;
                    }
                }
                if (numeric)
                    return ZkRowKind.Slider;
            }
            return ZkRowKind.Choice;
        }

        private int Count => option.Values?.Count ?? 0;
        private int Index => Math.Max(0, Math.Min(Count - 1, option.Index));

        public override ZkRowKind Kind => kind;
        public override string Label => option.Label ?? "";
        public override bool On => Count > 0 && option.Values[Index].Item2 is bool value && value;
        public override float Fraction => Count > 1 ? Index / (float) (Count - 1) : 0f;
        public override string ValueText => Count > 0 ? option.Values[Index].Item1 ?? "" : "";
        public override bool CanLeft => option.Index > 0;
        public override bool CanRight => option.Index < Count - 1;
        public override bool HasDefault => defaultIndex >= 0 && defaultIndex < Count;
        public override bool Changed => HasDefault && option.Index != defaultIndex;

        public override void LearnDefault(TextMenu.Item fromDefaults) {
            if (fromDefaults is TextMenu.Option<T> other && other.Values != null && other.Values.Count == Count)
                defaultIndex = other.Index;
        }

        public override bool Reset() {
            if (!Changed || option.Disabled)
                return false;
            ItemRows.Guard(() => {
                option.PreviousIndex = option.Index;
                option.Index = defaultIndex;
                option.ValueWiggler?.Start();
                option.OnValueChange?.Invoke(option.Values[defaultIndex].Item2);
            });
            Audio.Play(SFX.ui_main_button_toggle_off);
            return true;
        }
    }

    /// <summary>Everest's number slider for big ranges.</summary>
    internal sealed class IntSliderRow : ItemRow {
        private static readonly FieldInfo MinField = typeof(TextMenuExt.IntSlider).GetField("min", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo MaxField = typeof(TextMenuExt.IntSlider).GetField("max", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly TextMenuExt.IntSlider slider;
        private readonly bool known;
        private readonly int min, max;
        private bool hasDefault;
        private int defaultValue;

        public IntSliderRow(TextMenuExt.IntSlider slider, int indent, Func<bool> shown) : base(slider, indent, shown) {
            this.slider = slider;
            try {
                if (MinField != null && MaxField != null) {
                    min = (int) MinField.GetValue(slider);
                    max = (int) MaxField.GetValue(slider);
                    known = max > min;
                }
            } catch {
                known = false;
            }
        }

        public override ZkRowKind Kind => ZkRowKind.Slider;
        public override string Label => slider.Label ?? "";
        public override float Fraction => known ? Math.Max(0f, Math.Min(1f, (slider.Index - min) / (float) (max - min))) : 0.5f;
        public override string ValueText => slider.Index.ToString(CultureInfo.InvariantCulture);
        public override bool CanLeft => !known || slider.Index > min;
        public override bool CanRight => !known || slider.Index < max;
        public override bool HasDefault => hasDefault;
        public override bool Changed => hasDefault && slider.Index != defaultValue;

        public override void LearnDefault(TextMenu.Item fromDefaults) {
            if (fromDefaults is TextMenuExt.IntSlider other) {
                defaultValue = other.Index;
                hasDefault = !known || (defaultValue >= min && defaultValue <= max);
            }
        }

        // Selecting a number slider with Confirm does nothing useful, so do not flip two-value sliders by surprise.
        public override void Confirm() {
        }

        public override bool Reset() {
            if (!Changed || slider.Disabled)
                return false;
            ItemRows.Guard(() => {
                slider.PreviousIndex = slider.Index;
                slider.Index = defaultValue;
                slider.ValueWiggler?.Start();
                slider.OnValueChange?.Invoke(defaultValue);
            });
            Audio.Play(SFX.ui_main_button_toggle_off);
            return true;
        }
    }

    /// <summary>Everest's "pick one, and it shows its own extra settings" item. Its settings follow as indented rows.</summary>
    internal sealed class OptionSubMenuRow : ItemRow {
        private readonly TextMenuExt.OptionSubMenu menu;
        private int defaultIndex = -1;

        public OptionSubMenuRow(TextMenuExt.OptionSubMenu menu, int indent, Func<bool> shown) : base(menu, indent, shown) {
            this.menu = menu;
        }

        private int Count => menu.Menus?.Count ?? 0;
        private int Index => Math.Max(0, Math.Min(Count - 1, menu.MenuIndex));

        public override ZkRowKind Kind => ZkRowKind.Choice;
        public override string Label => menu.Label ?? "";
        public override string ValueText => Count > 0 ? menu.Menus[Index].Item1 ?? "" : "";
        public override bool CanLeft => menu.MenuIndex > 0;
        public override bool CanRight => menu.MenuIndex < Count - 1;
        public override bool HasDefault => defaultIndex >= 0 && defaultIndex < Count;
        public override bool Changed => HasDefault && menu.MenuIndex != defaultIndex;

        public override void LearnDefault(TextMenu.Item fromDefaults) {
            if (fromDefaults is TextMenuExt.OptionSubMenu other && (other.Menus?.Count ?? 0) == Count)
                defaultIndex = other.MenuIndex;
        }

        // Confirm would hand the controls to the item's own little menu. ZKMM shows those rows itself.
        public override void Confirm() {
        }

        public override bool Reset() {
            if (!Changed || menu.Disabled)
                return false;
            ItemRows.Guard(() => {
                menu.MenuIndex = defaultIndex;
                menu.ValueWiggler?.Start();
                menu.OnValueChange?.Invoke(defaultIndex);
            });
            Audio.Play(SFX.ui_main_button_toggle_off);
            return true;
        }
    }

    /// <summary>A button, or any other item ZKMM has no special look for: it is pressed like a button.</summary>
    internal sealed class ButtonRow : ItemRow {
        private readonly Func<string> label;

        public ButtonRow(TextMenu.Item item, Func<string> label, int indent, Func<bool> shown) : base(item, indent, shown) {
            this.label = label;
        }

        public override ZkRowKind Kind => ZkRowKind.Button;
        public override string Label => ItemRows.OneLine(label() ?? "");
    }

    /// <summary>
    /// A text setting, such as a hex colour. Everest's Mod Options can only edit these from the title screen
    /// (its button is greyed out in a level), so ZKMM lets the player type the new value right on the page.
    /// The value is written to the mod's settings the same way Everest would.
    /// </summary>
    internal sealed class TextSettingRow : ItemRow, IZkTextEdit {
        private readonly TextMenu.Button button;
        private readonly PropertyInfo property;
        private readonly object owner;
        private readonly string name;
        private readonly bool hasDefault;
        private readonly string defaultValue;
        private readonly int minLength, maxLength;
        // Set when the setting holds a colour: its digit count, and how it is written (with "#", in lower case).
        private readonly int colourDigits;
        private readonly bool hash, lower;

        public TextSettingRow(TextMenu.Button button, PropertyInfo property, object owner, string name,
            int minLength, int maxLength, bool hasDefault, string defaultValue, int indent, Func<bool> shown)
            : base(button, indent, shown) {
            this.button = button;
            this.property = property;
            this.owner = owner;
            this.name = name;
            this.hasDefault = hasDefault;
            this.defaultValue = defaultValue ?? "";
            this.minLength = Math.Max(0, minLength);
            this.maxLength = Math.Max(1, maxLength);

            string sample = ZkColor.TryParseHex(Text, out _) ? Text : ZkColor.TryParseHex(this.defaultValue, out _) ? this.defaultValue : null;
            if (sample != null) {
                sample = sample.Trim();
                hash = sample.StartsWith("#");
                string digits = hash ? sample.Substring(1) : sample;
                colourDigits = digits.Length;
                lower = digits.ToLowerInvariant() == digits && digits.ToUpperInvariant() != digits;
                AddDescription(colourDigits == 8
                    ? "A colour, typed as 8 hex digits: red, green, blue and opacity (for example FF8800FF)."
                    : "A colour, typed as 6 hex digits: red, green and blue (for example FF8800).");
            }
        }

        public override ZkRowKind Kind => ZkRowKind.Text;
        public override string Label => name;
        // Everest greys these out in a level only because its text entry screen needs the title screen.
        public override bool Disabled => false;
        public override string ValueText => Text;
        public override bool HasDefault => hasDefault;
        public override bool Changed => hasDefault && Text != defaultValue;

        public string Text {
            get {
                try {
                    return property.GetValue(owner) as string ?? "";
                } catch {
                    return "";
                }
            }
        }

        public int MinLength => minLength;
        public int MaxLength => colourDigits > 0 ? Math.Max(maxLength, colourDigits + 1) : maxLength;

        // The text entry screen is the title screen's; nothing to press here.
        public override void Left() {
        }

        public override void Right() {
        }

        public override void Confirm() {
        }

        public bool TrySet(string value, out string error) {
            error = null;
            value ??= "";
            if (colourDigits > 0) {
                string digits = value.Trim().TrimStart('#');
                if (digits.Length != colourDigits || !ZkColor.TryParseHex(digits, out _)) {
                    error = colourDigits == 8 ? "Type 8 hex digits, like FF8800FF" : "Type 6 hex digits, like FF8800";
                    return false;
                }
                digits = lower ? digits.ToLowerInvariant() : digits.ToUpperInvariant();
                value = (hash ? "#" : "") + digits;
            } else if (value.Length > maxLength) {
                error = "At most " + maxLength + " characters";
                return false;
            }
            return Set(value);
        }

        private bool Set(string value) {
            bool ok = false;
            ItemRows.Guard(() => {
                property.SetValue(owner, value);
                ok = true;
            });
            if (ok)
                button.Label = name + ": " + value;
            return ok;
        }

        public override bool Reset() {
            if (!Changed)
                return false;
            return Set(defaultValue);
        }

        // ---- finding them

        private sealed class Candidate {
            public PropertyInfo Property;
            public object Owner;
            public object FreshOwner;
            public string Name;
            public bool Used;
        }

        private static bool Wanted(PropertyInfo property) {
            if (property.PropertyType != typeof(string) || !property.CanRead || !property.CanWrite)
                return false;
            if (property.GetIndexParameters().Length > 0)
                return false;
            if (property.GetCustomAttribute<SettingIgnoreAttribute>() != null)
                return false;
            // Made for the title screen only: the mod does not want it changed in a level.
            SettingInGameAttribute inGame = property.GetCustomAttribute<SettingInGameAttribute>();
            if (inGame != null && !inGame.InGame)
                return false;
            // Disabled in a level by the mod itself.
            if (property.GetCustomAttribute<SettingInGameDisableAttribute>()?.InGame == true)
                return false;
            return true;
        }

        private static string NameOf(PropertyInfo property, string prefix) {
            string key = property.GetCustomAttribute<SettingNameAttribute>()?.Name ?? prefix + property.Name.ToLowerInvariant();
            return key.DialogCleanOrNull() ?? property.Name.SpacedPascalCase();
        }

        /// <summary>
        /// Work out which of the buttons Everest made for <paramref name="module"/> stand for text settings, the same
        /// way Everest names them ("Name: value"). Returns null when the module has none.
        /// Call after the module filled its menu, so the values match the button labels.
        /// </summary>
        public static Func<TextMenu.Button, int, Func<bool>, ItemRow> Finder(EverestModule module, bool withDefaults) {
            Type type;
            object settings;
            try {
                type = module.SettingsType;
                settings = module._Settings;
            } catch {
                return null;
            }
            if (type == null || settings == null)
                return null;
            SettingInGameAttribute typeInGame = type.GetCustomAttribute<SettingInGameAttribute>();
            if (typeInGame != null && !typeInGame.InGame)
                return null;

            string typeName = type.Name.ToLowerInvariant();
            if (typeName.EndsWith("settings"))
                typeName = typeName.Substring(0, typeName.Length - 8);
            string prefix = "modoptions_" + typeName + "_";

            object fresh = null;
            if (withDefaults) {
                try {
                    fresh = Activator.CreateInstance(type);
                } catch {
                    fresh = null;
                }
            }

            List<Candidate> found = new List<Candidate>();
            foreach (PropertyInfo property in type.GetProperties()) {
                try {
                    // A property with a hand-made entry is the mod's own business.
                    if (type.GetMethod("Create" + property.Name + "Entry", BindingFlags.Public | BindingFlags.Instance, null,
                            new[] { typeof(TextMenu), typeof(bool) }, null) != null)
                        continue;

                    if (Wanted(property)) {
                        found.Add(new Candidate { Property = property, Owner = settings, FreshOwner = fresh, Name = NameOf(property, prefix) });
                        continue;
                    }

                    // Settings grouped in a sub-menu.
                    Type subType = property.PropertyType;
                    if (subType.GetCustomAttribute<SettingSubMenuAttribute>() == null || !property.CanRead)
                        continue;
                    object sub = property.GetValue(settings);
                    if (sub == null)
                        continue;
                    object freshSub = null;
                    if (fresh != null) {
                        try {
                            freshSub = property.GetValue(fresh) ?? Activator.CreateInstance(subType);
                        } catch {
                            freshSub = null;
                        }
                    }
                    foreach (PropertyInfo subProperty in subType.GetProperties()) {
                        if (subType.GetMethod("Create" + subProperty.Name + "Entry", BindingFlags.Public | BindingFlags.Instance) != null)
                            continue;
                        if (Wanted(subProperty))
                            found.Add(new Candidate { Property = subProperty, Owner = sub, FreshOwner = freshSub, Name = NameOf(subProperty, prefix) });
                    }
                } catch (Exception e) {
                    Logger.Log(LogLevel.Verbose, ZKModMenuModule.LogTag, "Skipped a text setting of " + module.Metadata?.Name + ": " + e.Message);
                }
            }
            if (found.Count == 0)
                return null;

            return (button, indent, shown) => {
                string label = button.Label;
                if (label == null)
                    return null;
                foreach (Candidate candidate in found) {
                    if (candidate.Used)
                        continue;
                    string value;
                    try {
                        value = candidate.Property.GetValue(candidate.Owner) as string;
                    } catch {
                        continue;
                    }
                    if (label != candidate.Name + ": " + value)
                        continue;
                    candidate.Used = true;

                    bool hasDefault = false;
                    string defaultValue = null;
                    if (candidate.FreshOwner != null) {
                        try {
                            defaultValue = candidate.Property.GetValue(candidate.FreshOwner) as string;
                            hasDefault = true;
                        } catch {
                            hasDefault = false;
                        }
                    }
                    int max = candidate.Property.GetCustomAttribute<SettingMaxLengthAttribute>()?.Max ?? 12;
                    int min = candidate.Property.GetCustomAttribute<SettingMinLengthAttribute>()?.Min ?? 1;
                    return new TextSettingRow(button, candidate.Property, candidate.Owner, candidate.Name, min, max, hasDefault, defaultValue, indent, shown);
                }
                return null;
            };
        }
    }

    /// <summary>Turns the menu items a mod made for Everest's Mod Options into ZKMM rows.</summary>
    internal static class ItemRows {

        /// <summary>Run a piece of a mod's menu code. One mod's bug must not take the whole menu down.</summary>
        public static void Guard(Action action) {
            try {
                action();
            } catch (Exception e) {
                Logger.Log(LogLevel.Warn, ZKModMenuModule.LogTag, "A mod's setting threw an error:");
                Logger.LogDetailed(e, ZKModMenuModule.LogTag);
            }
        }

        public static string OneLine(string text) {
            if (text == null)
                return "";
            if (text.IndexOf('\n') < 0 && text.IndexOf('\r') < 0)
                return text.Trim();
            return string.Join(" ", text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
        }

        /// <summary>
        /// Build rows for <paramref name="items"/>. <paramref name="defaults"/> is the same list from a menu built
        /// with default settings, or null when defaults are not known.
        /// </summary>
        /// <param name="skipLabels">Buttons with one of these labels are left out.</param>
        /// <param name="special">Gets a say on each button first: a row of its own for it, or null for a plain button row.</param>
        public static void Build(List<ZkRow> rows, IList<TextMenu.Item> items, IList<TextMenu.Item> defaults,
            int indent, Func<bool> shown, bool top, ICollection<string> skipLabels,
            Func<TextMenu.Button, int, Func<bool>, ItemRow> special = null) {

            if (items == null)
                return;
            // Defaults are matched by position, so both menus must have the same shape.
            if (defaults != null) {
                if (defaults.Count != items.Count) {
                    defaults = null;
                } else {
                    for (int i = 0; i < items.Count; i++) {
                        if (items[i] == null || defaults[i] == null || items[i].GetType() != defaults[i].GetType()) {
                            defaults = null;
                            break;
                        }
                    }
                }
            }

            ItemRow last = null;
            bool firstHeader = top;

            for (int i = 0; i < items.Count; i++) {
                TextMenu.Item item = items[i];
                if (item == null)
                    continue;
                TextMenu.Item fromDefaults = defaults?[i];

                // Help text that fades in under the selected item.
                if (item is TextMenuExt.EaseInSubHeaderExt easeIn && !easeIn.FadeVisible) {
                    last?.AddDescription(easeIn.Title);
                    continue;
                }

                if (item is TextMenu.SubHeader subHeader) {
                    string title = subHeader.Title;
                    bool sectionTitle = firstHeader && title != null && title.Contains(" | v.");
                    firstHeader = false;
                    // Everest starts each mod's section with "Name | v.1.2.3"; the page header shows that already.
                    if (sectionTitle || string.IsNullOrWhiteSpace(title))
                        continue;
                    bool longText = title.Length > 56 || title.Contains("\n");
                    rows.Add(new ItemTextRow(longText ? ZkRowKind.Note : ZkRowKind.Header, item, () => subHeader.Title, indent, shown));
                    last = null;
                    continue;
                }
                firstHeader = false;

                if (item is TextMenu.Header header) {
                    if (!string.IsNullOrWhiteSpace(header.Title))
                        rows.Add(new ItemTextRow(ZkRowKind.Header, item, () => header.Title, indent, shown));
                    last = null;
                    continue;
                }

                if (item is TextMenuExt.SubMenu subMenu) {
                    if (!string.IsNullOrWhiteSpace(subMenu.Label))
                        rows.Add(new ItemTextRow(ZkRowKind.Header, item, () => subMenu.Label, indent, shown));
                    Func<bool> childShown = () => subMenu.Visible && (shown == null || shown());
                    Build(rows, subMenu.Items, (fromDefaults as TextMenuExt.SubMenu)?.Items, indent + 1, childShown, false, skipLabels, special);
                    last = null;
                    continue;
                }

                if (item is TextMenuExt.OptionSubMenu optionMenu) {
                    OptionSubMenuRow row = new OptionSubMenuRow(optionMenu, indent, shown);
                    if (fromDefaults != null)
                        row.LearnDefault(fromDefaults);
                    rows.Add(row);
                    TextMenuExt.OptionSubMenu defaultMenu = fromDefaults as TextMenuExt.OptionSubMenu;
                    for (int m = 0; m < (optionMenu.Menus?.Count ?? 0); m++) {
                        int which = m;
                        Func<bool> childShown = () => optionMenu.Visible && optionMenu.MenuIndex == which && (shown == null || shown());
                        IList<TextMenu.Item> childDefaults = defaultMenu?.Menus != null && defaultMenu.Menus.Count == optionMenu.Menus.Count
                            ? defaultMenu.Menus[m].Item2
                            : null;
                        Build(rows, optionMenu.Menus[m].Item2, childDefaults, indent + 1, childShown, false, skipLabels, special);
                    }
                    last = row;
                    continue;
                }

                ItemRow made = null;
                if (item is TextMenuExt.IntSlider intSlider) {
                    made = new IntSliderRow(intSlider, indent, shown);
                } else if (item is TextMenu.Button button) {
                    if (skipLabels != null && button.Label != null && skipLabels.Contains(button.Label))
                        continue;
                    made = special?.Invoke(button, indent, shown);
                    if (made != null) {
                        // Knows its own default.
                        rows.Add(made);
                        last = made;
                        continue;
                    }
                    made = new ButtonRow(item, () => button.Label, indent, shown);
                } else if (item is TextMenu.Setting || item is TextMenuExt.TextBox || item is TextMenuExt.Modal
                    || item is TextMenuExt.SearchToolTip || item is TextMenuExt.HeaderImage || !item.Selectable) {
                    // Parts of other screens; they make no sense as a row.
                    continue;
                } else {
                    made = MakeOptionRow(item, indent, shown);
                    if (made == null) {
                        TextMenu.Item unknown = item;
                        made = new ButtonRow(unknown, () => LabelOf(unknown), indent, shown);
                    }
                }

                if (fromDefaults != null)
                    made.LearnDefault(fromDefaults);
                rows.Add(made);
                last = made;
            }
        }

        // TextMenu.Option<T> is generic, so the matching row type is made at run time.
        private static ItemRow MakeOptionRow(TextMenu.Item item, int indent, Func<bool> shown) {
            for (Type type = item.GetType(); type != null && type != typeof(object); type = type.BaseType) {
                if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(TextMenu.Option<>))
                    continue;
                try {
                    Type rowType = typeof(OptionRow<>).MakeGenericType(type.GetGenericArguments()[0]);
                    return (ItemRow) Activator.CreateInstance(rowType, item, indent, shown);
                } catch (Exception e) {
                    Logger.Log(LogLevel.Warn, ZKModMenuModule.LogTag, "Could not wrap a " + item.GetType().Name + ": " + e.Message);
                    return null;
                }
            }
            return null;
        }

        private static string LabelOf(TextMenu.Item item) {
            try {
                string label = item.SearchLabel();
                if (!string.IsNullOrWhiteSpace(label))
                    return label;
            } catch {
                // Fall through to the type name.
            }
            return item.GetType().Name.SpacedPascalCase();
        }
    }
}
