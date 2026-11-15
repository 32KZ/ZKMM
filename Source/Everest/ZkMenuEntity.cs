using System;
using System.Collections.Generic;
using Celeste;
using Celeste.Mod;
using Celeste.Mod.Core;
using Microsoft.Xna.Framework.Input;
using Monocle;
using ZKModMenu.Core;

namespace ZKModMenu {
    /// <summary>
    /// The menu while it is on screen. It lives in the level like the pause menu does:
    /// it updates while the game is paused and draws on the HUD layer.
    /// It turns the game's input into menu actions and gives the menu what it needs from the game.
    /// </summary>
    internal sealed class ZkMenuEntity : Entity, IZkHost {
        private static readonly MonocleCanvas Canvas = new MonocleCanvas();

        // Controller buttons that can be bound to an action.
        private static readonly Buttons[] BindableButtons = {
            Buttons.A, Buttons.B, Buttons.X, Buttons.Y,
            Buttons.LeftShoulder, Buttons.RightShoulder, Buttons.LeftTrigger, Buttons.RightTrigger,
            Buttons.LeftStick, Buttons.RightStick, Buttons.Back
        };

        private static readonly Keys[] WasdKeys = { Keys.W, Keys.S, Keys.A, Keys.D };
        private static readonly ZkAction[] WasdActions = { ZkAction.Up, ZkAction.Down, ZkAction.Left, ZkAction.Right };

        /// <summary>The open menu, if there is one.</summary>
        public static ZkMenuEntity Current { get; private set; }

        private readonly Level level;
        private readonly int returnIndex;
        private readonly bool minimal;
        private readonly ZkMenu menu;
        private readonly List<IZkMod> mods;
        private List<ZkRow> extraRows;

        private bool closed;
        private bool closeRequested;
        private float cooldown = 0.15f; // Ignore the press that opened the menu.
        private bool hudHideChanged;
        private bool oldAllowHudHide;

        private readonly Queue<char> typed = new Queue<char>();
        private bool typingHooked;
        private bool oldCommandsEnabled;
        private bool listenArmed;
        private readonly float[] wasdTimers = new float[4];
        private readonly List<EverestMod> toWarm = new List<EverestMod>();
        private bool firstUpdate = true;

        public ZkMenuEntity(Level level, int returnIndex, bool minimal) {
            this.level = level;
            this.returnIndex = returnIndex;
            this.minimal = minimal;

            Tag = (int) Tags.HUD | (int) Tags.PauseUpdate;
            Depth = -20000;

            ModInfoDatabase.Prepare(ZKModMenuModule.Settings.LookUpModInfoOnline);
            mods = EverestMod.FindAll(ZKModMenuModule.Settings);
            menu = new ZkMenu(this);

            // Settings are counted a few mods per frame, in the order the list shows them.
            IZkOptions options = ZKModMenuModule.Settings;
            foreach (IZkMod mod in mods) {
                if (mod is EverestMod everestMod)
                    toWarm.Add(everestMod);
            }
            toWarm.Sort((a, b) => {
                int byFavourite = options.IsFavourite(a.Id).CompareTo(options.IsFavourite(b.Id));
                return byFavourite != 0 ? byFavourite : string.Compare(b.Name, a.Name, StringComparison.OrdinalIgnoreCase);
            });
        }

        // toWarm is sorted backwards so the next mod can be taken off the end cheaply.
        private void WarmUp(double milliseconds) {
            if (toWarm.Count == 0)
                return;
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            double perMs = System.Diagnostics.Stopwatch.Frequency / 1000.0;
            while (toWarm.Count > 0) {
                EverestMod mod = toWarm[toWarm.Count - 1];
                toWarm.RemoveAt(toWarm.Count - 1);
                mod.Warm();
                if ((System.Diagnostics.Stopwatch.GetTimestamp() - start) / perMs >= milliseconds)
                    break;
            }
        }

        // ------------------------------------------------------------------ entity

        public override void Added(Scene scene) {
            base.Added(scene);
            Current = this;
            // Holding the HUD-hide buttons must not blank the menu.
            oldAllowHudHide = level.AllowHudHide;
            level.AllowHudHide = false;
            hudHideChanged = true;
        }

        public override void Removed(Scene scene) {
            Shutdown();
            base.Removed(scene);
        }

        public override void SceneEnd(Scene scene) {
            Shutdown();
            base.SceneEnd(scene);
        }

        /// <summary>Put everything back the way it was. Safe to call more than once.</summary>
        private void Shutdown() {
            if (closed)
                return;
            closed = true;

            try {
                menu.Shutdown();
            } catch (Exception e) {
                Logger.LogDetailed(e, ZKModMenuModule.LogTag);
            }
            EverestMod.CloseAll();
            UnhookTyping();

            if (hudHideChanged) {
                level.AllowHudHide = oldAllowHudHide;
                hudHideChanged = false;
            }
            if (Current == this)
                Current = null;
        }

