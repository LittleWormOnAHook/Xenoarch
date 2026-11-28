using UnityEngine;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.Audio
{
    /// <summary>
    /// Generic UITK hook: one set of bubble callbacks on a panel root, so new buttons, tabs, sliders and
    /// text fields play UI sounds without editing every handler. Safe to call more than once.
    /// </summary>
    public static class UISoundBind
    {
        const string BoundClass = "xa-uisound-bound";

        public static void Attach(VisualElement root)
        {
            if (root == null || root.ClassListContains(BoundClass)) return;
            root.AddToClassList(BoundClass);
            root.RegisterCallback<ClickEvent>(OnClick);
            root.RegisterCallback<PointerEnterEvent>(OnEnter);
            root.RegisterCallback<PointerLeaveEvent>(OnLeave);
            root.RegisterCallback<ChangeEvent<float>>(OnSlider);
            root.RegisterCallback<ChangeEvent<int>>(OnSliderInt);
            root.RegisterCallback<ChangeEvent<bool>>(OnToggle);
            root.RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
        }

        static void OnClick(ClickEvent e)
        {
            if (!IsInteractive(e.target as VisualElement, out var el)) return;
            UISound.Play(ClassifyClick(el));
        }

        static void OnEnter(PointerEnterEvent e)
        {
            if (!IsInteractive(e.target as VisualElement, out var el)) return;
            if (!UISound.AllowHover(el)) return;
            UISound.Play(UISoundEvent.Hover);
        }

        static void OnLeave(PointerLeaveEvent e)
        {
            if (e.target is VisualElement el) UISound.ClearHover(el);
        }

        static void OnSlider(ChangeEvent<float> e)
        {
            if (e.target is Slider || e.target is Scroller) UISound.Play(UISoundEvent.Slider);
        }

        static void OnSliderInt(ChangeEvent<int> e)
        {
            if (e.target is SliderInt || e.target is DropdownField || e.target is RadioButtonGroup)
                UISound.Play(UISoundEvent.Slider);
        }

        static void OnToggle(ChangeEvent<bool> e)
        {
            if (e.target is Toggle || e.target is RadioButton) UISound.Play(UISoundEvent.Click);
        }

        static void OnKey(KeyDownEvent e)
        {
            var el = e.target as VisualElement;
            if (el == null || !IsTextInput(el)) return;
            if (e.character == '\0' && (e.keyCode == KeyCode.None || IsModifierOnly(e.keyCode))) return;
            if (char.IsControl(e.character) && e.character != '\b' && e.character != '\n' && e.character != '\t' && e.character != '\0')
                return;
            if (e.character == '\0' && !IsTypeKey(e.keyCode)) return;
            UISound.Play(UISoundEvent.Typing);
        }

        static bool IsModifierOnly(KeyCode k) =>
            k == KeyCode.LeftShift || k == KeyCode.RightShift || k == KeyCode.LeftControl || k == KeyCode.RightControl ||
            k == KeyCode.LeftAlt || k == KeyCode.RightAlt || k == KeyCode.LeftCommand || k == KeyCode.RightCommand;

        static bool IsTypeKey(KeyCode k) =>
            k == KeyCode.Backspace || k == KeyCode.Delete || k == KeyCode.Return || k == KeyCode.KeypadEnter ||
            k == KeyCode.Space || k == KeyCode.Tab;

        static bool IsTextInput(VisualElement el)
        {
            for (var v = el; v != null; v = v.parent)
            {
                if (v is TextField) return true;
                if (v.ClassListContains("unity-base-text-field") || v.ClassListContains("unity-text-field")) return true;
            }
            return false;
        }

        static bool IsInteractive(VisualElement start, out VisualElement el)
        {
            el = null;
            for (var v = start; v != null; v = v.parent)
            {
                if (v is Button || v is Toggle || v is RadioButton || v is DropdownField)
                {
                    el = v;
                    return true;
                }
                if (v.ClassListContains("xa-term__btn") || v.ClassListContains("xa-m-btn") ||
                    v.ClassListContains("unity-button") || v.ClassListContains("xa-tab"))
                {
                    el = v;
                    return true;
                }
            }
            return false;
        }

        static UISoundEvent ClassifyClick(VisualElement el)
        {
            string t = el is Button b ? b.text : el.name;
            if (string.IsNullOrEmpty(t)) t = "";
            var s = t.ToLowerInvariant();
            if (el.ClassListContains("xa-tab") || s.Contains("tab")) return UISoundEvent.Tab;
            if (LooksLike(s, "cancel", "back", "close", "dismiss")) return UISoundEvent.Cancel;
            if (LooksLike(s, "confirm", "apply", "save", "accept")) return UISoundEvent.Confirm;
            return UISoundEvent.Click;
        }

        static bool LooksLike(string s, params string[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                var key = keys[i];
                if (s == key) return true;
                if (s.StartsWith(key + " ") || s.StartsWith(key + ":") || s.EndsWith(" " + key)) return true;
            }
            return false;
        }
    }
}
