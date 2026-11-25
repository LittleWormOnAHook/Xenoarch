using UnityEngine;

namespace DMI.Xenoarch.Excavation
{
    /// <summary>What a tool stroke does to the ground. Mapped to the backend's own operations by the adapter.</summary>
    public enum ExcavationStroke
    {
        Remove,     // shovel / precision tool / drill: take material away
        Fill,       // put material back (debug, backfill)
        Smooth,     // brush-off / tidy edges
        TerrainHole // open a Unity Terrain hole (entrances into underground areas)
    }

    /// <summary>One tool stroke, in world space. Gameplay (cells, artifact damage) decides the stroke; the surface only renders it.</summary>
    public struct ExcavationEdit
    {
        public Vector3 position;
        public float radius;
        /// <summary>0..1 strength of the stroke (Digger "opacity").</summary>
        public float strength;
        public ExcavationStroke stroke;
        /// <summary>Backend material/texture layer index (e.g. regolith, compacted, rock).</summary>
        public int materialIndex;
    }

    /// <summary>
    /// Visual + collision ground deformation behind an excavation site (Phase 1C). The authoritative gameplay state
    /// (per-site cell grid, artifact exposure, condition) lives in Xenoarch data and saves; implementations only mirror
    /// strokes into the world mesh. First implementation: Digger PRO (DMI.Xenoarch.Integrations.Digger).
    /// </summary>
    public interface IExcavationSurface
    {
        bool IsBusy { get; }
        /// <summary>Smallest useful stroke radius (about one voxel); smaller strokes are enlarged to this.</summary>
        float MinRadius { get; }
        /// <summary>Queues/applies a stroke. Returns false if the backend refused it (busy buffer, indestructible area).</summary>
        bool Apply(in ExcavationEdit edit);
        /// <summary>Writes the backend's own mesh/voxel data for the given save slot.</summary>
        void Persist(string saveSlot);
    }
}
