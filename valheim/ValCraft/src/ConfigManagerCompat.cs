using System;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ValCraft
{
    // ConfigurationManager (shudnal's for Valheim, or BepInEx's original), if installed: its window
    // counts as a Valheim menu (the keyboard and mouse stay with Valheim instead of also driving
    // Minecraft), and its hotkey (F1 by default, also Minecraft's hide-HUD) doesn't reach Minecraft.
    // Found by reflection, so nothing is needed when it isn't there.
    public static class ConfigManagerCompat
    {
        static bool _looked;
        static Type _type;
        static FieldInfo _instanceField, _keybindField;
        static PropertyInfo _displaying;
        static UnityEngine.Object _instance;
        static float _nextSearch;

        static void Look()
        {
            if (_looked) return;
            _looked = true;
            _type = AccessTools.TypeByName("ConfigurationManager.ConfigurationManager");
            if (_type == null) return;
            _instanceField = AccessTools.Field(_type, "instance") ?? AccessTools.Field(_type, "Instance");
            _keybindField = AccessTools.Field(_type, "_keybind");
            _displaying = AccessTools.Property(_type, "DisplayingWindow");
            Plugin.Log("ConfigurationManager found: its window gets the keyboard and mouse while open");
        }

        static object Instance
        {
            get
            {
                Look();
                if (_type == null) return null;
                if (_instance) return _instance;
                if (Time.unscaledTime < _nextSearch) return null;
                _nextSearch = Time.unscaledTime + 2f;  // searching the scene is slow: not every frame
                _instance = (_instanceField != null && _instanceField.IsStatic ? _instanceField.GetValue(null) : null) as UnityEngine.Object;
                if (!_instance) _instance = UnityEngine.Object.FindFirstObjectByType(_type);
                return _instance ? _instance : null;
            }
        }

        public static bool WindowOpen
        {
            get
            {
                try
                {
                    var inst = Instance;
                    return inst != null && _displaying != null && (bool)_displaying.GetValue(inst);
                }
                catch (Exception) { return false; }
            }
        }

        // Its show/hide key, as the input system's Key (None if it isn't installed).
        public static Key Hotkey
        {
            get
            {
                try
                {
                    Look();
                    if (_keybindField == null) return Key.None;
                    var entry = (_keybindField.IsStatic ? _keybindField.GetValue(null) : _keybindField.GetValue(Instance)) as ConfigEntry<KeyboardShortcut>;
                    return entry == null ? Key.None : InputBridge.FromKeyCode(entry.Value.MainKey);
                }
                catch (Exception) { return Key.None; }
            }
        }
    }
}
