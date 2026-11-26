using System.Collections;
using System.Collections.Generic;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Galaxy;
using DMI.Xenoarch.Interaction;
using DMI.Xenoarch.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DMI.Xenoarch.Excavation
{
    /// <summary>
    /// Phase 1D buried chambers (<see cref="ChamberDefinition"/>). On scene load, for every chamber whose host dig site
    /// is in the scene: a sealed door on the host site's floor (usable once that floor is dug out above it), and the
    /// chamber itself far below: entry vestibule, tunnel, room with stone walls, lights and glowing glyphs, and a
    /// diggable soil bed (an underground <see cref="ExcavationSite"/> with its own finds and save). Door and exit move
    /// the player with a short fade. Everything is runtime-only; scene files are untouched.
    /// </summary>
    public sealed class ChamberDirector : MonoBehaviour
    {
        public static ChamberDirector Instance { get; private set; }
        readonly List<Built> _built = new List<Built>();
        bool _moving;

        sealed class Built { public ChamberDefinition def; public ExcavationSite host; public GameObject root; public Transform arrival, door; public ExcavationSite floor; }

        void Awake() => Instance = this;
        void OnEnable() => SceneManager.sceneLoaded += OnLoaded;
        void OnDisable() => SceneManager.sceneLoaded -= OnLoaded;
        void Start() => Build(SceneManager.GetActiveScene());
        void OnLoaded(Scene s, LoadSceneMode m) { if (m == LoadSceneMode.Single || s == SceneManager.GetActiveScene()) Build(s); }
        float _retry;
        // Host dig sites are built by ExcavationDirector; retry briefly in case it ran after us.
        void Update() { if (Time.unscaledTime >= _retry) { _retry = Time.unscaledTime + 2f; Build(SceneManager.GetActiveScene()); } }
        static ChamberDefinition[] _all;
        static ChamberDefinition[] Defs => _all ??= ChamberDefinition.All();

        void Build(Scene scene)
        {
            _built.RemoveAll(b => b.root == null);
            if (XenoarchRuntime.Session == null || !scene.IsValid()) return;
            foreach (var def in Defs)
            {
                if (def == null || string.IsNullOrEmpty(def.hostSiteId) || _built.Exists(b => b.def == def && b.root != null)) continue;
                var host = ExcavationSite.Find(def.hostSiteId);
                if (host == null || host.gameObject.scene != scene) continue;
                try { _built.Add(BuildOne(def, host, scene)); }
                catch (System.Exception e) { Debug.LogException(e); }
            }
        }

        // ------------------------------------------------------------------ geometry
        Built BuildOne(ChamberDefinition def, ExcavationSite host, Scene scene)
        {
            var g = host.Grid;
            int ex = Mathf.Clamp(def.entranceCell.x, 1, g.nx - 2), ez = Mathf.Clamp(def.entranceCell.y, 1, g.nz - 2);
            var doorPos = host.CellCentre(ex, g.ny - 1, ez, 0f);   // top of the site's bedrock floor

            var root = new GameObject($"Chamber {def.Id}");
            SceneManager.MoveGameObjectToScene(root, scene);
            var b = new Built { def = def, host = host, root = root };

            // Sealed door on the host floor.
            var mats = Mats(def);
            var door = Box(root.transform, "Sealed Door", doorPos + Vector3.up * 0.06f, new Vector3(def.doorSize, 0.12f, def.doorSize), mats.stone, true);
            door.transform.rotation = Quaternion.Euler(0f, def.yaw, 0f);
            var ring = Box(door.transform, "Door Glyph Ring", Vector3.zero, Vector3.one, mats.glow, false);
            ring.transform.localPosition = new Vector3(0f, 0.52f, 0f); ring.transform.localScale = new Vector3(0.55f, 0.1f, 0.55f);
            door.AddComponent<ChamberDoor>().Init(this, def);
            b.door = door.transform;

            // The chamber, depthBelow under the door, tunnel along yaw.
            var c = new GameObject("Chamber Interior").transform;
            c.SetParent(root.transform, false);
            // At least depthBelow under the door, and below the terrain's own volume so terrain / Digger never touch it.
            float y = doorPos.y - def.depthBelow;
            foreach (var tr in Terrain.activeTerrains) if (tr != null) y = Mathf.Min(y, tr.transform.position.y - 8f);
            c.position = new Vector3(doorPos.x, y, doorPos.z);
            c.rotation = Quaternion.Euler(0f, def.yaw, 0f);
            const float t = 0.4f;   // wall thickness
            float w = def.tunnelWidth, h = def.tunnelHeight, L = def.tunnelLength, S = def.roomSize, D = def.dirtDepth + 0.3f, H = def.headroom;

            // Vestibule (3 x 3, open towards +z), floor at y = 0.
            float v = 3f;
            Slab(c, mats.stone, new Vector3(0f, -t * 0.5f, 0f), new Vector3(v + 2 * t, t, v + 2 * t));
            Slab(c, mats.stone, new Vector3(0f, h + t * 0.5f, 0f), new Vector3(v + 2 * t, t, v + 2 * t));
            Slab(c, mats.stone, new Vector3(-(v + t) * 0.5f, h * 0.5f, 0f), new Vector3(t, h, v + 2 * t));
            Slab(c, mats.stone, new Vector3((v + t) * 0.5f, h * 0.5f, 0f), new Vector3(t, h, v + 2 * t));
            Slab(c, mats.stone, new Vector3(0f, h * 0.5f, -(v + t) * 0.5f), new Vector3(v + 2 * t, h, t));
            // Vestibule front wall around the tunnel mouth.
            float side = (v - w) * 0.5f;
            if (side > 0.01f)
            {
                Slab(c, mats.stone, new Vector3(-(w + side) * 0.5f, h * 0.5f, (v + t) * 0.5f), new Vector3(side, h, t));
                Slab(c, mats.stone, new Vector3((w + side) * 0.5f, h * 0.5f, (v + t) * 0.5f), new Vector3(side, h, t));
            }
            // Exit: a glowing panel on the back wall.
            var exit = Box(c, "Chamber Exit", Vector3.zero, new Vector3(1.2f, 1.6f, 0.08f), mats.glow, true);
            exit.transform.localPosition = new Vector3(0f, 1.1f, -v * 0.5f + 0.05f);
            exit.AddComponent<ChamberExit>().Init(this, def);
            b.arrival = new GameObject("Arrival").transform; b.arrival.SetParent(c, false); b.arrival.localPosition = new Vector3(0f, 0.15f, -0.6f);

            // Tunnel: z from v/2 to v/2 + L.
            float z0 = v * 0.5f, zc = z0 + L * 0.5f;
            Slab(c, mats.stone, new Vector3(0f, -t * 0.5f, zc), new Vector3(w + 2 * t, t, L + t));
            Slab(c, mats.stone, new Vector3(0f, h + t * 0.5f, zc), new Vector3(w + 2 * t, t, L + t));
            Slab(c, mats.stone, new Vector3(-(w + t) * 0.5f, h * 0.5f, zc), new Vector3(t, h, L + t));
            Slab(c, mats.stone, new Vector3((w + t) * 0.5f, h * 0.5f, zc), new Vector3(t, h, L + t));
            for (float z = z0 + 1.5f; z < z0 + L - 0.5f; z += 2.5f)
            {
                Strip(c, mats.glow, new Vector3(-w * 0.5f + 0.02f, h * 0.55f, z), new Vector3(0.03f, 0.12f, 0.9f));
                Strip(c, mats.glow, new Vector3(w * 0.5f - 0.02f, h * 0.55f, z), new Vector3(0.03f, 0.12f, 0.9f));
            }
            Lamp(c, new Vector3(0f, h - 0.3f, 0f), 7f, 1.6f, def.glyphColor);
            Lamp(c, new Vector3(0f, h - 0.3f, zc), Mathf.Max(7f, L * 0.7f), 1.4f, def.glyphColor);

            // Room: soil top at y = 0 (level with the tunnel floor), soil bed S x S, walls from -D to H.
            float r0 = z0 + L, rc = r0 + S * 0.5f, wallH = D + H, wallY = (H - D) * 0.5f;
            Slab(c, mats.stone, new Vector3(0f, -D - t * 0.5f, rc), new Vector3(S + 2 * t, t, S + 2 * t));
            Slab(c, mats.stone, new Vector3(0f, H + t * 0.5f, rc), new Vector3(S + 2 * t, t, S + 2 * t));
            Slab(c, mats.stone, new Vector3(-(S + t) * 0.5f, wallY, rc), new Vector3(t, wallH, S + 2 * t));
            Slab(c, mats.stone, new Vector3((S + t) * 0.5f, wallY, rc), new Vector3(t, wallH, S + 2 * t));
            Slab(c, mats.stone, new Vector3(0f, wallY, r0 + S + t * 0.5f), new Vector3(S + 2 * t, wallH, t));
            // Entry wall with the tunnel opening (0..h, width w); below the soil it is solid.
            float rs = (S - w) * 0.5f;
            Slab(c, mats.stone, new Vector3(-(w + rs) * 0.5f, wallY, r0 - t * 0.5f), new Vector3(rs, wallH, t));
            Slab(c, mats.stone, new Vector3((w + rs) * 0.5f, wallY, r0 - t * 0.5f), new Vector3(rs, wallH, t));
            Slab(c, mats.stone, new Vector3(0f, (h + H) * 0.5f, r0 - t * 0.5f), new Vector3(w, Mathf.Max(0.01f, H - h), t));
            Slab(c, mats.stone, new Vector3(0f, -D * 0.5f, r0 - t * 0.5f), new Vector3(w, D, t));
            for (int i = 0; i < 4; i++)
            {
                float a = (i + 0.5f) / 4f * S - S * 0.5f;
                Strip(c, mats.glow, new Vector3(a, H - 0.6f, r0 + S - 0.02f), new Vector3(0.7f, 0.5f, 0.03f));
                Strip(c, mats.glow, new Vector3(-S * 0.5f + 0.02f, H - 0.6f, r0 + (i + 0.5f) / 4f * S), new Vector3(0.03f, 0.5f, 0.7f));
                Strip(c, mats.glow, new Vector3(S * 0.5f - 0.02f, H - 0.6f, r0 + (i + 0.5f) / 4f * S), new Vector3(0.03f, 0.5f, 0.7f));
            }
            Lamp(c, new Vector3(0f, H - 0.5f, rc), S * 1.3f, 2.2f, Color.Lerp(def.glyphColor, new Color(1f, 0.85f, 0.65f), 0.5f));
            Lamp(c, new Vector3(0f, 0.6f, rc), S, 0.8f, new Color(1f, 0.85f, 0.65f));

            // Diggable soil bed: an underground dig site (trench mesh, own finds, saved under FloorSiteId).
            var centre = c.TransformPoint(new Vector3(0f, 0f, rc));
            var sd = ScriptableObject.CreateInstance<ArchaeologicalSiteDefinition>();
            sd.SetIdForTooling(def.FloorSiteId);
            sd.name = def.FloorSiteId; sd.displayName = def.displayName; sd.bodyId = host.Definition.bodyId; sd.sceneName = scene.name;
            sd.position = centre; sd.radius = S * 0.5f; sd.depth = def.GridDepth; sd.cellSize = def.cellSize; sd.style = host.Definition.style;
            sd.artifacts = def.artifacts;
            // Grids are axis-aligned: only yaw 0 / 90 / 180 / 270 keep the bed flush with the walls.
            b.floor = ExcavationSite.Create(sd, centre.y, scene, underground: true);

            Debug.Log($"[Xenoarch] Chamber {def.Id}: door at {doorPos} (host {host.Definition.Id} cell {ex},{ez}), interior at y {c.position.y:0}, room {S:0.#} m with {def.artifacts.Count} find(s)");
            return b;
        }

        static void Slab(Transform parent, Material m, Vector3 local, Vector3 size) => Box(parent, "Wall", local, size, m, true, true);
        static void Strip(Transform parent, Material m, Vector3 local, Vector3 size) => Box(parent, "Glyphs", local, size, m, false, true);

        static GameObject Box(Transform parent, string name, Vector3 pos, Vector3 size, Material m, bool collider, bool local = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            if (local) go.transform.localPosition = pos; else go.transform.position = pos;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            if (!collider) Destroy(go.GetComponent<Collider>());
            return go;
        }

        static void Lamp(Transform parent, Vector3 local, float range, float intensity, Color c)
        {
            var go = new GameObject("Chamber Light");
            go.transform.SetParent(parent, false); go.transform.localPosition = local;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.range = range; l.intensity = intensity; l.color = c; l.shadows = LightShadows.None;
        }

        static readonly Dictionary<string, (Material stone, Material glow)> _mats = new Dictionary<string, (Material, Material)>();
        static (Material stone, Material glow) Mats(ChamberDefinition def)
        {
            if (_mats.TryGetValue(def.Id, out var m) && m.stone != null) return m;
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var stone = new Material(sh) { name = $"Chamber Stone ({def.Id})" };
            stone.SetColor("_BaseColor", def.wallColor); stone.color = def.wallColor; stone.SetFloat("_Smoothness", 0.15f);
            var glow = new Material(sh) { name = $"Chamber Glyphs ({def.Id})" };
            glow.SetColor("_BaseColor", def.glyphColor * 0.4f); glow.color = def.glyphColor * 0.4f;
            glow.EnableKeyword("_EMISSION"); glow.SetColor("_EmissionColor", def.glyphColor * 2.2f);
            glow.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            DMI.Xenoarch.Scenes.ShaderPrewarm.Warm(stone); DMI.Xenoarch.Scenes.ShaderPrewarm.Warm(glow);
            m = (stone, glow); _mats[def.Id] = m;
            return m;
        }

        // ------------------------------------------------------------------ door / exit
        /// <summary>True once the host floor above the door is dug out (centre column down to the bedrock floor).</summary>
        public bool DoorExposed(ChamberDefinition def)
        {
            var b = _built.Find(x => x.def == def);
            if (b == null || b.host == null) return false;
            var g = b.host.Grid;
            int ex = Mathf.Clamp(def.entranceCell.x, 1, g.nx - 2), ez = Mathf.Clamp(def.entranceCell.y, 1, g.nz - 2);
            return g.TopDepth(ex, ez) >= (g.ny - 1) * g.cell - 0.05f;
        }

        public bool Opened(ChamberDefinition def) => XenoarchRuntime.Session != null && XenoarchRuntime.Session.Discovery.Has(DMI.Xenoarch.Discovery.Facts.ChamberOpened(def.Id));

        public void Enter(ChamberDefinition def)
        {
            var b = _built.Find(x => x.def == def);
            if (b == null || _moving) return;
            var s = XenoarchRuntime.Session;
            if (!Opened(def))
            {
                s.Discovery.Publish(DMI.Xenoarch.Discovery.Facts.ChamberOpened(def.Id));
                s.Economy?.Award(s.Config.economy != null ? s.Config.economy.chamberOpened : 10, "chamber:" + def.Id, "opened " + def.displayName);
                if (!string.IsNullOrEmpty(def.openedMessage)) DMI.Xenoarch.UI.Toast.Show(def.openedMessage, 6f);
            }
            StartCoroutine(Move(b.arrival.position, b.arrival.parent.rotation));
        }

        public void Leave(ChamberDefinition def)
        {
            var b = _built.Find(x => x.def == def);
            if (b == null || _moving) return;
            StartCoroutine(Move(b.door.position + Vector3.up * 0.3f, b.door.rotation));   // stand on the door slab in the pit
        }

        IEnumerator Move(Vector3 pos, Quaternion rot)
        {
            var rig = PlayerRigLocator.Find();
            if (rig == null) yield break;
            _moving = true;
            rig.SetInputEnabled(false);
            var loader = XenoarchRuntime.Scenes;
            if (loader != null) yield return loader.FadeTo(1f);
            rig.Teleport(pos, Quaternion.Euler(0f, rot.eulerAngles.y, 0f));
            yield return null;
            if (loader != null) yield return loader.FadeTo(0f);
            rig.SetInputEnabled(true);
            _moving = false;
        }
    }

    /// <summary>The sealed chamber door on a dig site floor (usable once uncovered).</summary>
    public sealed class ChamberDoor : MonoBehaviour, IInteractable
    {
        ChamberDirector _dir; ChamberDefinition _def;
        public void Init(ChamberDirector d, ChamberDefinition def) { _dir = d; _def = def; }
        public Transform InteractionTransform => transform;
        public float GetPriority(in InteractionContext c) => _dir != null && _dir.DoorExposed(_def) ? 2f : -1f;
        public string GetPrompt(in InteractionContext c) => _dir != null && _dir.Opened(_def) ? $"Enter {_def.displayName}" : $"Open the sealed door ({_def.displayName})";
        public float GetHoldDuration(in InteractionContext c) => _dir != null && _dir.Opened(_def) ? 0f : 1.2f;
        public bool TryInteract(in InteractionContext c) { if (_dir == null || !_dir.DoorExposed(_def)) return false; _dir.Enter(_def); return true; }
    }

    /// <summary>The way back up from a chamber to its dig site.</summary>
    public sealed class ChamberExit : MonoBehaviour, IInteractable
    {
        ChamberDirector _dir; ChamberDefinition _def;
        public void Init(ChamberDirector d, ChamberDefinition def) { _dir = d; _def = def; }
        public Transform InteractionTransform => transform;
        public float GetPriority(in InteractionContext c) => 2f;
        public string GetPrompt(in InteractionContext c) => "Climb back up to the dig site";
        public float GetHoldDuration(in InteractionContext c) => 0f;
        public bool TryInteract(in InteractionContext c) { _dir?.Leave(_def); return true; }
    }
}
