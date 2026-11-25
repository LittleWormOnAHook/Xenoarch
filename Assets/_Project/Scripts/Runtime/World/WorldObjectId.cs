using System;
using UnityEngine;

namespace DMI.Xenoarch.World
{
    /// <summary>
    /// Stable id for a scene object whose state persists (pickups, later artifacts/excavation volumes).
    /// Generated once in the editor. Duplicating the object in the editor copies the id: run
    /// Xenoarch > Validate > Check Scene World Ids to catch that.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldObjectId : MonoBehaviour
    {
        [SerializeField] private string id;
        public string Id => id;

        public void SetIdForTooling(string value) => id = value;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrEmpty(id)) id = "wo." + Guid.NewGuid().ToString("N");
        }
#endif
    }
}
