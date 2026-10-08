using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using ValCraft.Link;

namespace ValCraft
{
    // Valheim's window has focus. While Minecraft drives the player, keyboard and mouse go to
    // Minecraft (through the input ring) and Valheim's own controls see nothing (Patches: ZInput),
    // except a few keys Valheim keeps: Esc (its menu), M (map), G (use: doors, chests, portals,
    // beds, traders) and F7 (hand the controls back to Valheim). All but Esc can be changed in the
    // config ([Controls]); Minecraft's own keys are changed in Minecraft's options (O, then Controls).
    public static class InputBridge
    {
        public static Key UseKey => _use.Key;
        public static Key MapKey => _map.Key;
        public static Key ToggleKey => _toggle.Key;
        public static Key TerrainKey => _terrain.Key;   // block terrain on/off
        public static Key OptionsKey => _options.Key;   // Minecraft's pause/options screen (Esc is Valheim's)
        public static Key RotateKey => _rotate.Key;     // held with the mouse wheel: rotate a build piece (either Alt by default)

        static KeySetting _use = new KeySetting(Key.G), _map = new KeySetting(Key.M), _toggle = new KeySetting(Key.F7),
            _terrain = new KeySetting(Key.F8), _options = new KeySetting(Key.O), _rotate = new KeySetting(Key.LeftAlt);

        // A [Controls] key: a KeyCode setting (mod managers' config editors and ConfigurationManager
        // show a key picker for those), used as the input system's Key. Changes apply right away.
        sealed class KeySetting
        {
            readonly Key _fallback;
            public Key Key;
            public KeySetting(Key fallback) { _fallback = Key = fallback; }

            public KeySetting Bind(BepInEx.Configuration.ConfigFile config, string name, KeyCode fallback, string description)
            {
                var entry = config.Bind("Controls", name, fallback, description);
                void Apply()
                {
                    var key = FromKeyCode(entry.Value);
                    if (key == Key.None)
                    {
                        Plugin.Warn($"[Controls] {name}: {entry.Value} can't be used (a keyboard key is needed), using {_fallback}");
                        key = _fallback;
                    }
                    Key = key;
                }
                Apply();
                entry.SettingChanged += (_, __) => { Apply(); Plugin.Log($"[Controls] {name} is now {Key}"); };
                return this;
            }
        }

        public static void Init(BepInEx.Configuration.ConfigFile config)
        {
            const string note = " Valheim keeps this key while Minecraft drives, so Minecraft won't get it: pick one Minecraft doesn't use. Changes apply right away.";
            _toggle.Bind(config, "ValheimControls", KeyCode.F7, "Hand the controls back to Valheim (and again to return to Minecraft)." + note);
            _terrain.Bind(config, "BlockTerrain", KeyCode.F8, "Block terrain on/off (Valheim's ground as Minecraft blocks)." + note);
            _use.Bind(config, "Use", KeyCode.G, "Valheim's \"use\": doors, chests, portals, beds, traders." + note);
            _options.Bind(config, "MinecraftOptions", KeyCode.O, "Minecraft's options menu (where Minecraft's own keys are changed, under Controls)." + note);
            _map.Bind(config, "Map", KeyCode.M, "Valheim's map. Set it to the same key as Valheim's own map key (Valheim's settings), so Minecraft doesn't get that key too." + note);
            _rotate.Bind(config, "BuildRotate", KeyCode.LeftAlt, "Held with the mouse wheel to rotate a Valheim build piece (hoe, Build Hammer). Either Alt, Ctrl or Shift counts." + note);
        }

        // Unity's old KeyCode (what config editors offer) to the input system's Key. None for mouse buttons and the like.
        public static Key FromKeyCode(KeyCode code)
        {
            if (code >= KeyCode.Alpha0 && code <= KeyCode.Alpha9) return Key.Digit0 + (code - KeyCode.Alpha0);
            if (code >= KeyCode.Keypad0 && code <= KeyCode.Keypad9) return Key.Numpad0 + (code - KeyCode.Keypad0);
            switch (code)
            {
                case KeyCode.None: return Key.None;
                case KeyCode.Return: return Key.Enter;
                case KeyCode.KeypadEnter: return Key.NumpadEnter;
                case KeyCode.KeypadPlus: return Key.NumpadPlus;
                case KeyCode.KeypadMinus: return Key.NumpadMinus;
                case KeyCode.KeypadMultiply: return Key.NumpadMultiply;
                case KeyCode.KeypadDivide: return Key.NumpadDivide;
                case KeyCode.KeypadPeriod: return Key.NumpadPeriod;
                case KeyCode.KeypadEquals: return Key.NumpadEquals;
                case KeyCode.BackQuote: return Key.Backquote;
                case KeyCode.LeftControl: return Key.LeftCtrl;
                case KeyCode.RightControl: return Key.RightCtrl;
                case KeyCode.LeftWindows: case KeyCode.LeftCommand: return Key.LeftMeta;
                case KeyCode.RightWindows: case KeyCode.RightCommand: return Key.RightMeta;
                case KeyCode.Numlock: return Key.NumLock;
                case KeyCode.Print: return Key.PrintScreen;
                case KeyCode.Menu: return Key.ContextMenu;
            }
            return System.Enum.TryParse(code.ToString(), out Key key) ? key : Key.None;
        }

