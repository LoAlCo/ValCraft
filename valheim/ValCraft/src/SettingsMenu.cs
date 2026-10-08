using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using ValCraft.Link;

namespace ValCraft
{
    // "ValCraft settings" in Valheim's pause menu (under Valheim's own Settings): every ValCraft
    // setting from its config, by section, changed in place (on/off, - and +, a choice, a key, text), plus
    // a button to Minecraft's own options screen. Saved straight to the config like any other change.
    public static class SettingsMenu
    {
        static GameObject _panel;
        static Menu _menu;
        static Transform _content;
        static TMP_Text _textTemplate;
        static readonly List<Action> _refresh = new List<Action>();
        static ConfigEntry<KeyCode> _capturing;
        static Action _afterCapture;

        public static bool IsOpen => _panel && _panel.activeSelf;

        // ---- the button in the pause menu -----------------------------------------------------
        [HarmonyPatch(typeof(Menu), "Start")]
        static class MenuStart
        {
            static void Postfix(Menu __instance)
            {
                try { AddButton(__instance); }
                catch (Exception e) { Plugin.Warn($"settings menu: {e}"); }
            }
        }

        // While the panel is open Esc closes it (back to the pause menu), not the whole menu.
        [HarmonyPatch(typeof(Menu), "Update")]
        static class MenuUpdate
        {
            static bool Prefix() => !IsOpen;
        }

        static void AddButton(Menu menu)
        {
            var src = menu.m_settingsButton;
            if (!src) return;
            var go = UnityEngine.Object.Instantiate(src.gameObject, src.transform.parent);
            go.name = "ValCraftSettings";
            go.transform.SetSiblingIndex(src.transform.GetSiblingIndex() + 1);
            var button = go.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => Open(menu));
            SetText(go, "ValCraft settings");
            _textTemplate = src.GetComponentInChildren<TMP_Text>(true);
        }

        static void SetText(GameObject go, string text)
        {
            foreach (var t in go.GetComponentsInChildren<TMP_Text>(true)) t.text = text;
        }

        // ---- the panel ------------------------------------------------------------------------
        static void Open(Menu menu)
        {
            if (!_panel) Build(menu);
            _menu = menu;
            foreach (var r in _refresh) r();
            if (menu.m_menuDialog) menu.m_menuDialog.gameObject.SetActive(false);  // the panel takes its place
            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling();
        }

        public static void Close()
        {
            _capturing = null;
            if (!IsOpen) return;
            _panel.SetActive(false);
            if (_menu && _menu.m_menuDialog && Menu.IsVisible()) _menu.m_menuDialog.gameObject.SetActive(true);
        }

        static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static TMP_Text Text(Transform parent, string text, float size, Color colour, TextAlignmentOptions align = TextAlignmentOptions.Left)
        {
            var rt = Rect("Text", parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (_textTemplate)
            {
                t.font = _textTemplate.font;
                t.fontSharedMaterial = _textTemplate.fontSharedMaterial;
            }
            t.text = text;
            t.fontSize = size;
            t.color = colour;
            t.alignment = align;
            t.enableWordWrapping = true;
            t.raycastTarget = false;
            return t;
        }

        // A plain button of our own (Valheim's menu entries carry hover arrows placed for a wide
        // entry): a dark box that lights up under the mouse, a centred label.
        static Button SmallButton(Transform parent, string text, float width, Action onClick, float fontSize = 23)
        {
            var rt = Rect("Button", parent);
            var box = rt.gameObject.AddComponent<Image>();
            box.color = Color.white;
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = box;
            var colours = b.colors;
            colours.normalColor = new Color(0.32f, 0.24f, 0.16f, 0.75f);
            colours.highlightedColor = new Color(0.62f, 0.42f, 0.2f, 0.9f);
            colours.pressedColor = new Color(0.8f, 0.55f, 0.25f, 1f);
            colours.selectedColor = colours.normalColor;
            colours.disabledColor = new Color(0.2f, 0.18f, 0.16f, 0.5f);
            colours.fadeDuration = 0.05f;
            b.colors = colours;
            b.onClick.AddListener(() => onClick());
            var colour = _textTemplate ? _textTemplate.color : Color.white;
            var label = Text(rt, text, fontSize, colour, TextAlignmentOptions.Center);
            label.name = Label;
            label.enableWordWrapping = false;
            var lr = label.rectTransform;
            lr.anchorMin = Vector2.zero;
            lr.anchorMax = Vector2.one;
            lr.offsetMin = lr.offsetMax = Vector2.zero;
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = le.minWidth = width;
            le.preferredHeight = le.minHeight = 40;
            le.flexibleWidth = 0;
            return b;
        }

        static void Build(Menu menu)
        {
            var root = menu.m_root ? menu.m_root : menu.transform;
            var panel = Rect("ValCraftSettingsPanel", root);
            panel.anchorMin = Vector2.zero;
            panel.anchorMax = Vector2.one;
            panel.offsetMin = panel.offsetMax = Vector2.zero;
            panel.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);   // dims the menu behind
            panel.gameObject.AddComponent<SettingsUpdater>();
            _panel = panel.gameObject;

            var window = Rect("Window", panel);
            window.anchorMin = window.anchorMax = new Vector2(0.5f, 0.5f);
            window.sizeDelta = new Vector2(980, 760);
            window.gameObject.AddComponent<Image>().color = new Color(0.09f, 0.08f, 0.07f, 0.96f);
            var frame = Rect("Frame", window);                                     // a thin gold border
            frame.anchorMin = Vector2.zero;
            frame.anchorMax = Vector2.one;
            frame.offsetMin = new Vector2(4, 4);
            frame.offsetMax = new Vector2(-4, -4);
            var outline = frame.gameObject.AddComponent<Outline>();
            frame.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0);
            outline.effectColor = new Color(0.8f, 0.55f, 0.2f, 0.8f);
            outline.effectDistance = new Vector2(2, 2);