        /// <summary>Close the menu without going back to the pause menu.</summary>
        /// <param name="resume">Also resume the game, so the player is not left paused without a menu.</param>
        public void ForceClose(bool resume) {
            Shutdown();
            RemoveSelf();
            if (resume && level.Paused) {
                level.Paused = false;
                Engine.FreezeTimer = 0.15f;
            }
        }

        private void CloseToPauseMenu() {
            Shutdown();
            RemoveSelf();
            ZKModMenuModule.ReturnToPauseMenu(level, returnIndex, minimal);
        }

        private void CloseAndResume() {
            Audio.Play(SFX.ui_main_button_back);
            Shutdown();
            RemoveSelf();
            level.Paused = false;
            Engine.FreezeTimer = 0.15f;
        }

        public override void Update() {
            base.Update();
            if (closed)
                return;

            // Something else resumed the game under us (another mod, a cutscene): get out of the way.
            if (!level.Paused) {
                ForceClose(false);
                return;
            }
            if (closeRequested) {
                CloseToPauseMenu();
                return;
            }

            float dt = Engine.RawDeltaTime;
            WarmUp(firstUpdate ? 40.0 : 5.0);
            firstUpdate = false;
            EverestMod.UpdateOpen();
            // A mod's button replaced the options menu with a screen of its own. Leave it to that screen.
            if (EverestMod.TakenOver) {
                ForceClose(false);
                return;
            }
            menu.Update(dt);

            // A mod is showing a screen of its own. Wait for it, then ignore the press that closed it.
            if (menu.Suspended) {
                cooldown = 0.2f;
                return;
            }
            if (cooldown > 0f) {
                cooldown -= dt;
                DropPresses();
                return;
            }

            if (menu.Typing) {
                UpdateTyping();
                DropPresses();
            } else if (menu.Listening != null) {
                UpdateListening();
                DropPresses();
            } else {
                UpdateInput(dt);
            }

            if (closeRequested && !closed)
                CloseToPauseMenu();
        }

        public override void Render() {
            if (closed)
                return;
            try {
                menu.Render(Canvas);
            } catch (Exception e) {
                // A drawing bug must not leave the player stuck in a broken menu.
                Logger.Log(LogLevel.Error, ZKModMenuModule.LogTag, "Drawing the menu failed:");
                Logger.LogDetailed(e, ZKModMenuModule.LogTag);
                closeRequested = true;
            }
        }

        // ------------------------------------------------------------------ input

        private static bool Has(Binding binding, Keys key) {
            return binding != null && binding.Keyboard != null && binding.Keyboard.Contains(key);
        }

        // W, A, S and D move too, unless the player uses that key for something the menu listens to.
        private static bool WasdFree(Keys key) {
            Settings s = Settings.Instance;
            if (s != null && (Has(s.MenuUp, key) || Has(s.MenuDown, key) || Has(s.MenuLeft, key) || Has(s.MenuRight, key)
                || Has(s.Confirm, key) || Has(s.Cancel, key) || Has(s.Pause, key)))
                return false;
            if (Has(ZKModMenuModule.Settings.OtherActionButton?.Binding, key))
                return false;
            CoreModuleSettings core = CoreModule.Settings;
            if (core != null && (Has(core.MenuSearch?.Binding, key) || Has(core.MenuPageUp?.Binding, key) || Has(core.MenuPageDown?.Binding, key)))
                return false;
            return true;
        }