        static bool RotateHeld(Keyboard kb)
        {
            if (RotateKey == Key.LeftAlt || RotateKey == Key.RightAlt) return kb.leftAltKey.isPressed || kb.rightAltKey.isPressed;
            if (RotateKey == Key.LeftCtrl || RotateKey == Key.RightCtrl) return kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;
            if (RotateKey == Key.LeftShift || RotateKey == Key.RightShift) return kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            return kb[RotateKey].isPressed;
        }

        static float _lookDx, _lookDy;
        static readonly bool[] _down = new bool[512];
        static readonly bool[] _buttons = new bool[8];
        static bool _textHooked;
        static readonly Queue<char> _text = new Queue<char>();
        static Vector2Int _lastCursor = new Vector2Int(-1, -1);
        static MethodInfo _interact;

        // Valheim should see no input at all this frame (its own copy of the check, for the ZInput patches).
        public static bool BlockValheim => Puppet.MinecraftOwnsPlayer && !Puppet.ValheimMenuOpen && !Plugin.Paused;
        // Valheim may still see its menu keys (only while no Minecraft screen is up).
        public static bool AllowValheimMenuKeys => !Puppet.McScreenOpen;

        public static void ConsumeLook(out float dx, out float dy)
        {
            dx = _lookDx;
            dy = _lookDy;
            _lookDx = _lookDy = 0f;
        }

        public static void ReleaseAll()
        {
            Shm.PushInput(Proto.InReleaseAll, 0);
            for (int i = 0; i < _down.Length; i++) _down[i] = false;
            for (int i = 0; i < _buttons.Length; i++) _buttons[i] = false;
        }

        // Main thread, every frame (Update), before Puppet.Frame.
        public static void Frame()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (!_textHooked && kb != null)
            {
                kb.onTextInput += c => { if (Route && Puppet.McScreenOpen) _text.Enqueue(c); };
                _textHooked = true;
            }
            if (kb != null && kb[ToggleKey].wasPressedThisFrame && Player.m_localPlayer && !SettingsMenu.IsOpen)
            {
                Plugin.Paused = !Plugin.Paused;
                ReleaseAll();
                Plugin.Message(Plugin.Paused ? $"ValCraft: Valheim controls ({ToggleKey} for Minecraft)" : "ValCraft: Minecraft controls");
            }

            if (kb != null && kb[TerrainKey].wasPressedThisFrame && Player.m_localPlayer && !Puppet.McScreenOpen && !SettingsMenu.IsOpen) BlockTerrain.Toggle();

            if (!Route)
            {
                _text.Clear();
                if (mouse != null) mouse.delta.ReadValue();
                return;
            }

            if (kb != null)
            {
                Key configManagerKey = ConfigManagerCompat.Hotkey;
                foreach (var key in kb.allKeys)
                {
                    if (key == null) continue;
                    bool pressed = key.wasPressedThisFrame, released = key.wasReleasedThisFrame;
                    if (!pressed && !released) continue;
                    var k = key.keyCode;
                    if (k == configManagerKey && configManagerKey != Key.None) continue;  // ConfigurationManager's window key
                    if (!Puppet.McScreenOpen)
                    {
                        // Keys Valheim keeps while no Minecraft screen is open.
                        if (k == Key.Escape || k == MapKey || k == ToggleKey || k == TerrainKey) continue;
                        if (k == UseKey) { if (pressed) UseValheimTarget(); continue; }
                        if (k == OptionsKey) { if (pressed) { ReleaseAll(); Shm.PushInput(Proto.InOpenMenu, 0); } continue; }
                    }
                    int sdl = Sdl(k);
                    if (sdl <= 0) continue;
                    if (pressed && !_down[sdl]) { _down[sdl] = true; Shm.PushInput(Proto.InKey, (ushort)sdl, 1); }
                    else if (released && _down[sdl]) { _down[sdl] = false; Shm.PushInput(Proto.InKey, (ushort)sdl, 0); }
                }
                while (_text.Count > 0) Shm.PushInput(Proto.InText, 0, _text.Dequeue());
            }

