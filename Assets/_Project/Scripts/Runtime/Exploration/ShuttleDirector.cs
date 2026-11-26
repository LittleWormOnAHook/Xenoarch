using System.Collections;
using System.Collections.Generic;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Excavation;
using DMI.Xenoarch.Galaxy;
using DMI.Xenoarch.Player;
using DMI.Xenoarch.Scenes;
using DMI.Xenoarch.Ship;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DMI.Xenoarch.Exploration
{
    /// <summary>
    /// On a planet scene: lands the shuttle (an instance of <see cref="ShipDefinition.shuttlePrefab"/>) at the
    /// <see cref="LandingZone"/> marker of the chosen dig site, or on auto-picked semi-flat ground near it
    /// (<see cref="LandingZonePicker"/>), and creates the "shuttle" arrival spawn beside it. Runs from sceneLoaded, so the
    /// player is placed there by the scene loader; the loading screen waits on <see cref="BusyStatus"/> until the
    /// terrain has settled and the shuttle is grounded.
    /// </summary>
    public sealed class ShuttleDirector : MonoBehaviour
    {
        public static ShuttleDirector Instance { get; private set; }
        public GameObject Shuttle { get; private set; }
        public string LastPick { get; private set; }
        static bool _busy;
        readonly Collider[] _buf = new Collider[64];

        void Awake() { Instance = this; if (!SceneLoader.ReadyGates.Contains(BusyStatus)) SceneLoader.ReadyGates.Add(BusyStatus); }
        void OnEnable() => SceneManager.sceneLoaded += OnLoaded;
        void OnDisable() => SceneManager.sceneLoaded -= OnLoaded;
        bool _landedOnce; // OnLoaded runs before Start for a loaded scene; Start only covers direct play
        void Start() { if (!_landedOnce) Land(SceneManager.GetActiveScene(), false); }
        void OnLoaded(Scene s, LoadSceneMode m) { if (m == LoadSceneMode.Single) Land(s, true); }

        public static string BusyStatus() => _busy ? "Choosing landing zone" : null;

        void Land(Scene scene, bool arriving)
        {
            Shuttle = null; _busy = false;
            _landedOnce = true;
            var session = XenoarchRuntime.Session;
            if (session == null || !scene.IsValid()) return;
            var x = session.Exploration;
            var body = x.CurrentBody;
            if (body == null || body.sceneName != scene.name) body = x.Galaxy != null ? x.Galaxy.FindBodyByScene(scene.name) : null;
            if (!ShipTravel.UsesShuttle(body)) return;
            Terrain terrain = null;
            foreach (var r in scene.GetRootGameObjects()) { terrain = r.GetComponentInChildren<Terrain>(); if (terrain != null) break; }
            if (terrain == null) return;

            _busy = true;
            var def = session.Config.starterShip;
            string siteId = x.Ship.shuttleSiteId ?? "";
            var site = string.IsNullOrEmpty(siteId) || x.Sites == null ? null : x.Sites.Get(siteId);
            if (site != null && site.sceneName != scene.name) { site = null; siteId = ""; }

            // Shuttle instance (inactive while measured and placed).
            GameObject holder = null; Bounds local = new Bounds(Vector3.zero, Vector3.one * 8f);
            if (def != null && def.shuttlePrefab != null)
            {
                holder = new GameObject("Shuttle (landed)");
                SceneManager.MoveGameObjectToScene(holder, scene);
                holder.SetActive(false);
                var inst = Instantiate(def.shuttlePrefab, holder.transform, false);
                inst.name = def.shuttlePrefab.name;
                // Instance-only fix: the vendor model has convex hull colliders that Unity can't build fully ("partial
                // hull" warnings); a static craft doesn't need convex colliders. The prefab asset is untouched.
                foreach (var mc in inst.GetComponentsInChildren<MeshCollider>(true)) if (mc.convex && mc.GetComponentInParent<Rigidbody>(true) == null) mc.convex = false;
                foreach (var rb in inst.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;
                if (def.shuttleScale > 0.01f) inst.transform.localScale = Vector3.one * def.shuttleScale; // match the bay shuttle
                _gears.Clear();
                if (def.shuttleGearNames != null)
                    foreach (var gn in def.shuttleGearNames)
                    {
                        var g = FindDeep(inst.transform, gn);
                        if (g != null) { g.gameObject.SetActive(true); _gears.Add(g); }
                        else Debug.LogWarning($"[Xenoarch] Shuttle: landing gear '{gn}' not found on {inst.name}");
                    }
                local = LandingZonePicker.LocalBounds(holder.transform);
                _groundOffset = -GearMinY(holder.transform, local);
            }
            else Debug.LogWarning("[Xenoarch] Shuttle: ShipDefinition.shuttlePrefab not set (run Setup 10); placing the arrival point only");

            var rules = def != null ? def.landingRules : LandingZonePicker.Rules.Default;
            if (rules.angles <= 0) rules = LandingZonePicker.Rules.Default;
            float footprint = def != null && def.shuttleFootprint > 0f ? def.shuttleFootprint : Mathf.Max(3f, LandingZonePicker.FootprintOf(local));
            rules.footprint = footprint;
            var height = LandingZonePicker.ForTerrain(terrain);

            var spawnDefault = PlayerSpawnPoint.Find(PlayerSpawnIds.Default);
            Vector3 target = site != null ? site.position : spawnDefault != null ? spawnDefault.transform.position : terrain.transform.position + terrain.terrainData.size * 0.5f;
            Vector3 pos; float yaw; string how;
            var marker = LandingZone.Find(siteId);
            if (marker != null)
            {
                pos = marker.transform.position;
                if (height(pos.x, pos.z, out float gy, out _)) pos.y = gy;
                yaw = marker.transform.eulerAngles.y;
                how = $"marker '{marker.name}'";
            }
            else
            {
                var avoid = Avoid(x, scene.name);
                var rig = PlayerRigLocator.Find();
                var res = LandingZonePicker.Pick(target, avoid, rules, height, (c, rad) => Blocked(c, rad, scene, terrain, holder != null ? holder.transform : null, rig?.Root), def != null ? def.shuttleYawOffset : 0f);
                if (res.found) { pos = res.position; yaw = res.yaw; how = $"auto-pick {res}"; }
                else
                {
                    pos = target + Vector3.right * (footprint + 6f);
                    if (height(pos.x, pos.z, out float gy, out _)) pos.y = gy;
                    yaw = 0f; how = "no flat ground found, fallback beside the target";
                }
            }

            if (holder != null)
            {
                holder.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
                // Rest the landing gear (lowest parts) on the ground.
                GroundOnGear(holder.transform, height);
                holder.AddComponent<ShuttleInteractable>();
                holder.SetActive(true);
                Shuttle = holder;
            }

            // Player arrival beside the shuttle, facing the site.
            Vector3 toTarget = target - pos; toTarget.y = 0f;
            Vector3 dir = toTarget.sqrMagnitude > 1f ? toTarget.normalized : Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            Vector3 sp = marker != null && marker.playerOffset != Vector3.zero ? marker.transform.TransformPoint(marker.playerOffset) : pos + dir * (footprint + 2f);
            if (height(sp.x, sp.z, out float sy, out _)) sp.y = sy + 0.05f;
            var spawnGo = new GameObject("Spawn_shuttle (runtime)");
            SceneManager.MoveGameObjectToScene(spawnGo, scene);
            var face = target - sp; face.y = 0f;
            spawnGo.transform.SetPositionAndRotation(sp, face.sqrMagnitude > 0.01f ? Quaternion.LookRotation(face) : Quaternion.identity);
            spawnGo.AddComponent<PlayerSpawnPoint>().spawnId = ShipTravel.ShuttleSpawn;

            LastPick = $"{scene.name}: shuttle near {(site != null ? site.Id : "base zone")} via {how}, gear {_gears.Count} down{(def != null && def.shuttleScale > 0.01f ? $", scale {def.shuttleScale:0.###}" : "")}";
            Debug.Log($"[Xenoarch] {LastPick}");
            // Direct play in the Editor: nothing teleports the player (they stay where you placed them).
            _scene = scene; _terrain = terrain; _rules = rules; _footprint = footprint; _yawOffset = def != null ? def.shuttleYawOffset : 0f;
            StartCoroutine(Settle(holder, height, arriving));
        }

        readonly List<Transform> _gears = new List<Transform>();
        Scene _scene; Terrain _terrain; LandingZonePicker.Rules _rules; float _footprint, _yawOffset;
        [Tooltip("After the player is placed (landing, save load, Continue, autosave restore), the shuttle is moved next to them if it is farther than this (m).")]
        public float maxPlayerDistance = 40f;
        float _groundOffset;

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++) { var r = FindDeep(t.GetChild(i), name); if (r != null) return r; }
            return null;
        }

        /// <summary>Lowest point of the gear renderers in holder space (whole model if no gear).</summary>
        float GearMinY(Transform holder, Bounds whole)
        {
            float min = float.PositiveInfinity;
            var toLocal = holder.worldToLocalMatrix;
            foreach (var g in _gears)
                foreach (var mf in g.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    var mb = mf.sharedMesh.bounds; var m = toLocal * mf.transform.localToWorldMatrix;
                    for (int i = 0; i < 8; i++)
                        min = Mathf.Min(min, m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))).y);
                }
            return float.IsInfinity(min) ? whole.min.y : min;
        }

        /// <summary>Put the gear feet on the highest ground under any gear (so none sinks in).</summary>
        void GroundOnGear(Transform holder, LandingZonePicker.HeightFn height)
        {
            var p = holder.position; float ground = float.NegativeInfinity;
            if (height(p.x, p.z, out float gy, out _)) ground = gy;
            foreach (var g in _gears) { var gp = g.position; if (height(gp.x, gp.z, out float y, out _)) ground = Mathf.Max(ground, y); }
            if (!float.IsInfinity(ground)) holder.position = new Vector3(p.x, ground + _groundOffset, p.z);
        }

        IEnumerator Settle(GameObject holder, LandingZonePicker.HeightFn height, bool arriving)
        {
            // Wait for the scene loader to place the player (arrivals), then for Digger to finish replaying saved digs
            // (terrain can change); then make sure the shuttle is beside the player and re-ground it once.
            float t = 0f; int startFrame = Time.frameCount;
            yield return null;
            while (arriving && SceneLoader.PlacedFrame < startFrame && t < 5f) { t += Time.unscaledDeltaTime; yield return null; }
            t = 0f;
            while (ExcavationSite.BusyStatus() != null && t < 20f) { t += Time.unscaledDeltaTime; yield return null; }
            if (holder != null) { EnsureNearPlayer(holder, height); GroundOnGear(holder.transform, height); }
            _busy = false;
        }

        /// <summary>The landed shuttle must always be beside the player, whatever brought them here (shuttle descent,
        /// airlock disembark, a saved pose from Load / Continue / autosave). The player is placed by the scene loader
        /// after sceneLoaded, so this runs a frame later: if the shuttle is too far, re-pick flat ground near the player.</summary>
        void EnsureNearPlayer(GameObject holder, LandingZonePicker.HeightFn height)
        {
            var rig = PlayerRigLocator.Find();
            if (rig == null || holder == null) return;
            Vector3 p = rig.Root.position, s = holder.transform.position;
            float d = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(s.x, s.z));
            if (d <= maxPlayerDistance) return;
            var avoid = new List<(Vector3, float)> { (p, 3f + _footprint * 0.5f) }; // never on top of the player
            var x = XenoarchRuntime.Session?.Exploration;
            if (x != null) avoid.AddRange(Avoid(x, _scene.name));
            var res = LandingZonePicker.Pick(p, avoid, _rules, height, (c, rad) => Blocked(c, rad, _scene, _terrain, holder.transform, rig.Root), _yawOffset);
            Vector3 pos; float yaw;
            if (res.found && Vector3.Distance(res.position, p) <= maxPlayerDistance * 1.5f) { pos = res.position; yaw = res.yaw; }
            else
            {
                var fwd = rig.Root.forward; fwd.y = 0f; if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
                pos = p + Quaternion.Euler(0f, 60f, 0f) * fwd.normalized * (_footprint + 4f);
                if (height(pos.x, pos.z, out float gy, out _)) pos.y = gy;
                yaw = rig.Root.eulerAngles.y + 180f;
            }
            holder.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            LastPick += $"; moved next to the player ({d:0} m away)";
            Debug.Log($"[Xenoarch] Shuttle was {d:0} m from the player; landed beside them at {pos}");
        }

        static List<(Vector3, float)> Avoid(ExplorationService x, string sceneName)
        {
            var list = new List<(Vector3, float)>();
            foreach (var s in x.SitesInScene(sceneName)) list.Add((s.position, s.radius * 1.42f));
            return list;
        }

        bool Blocked(Vector3 ground, float radius, Scene scene, Terrain terrain, Transform ignoreA, Transform ignoreB)
        {
            int n = Physics.OverlapBoxNonAlloc(ground + Vector3.up * 2.8f, new Vector3(radius, 2.5f, radius), _buf, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = _buf[i];
                if (c == null || c is TerrainCollider || c.gameObject.scene != scene) continue;
                if (terrain != null && c.transform.IsChildOf(terrain.transform)) continue; // Digger chunks
                if (ignoreA != null && c.transform.IsChildOf(ignoreA)) continue;
                if (ignoreB != null && c.transform.IsChildOf(ignoreB)) continue;
                if (c.GetComponentInParent<ExcavationSite>() != null) continue;
                return true;
            }
            return false;
        }
    }
}
