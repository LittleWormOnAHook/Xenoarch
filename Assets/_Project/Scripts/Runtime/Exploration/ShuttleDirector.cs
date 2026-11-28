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

        static Terrain FindTerrain(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return null;
            foreach (var r in scene.GetRootGameObjects())
            {
                var terr = r.GetComponentInChildren<Terrain>();
                if (terr != null && terr.terrainData != null) return terr;
            }
            return null;
        }

        void EnsureTerrain()
        {
            if (_terrain != null && _terrain.terrainData != null) return;
            var scene = _scene.IsValid() ? _scene : SceneManager.GetActiveScene();
            _terrain = FindTerrain(scene);
        }

        LandingZonePicker.HeightFn WorldHeight(Transform ignore = null)
        {
            EnsureTerrain();
            return LandingZonePicker.ForWorld(_terrain, ignore);
        }

        bool PlaceOnGround(LandingZonePicker.HeightFn height, Vector3 preferred, float search, out Vector3 pos)
        {
            pos = preferred;
            if (height != null && height(preferred.x, preferred.z, out float y, out _))
            {
                pos = new Vector3(preferred.x, y, preferred.z);
                return true;
            }
            return LandingZonePicker.FindOnGround(height, preferred, search, out pos);
        }

        void Land(Scene scene, bool arriving)
        {
            // Never leave a half-built (inactive) shuttle or a stuck loading gate behind if anything below throws.
            try
            {
                if (FindTerrain(scene) == null && scene.IsValid() && scene.isLoaded)
                {
                    StartCoroutine(LandWhenTerrainReady(scene, arriving));
                    return;
                }
                LandCore(scene, arriving);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Xenoarch] Shuttle landing failed: {e}");
                _busy = false;
                if (_building != null)
                {
                    _building.SetActive(true);
                    Shuttle = _building;
                    StartCoroutine(Settle(_building, WorldHeight(_building.transform), arriving));
                }
            }
            _building = null;
        }

        IEnumerator LandWhenTerrainReady(Scene scene, bool arriving)
        {
            _busy = true;
            float t = 0f;
            while (t < 8f && FindTerrain(scene) == null)
            {
                if (!scene.IsValid() || !scene.isLoaded) { _busy = false; yield break; }
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            try { LandCore(scene, arriving); }
            catch (System.Exception e)
            {
                Debug.LogError($"[Xenoarch] Shuttle landing failed: {e}");
                _busy = false;
                if (_building != null)
                {
                    _building.SetActive(true);
                    Shuttle = _building;
                    StartCoroutine(Settle(_building, WorldHeight(_building.transform), arriving));
                }
            }
            _building = null;
        }

        GameObject _building;

        void LandCore(Scene scene, bool arriving)
        {
            Shuttle = null; _busy = false; _building = null; _preplaced = null;
            _landedOnce = true;
            var session = XenoarchRuntime.Session;
            if (session == null || !scene.IsValid()) return;
            var x = session.Exploration;
            var body = x.CurrentBody;
            if (body == null || body.sceneName != scene.name) body = x.Galaxy != null ? x.Galaxy.FindBodyByScene(scene.name) : null;
            if (!ShipTravel.UsesShuttle(body)) return;
            Terrain terrain = FindTerrain(scene);

            _busy = true;
            var def = session.Config.starterShip;
            string siteId = x.Ship.shuttleSiteId ?? "";
            var site = string.IsNullOrEmpty(siteId) ? null : x.SiteDef(siteId);
            if (site != null && site.sceneName != scene.name) { site = null; siteId = ""; }
            _scene = scene; _terrain = terrain;

            // Primary path: the scene's preplaced landing zones (shuttle + arrival spawn), switched on/off.
            var set = LandingZoneSet.In(scene);
            if (set != null && LandPreplaced(set, scene, siteId, arriving)) return;
            Debug.LogWarning($"[Xenoarch] Shuttle: no preplaced landing zone in {scene.name} (run Tools/Xenoarch/Landing/Build Landing Zones); spawning one at runtime (fallback)");

            // Shuttle instance (inactive while measured and placed).
            GameObject holder = null; Bounds local = new Bounds(Vector3.zero, Vector3.one * 8f);
            if (def != null && def.shuttlePrefab != null)
            {
                holder = new GameObject("Shuttle (landed)");
                SceneManager.MoveGameObjectToScene(holder, scene);
                holder.SetActive(false);
                _building = holder; _terrain = terrain;
                var inst = Instantiate(def.shuttlePrefab, holder.transform, false);
                inst.name = def.shuttlePrefab.name;
                // The vendor prefab is saved at a scene pose (on Epsilon this was local (-244, 234, 1352)), so without
                // this reset the visible craft sits ~230 m up and more than a kilometre away from the holder.
                ResetPrefabLocalPose(holder.transform);
                // Instance-only fix: the vendor model has convex hull colliders that Unity can't build fully ("partial
                // hull" warnings); a static craft doesn't need convex colliders. The prefab asset is untouched.
                foreach (var mc in inst.GetComponentsInChildren<MeshCollider>(true)) if (mc.convex && mc.GetComponentInParent<Rigidbody>(true) == null) mc.convex = false;
                foreach (var rb in inst.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;
                // The craft has no Rigidbody, so its ~116 colliders were static colliders teleported every descent frame.
                EnsureKinematicBody(holder);
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
            var height = LandingZonePicker.ForWorld(terrain, holder != null ? holder.transform : null);

            var spawnDefault = PlayerSpawnPoint.Find(PlayerSpawnIds.Default);
            Vector3 target;
            if (site != null && site.hasLandingHint) target = site.landingHint;
            else if (site != null) target = site.position;
            else if (spawnDefault != null) target = spawnDefault.transform.position;
            else if (terrain != null && terrain.terrainData != null) target = terrain.transform.position + terrain.terrainData.size * 0.5f;
            else target = Vector3.zero;
            Vector3 pos; float yaw; string how;
            var marker = LandingZone.Find(siteId);
            if (marker != null)
            {
                pos = marker.transform.position;
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
                    yaw = 0f; how = "no flat ground found, fallback beside the target";
                }
            }
            if (!PlaceOnGround(height, pos, 200f, out var groundedPos))
            {
                if (!PlaceOnGround(height, target, 400f, out groundedPos))
                    Debug.LogWarning($"[Xenoarch] Shuttle: no valid ground near {(site != null ? site.Id : "the landing target")} in {scene.name}; using the last fallback");
                else how += ", snapped to valid ground near the site";
            }
            pos = groundedPos;
            Debug.Log($"[ShuttleDiag] LandCore scene={scene.name} site={(site != null ? site.Id : "none")} arriving={arriving} how={how} pos={pos:F1} target={target:F1} terrain={(terrain != null)}");

            if (holder != null)
            {
                holder.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
                // Rest the landing gear (lowest parts) on the ground.
                GroundOnGear(holder.transform, height);
                foreach (var bay in holder.GetComponentsInChildren<ShuttleBayShuttle>(true)) DestroyImmediate(bay); // bay-only menu
                holder.AddComponent<ShuttleInteractable>();
                holder.SetActive(true);
                GroundByBounds(holder.transform, height);
                Shuttle = holder;
                _descendGround = holder.transform.position;
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

        LandingZoneSet _preplaced;

        /// <summary>Switches on the preplaced zone for the chosen site; false if the scene has none usable.</summary>
        bool LandPreplaced(LandingZoneSet set, Scene scene, string siteId, bool arriving)
        {
            // Exact site match only. The shared procedural template's base/first pad is often off the generated terrain
            // and is not the chosen site, so an unmatched siteId must use the runtime picker at the site's landing hint.
            var slot = string.IsNullOrEmpty(siteId) ? set.Activate(siteId) : set.ActivateExact(siteId);
            if (slot == null || slot.shuttle == null) { _preplaced = null; return false; }
            _preplaced = set;
            UsePreplaced(slot);
            LastPick = $"{scene.name}: preplaced landing zone '{slot.name}' ({(string.IsNullOrEmpty(slot.siteId) ? "base" : slot.siteId)}) for {(string.IsNullOrEmpty(siteId) ? "no site" : siteId)}";
            Debug.Log($"[Xenoarch] {LastPick}");
            StartCoroutine(SettlePreplaced(set, arriving));
            return true;
        }

        void UsePreplaced(LandingZoneSlot slot)
        {
            var sh = slot.shuttle;
            foreach (var bay in sh.GetComponentsInChildren<ShuttleBayShuttle>(true)) Destroy(bay); // the bay's flight menu, not ours
            if (sh.GetComponentInChildren<ShuttleInteractable>(true) == null) sh.AddComponent<ShuttleInteractable>();
            Shuttle = sh;
        }

        IEnumerator SettlePreplaced(LandingZoneSet set, bool arriving)
        {
            float t = 0f; int startFrame = Time.frameCount;
            yield return null;
            while (arriving && SceneLoader.PlacedFrame < startFrame && t < 5f) { t += Time.unscaledDeltaTime; yield return null; }
            // A saved pose / Continue far from the chosen zone: use the zone nearest the player instead.
            var rig = PlayerRigLocator.Find();
            if (rig != null && Shuttle != null)
            {
                var p = rig.Root.position; var s = Shuttle.transform.position;
                float d = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(s.x, s.z));
                if (d > maxPlayerDistance)
                {
                    var near = set.ActivateNearest(p);
                    if (near != null && near.shuttle != null) { UsePreplaced(near); LastPick += $"; player {d:0} m away, switched to nearest zone '{near.name}'"; }
                }
            }
            _busy = false;
            if (Shuttle != null) LogPlaced(Shuttle, "preplaced zone");
            t = 0f;
            while (SceneLoader.Busy && t < 240f) { t += Time.unscaledDeltaTime; yield return null; }
            yield return new WaitForSecondsRealtime(0.5f);
            if (_scene.IsValid() && _scene.isLoaded) Verify(WorldHeight(Shuttle != null ? Shuttle.transform : null));
        }

        /// <summary>Rest the lowest visible gear (or hull) renderer on the highest ground under the craft, measured from
        /// world renderer bounds after activation (the old mesh-bounds estimate taken while inactive could be badly off).</summary>
        void GroundByBounds(Transform holder, LandingZonePicker.HeightFn height)
        {
            if (holder == null) return;
            if (!holder.gameObject.activeInHierarchy) { GroundOnGear(holder, height); return; }
            float minY = float.PositiveInfinity;
            foreach (var g in _gears) if (g != null) foreach (var r in g.GetComponentsInChildren<Renderer>()) if (r.enabled) minY = Mathf.Min(minY, r.bounds.min.y);
            if (float.IsInfinity(minY)) foreach (var r in holder.GetComponentsInChildren<Renderer>()) if (r.enabled) minY = Mathf.Min(minY, r.bounds.min.y);
            if (float.IsInfinity(minY)) { GroundOnGear(holder, height); return; }
            var p = holder.position; float ground = float.NegativeInfinity;
            if (height(p.x, p.z, out float gy, out _)) ground = gy;
            foreach (var g in _gears) if (g != null) { var gp = g.position; if (height(gp.x, gp.z, out float y, out _)) ground = Mathf.Max(ground, y); }
            if (float.IsNegativeInfinity(ground) || !PlausibleGroundY(ground, p))
            {
                float rejected = ground;
                if (!PlaceOnGround(height, p, 200f, out var safe) || !PlausibleGroundY(safe.y, p))
                {
                    if (!TrySurfaceY(p.x, p.z, out float surf, out _) || !PlausibleGroundY(surf, p))
                    {
                        Debug.LogWarning($"[ShuttleDiag] GroundByBounds aborted: no plausible ground at {p:F1} (rejected Y={rejected:F1})");
                        return;
                    }
                    safe = new Vector3(p.x, surf, p.z);
                }
                Debug.Log($"[ShuttleDiag] GroundByBounds rejected Y={rejected:F1} at {p:F1}; using {safe:F1}");
                holder.position = new Vector3(safe.x, holder.position.y, safe.z);
                p = holder.position;
                ground = safe.y;
            }
            float delta = ground - minY;
            if (Mathf.Abs(delta) > 80f)
            {
                Debug.LogWarning($"[ShuttleDiag] GroundByBounds refused {delta:F1} m snap (holder={p:F1} minY={minY:F1} ground={ground:F1})");
                return;
            }
            holder.position = p + Vector3.up * delta;
        }

        bool TrySurfaceY(float x, float z, out float y, out bool hole)
        {
            y = 0f; hole = false;
            return LandingZonePicker.TryTerrainSurface(_terrain, x, z, out y, out _, out hole, out bool onMap) && onMap;
        }

        bool PlausibleGroundY(float y, Vector3 near)
        {
            if (float.IsNaN(y) || float.IsInfinity(y)) return false;
            if (TrySurfaceY(near.x, near.z, out float surf, out _))
                return y >= surf - LandingZonePicker.MaxDropBelowSurface && y <= surf + LandingZonePicker.MaxRiseAboveSurface;
            if (Mathf.Abs(y - near.y) > 80f) return false;
            return true;
        }

        /// <summary>The Creepy Cat Mark_2 Shuttle prefab is referenced, not edited, but it was saved with a world
        /// position from a vendor scene. Instantiating it under the landing holder keeps that local pose, so the
        /// visible mesh is hundreds of metres from the holder (Editor.log: minY 241 with holder at Y 8.9, and a 1.3 km
        /// Z offset). Zero the instance local pose; do not touch the prefab asset.</summary>
        void ResetPrefabLocalPose(Transform holder)
        {
            if (holder == null || holder.childCount == 0) return;
            var inst = holder.GetChild(0);
            var before = inst.localPosition;
            // Only the vendor scene pose (~1.4 km off). A preplaced zone child carries a deliberate small offset and slope tilt
            // (Moon: (0.6, 2.9, 10.1) / (355, 356, 230)); zeroing that knocked the craft off its pad and level.
            if (before.magnitude < 50f) return;
            if (before.sqrMagnitude > 0.01f || inst.localEulerAngles.sqrMagnitude > 0.01f)
                Debug.Log($"[ShuttleDiag] reset prefab localPosition {before:F1} / euler {inst.localEulerAngles:F1} -> 0 (vendor prefab was saved at a scene pose)");
            inst.localPosition = Vector3.zero;
            inst.localRotation = Quaternion.identity;
        }

        const float DescendHeight = 45f;
        const float DescendSeconds = 5f;
        Coroutine _descend;

        void StartDescent(Transform holder, Vector3 ground, string reason)
        {
            if (holder == null) return;
            if (_descend != null) StopCoroutine(_descend);
            _descend = StartCoroutine(DescendToGround(holder, ground, reason));
        }

        IEnumerator DescendToGround(Transform holder, Vector3 ground, string reason)
        {
            ResetPrefabLocalPose(holder);
            // The Creepy Cat craft has no Rigidbody, so this used to teleport ~116 static colliders every frame and
            // anything they came down on (rover, player, rocks) was shoved out by depenetration. A kinematic body moved
            // with MovePosition in FixedUpdate sweeps them like a real moving object instead.
            var rb = EnsureKinematicBody(holder.gameObject);
            holder.gameObject.SetActive(true);
            var start = new Vector3(ground.x, ground.y + DescendHeight, ground.z);
            holder.SetPositionAndRotation(start, holder.rotation);
            if (rb != null) { rb.position = start; rb.interpolation = RigidbodyInterpolation.Interpolate; }
            // Arrivals: hold at the top until the loading screen is gone (it used to land unseen behind it).
            float wait = 0f;
            while (SceneLoader.Busy && wait < 240f && holder != null) { wait += Time.unscaledDeltaTime; yield return null; }
            if (holder == null) { _descend = null; yield break; }
            Debug.Log($"[ShuttleDiag] descend start={start:F1} ground={ground:F1} reason={reason} heldBehindLoading={wait:0.0}s body={(rb != null ? "kinematic" : "none")}");
            float t = 0f;
            while (t < DescendSeconds && holder != null)
            {
                yield return new WaitForFixedUpdate();
                if (holder == null) break;
                t += Time.fixedDeltaTime;
                float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / DescendSeconds));
                var p = new Vector3(ground.x, Mathf.Lerp(start.y, ground.y, u), ground.z);
                if (rb != null) rb.MovePosition(p); else holder.position = p;
            }
            if (holder != null)
            {
                if (rb != null) rb.interpolation = RigidbodyInterpolation.None; // parked: nothing to interpolate, no one-frame pull-back
                holder.position = ground;
                GroundByBounds(holder, WorldHeight(holder));
                if (rb != null) { rb.position = holder.position; rb.rotation = holder.rotation; }
                Physics.SyncTransforms();
                Debug.Log($"[ShuttleDiag] descend done pos={holder.position:F1} reason={reason}");
                LogTouchdown(holder, reason);
                LogPlaced(holder.gameObject, reason);
            }
            _descend = null;
        }

        /// <summary>Runtime-only: a kinematic, gravity-free Rigidbody on the landing holder (never on the vendor prefab).
        /// The shuttle is only ever moved by script, so it must never be simulated, fall, bounce or be pushed.</summary>
        static Rigidbody EnsureKinematicBody(GameObject holder)
        {
            if (holder == null) return null;
            var rb = holder.GetComponent<Rigidbody>();
            if (rb == null) rb = holder.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.constraints = RigidbodyConstraints.FreezeAll;
            rb.interpolation = RigidbodyInterpolation.None;
            return rb;
        }

        /// <summary>[ShuttleDiag] touchdown: Rigidbody state, collider make-up, the gap between the lowest collider and the
        /// terrain (negative = buried in it), and any foreign collider inside the hull's box (the rover, player or rocks
        /// there get pushed out by depenetration, which reads as bouncing).</summary>
        void LogTouchdown(Transform holder, string reason)
        {
            if (holder == null) return;
            var rb = holder.GetComponent<Rigidbody>();
            int bodies = 0, dynamicBodies = 0, cols = 0, concave = 0;
            foreach (var b in holder.GetComponentsInChildren<Rigidbody>(true)) { bodies++; if (!b.isKinematic) dynamicBodies++; }
            bool any = false; Bounds hull = default;
            foreach (var c in holder.GetComponentsInChildren<Collider>())
            {
                if (!c.enabled || c.isTrigger) continue;
                cols++;
                if (c is MeshCollider mc && !mc.convex) concave++;
                if (any) hull.Encapsulate(c.bounds); else { hull = c.bounds; any = true; }
            }
            var hits = new List<string>();
            if (any)
            {
                int n = Physics.OverlapBoxNonAlloc(hull.center, hull.extents, _buf, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n && hits.Count < 6; i++)
                {
                    var c = _buf[i];
                    if (c == null || c is TerrainCollider || c.transform.IsChildOf(holder)) continue;
                    if (_terrain != null && c.transform.IsChildOf(_terrain.transform)) continue; // Digger chunks
                    var arb = c.attachedRigidbody;
                    hits.Add(arb != null ? $"{c.name}(rb{(arb.isKinematic ? " kinematic" : " dynamic")})" : c.name);
                }
            }
            var hp = holder.position;
            string gap = any && TrySurfaceY(hp.x, hp.z, out float surf, out _) ? (hull.min.y - surf).ToString("F2") : "n/a";
            string rbs = rb != null ? $"kinematic={rb.isKinematic} gravity={rb.useGravity} interp={rb.interpolation} constraints={rb.constraints} sleeping={rb.IsSleeping()}" : "none";
            Debug.Log($"[ShuttleDiag] touchdown reason={reason} pos={hp:F1} rb=({rbs}) bodies={bodies} dynamic={dynamicBodies} colliders={cols} concaveMesh={concave} colliderGapToTerrain={gap} m foreignInHullBox=[{string.Join(", ", hits)}]");
        }

        void Update()
        {
            // Dev: F7 brings the shuttle beside the player (lands one if it is missing).
            var cfg = XenoarchRuntime.Session?.Config;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (cfg == null || !cfg.enableDebugHotkeys || kb == null || SceneLoader.Busy || !kb.f7Key.wasPressedThisFrame) return;
            Summon();
        }

        /// <summary>Dev summon: moves the active shuttle next to the player (runtime only; the scene is untouched).</summary>
        public void Summon()
        {
            if (CallToPlayer(dev: true, out var message))
                DMI.Xenoarch.UI.Toast.Show(message, 2.5f);
            else
                Debug.LogWarning("[Xenoarch] Summon shuttle: " + (message ?? "failed"));
        }

        [Tooltip("Seconds between Emergency Shuttle Call uses from the inventory.")]
        public float emergencyCooldown = 90f;
        float _emergencyReadyAt;

        public float EmergencyCooldownRemaining => Mathf.Max(0f, _emergencyReadyAt - Time.unscaledTime);

        /// <summary>Player emergency call: land the shuttle on valid ground near the player. Honours the cooldown.</summary>
        public bool CallEmergency(out string message) => CallToPlayer(dev: false, out message);

        bool CallToPlayer(bool dev, out string message)
        {
            message = null;
            if (SceneLoader.Busy) { message = "Can't call the shuttle while a scene is loading."; return false; }
            if (!dev)
            {
                var session = XenoarchRuntime.Session;
                if (session == null || !session.EmergencyShuttleUnlocked)
                {
                    message = "Emergency Shuttle Call is locked. Craft the Emergency Shuttle Beacon at the ship workshop.";
                    return false;
                }
                float left = EmergencyCooldownRemaining;
                if (left > 0.5f) { message = $"Emergency shuttle call is cooling down ({left:0}s)."; return false; }
            }
            var rig = PlayerRigLocator.Find();
            if (rig == null) { message = "Can't call the shuttle: no player position."; return false; }
            if (Shuttle == null)
            {
                Land(SceneManager.GetActiveScene(), false);
                if (Shuttle == null) { message = "No shuttle is available in this area."; return false; }
            }
            var height = WorldHeight(Shuttle.transform);
            var fwd = rig.Root.forward; fwd.y = 0f; if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            float fp = _footprint > 0f ? _footprint : 8f;
            var around = rig.Root.position + fwd.normalized * (fp + 4f);
            Vector3 pos; float callYaw = rig.Root.eulerAngles.y + 90f; string callHow = "straight drop";
            // Pick clear, semi-flat ground. The old straight drop in front of the player ignored the rover, rocks and the
            // player (Moon log: rover deployed at (-228.7, 0.1, -144.0), the shuttle then came down 2.4 m from it).
            var callRules = _rules; if (callRules.angles <= 0) callRules = LandingZonePicker.Rules.Default; callRules.footprint = fp;
            var callAvoid = new List<(Vector3, float)> { (rig.Root.position, 3f + fp * 0.5f) };
            var callScene = Shuttle.scene; var shuttleT = Shuttle.transform;
            var callPick = LandingZonePicker.Pick(around, callAvoid, callRules, height, (c, rad) => Blocked(c, rad, callScene, _terrain, shuttleT, rig.Root), _yawOffset);
            if (callPick.found && Vector3.Distance(callPick.position, rig.Root.position) <= 80f) { pos = callPick.position; callYaw = callPick.yaw; callHow = "clear ground " + callPick; }
            else if (!PlaceOnGround(height, around, 80f, out pos) && !PlaceOnGround(height, rig.Root.position, 160f, out pos))
            {
                message = "No valid ground near you for the shuttle to land.";
                return false;
            }
            if (!PlausibleGroundY(pos.y, rig.Root.position))
            {
                Debug.LogWarning($"[ShuttleDiag] CallToPlayer rejected Y={pos.y:F1} near player {rig.Root.position:F1}");
                message = "No valid ground near you for the shuttle to land.";
                return false;
            }
            ResetPrefabLocalPose(Shuttle.transform);
            Shuttle.SetActive(true);
            Shuttle.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, callYaw, 0f));
            GroundByBounds(Shuttle.transform, height);
            var landed = Shuttle.transform.position;
            if (!PlausibleGroundY(landed.y, rig.Root.position))
            {
                if (TrySurfaceY(landed.x, landed.z, out float surf, out bool hole) && !hole)
                {
                    Debug.LogWarning($"[ShuttleDiag] CallToPlayer lifted shuttle from {landed:F1} to surface Y={surf:F1}");
                    landed = new Vector3(landed.x, surf, landed.z);
                }
                else Debug.LogWarning($"[ShuttleDiag] CallToPlayer final Y still implausible at {landed:F1}");
            }
            Debug.Log($"[ShuttleDiag] CallToPlayer dev={dev} player={rig.Root.position:F1} requested={pos:F1} ground={landed:F1} via {callHow}");
            if (!dev) _emergencyReadyAt = Time.unscaledTime + Mathf.Max(0f, emergencyCooldown);
            StartDescent(Shuttle.transform, landed, dev ? "dev summon (F7)" : "emergency call");
            message = dev ? "Shuttle summoned (dev F7)." : "Shuttle inbound. Landing near your position.";
            return true;
        }

        readonly List<Transform> _gears = new List<Transform>();
        Scene _scene; Terrain _terrain; LandingZonePicker.Rules _rules; float _footprint, _yawOffset;
        [Tooltip("After the player is placed (landing, save load, Continue, autosave restore), the shuttle is moved next to them if it is farther than this (m).")]
        public float maxPlayerDistance = 40f;
        float _groundOffset;
        Vector3 _descendGround;

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
            if (!float.IsNegativeInfinity(ground) && PlausibleGroundY(ground, p))
                holder.position = new Vector3(p.x, ground + _groundOffset, p.z);
            else
                Debug.LogWarning($"[ShuttleDiag] GroundOnGear skipped implausible Y={ground:F1} at {p:F1}");
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
            if (holder != null)
            {
                ResetPrefabLocalPose(holder.transform);
                EnsureNearPlayer(holder, height);
                GroundByBounds(holder.transform, height);
                _descendGround = holder.transform.position;
            }
            _busy = false;
            if (holder != null)
            {
                var ap = holder.transform.position;
                TrySurfaceY(ap.x, ap.z, out float asurf, out bool ahole);
                Debug.Log($"[ShuttleDiag] after load pos={ap:F1} surfY={asurf:F1} hole={ahole} plausible={PlausibleGroundY(ap.y, ap)} local0={holder.transform.childCount > 0 && holder.transform.GetChild(0).localPosition.sqrMagnitude < 0.01f}");
                if (arriving)
                    StartDescent(holder.transform, _descendGround, "after load (descent)");
                else
                    LogPlaced(holder, "after load");
            }
            // Watchdog: once the loading screen is gone, check the shuttle is still there, visible, grounded and near the
            // player (a late player move, a destroyed or disabled holder); repair and log what was wrong.
            t = 0f;
            while (SceneLoader.Busy && t < 240f) { t += Time.unscaledDeltaTime; yield return null; }
            for (int check = 0; check < 3; check++)
            {
                yield return new WaitForSecondsRealtime(check == 0 ? 0.5f : 3f);
                if (!_scene.IsValid() || !_scene.isLoaded) yield break;
                Verify(height);
            }
        }

        /// <summary>Repairs the landed shuttle if it vanished, was disabled, sank or is far from the player.</summary>
        void Verify(LandingZonePicker.HeightFn height)
        {
            if (_descend != null) return; // don't fight the descent
            var holder = Shuttle;
            if (_preplaced != null)
            {
                if (holder == null) { Debug.LogWarning("[Xenoarch] Shuttle watchdog: the preplaced shuttle was destroyed; using the runtime fallback"); _preplaced = null; Land(_scene, false); return; }
                if (!holder.activeInHierarchy) { _preplaced.Active?.gameObject.SetActive(true); holder.SetActive(true); LogPlaced(holder, "watchdog: re-enabled the preplaced shuttle"); }
                // Fall through to re-ground / relocate if the pad is off the terrain.
            }
            else 
            if (holder == null)
            {
                Debug.LogWarning("[Xenoarch] Shuttle watchdog: the landed shuttle was destroyed; landing it again");
                Land(_scene, false);
                return;
            }
            ResetPrefabLocalPose(holder.transform);
            string fixedWhat = null;
            if (!holder.activeSelf) { holder.SetActive(true); fixedWhat = "re-enabled"; }
            int shown = 0;
            foreach (var r in holder.GetComponentsInChildren<Renderer>(true)) if (r.enabled && r.gameObject.activeInHierarchy) shown++;
            if (shown == 0) Debug.LogWarning("[Xenoarch] Shuttle watchdog: the shuttle has no visible renderers (prefab/materials?)");
            var rig = PlayerRigLocator.Find();
            if (rig != null)
            {
                Vector3 p = rig.Root.position, s = holder.transform.position;
                if (Vector2.Distance(new Vector2(p.x, p.z), new Vector2(s.x, s.z)) > maxPlayerDistance) { EnsureNearPlayer(holder, height); fixedWhat = (fixedWhat != null ? fixedWhat + ", " : "") + "moved to the player"; }
            }
            var hp = holder.transform.position;
            bool heightOk = height(hp.x, hp.z, out float gy, out _);
            bool surfaceOk = TrySurfaceY(hp.x, hp.z, out float surfY, out bool inHole);
            bool sunk = surfaceOk && (inHole || hp.y < surfY - LandingZonePicker.MaxDropBelowSurface || (heightOk && gy < surfY - LandingZonePicker.MaxDropBelowSurface));
            if (!heightOk || sunk || (heightOk && !PlausibleGroundY(gy, hp)))
            {
                if (PlaceOnGround(height, hp, 200f, out var safe) && PlausibleGroundY(safe.y, hp))
                {
                    holder.transform.position = safe;
                    GroundByBounds(holder.transform, height);
                    fixedWhat = (fixedWhat != null ? fixedWhat + ", " : "") + "moved onto valid ground";
                    Debug.Log($"[ShuttleDiag] Verify relocated from {hp:F1} (heightOk={heightOk} gy={gy:F1} surf={surfY:F1} hole={inHole}) to {holder.transform.position:F1}");
                }
                else if (surfaceOk && !inHole && PlausibleGroundY(surfY, hp))
                {
                    holder.transform.position = new Vector3(hp.x, surfY, hp.z);
                    GroundByBounds(holder.transform, height);
                    fixedWhat = (fixedWhat != null ? fixedWhat + ", " : "") + "lifted to terrain surface";
                    Debug.Log($"[ShuttleDiag] Verify lifted from {hp:F1} to surface {holder.transform.position:F1}");
                }
            }
            else if (hp.y < gy + _groundOffset - 1.5f)
            { GroundByBounds(holder.transform, height); fixedWhat = (fixedWhat != null ? fixedWhat + ", " : "") + "re-grounded"; }
            if (fixedWhat != null) LogPlaced(holder, "watchdog: " + fixedWhat);
        }

        void LogPlaced(GameObject holder, string when)
        {
            var rig = PlayerRigLocator.Find();
            var pos = holder.transform.position;
            string pl = rig != null ? $", player at {rig.Root.position:F1} ({Vector3.Distance(rig.Root.position, pos):0} m away)" : ", no player";
            int rs = 0; foreach (var r in holder.GetComponentsInChildren<Renderer>(true)) if (r.enabled) rs++;
            Debug.Log($"[Xenoarch] Shuttle placed at {pos:F1} yaw {holder.transform.eulerAngles.y:0} in {holder.scene.name} ({when}; active={holder.activeInHierarchy}, {rs} renderers){pl}");
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
                if (!PlaceOnGround(height, pos, 120f, out pos)) PlaceOnGround(height, p, 160f, out pos);
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