            if (mouse != null)
            {
                Vector2 d = mouse.delta.ReadValue();
                if (Puppet.McScreenOpen)
                {
                    // Minecraft screens use the real (unlocked) cursor, in overlay pixels from the top left.
                    Vector2 p = mouse.position.ReadValue();
                    var c = new Vector2Int(Mathf.Clamp((int)p.x, 0, Screen.width - 1), Mathf.Clamp(Screen.height - 1 - (int)p.y, 0, Screen.height - 1));
                    if (c != _lastCursor) { _lastCursor = c; Shm.PushInput(Proto.InCursor, 0, c.x, c.y); }
                }
                else
                {
                    // Minecraft's look is integrated here (Puppet) in raw mouse counts, y down.
                    _lookDx += d.x;
                    _lookDy -= d.y;
                    _lastCursor = new Vector2Int(-1, -1);
                }
                Button(mouse.leftButton, 1);
                Button(mouse.rightButton, 3);
                Button(mouse.middleButton, 2);
                Button(mouse.backButton, 4);
                Button(mouse.forwardButton, 5);
                float scroll = mouse.scroll.ReadValue().y;
                if (scroll != 0f && !BuildRotate)
                {
                    // Windows reports 120 per notch; some setups normalise it to 1.
                    int amount = Mathf.Abs(scroll) >= 30f ? Mathf.RoundToInt(scroll) : Mathf.RoundToInt(Mathf.Sign(scroll) * 120f);
                    Shm.PushInput(Proto.InScroll, 0, amount);
                }
            }
        }

        // Alt + mouse wheel with a build tool out rotates Valheim's piece instead of scrolling the hotbar.
        public static bool BuildRotate
        {
            get
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                return BuildTools.Active && kb != null && RotateHeld(kb);
            }
        }

        static bool Route => Puppet.MinecraftOwnsPlayer && !Puppet.ValheimMenuOpen && !Plugin.Paused && Shm.Valid;

        static void Button(ButtonControl b, int sdl)
        {
            if (b == null) return;
            bool down = b.isPressed;
            if (down == _buttons[sdl]) return;
            _buttons[sdl] = down;
            Shm.PushInput(Proto.InMouseButton, (ushort)sdl, down ? 1 : 0);
        }

        // G on something Valheim can use (door, chest, portal, bed, workbench, trader): use it.
        static void UseValheimTarget()
        {
            var player = Player.m_localPlayer;
            if (!player) return;
            var target = player.GetHoverObject();
            if (!target) { Plugin.Log($"{UseKey}: nothing to use under the crosshair"); return; }
            // Altars, boss item stands and locked doors take their item without the Valheim inventory.
            if (Offerings.TryOffer(player, target)) { Plugin.Log("offered at " + target.name); return; }
            _interact ??= AccessTools.Method(typeof(Player), "Interact", new[] { typeof(GameObject), typeof(bool), typeof(bool) });
            _interact?.Invoke(player, new object[] { target, false, false });
            Plugin.Log("used " + target.name);
        }

        // Unity Input System key -> SDL scancode (USB HID usage), what Minecraft's input takes.
        static int Sdl(Key k)
        {
            if (k >= Key.A && k <= Key.Z) return 4 + (k - Key.A);
            if (k >= Key.Digit1 && k <= Key.Digit9) return 30 + (k - Key.Digit1);
            if (k >= Key.F1 && k <= Key.F12) return 58 + (k - Key.F1);
            if (k >= Key.Numpad1 && k <= Key.Numpad9) return 89 + (k - Key.Numpad1);
            switch (k)
            {
                case Key.Digit0: return 39;
                case Key.Enter: return 40;
                case Key.Escape: return 41;
                case Key.Backspace: return 42;
                case Key.Tab: return 43;
                case Key.Space: return 44;
                case Key.Minus: return 45;
                case Key.Equals: return 46;
                case Key.LeftBracket: return 47;
                case Key.RightBracket: return 48;
                case Key.Backslash: return 49;
                case Key.Semicolon: return 51;
                case Key.Quote: return 52;
                case Key.Backquote: return 53;
                case Key.Comma: return 54;
                case Key.Period: return 55;
                case Key.Slash: return 56;
                case Key.CapsLock: return 57;
                case Key.PrintScreen: return 70;
                case Key.ScrollLock: return 71;
                case Key.Pause: return 72;
                case Key.Insert: return 73;
                case Key.Home: return 74;
                case Key.PageUp: return 75;
                case Key.Delete: return 76;
                case Key.End: return 77;
                case Key.PageDown: return 78;
                case Key.RightArrow: return 79;
                case Key.LeftArrow: return 80;
                case Key.DownArrow: return 81;
                case Key.UpArrow: return 82;
                case Key.NumLock: return 83;
                case Key.NumpadDivide: return 84;
                case Key.NumpadMultiply: return 85;
                case Key.NumpadMinus: return 86;
                case Key.NumpadPlus: return 87;
                case Key.NumpadEnter: return 88;
                case Key.Numpad0: return 98;
                case Key.NumpadPeriod: return 99;
                case Key.OEM1: return 100;
                case Key.ContextMenu: return 101;
                case Key.LeftCtrl: return 224;
                case Key.LeftShift: return 225;
                case Key.LeftAlt: return 226;
                case Key.LeftMeta: return 227;
                case Key.RightCtrl: return 228;
                case Key.RightShift: return 229;
                case Key.RightAlt: return 230;
                case Key.RightMeta: return 231;
                default: return 0;
            }
        }
    }
}
