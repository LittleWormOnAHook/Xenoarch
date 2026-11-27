using System;
using System.IO;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Galaxy;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DMI.Xenoarch.UI
{
    /// <summary>
    /// t246u: when a slot is saved, renders the gameplay camera (no UI) to a small JPG and writes the slot's details
    /// (planet, location, story %, focused objective) next to it in &lt;save folder&gt;/thumbs. The save file itself is
    /// unchanged, so older saves still load; they just show no picture. Bootstraps itself onto the runtime object.
    /// </summary>
    public sealed class SaveThumbnails : MonoBehaviour
    {
        const int Width = 480, Height = 270;
        DMI.Xenoarch.Save.SaveService _hooked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoCreate()
        {
            var rt = XenoarchRuntime.Instance;
            if (rt == null || rt.GetComponent<SaveThumbnails>() != null) return;
            rt.gameObject.AddComponent<SaveThumbnails>();
        }

        void Update()
        {
            var saves = XenoarchRuntime.Session?.Saves;
            if (saves == _hooked) return;
            if (_hooked != null) _hooked.Saved -= OnSaved;
            _hooked = saves;
            if (_hooked != null) _hooked.Saved += OnSaved;
        }

        void OnDestroy() { if (_hooked != null) _hooked.Saved -= OnSaved; }

        void OnSaved(string slot)
        {
            try
            {
                var folder = SaveSlots.ThumbFolder();
                if (folder == null) return;
                Directory.CreateDirectory(folder);
                File.WriteAllText(SaveSlots.MetaPath(slot), JsonUtility.ToJson(BuildMeta()));
                var tex = Capture();
                if (tex != null)
                {
                    File.WriteAllBytes(SaveSlots.ThumbPath(slot), tex.EncodeToJPG(82));
                    Destroy(tex);
                }
            }
            catch (Exception e) { Debug.LogWarning($"[Xenoarch] Save thumbnail for '{slot}' failed: {e.Message}"); }
        }

        static SaveSlots.Meta BuildMeta()
        {
            var s = XenoarchRuntime.Session;
            string scene = SceneManager.GetActiveScene().name;
            var m = new SaveSlots.Meta { location = SaveSlots.Pretty(scene) };
            if (s == null) return m;
            var body = s.Exploration?.CurrentBody;
            m.title = body != null ? body.displayName : SaveSlots.Pretty(scene);
            m.planet = ShipTravel.LocationText(s.Exploration);
            m.credits = s.Economy != null ? s.Economy.Credits : 0;
            var main = s.Objectives?.Main();
            if (main != null) m.story = MUI.Progress(main, out _, out _);
            var f = s.Objectives?.Focused();
            if (f != null) m.focused = f.title;
            return m;
        }

        /// <summary>Renders the gameplay camera into a small texture (UI Toolkit overlays are not part of it).</summary>
        static Texture2D Capture()
        {
            var cam = Camera.main;
            if (cam == null || !cam.isActiveAndEnabled)
                foreach (var c in Camera.allCameras) if (c.isActiveAndEnabled && c.targetTexture == null) { cam = c; break; }
            if (cam == null) return null;

            var rt = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prevTarget = cam.targetTexture; var prevActive = RenderTexture.active;
            try
            {
                var req = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
                else { cam.targetTexture = rt; cam.Render(); }
                RenderTexture.active = rt;
                var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                tex.Apply(false);
                return tex;
            }
            finally
            {
                cam.targetTexture = prevTarget;
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
            }
        }
    }
}
