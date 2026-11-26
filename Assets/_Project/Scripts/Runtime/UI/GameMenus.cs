using System;
using System.Collections.Generic;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Save;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.UI
{
    /// <summary>
    /// Player settings (PlayerPrefs, not part of a save): autosave timer / triggers, master volume, display.
    /// Adapted from ASH ASHGameSettings (static props + Apply).
    /// </summary>
    public static class GameSettings
    {
        const string P = "xenoarch.settings.";
        public static int AutosaveMinutes { get => PlayerPrefs.GetInt(P + "autosaveMinutes", 10); set => PlayerPrefs.SetInt(P + "autosaveMinutes", Mathf.Clamp(value, 0, 60)); }
        public static bool AutosaveOnTravel { get => PlayerPrefs.GetInt(P + "autosaveTravel", 1) == 1; set => PlayerPrefs.SetInt(P + "autosaveTravel", value ? 1 : 0); }
        public static bool AutosaveOnDiscovery { get => PlayerPrefs.GetInt(P + "autosaveDiscovery", 1) == 1; set => PlayerPrefs.SetInt(P + "autosaveDiscovery", value ? 1 : 0); }
        public static float MasterVolume { get => PlayerPrefs.GetFloat(P + "masterVolume", 1f); set => PlayerPrefs.SetFloat(P + "masterVolume", Mathf.Clamp01(value)); }
        public static bool Fullscreen { get => PlayerPrefs.GetInt(P + "fullscreen", Screen.fullScreen ? 1 : 0) == 1; set => PlayerPrefs.SetInt(P + "fullscreen", value ? 1 : 0); }
        public static bool VSync { get => PlayerPrefs.GetInt(P + "vsync", 1) == 1; set => PlayerPrefs.SetInt(P + "vsync", value ? 1 : 0); }

        public static void Apply()
        {
            AudioListener.volume = MasterVolume;
            QualitySettings.vSyncCount = VSync ? 1 : 0;
#if !UNITY_EDITOR
            Screen.fullScreen = Fullscreen;
#endif
            PlayerPrefs.Save();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void ApplyOnBoot() { AudioListener.volume = MasterVolume; QualitySettings.vSyncCount = VSync ? 1 : 0; }
    }

    /// <summary>Save slots shown by the menus: autosave (written only by autosave), quick save (F5) and manual slots.</summary>
    public static class SaveSlots
    {
        public const string Auto = "auto";
        public const int ManualCount = 6;
        public static string Quick => XenoarchRuntime.Session?.Config.quickSaveSlot ?? "quick";
        public static string Manual(int i) => "slot" + i;

        public static IEnumerable<(string slot, string label)> All()
        {
            yield return (Auto, "Autosave");
            yield return (Quick, "Quick save (F5)");
            for (int i = 1; i <= ManualCount; i++) yield return (Manual(i), $"Slot {i}");
        }

        public struct Info { public bool exists; public string place; public long ticks; public float played; }

        public static Info Read(string slot)
        {
            var s = XenoarchRuntime.Session;
            var info = new Info();
            if (s == null || !s.Saves.HasSave(slot)) return info;
            if (!s.Saves.TryRead(slot, out var f, out _)) { info.exists = true; info.place = "(unreadable save)"; return info; }
            info.exists = true; info.ticks = f.savedUtcTicks; info.played = f.playSeconds;
            try
            {
                var loc = JsonUtility.FromJson<PlayerLocationState>(f.Get(GameSession.LocationKey) ?? "{}");
                info.place = Pretty(loc?.sceneName);
            }
            catch { info.place = "?"; }
            return info;
        }

        /// <summary>The most recent save of any slot (Continue), or null.</summary>
        public static string Newest()
        {
            string best = null; long t = 0;
            foreach (var (slot, _) in All()) { var i = Read(slot); if (i.exists && i.ticks >= t) { t = i.ticks; best = slot; } }
            return best;
        }

        public static string Pretty(string scene) => string.IsNullOrEmpty(scene) ? "Unknown" : scene.Replace("_", " ");
        public static string When(long ticks) => ticks <= 0 ? "" : new DateTime(ticks, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        public static string PlayTime(float s) { var t = TimeSpan.FromSeconds(s); return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes:00}m" : $"{t.Minutes}m {t.Seconds:00}s"; }
    }

    /// <summary>
    /// Code-built UI Toolkit menu screens shared by the main menu and the pause menu, following ASH's menu flow
    /// (ASHMainMenu / ASHSaveSlotsPanel / ASHSettingsPanel) in the Xenoarch palette: a button column, a slot list
    /// (save asks before overwriting; autosave is load-only) and a settings page.
    /// </summary>
    public static class MenuScreens
    {
        public static VisualElement Panel(string title, string subtitle = null)
        {
            var p = new VisualElement(); p.AddToClassList("xa-menu-panel");
            p.style.width = 640;
            var t = new Label(title); t.AddToClassList("xa-title"); t.style.fontSize = 40; p.Add(t);
            if (subtitle != null) { var s = new Label(subtitle); s.AddToClassList("xa-subtitle"); p.Add(s); }
            return p;
        }

        public static Button Button(VisualElement parent, string text, Action click, bool enabled = true)
        {
            var b = new Button(() => click?.Invoke()) { text = text };
            b.AddToClassList("xa-button");
            b.SetEnabled(enabled);
            parent.Add(b);
            return b;
        }

        /// <summary>Menu button with a Kenney glyph before the text (t155u icon style, pause menu).</summary>
        public static Button GlyphButton(VisualElement parent, string text, string glyph, Action click, bool enabled = true)
        {
            var b = new Button(() => click?.Invoke()) { text = "" };
            b.AddToClassList("xa-button");
            XenoarchIcons.EnsureStyles(b);
            var g = XenoarchIcons.Glyph(glyph, "xa-glyph");
            b.Add(g);
            var l = new Label(text) { pickingMode = PickingMode.Ignore }; l.AddToClassList("xa-button__label"); b.Add(l);
            b.SetEnabled(enabled);
            parent.Add(b);
            return b;
        }

        public static Label Note(VisualElement parent, string text)
        {
            var l = new Label(text); l.AddToClassList("xa-hint"); l.style.whiteSpace = WhiteSpace.Normal; l.style.marginTop = 4; l.style.marginBottom = 4;
            parent.Add(l); return l;
        }

        /// <summary>Save / load slot list. <paramref name="onDone"/> runs after a load starts (to close the menu).</summary>
        public static VisualElement Slots(bool saveMode, Action back, Action onDone)
        {
            var p = Panel(saveMode ? "SAVE GAME" : "LOAD GAME");
            var list = new ScrollView(); list.style.maxHeight = 520; p.Add(list);
            void Rebuild()
            {
                list.Clear();
                foreach (var (slot, label) in SaveSlots.All())
                {
                    var info = SaveSlots.Read(slot);
                    bool auto = slot == SaveSlots.Auto;
                    string line = info.exists ? $"{label}  -  {info.place}   ({SaveSlots.When(info.ticks)}, played {SaveSlots.PlayTime(info.played)})" : $"{label}  -  empty";
                    bool ok = saveMode ? !auto : info.exists;
                    var b = Button(list, line, () =>
                    {
                        if (saveMode)
                        {
                            void DoSave() { XenoarchRuntime.Instance?.SaveGame(slot); Toast.Show($"Saved to {label}.", 2.5f); Rebuild(); }
                            if (info.exists) Confirm(p, $"Overwrite {label}?", DoSave); else DoSave();
                        }
                        else if (XenoarchRuntime.Instance != null && XenoarchRuntime.Instance.LoadGame(slot)) onDone?.Invoke();
                    }, ok);
                    b.style.height = 46; b.style.fontSize = 15;
                }
            }
            Rebuild();
            if (saveMode) Note(p, "The autosave slot is written automatically (timer, landing/launch, discoveries; see Settings).");
            Button(p, "Back", back);
            return p;
        }

        public static VisualElement Settings(Action back)
        {
            var p = Panel("SETTINGS");
            var autosave = new SliderInt("Autosave every (min, 0 = off)", 0, 30) { value = GameSettings.AutosaveMinutes, showInputField = true };
            autosave.RegisterValueChangedCallback(e => { GameSettings.AutosaveMinutes = e.newValue; GameSettings.Apply(); });
            p.Add(autosave);
            Toggle(p, "Autosave on landing / launch", GameSettings.AutosaveOnTravel, v => GameSettings.AutosaveOnTravel = v);
            Toggle(p, "Autosave on discoveries", GameSettings.AutosaveOnDiscovery, v => GameSettings.AutosaveOnDiscovery = v);
            var vol = new Slider("Master volume", 0f, 1f) { value = GameSettings.MasterVolume };
            vol.RegisterValueChangedCallback(e => { GameSettings.MasterVolume = e.newValue; GameSettings.Apply(); });
            p.Add(vol);
            Toggle(p, "Fullscreen", GameSettings.Fullscreen, v => GameSettings.Fullscreen = v);
            Toggle(p, "VSync", GameSettings.VSync, v => GameSettings.VSync = v);
            foreach (var c in p.Children()) { c.style.color = XenoarchPalette.Text; c.style.marginTop = 6; }
            Button(p, "Back", back);
            return p;
        }

        static void Toggle(VisualElement p, string label, bool value, Action<bool> set)
        {
            var t = new Toggle(label) { value = value };
            t.RegisterValueChangedCallback(e => { set(e.newValue); GameSettings.Apply(); });
            p.Add(t);
        }

        /// <summary>Modal yes/no over <paramref name="host"/>.</summary>
        public static void Confirm(VisualElement host, string question, Action yes)
        {
            var root = host.panel?.visualTree ?? host;
            var veil = new VisualElement();
            XenoarchUI.FillRoot(veil);
            veil.style.backgroundColor = XenoarchPalette.A(XenoarchPalette.PanelDeep, 0.8f);
            veil.style.alignItems = Align.Center; veil.style.justifyContent = Justify.Center;
            var box = Panel(question);
            box.style.width = 520;
            box.Q<Label>().style.fontSize = 22;
            Button(box, "Yes", () => { veil.RemoveFromHierarchy(); yes(); });
            Button(box, "Cancel", () => veil.RemoveFromHierarchy());
            veil.Add(box);
            root.Add(veil);
        }

        public static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
