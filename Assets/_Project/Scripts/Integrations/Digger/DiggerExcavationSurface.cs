using Digger.Modules.Core.Sources;
using Digger.Modules.Runtime.Sources;
using DMI.Xenoarch.Excavation;
using UnityEngine;

namespace DMI.Xenoarch.Integrations.Digger
{
    /// <summary>
    /// Thin adapter: Xenoarch excavation strokes -> Digger PRO runtime API (DiggerMasterRuntime). No vendor files are
    /// modified. Put this next to the scene's DiggerMasterRuntime (Tools > Digger > Setup for runtime).
    /// Persistence: Digger writes its own voxel data under persistentDataPath/DiggerData/&lt;prefix&gt;; we use the
    /// Xenoarch save slot as the prefix so each slot has its own dug terrain. Gameplay cell data stays in our save.
    /// </summary>
    [AddComponentMenu("Xenoarch/Excavation/Digger Excavation Surface")]
    public sealed class DiggerExcavationSurface : MonoBehaviour, IExcavationSurface
    {
        [SerializeField] DiggerMasterRuntime digger;
        [Tooltip("Digger brush used for Remove/Fill/Smooth strokes.")]
        [SerializeField] BrushType brush = BrushType.Sphere;

        public bool IsBusy => digger == null || digger.IsRunningAsync;

        void Awake()
        {
            if (digger == null) digger = FindAnyObjectByType<DiggerMasterRuntime>();
            if (digger == null) Debug.LogWarning("[Xenoarch] DiggerExcavationSurface: no DiggerMasterRuntime in scene", this);
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
            return digger.ModifyAsyncBuffured(edit.position, brush, action, edit.materialIndex,
                Mathf.Clamp01(edit.strength), Mathf.Max(0.05f, edit.radius));
        }

        public void Persist(string saveSlot)
        {
            if (digger == null) return;
            digger.SetPersistenceDataPathPrefix(saveSlot);
            digger.PersistAll();
        }
    }
}
