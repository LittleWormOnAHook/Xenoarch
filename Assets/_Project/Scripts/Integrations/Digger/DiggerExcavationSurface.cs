using Digger.Modules.Core.Sources;
using Digger.Modules.Runtime.Sources;
using DMI.Xenoarch.Excavation;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;

namespace DMI.Xenoarch.Integrations.Digger
{
    /// <summary>
    /// Thin adapter: Xenoarch excavation strokes -> Digger PRO runtime API (DiggerMasterRuntime). No vendor files are
    /// modified. Lives next to the scene's DiggerMasterRuntime (created by Xenoarch > Setup > 10 Phase 1C).
    /// The excavation grid is the source of truth: each cut cell is carved here, and on load the saved grid is replayed
    /// (ExcavationSite.Replay), so Digger's own persistence is not needed. Edits made in Play mode are reverted by Digger
    /// when the Editor leaves Play mode.
    /// </summary>
    [AddComponentMenu("Xenoarch/Excavation/Digger Excavation Surface")]
    [DefaultExecutionOrder(-100)]   // before DiggerMasterRuntime (-10) initialises the voxel grid
    public sealed class DiggerExcavationSurface : MonoBehaviour, IExcavationSurface
    {
        [SerializeField] DiggerMasterRuntime digger;
        [Tooltip("Digger brush used for Remove/Fill/Smooth strokes.")]
        [SerializeField] BrushType brush = BrushType.Sphere;
        [Tooltip("Queued async edits (one stroke cuts several cells; load replays many).")]
        [SerializeField] int bufferSize = 512;
        [Tooltip("Runtime voxel height (m). Digger's default is 1 m: with ~5 cm horizontal voxels a dig sphere under 0.5 m " +
                 "often touches no voxel layer at all, so holes stopped in ~1 m steps with spiky walls. Applied before Digger " +
                 "initialises (Play only, the scene is not changed). 0 = keep the scene setting.")]
        [SerializeField] float runtimeVoxelHeight = 0.1f;
        [Tooltip("t301u: dug holes cast/receive no shadows so their walls and floor stay lit like the ground around them.")]
        [SerializeField] bool shadowlessDigs = true;

        float _minRadius = 0.15f;
        public bool IsBusy => digger == null || digger.IsRunningAsync;
        public float MinRadius => _minRadius;

        void Awake()
        {
            if (shadowlessDigs) HookChunkShadows();
            if (digger == null) digger = FindAnyObjectByType<DiggerMasterRuntime>();
            if (digger == null) { Debug.LogWarning("[Xenoarch] DiggerExcavationSurface: no DiggerMasterRuntime in scene", this); return; }
            digger.BufferSize = Mathf.Max(1, bufferSize);
            var master = FindAnyObjectByType<DiggerMaster>();
            var t = Terrain.activeTerrain;
            if (master != null && runtimeVoxelHeight > 0.01f)
            {
                master.AutoVoxelHeight = false;
                master.VoxelHeight = runtimeVoxelHeight;
            }
            if (master != null && t != null && t.terrainData != null)
            {
                float horiz = t.terrainData.heightmapScale.x / Mathf.Max(1, master.ResolutionMult);
                float vert = master.AutoVoxelHeight ? horiz : master.VoxelHeight;
                // A carve must span at least ~1.5 voxels in every axis or it can fall between voxel samples (no visible cut).
                _minRadius = Mathf.Max(0.1f, horiz * 1.5f, vert * 1.6f);
            }
        }

        // Runs before DiggerMasterRuntime (-10) so chunks it loads or carves come through the hook. Runtime only:
        // the listener is not serialized, so scenes and the vendor Digger files stay untouched.
        void HookChunkShadows()
        {
            foreach (var ds in FindObjectsByType<DiggerSystem>(FindObjectsInactive.Include))
            {
                if (ds.onChunkObjectCreated == null) ds.onChunkObjectCreated = new UnityEvent<ChunkObject>();
                ds.onChunkObjectCreated.RemoveListener(MakeShadowless);
                ds.onChunkObjectCreated.AddListener(MakeShadowless);
            }
        }

        static void MakeShadowless(ChunkObject chunk)
        {
            if (chunk == null) return;
            foreach (var r in chunk.GetComponentsInChildren<MeshRenderer>(true))
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
        }

        void Start()
        {
            if (shadowlessDigs)
                foreach (var c in FindObjectsByType<ChunkObject>(FindObjectsInactive.Include)) MakeShadowless(c);
            var ds = FindAnyObjectByType<DiggerSystem>();
            if (ds != null)
                Debug.Log($"[Xenoarch] Digger voxels: {ds.HeightmapScale.x:0.###} x {ds.HeightmapScale.y:0.###} m (h x v), min carve radius {_minRadius:0.##} m");
        }

        public bool Apply(in ExcavationEdit edit)
        {
            if (digger == null) return false;
            var action = edit.stroke switch
            {
                ExcavationStroke.Fill => ActionType.Add,
                ExcavationStroke.Smooth => ActionType.Smooth,
                ExcavationStroke.TerrainHole => ActionType.PaintHoles,
                _ => ActionType.Dig,
            };
            return digger.ModifyAsyncBuffured(edit.position, brush, action, Mathf.Max(0, edit.materialIndex),
                Mathf.Clamp01(edit.strength), Mathf.Max(_minRadius, edit.radius));
        }

        public void Persist(string saveSlot)
        {
            if (digger == null) return;
            digger.SetPersistenceDataPathPrefix(saveSlot);
            digger.PersistAll();
        }
    }
}
