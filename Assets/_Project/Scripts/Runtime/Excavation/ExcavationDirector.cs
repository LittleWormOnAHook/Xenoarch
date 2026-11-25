using System.Collections.Generic;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DMI.Xenoarch.Excavation
{
    /// <summary>
    /// On every scene load: builds an <see cref="ExcavationSite"/> for each archaeological site of that scene (from the
    /// site definitions, nothing placed by hand) and gives the player the <see cref="DigTool"/>.
    /// </summary>
    public sealed class ExcavationDirector : MonoBehaviour
    {
        void OnEnable() => SceneManager.sceneLoaded += OnLoaded;
        void OnDisable() => SceneManager.sceneLoaded -= OnLoaded;
        void Start() => Build(SceneManager.GetActiveScene());
        void OnLoaded(Scene s, LoadSceneMode m) { if (m == LoadSceneMode.Single || s == SceneManager.GetActiveScene()) Build(s); }

        void Build(Scene scene)
        {
            var session = XenoarchRuntime.Session;
            if (session == null || !scene.IsValid()) return;
            var existing = new HashSet<string>();
            foreach (var s in ExcavationSite.Active) if (s != null && s.gameObject.scene == scene) existing.Add(s.Definition.Id);
            int made = 0;
            foreach (var def in session.Exploration.SitesInScene(scene.name))
            {
                if (def == null || existing.Contains(def.Id)) continue;
                ExcavationSite.Create(def, SurfaceY(def.position), scene);
                made++;
            }
            var rig = PlayerRigLocator.Find();
            if (rig != null && made > 0 && rig.Root.GetComponent<DigTool>() == null) rig.Root.gameObject.AddComponent<DigTool>();
        }

        static float SurfaceY(Vector3 p) => ExcavationSite.TerrainHeight(p) ?? p.y;
    }
}
