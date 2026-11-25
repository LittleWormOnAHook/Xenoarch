using Digger.Modules.Core.Sources;
using Digger.Modules.Runtime.Sources;
using DMI.Xenoarch.Excavation;
using UnityEngine;

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
    public sealed class DiggerExcavationSurface : MonoBehaviour, IExcavationSurface
    {
        [SerializeField] DiggerMasterRuntime digger;
        [Tooltip("Digger brush used for Remove/Fill/Smooth strokes.")]
        [SerializeField] BrushType brush = BrushType.Sphere;
        [Tooltip("Queued async edits (one stroke cuts several cells; load replays many).")]
        [SerializeField] int bufferSize = 512;

        float _minRadius = 0.15f;
        public bool IsBusy => digger == null || digger.IsRunningAsync;
        public float MinRadius => _minRadius;

        void Awake()
        {
            if (digger == null) digger = FindAnyObjectByType<DiggerMasterRuntime>();
            if (digger == null) { Debug.LogWarning("[Xenoarch] DiggerExcavationSurface: no DiggerMasterRuntime in scene", this); return; }
            digger.BufferSize = Mathf.Max(1, bufferSize);
            var master = FindAnyObjectByType<DiggerMaster>();
            var t = Terrain.activeTerrain;
            if (master != null && t != null && t.terrainData != null)
                _minRadius = Mathf.Max(0.1f, t.terrainData.heightmapScale.x / Mathf.Max(1, master.ResolutionMult));
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
