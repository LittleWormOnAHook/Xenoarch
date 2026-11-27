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

        public struct Info
        {
            public bool exists; public string place; public long ticks; public float played;
            /// <summary>t246u save details (thumbs/&lt;slot&gt;.info, written when saving). Old saves: planet empty, story -1.</summary>
            public string planet, title, focused; public float story;
            public string Title => !string.IsNullOrEmpty(title) ? title : (place ?? "Save");
        }

        /// <summary>t246u: details written next to each save when it is made (screenshot in thumbs/&lt;slot&gt;.jpg).</summary>
        [Serializable]
        public sealed class Meta
        {
            public string planet, location, title, focused;
            public float story = -1f;
            public int credits;
        }

        /// <summary>Folder for save thumbnails and details. A subfolder, so FileSaveStorage.ListSlots (*.json) never sees them.</summary>
        public static string ThumbFolder()
        {
            var st = XenoarchRuntime.Session?.Saves.Storage as FileSaveStorage;
            return st != null ? System.IO.Path.Combine(st.Folder, "thumbs") : null;
        }

        static string Safe(string slot) { foreach (var c in System.IO.Path.GetInvalidFileNameChars()) slot = slot.Replace(c, '_'); return slot; }
        public static string ThumbPath(string slot) { var f = ThumbFolder(); return f == null || string.IsNullOrEmpty(slot) ? null : System.IO.Path.Combine(f, Safe(slot) + ".jpg"); }
        public static string MetaPath(string slot) { var f = ThumbFolder(); return f == null || string.IsNullOrEmpty(slot) ? null : System.IO.Path.Combine(f, Safe(slot) + ".info"); }

        public static Meta ReadMeta(string slot)
        {
            try
            {
                var p = MetaPath(slot);
                if (p != null && System.IO.File.Exists(p)) return JsonUtility.FromJson<Meta>(System.IO.File.ReadAllText(p));
            }
            catch (Exception e) { Debug.LogWarning($"[Xenoarch] Save details for '{slot}' unreadable: {e.Message}"); }
            return null;
        }

        static readonly Dictionary<string, (long stamp, Texture2D tex)> _thumbs = new Dictionary<string, (long, Texture2D)>();

        /// <summary>The slot's screenshot (cached per file time), or null for saves made before thumbnails existed.</summary>
        public static Texture2D Thumb(string slot)
        {
            var p = ThumbPath(slot);
            if (p == null || !System.IO.File.Exists(p)) return null;
            long stamp = System.IO.File.GetLastWriteTimeUtc(p).Ticks;
            if (_thumbs.TryGetValue(p, out var c) && c.stamp == stamp && c.tex != null) return c.tex;
            try
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGB24, false) { name = "SaveThumb_" + slot };
                if (!tex.LoadImage(System.IO.File.ReadAllBytes(p))) { UnityEngine.Object.Destroy(tex); return null; }
                if (c.tex != null) UnityEngine.Object.Destroy(c.tex);
                _thumbs[p] = (stamp, tex);
                return tex;
            }
            catch (Exception e) { Debug.LogWarning($"[Xenoarch] Save thumbnail for '{slot}' unreadable: {e.Message}"); return null; }
        }

        /// <summary>Shows the slot's screenshot on <paramref name="e"/> (cover), or leaves the dark placeholder.</summary>
        public static void ApplyThumb(VisualElement e, string slot)
        {
            if (e == null) return;
            var t = Thumb(slot);
            if (t != null) { e.style.backgroundImage = new StyleBackground(t); }
        }

        /// <summary>Removes a slot's thumbnail and details (New Game clears the dev slots).</summary>
        public static void DeleteThumb(string slot)
        {
            try
            {
                var a = ThumbPath(slot); if (a != null && System.IO.File.Exists(a)) System.IO.File.Delete(a);
                var b = MetaPath(slot); if (b != null && System.IO.File.Exists(b)) System.IO.File.Delete(b);
            }
            catch (Exception e) { Debug.LogWarning($"[Xenoarch] Could not delete save thumbnail '{slot}': {e.Message}"); }
        }

        public static Info Read(string slot)
        {
            var s = XenoarchRuntime.Session;
            var info = new Info { story = -1f };
            if (s == null || !s.Saves.HasSave(slot)) return info;
            if (!s.Saves.TryRead(slot, out var f, out _)) { info.exists = true; info.place = "(unreadable save)"; return info; }
            info.exists = true; info.ticks = f.savedUtcTicks; info.played = f.playSeconds;
            try
            {
                var loc = JsonUtility.FromJson<PlayerLocationState>(f.Get(GameSession.LocationKey) ?? "{}");
                info.place = Pretty(loc?.sceneName);
            }
            catch { info.place = "?"; }
            var m = ReadMeta(slot);
            if (m != null)
            {
                if (!string.IsNullOrEmpty(m.location)) info.place = m.location;
                info.planet = m.planet; info.title = m.title; info.focused = m.focused; info.story = m.story;
            }
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
            var p = new VisualElement(); p.AddToClassList("xa-menu-panel"); p.AddToClassList("xa-m-panel");
            p.style.width = 640;
            p.style.paddingTop = 28; p.style.paddingBottom = 28; p.style.paddingLeft = 32; p.style.paddingRight = 32;
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

        /// <summary>Modal yes/no over <paramref name="host"/>: t246u full-width confirm band (question as the title, the
        /// rest of the sentence below).</summary>
        public static void Confirm(VisualElement host, string question, Action yes)
        {
            string title = question, text = null;
            int q = question.IndexOf('?');
            if (q > 0 && q < question.Length - 1) { title = question.Substring(0, q + 1); text = question.Substring(q + 1).Trim(); }
            MUI.ConfirmBand(host, title, text, "Continue", yes, "Back");
        }

        /// <summary>t246u Morpheus menu layout: left column (logo or title + subtitle, then buttons) and a right side
        /// area for a card. Fills its parent.</summary>
        public static VisualElement MenuLayout(bool logo, string title, string subtitle, out VisualElement col, out VisualElement side)
        {
            var root = new VisualElement(); root.AddToClassList("xa-m-menu");
            XenoarchIcons.EnsureStyles(root);
            col = new VisualElement(); col.AddToClassList("xa-m-menu__col"); root.Add(col);
            side = new VisualElement(); side.AddToClassList("xa-m-menu__side"); root.Add(side);
            if (logo)
            {
                var lg = new VisualElement(); lg.AddToClassList("xa-m-logo");
                var mark = new VisualElement(); mark.AddToClassList("xa-m-logo__mark"); lg.Add(mark);
                var tx = new VisualElement();
                MUI.L(tx, title.ToUpperInvariant(), "xa-m-logo__name");
                if (subtitle != null) MUI.L(tx, subtitle.ToUpperInvariant(), "xa-m-logo__tag");
                lg.Add(tx);
                col.Add(lg);
                col.style.width = 900; // the logo is wider than the button column
            }
            else
            {
                MUI.L(col, title.ToUpperInvariant(), "xa-m-menu__title");
                MUI.L(col, (subtitle ?? "").ToUpperInvariant(), "xa-m-menu__sub");
            }
            return root;
        }

        /// <summary>Menu button with a glyph, sized for the left menu column.</summary>
        public static Button ColumnButton(VisualElement col, string text, string glyph, Action click, bool enabled = true, string badge = null)
        {
            var b = GlyphButton(col, text.ToUpperInvariant(), glyph, click, enabled);
            b.style.width = 440;
            if (badge != null) { var sp = new VisualElement(); sp.style.flexGrow = 1; b.Add(sp); MUI.Badge(b, badge, "main").style.marginRight = 14; }
            return b;
        }

        /// <summary>Card describing a save slot (Continue on the main menu, details on the save/load screen).</summary>
        public static VisualElement SaveCard(string slot, string label, string tag)
        {
            var info = SaveSlots.Read(slot);
            var card = new VisualElement(); card.AddToClassList("xa-m-panel"); card.style.width = 560;
            var thumb = new VisualElement(); thumb.AddToClassList("xa-m-thumb"); thumb.style.height = 250; card.Add(thumb);
            SaveSlots.ApplyThumb(thumb, slot);
            if (tag != null) { var b = MUI.Badge(thumb, tag, "side"); b.style.position = Position.Absolute; b.style.left = 16; b.style.top = 14; b.style.backgroundColor = new Color(0.09f, 0.02f, 0.035f, 0.7f); }
            var body = new VisualElement(); body.style.paddingLeft = 20; body.style.paddingRight = 20; body.style.paddingTop = 14; body.style.paddingBottom = 16; card.Add(body);
            MUI.L(body, info.Title.ToUpperInvariant(), "xa-term__title").style.fontSize = 20;
            MUI.L(body, $"{label}  ·  {info.place}" + (string.IsNullOrEmpty(info.planet) ? "" : $"  ·  {info.planet}"), "xa-m-dim");
            if (info.story >= 0f) MUI.BarRow(body, info.story, "story");
            var foot = new VisualElement(); foot.style.flexDirection = FlexDirection.Row; foot.style.justifyContent = Justify.SpaceBetween; foot.style.marginTop = 6;
            MUI.L(foot, "PLAYED " + SaveSlots.PlayTime(info.played), "xa-m-dim");
            MUI.L(foot, SaveSlots.When(info.ticks), "xa-m-dim");
            body.Add(foot);
            return card;
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