            var title = Text(window, "ValCraft settings", 34, new Color(1f, 0.82f, 0.45f), TextAlignmentOptions.Center);
            var trt = title.rectTransform;
            trt.anchorMin = new Vector2(0, 1);
            trt.anchorMax = new Vector2(1, 1);
            trt.pivot = new Vector2(0.5f, 1);
            trt.sizeDelta = new Vector2(0, 56);
            trt.anchoredPosition = new Vector2(0, -14);

            // the list, scrolling
            var view = Rect("Viewport", window);
            view.anchorMin = new Vector2(0, 0);
            view.anchorMax = new Vector2(1, 1);
            view.offsetMin = new Vector2(30, 80);
            view.offsetMax = new Vector2(-30 - ScrollBarWidth - 8, -76);
            view.gameObject.AddComponent<RectMask2D>();
            view.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.25f);
            var content = Rect("Content", view);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.sizeDelta = Vector2.zero;
            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(16, 16, 10, 10);
            vlg.spacing = 6;
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = view;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.scrollSensitivity = 120;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.verticalScrollbar = ScrollBar(window);
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            _content = content;

            // the bottom row: Minecraft's own options, and back
            var bottom = Rect("Bottom", window);
            bottom.anchorMin = new Vector2(0, 0);
            bottom.anchorMax = new Vector2(1, 0);
            bottom.pivot = new Vector2(0.5f, 0);
            bottom.sizeDelta = new Vector2(0, 60);
            bottom.anchoredPosition = new Vector2(0, 14);
            var hlg = bottom.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.spacing = 24;
            hlg.childControlWidth = hlg.childControlHeight = true;
            hlg.childForceExpandWidth = hlg.childForceExpandHeight = false;
            var mc = SmallButton(bottom, "Minecraft options", 280, OpenMinecraftOptions);
            _refresh.Add(() => mc.interactable = Puppet.Puppeting);
            SmallButton(bottom, "Back", 200, Close);

            Populate();
        }

        const float ScrollBarWidth = 16;

        // The bar right of the list: a dark track, a gold handle to drag.
        static Scrollbar ScrollBar(Transform window)
        {
            var track = Rect("ScrollBar", window);
            track.anchorMin = new Vector2(1, 0);
            track.anchorMax = new Vector2(1, 1);
            track.pivot = new Vector2(1, 0.5f);
            track.offsetMin = new Vector2(-30 - ScrollBarWidth, 80);
            track.offsetMax = new Vector2(-30, -76);
            track.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);
            var area = Rect("Sliding Area", track);
            area.anchorMin = Vector2.zero;
            area.anchorMax = Vector2.one;
            area.offsetMin = area.offsetMax = Vector2.zero;
            var handle = Rect("Handle", area);
            handle.offsetMin = handle.offsetMax = Vector2.zero;
            var image = handle.gameObject.AddComponent<Image>();
            image.color = Color.white;
            var bar = track.gameObject.AddComponent<Scrollbar>();
            bar.handleRect = handle;
            bar.targetGraphic = image;
            bar.direction = Scrollbar.Direction.BottomToTop;
            var colours = bar.colors;
            colours.normalColor = new Color(0.8f, 0.55f, 0.2f, 0.7f);
            colours.highlightedColor = new Color(1f, 0.7f, 0.3f, 0.9f);
            colours.pressedColor = new Color(1f, 0.82f, 0.45f, 1f);
            colours.selectedColor = colours.normalColor;
            colours.fadeDuration = 0.05f;
            bar.colors = colours;
            return bar;
        }

        static void OpenMinecraftOptions()
        {
            Close();
            if (Menu.instance) Menu.instance.Hide();
            Shm.PushInput(Proto.InOpenMenu, 0);  // Minecraft's options screen, as its key (O) opens it
        }

        // ---- the settings ---------------------------------------------------------------------
        // Behind "Advanced settings": which Minecraft to start and how (set up for you), and Debug
        // (exports and stats for making ValCraft, not playing it).
        static readonly string[] AdvancedSections = { "Minecraft", "Debug" };
        static bool _showAdvanced;

        static void Populate()
        {
            var config = Plugin.Instance.Config;
            var bySection = new SortedDictionary<string, List<ConfigEntryBase>>(StringComparer.Ordinal);
            foreach (var kv in config)
            {
                var e = kv.Value;
                if (!Supported(e)) continue;
                if (!bySection.TryGetValue(kv.Key.Section, out var list)) bySection[kv.Key.Section] = list = new List<ConfigEntryBase>();
                list.Add(e);
            }
            foreach (var section in bySection)
                if (Array.IndexOf(AdvancedSections, section.Key) < 0) Section(_content, section.Key, section.Value);

            // the advanced ones, folded away under a button
            var row = Rect("AdvancedRow", _content);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            h.padding = new RectOffset(0, 0, 14, 6);
            var advanced = Rect("Advanced", _content);
            var v = advanced.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 6;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            foreach (var name in AdvancedSections)
                if (bySection.TryGetValue(name, out var list)) Section(advanced, name, list);
            Button button = null;
            Action show = () =>
            {
                advanced.gameObject.SetActive(_showAdvanced);
                Show(button, _showAdvanced ? "Hide advanced settings" : "Advanced settings");
            };
            button = SmallButton(row, "", 340, () => { _showAdvanced = !_showAdvanced; show(); });
            _refresh.Add(show);
        }

        static void Section(Transform parent, string name, List<ConfigEntryBase> entries)
        {
            var head = Text(parent, Pretty(name), 30, new Color(1f, 0.82f, 0.45f));
            head.gameObject.AddComponent<LayoutElement>().preferredHeight = 44;
            foreach (var e in entries) Row(parent, e);
        }

        static bool Supported(ConfigEntryBase e)
        {
            var t = e.SettingType;
            if (t == typeof(bool) || t == typeof(int) || t == typeof(float) || t == typeof(KeyCode)) return true;
            return t == typeof(string);
        }

        static string Pretty(string key) => Regex.Replace(key, "(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ");

        static void Row(Transform parent, ConfigEntryBase e)
        {
            var row = Rect("Row", parent);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 10;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            row.gameObject.AddComponent<LayoutElement>().minHeight = 58;

            // its name, and what it does in small print
            var words = Rect("Words", row);
            var v = words.gameObject.AddComponent<VerticalLayoutGroup>();
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            words.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            Text(words, Pretty(e.Definition.Key), 25, Color.white);
            var desc = e.Description.Description ?? "";
            int dot = desc.IndexOf(". ", StringComparison.Ordinal);
            if (dot > 0) desc = desc.Substring(0, dot + 1);
            Text(words, desc, 17, new Color(0.78f, 0.74f, 0.66f));

            var controls = Rect("Controls", row);
            var ch = controls.gameObject.AddComponent<HorizontalLayoutGroup>();
            ch.spacing = 6;
            ch.childAlignment = TextAnchor.MiddleRight;
            ch.childControlWidth = ch.childControlHeight = true;
            ch.childForceExpandWidth = ch.childForceExpandHeight = false;
            var cle = controls.gameObject.AddComponent<LayoutElement>();
            cle.preferredWidth = cle.minWidth = 300;

            if (e is ConfigEntry<bool> b)
            {
                Button button = null;
                button = SmallButton(controls, "", 300, () => { b.Value = !b.Value; Show(button, b.Value ? "On" : "Off"); });
                _refresh.Add(() => Show(button, b.Value ? "On" : "Off"));
            }
            else if (e is ConfigEntry<int> i)
            {
                var range = e.Description.AcceptableValues as AcceptableValueRange<int>;
                int step = range != null ? Math.Max(1, (range.MaxValue - range.MinValue) / 40) : 1;
                Stepper(controls, () => i.Value.ToString(CultureInfo.InvariantCulture), d =>
                {
                    int n = i.Value + d * step;
                    if (range != null) n = Mathf.Clamp(n, range.MinValue, range.MaxValue);
                    i.Value = n;
                });
            }
            else if (e is ConfigEntry<float> f)
            {
                var range = e.Description.AcceptableValues as AcceptableValueRange<float>;
                float step = range != null ? (range.MaxValue - range.MinValue) / 20f : (Mathf.Abs((float)e.DefaultValue) >= 10f ? 1f : 0.25f);
                Stepper(controls, () => f.Value.ToString("0.##", CultureInfo.InvariantCulture), d =>
                {
                    float n = (float)Math.Round(f.Value + d * step, 3);
                    if (range != null) n = Mathf.Clamp(n, range.MinValue, range.MaxValue);
                    else n = Mathf.Max(0f, n);
                    f.Value = n;
                });
            }
            else if (e is ConfigEntry<string> s && e.Description.AcceptableValues is AcceptableValueList<string> list)
            {
                Button button = null;
                button = SmallButton(controls, "", 300, () =>
                {
                    int at = Array.IndexOf(list.AcceptableValues, s.Value);
                    s.Value = list.AcceptableValues[(at + 1) % list.AcceptableValues.Length];
                    Show(button, s.Value);
                });
                _refresh.Add(() => Show(button, s.Value));
            }
            else if (e is ConfigEntry<string> text)
            {
                var field = TextField(controls, 300);
                field.onEndEdit.AddListener(v => text.Value = v.Trim());
                _refresh.Add(() => field.SetTextWithoutNotify(text.Value));
            }
            else if (e is ConfigEntry<KeyCode> k)
            {
                Button button = null;
                button = SmallButton(controls, "", 300, () =>
                {
                    _capturing = k;
                    _afterCapture = () => Show(button, k.Value.ToString());
                    Show(button, "Press a key...");
                });
                _refresh.Add(() => Show(button, k.Value.ToString()));
            }
        }

        static void Stepper(Transform parent, Func<string> value, Action<int> change)
        {
            TMP_Text label = null;
            SmallButton(parent, "-", 64, () => { change(-1); label.text = value(); }, 34);
            label = Text(parent, value(), 25, Color.white, TextAlignmentOptions.Center);
            var le = label.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = le.minWidth = 148;
            SmallButton(parent, "+", 64, () => { change(1); label.text = value(); }, 34);
            _refresh.Add(() => label.text = value());
        }

        // A text box (a path, a list of names): saved when you press Enter or click away.
        static TMP_InputField TextField(Transform parent, float width)
        {
            var rt = Rect("TextField", parent);
            rt.gameObject.AddComponent<Image>().color = new Color(0.02f, 0.02f, 0.02f, 0.8f);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = le.minWidth = width;
            le.preferredHeight = le.minHeight = 40;
            var area = Rect("Text Area", rt);
            area.anchorMin = Vector2.zero;
            area.anchorMax = Vector2.one;
            area.offsetMin = new Vector2(10, 4);
            area.offsetMax = new Vector2(-10, -4);
            area.gameObject.AddComponent<RectMask2D>();
            var text = Text(area, "", 19, Color.white);
            text.enableWordWrapping = false;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            var tr = text.rectTransform;
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.offsetMin = tr.offsetMax = Vector2.zero;
            var field = rt.gameObject.AddComponent<TMP_InputField>();
            field.textViewport = area;
            field.textComponent = text;
            field.fontAsset = text.font;
            field.pointSize = 19;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.caretColor = Color.white;
            field.customCaretColor = true;
            field.selectionColor = new Color(0.8f, 0.55f, 0.25f, 0.5f);
            return field;
        }

        // True while a text box is being typed in (Esc then leaves the box, not the panel).
        static bool Typing()
        {
            var selected = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
            var field = selected ? selected.GetComponent<TMP_InputField>() : null;
            return field && field.isFocused;
        }

        const string Label = "ValCraftLabel";

        static void Show(Button b, string text)
        {
            var label = b ? b.transform.Find(Label) : null;
            if (label) label.GetComponent<TMP_Text>().text = text;
        }

        // Runs while the panel is open: Esc closes it (or cancels a key being chosen); a key press
        // while choosing a key sets it.
        sealed class SettingsUpdater : MonoBehaviour
        {
            void Update()
            {
                var kb = Keyboard.current;
                if (kb == null) return;
                if (_capturing != null)
                {
                    if (kb.escapeKey.wasPressedThisFrame) { _capturing = null; _afterCapture?.Invoke(); return; }
                    foreach (KeyCode code in Enum.GetValues(typeof(KeyCode)))
                    {
                        var key = InputBridge.FromKeyCode(code);
                        if (key == Key.None || !kb[key].wasPressedThisFrame) continue;
                        _capturing.Value = code;
                        _capturing = null;
                        _afterCapture?.Invoke();
                        return;
                    }
                    return;
                }
                if (!kb.escapeKey.wasPressedThisFrame) return;
                if (Typing()) EventSystem.current.SetSelectedGameObject(null);
                else Close();
            }
        }
    }
}