        private bool Wasd(int index, float dt) {
            Keys key = WasdKeys[index];
            if (!MInput.Keyboard.Check(key) || !WasdFree(key)) {
                wasdTimers[index] = 0f;
                return false;
            }
            if (MInput.Keyboard.Pressed(key)) {
                wasdTimers[index] = 0.4f;
                return true;
            }
            wasdTimers[index] -= dt;
            if (wasdTimers[index] <= 0f) {
                wasdTimers[index] = 0.1f;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Some buttons remember a press for a moment (the game does this so a slightly early press still counts).
        /// Forget those, or a press made while typing or binding a key would act again right afterwards.
        /// </summary>
        private static void DropPresses() {
            Input.Pause.ConsumeBuffer();
            Input.ESC.ConsumeBuffer();
            // Reading these is what makes them forget.
            ButtonBinding other = ZKModMenuModule.Settings.OtherActionButton;
            bool ignored = other != null && other.Pressed;
            CoreModuleSettings core = CoreModule.Settings;
            if (core != null) {
                ignored = core.MenuSearch != null && core.MenuSearch.Pressed;
                ignored = core.MenuPageUp != null && core.MenuPageUp.Pressed;
                ignored = core.MenuPageDown != null && core.MenuPageDown.Pressed;
            }
        }

        private void UpdateInput(float dt) {
            ButtonBinding other = ZKModMenuModule.Settings.OtherActionButton;
            bool alt = other != null && other.Pressed;
            bool enter = MInput.Keyboard.Pressed(Keys.Enter);
            bool escape = Input.ESC.Pressed;
            bool pause = Input.Pause.Pressed;
            // Each press acts once, here. Without this the game would see it again for a few frames.
            Input.Pause.ConsumeBuffer();
            Input.ESC.ConsumeBuffer();

            // The pause button (Start on a controller) leaves the menu and resumes the game.
            // Enter is the pause key on a keyboard, but in here it confirms.
            if (pause && !enter && !escape && !alt) {
                CloseAndResume();
                return;
            }

            // The "other action" key wins over Back: on a keyboard, X is both by default.
            if (alt) {
                menu.Act(ZkAction.Alt);
                return;
            }
            if (Input.MenuConfirm.Pressed || enter) {
                menu.Act(ZkAction.Confirm);
                return;
            }
            if (Input.MenuCancel.Pressed || escape || MInput.Keyboard.Pressed(Keys.Back)) {
                menu.Act(ZkAction.Back);
                return;
            }

            CoreModuleSettings core = CoreModule.Settings;
            if (core?.MenuSearch != null && core.MenuSearch.Pressed) {
                menu.Act(ZkAction.Search);
                return;
            }
            if (core?.MenuPageUp != null && core.MenuPageUp.Pressed) {
                menu.Act(ZkAction.PageUp);
                return;
            }
            if (core?.MenuPageDown != null && core.MenuPageDown.Pressed) {
                menu.Act(ZkAction.PageDown);
                return;
            }

            int wasd = -1;
            for (int i = 0; i < 4; i++) {
                if (Wasd(i, dt) && wasd < 0)
                    wasd = i;
            }

            if (Input.MenuUp.Pressed)
                menu.Act(ZkAction.Up);
            else if (Input.MenuDown.Pressed)
                menu.Act(ZkAction.Down);
            else if (Input.MenuLeft.Pressed)
                menu.Act(ZkAction.Left);
            else if (Input.MenuRight.Pressed)
                menu.Act(ZkAction.Right);
            else if (wasd >= 0)
                menu.Act(WasdActions[wasd]);
        }

        // ---- typing into the search box

        private void OnTextInput(char c) {
            // Arrives outside the normal update, so it is only queued here.
            lock (typed)
                typed.Enqueue(c);
        }

        private void UnhookTyping() {
            if (!typingHooked)
                return;
            typingHooked = false;
            TextInput.OnInput -= OnTextInput;
            Engine.Commands.Enabled = oldCommandsEnabled;
            lock (typed)
                typed.Clear();
        }

        private void UpdateTyping() {
            while (menu.Typing) {
                char c;
                lock (typed) {
                    if (typed.Count == 0)
                        break;
                    c = typed.Dequeue();
                }

                if (c == '\b')
                    menu.TypingKey(ZkTypingKey.Backspace);
                else if (c == '\r' || c == '\n')
                    menu.TypingKey(ZkTypingKey.Enter);
                else if (!char.IsControl(c) && ActiveFont.FontSize.Characters.ContainsKey(c))
                    menu.TypeChar(c);
            }
            if (!menu.Typing)
                return;

            MInput.GamePadData pad = MInput.GamePads[Input.Gamepad];
            if (MInput.Keyboard.Pressed(Keys.Escape) || pad.Pressed(Buttons.B) || pad.Pressed(Buttons.Start))
                menu.TypingKey(ZkTypingKey.Escape);
            else if (MInput.Keyboard.Pressed(Keys.Down) || pad.Pressed(Buttons.DPadDown) || pad.Pressed(Buttons.A))
                menu.TypingKey(ZkTypingKey.Down);
        }

        // ---- waiting for a key or button to bind

        private void StopListening(string message, bool ok) {
            Audio.Play(ok ? SFX.ui_main_button_select : SFX.ui_main_button_back);
            menu.EndListening(message);
            cooldown = 0.2f;
        }

        private void UpdateListening() {
            BindingRow row = menu.Listening as BindingRow;
            if (row == null) {
                menu.EndListening(null);
                return;
            }
            // The press that selected the row is still down on the first frame.
            if (listenArmed) {
                listenArmed = false;
                return;
            }

            foreach (Keys key in MInput.Keyboard.CurrentState.GetPressedKeys()) {
                if (!MInput.Keyboard.Pressed(key))
                    continue;
                if (key == Keys.Escape || key == Keys.Back) {
                    StopListening(null, false);
                } else if (key == Keys.Delete) {
                    row.Unbind();
                    StopListening(row.Label + " is no longer bound", true);
                } else if (row.Bind(key)) {
                    StopListening(row.Label + " bound to " + KeyNames.Of(key), true);
                } else {
                    StopListening(KeyNames.Of(key) + " cannot be used for this", false);
                }
                return;
            }

            MInput.GamePadData pad = MInput.GamePads[Input.Gamepad];
            if (pad.Pressed(Buttons.Start)) {
                StopListening(null, false);
                return;
            }
            foreach (Buttons button in BindableButtons) {
                if (!pad.Pressed(button))
                    continue;
                if (row.Bind(button))
                    StopListening(row.Label + " bound to " + KeyNames.Of(button), true);
                else
                    StopListening(KeyNames.Of(button) + " cannot be used for this", false);
                return;
            }
        }

        // ------------------------------------------------------------------ IZkHost

        public IReadOnlyList<IZkMod> Mods => mods;

        public IZkOptions Options => ZKModMenuModule.Settings;

        public string Version => ZKModMenuModule.VersionText;

        public IReadOnlyList<ZkRow> ExtraSettingsRows {
            get {
                if (extraRows == null) {
                    extraRows = new List<ZkRow>();
                    try {
                        extraRows.AddRange(BindingRow.Find(ZKModMenuModule.Instance));
                    } catch (Exception e) {
                        Logger.LogDetailed(e, ZKModMenuModule.LogTag);
                    }
                }
                return extraRows;
            }
        }

        public void SaveOptions() {
            try {
                ZKModMenuModule.Instance.SaveSettings();
            } catch (Exception e) {
                Logger.LogDetailed(e, ZKModMenuModule.LogTag);
            }
        }

        public void Play(ZkSound sound) {
            switch (sound) {
                case ZkSound.MoveUp:
                    Audio.Play(SFX.ui_main_roll_up);
                    break;
                case ZkSound.MoveDown:
                    Audio.Play(SFX.ui_main_roll_down);
                    break;
                case ZkSound.Select:
                    Audio.Play(SFX.ui_main_button_select);
                    break;
                case ZkSound.Back:
                    Audio.Play(SFX.ui_main_button_back);
                    break;
                case ZkSound.ToggleOn:
                case ZkSound.Favourite:
                    Audio.Play(SFX.ui_main_button_toggle_on);
                    break;
                case ZkSound.ToggleOff:
                    Audio.Play(SFX.ui_main_button_toggle_off);
                    break;
                case ZkSound.Invalid:
                    Audio.Play(SFX.ui_main_button_invalid);
                    break;
                case ZkSound.Type:
                    Audio.Play(SFX.ui_main_rename_entry_char);
                    break;
                case ZkSound.Erase:
                    Audio.Play(SFX.ui_main_rename_entry_backspace);
                    break;
            }
        }

        private static string FirstKey(Binding binding, string fallback) {
            if (binding?.Keyboard != null && binding.Keyboard.Count > 0)
                return KeyNames.Of(binding.Keyboard[0]);
            return fallback;
        }

        private static string FirstButton(Binding binding, string fallback) {
            if (binding?.Controller != null && binding.Controller.Count > 0)
                return KeyNames.Of(binding.Controller[0]);
            return fallback;
        }

        public string Glyph(ZkAction action) {
            bool pad;
            try {
                pad = Input.GuiInputController();
            } catch {
                pad = false;
            }
            Settings s = Settings.Instance;

            switch (action) {
                case ZkAction.Confirm:
                    return pad ? FirstButton(s?.Confirm, "A") : FirstKey(s?.Confirm, "Enter");
                case ZkAction.Alt: {
                    Binding other = ZKModMenuModule.Settings.OtherActionButton?.Binding;
                    return pad ? FirstButton(other, "X") : FirstKey(other, "X");
                }
                case ZkAction.Back:
                    return pad ? FirstButton(s?.Cancel, "B") : "Bksp";
            }
            return "";
        }

        public void BeginTyping() {
            if (typingHooked)
                return;
            typingHooked = true;
            lock (typed)
                typed.Clear();
            // The debug console must not pop open while typing.
            oldCommandsEnabled = Engine.Commands.Enabled;
            Engine.Commands.Enabled = false;
            TextInput.OnInput += OnTextInput;
        }

        public void EndTyping() {
            UnhookTyping();
            cooldown = 0.12f; // Enter and Escape mean other things outside the search box.
        }

        public void BeginListening(ZkKeybindRow row) {
            listenArmed = true;
        }

        public void RequestClose() {
            closeRequested = true;
        }

        public void OptionsChanged() {
            ModInfoDatabase.Prepare(ZKModMenuModule.Settings.LookUpModInfoOnline);
        }
    }
}
