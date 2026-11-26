using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Exploration;
using DMI.Xenoarch.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DMI.Xenoarch.Journals
{
    /// <summary>
    /// Runtime journal placement for spots the Editor menu cannot bake: aboard the ship (its scene is not edited) and near
    /// a cache (caches are generated at load). Also a fallback for surface scenes where Place Journals has not been run
    /// yet (any entry with no pickup in the scene is spawned at its data spot), so new planets get journals by default.
    /// Collected entries are never spawned.
    /// </summary>
    public sealed class JournalDirector : MonoBehaviour
    {
        void OnEnable() => SceneManager.sceneLoaded += OnLoaded;
        void OnDisable() => SceneManager.sceneLoaded -= OnLoaded;
        void Start() => StartCoroutine(Place(SceneManager.GetActiveScene()));
        void OnLoaded(Scene s, LoadSceneMode m) { if (m == LoadSceneMode.Single) StartCoroutine(Place(s)); }

        IEnumerator Place(Scene scene)
        {
            // After the load settles (caches are generated on load; the player spawn is final).
            yield return null;
            float until = Time.realtimeSinceStartup + 30f;
            while (Scenes.SceneLoader.Busy && Time.realtimeSinceStartup < until) yield return null;
            yield return new WaitForSecondsRealtime(0.5f);
            var session = XenoarchRuntime.Session;
            if (session == null || !scene.IsValid() || !scene.isLoaded) yield break;

            var present = new HashSet<string>();
            foreach (var r in scene.GetRootGameObjects()) foreach (var p in r.GetComponentsInChildren<JournalPickup>(true)) present.Add(p.entryId);
            int made = 0;

            if (scene.name == SceneNames.ShipStarter || scene.name == session.Config.firstGameplayScene)
            {
                var spawn = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PlayerSpawnPoint>(true)).FirstOrDefault(p => p.spawnId == PlayerSpawnIds.Default);
                if (spawn != null)
                {
                    int i = 0;
                    foreach (var e in JournalService.All.Where(x => x.placement == JournalPlacement.Ship))
                    {
                        int slot = i++;
                        if (present.Contains(e.Id) || session.Journals.Has(e.Id)) continue;
                        var t = spawn.transform;
                        var p = t.position + Flat(t.forward) * 1.6f + Flat(t.right) * ((slot - 0.5f) * 0.55f) + Vector3.up * 1.2f;
                        if (Physics.Raycast(p, Vector3.down, out var hit, 3f, ~0, QueryTriggerInteraction.Ignore)) p = hit.point + Vector3.up * 0.02f; else p.y = t.position.y + 0.02f;
                        Spawn(e, p, Quaternion.LookRotation(-Flat(t.forward)), scene); made++;
                    }
                }
            }
            else
            {
                var body = session.Config.galaxy != null ? session.Config.galaxy.FindBodyByScene(scene.name) : null;
                if (body != null)
                {
                    Terrain terrain = null;
                    foreach (var r in scene.GetRootGameObjects()) { terrain = r.GetComponentInChildren<Terrain>(); if (terrain != null) break; }
                    var height = JournalSpots.TerrainHeight(terrain);
                    var sites = session.Exploration.SitesInScene(scene.name).ToList();
                    // Fallback for baked kinds (Place Journals not run on this scene yet).
                    var slots = new Dictionary<string, int>();
                    foreach (var e in JournalSpots.For(body.Id, JournalPlacement.BaseLanding, JournalPlacement.FirstDigSite, JournalPlacement.SiteLanding))
                    {
                        string key = e.placement + ":" + e.siteIndex; slots.TryGetValue(key, out int slot); slots[key] = slot + 1;
                        if (present.Contains(e.Id) || session.Journals.Has(e.Id)) continue;
                        if (JournalSpots.TryPlace(e, slot, scene, sites, height, out var pos, out var rot)) { Spawn(e, pos, rot, scene); made++; }
                    }
                    // Near the cache closest to the base zone.
                    var vd = GetComponent<Valuables.ValuableSiteDirector>();
                    var baseZone = FindObjectsByType<LandingZone>().FirstOrDefault(z => z.gameObject.scene == scene && string.IsNullOrEmpty(z.siteId));
                    if (vd != null && vd.Sites.Count > 0 && baseZone != null)
                    {
                        var bp = baseZone.transform.position;
                        var cache = vd.Sites.OrderBy(c => Flat(c.position - bp).sqrMagnitude).First();
                        int slot = 0;
                        foreach (var e in JournalSpots.For(body.Id, JournalPlacement.NearCache))
                        {
                            int s = slot++;
                            if (present.Contains(e.Id) || session.Journals.Has(e.Id)) continue;
                            var toward = Flat(bp - cache.position).normalized; if (toward.sqrMagnitude < 0.5f) toward = Vector3.forward;
                            var a = cache.position + Quaternion.Euler(0f, s * 30f, 0f) * toward * 3.5f;
                            var h = height(a); if (!h.HasValue) continue;
                            a.y = h.Value + 0.02f;
                            Spawn(e, a, Quaternion.LookRotation(Flat(cache.position - a).sqrMagnitude > 0.01f ? Flat(cache.position - a) : Vector3.forward), scene); made++;
                        }
                    }
                }
            }
            if (made > 0) Debug.Log($"[Xenoarch] Journals: {made} datapad(s) placed at runtime in {scene.name}");
        }

        public static JournalPickup Spawn(JournalEntry e, Vector3 pos, Quaternion rot, Scene scene)
        {
            var go = new GameObject(JournalSpots.ObjectName(e.Id));
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.SetPositionAndRotation(pos, rot);
            go.SetActive(false);
            var p = go.AddComponent<JournalPickup>(); p.entryId = e.Id;
            go.SetActive(true);
            return p;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }
}
