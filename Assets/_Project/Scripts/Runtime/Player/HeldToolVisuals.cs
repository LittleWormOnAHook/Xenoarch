using System.Collections.Generic;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Lighting;
using DMI.Xenoarch.Excavation;
using DMI.Xenoarch.Exploration;
using UnityEngine;

namespace DMI.Xenoarch.Player
{
    /// <summary>
    /// Shows the matching first-person tool model on <see cref="FirstPersonArms.RightSlot"/> when a tool is
    /// equipped from the hotbar, and raises the arm via <see cref="FirstPersonArms.SetHeld"/>. Hidden on
    /// holster or tool switch. The probe and drill only appear when those items are actually owned
    /// (their equip paths already refuse if the inventory count is 0). Added by <see cref="PlayerToolsBootstrap"/>.
    /// </summary>
    public sealed class HeldToolVisuals : MonoBehaviour
    {
        FirstPersonArms _arms;
        DigTool _dig;
        DepthProbeTool _probe;
        SurfaceScannerTool _scanner;
        HeldToolCatalog _catalog;
        HeldToolMeshFactory.Materials _runtimeMats;
        readonly Dictionary<string, GameObject> _instances = new Dictionary<string, GameObject>();
        string _shown;
        bool _held;

        void Awake()
        {
            _arms = GetComponentInChildren<FirstPersonArms>(true);
            _dig = GetComponent<DigTool>();
            _probe = GetComponent<DepthProbeTool>();
            _scanner = GetComponentInChildren<SurfaceScannerTool>(true);
            _catalog = HeldToolCatalog.Load();
        }

        void OnDisable()
        {
            Show(null);
            SetHeld(false);
        }

        public bool HasTool => CurrentId() != null;

        void LateUpdate()
        {
            if (_arms == null) _arms = GetComponentInChildren<FirstPersonArms>(true);
            if (_dig == null) _dig = GetComponent<DigTool>();
            if (_probe == null) _probe = GetComponent<DepthProbeTool>();
            if (_scanner == null) _scanner = GetComponentInChildren<SurfaceScannerTool>(true);
            if (_arms != null && _arms.ForcedHidden)
            {
                Show(null);
                foreach (var inst in _instances.Values) if (inst != null) inst.SetActive(false);
                return;
            }
            Show(CurrentId());
        }

        string CurrentId()
        {
            if (XenoarchRuntime.Session == null) return null;
            if (_scanner != null && _scanner.Equipped) return HeldToolMeshFactory.ScannerId;
            if (_probe != null && _probe.Equipped) return HeldToolMeshFactory.ProbeId;
            if (_dig != null && _dig.Equipped >= 0)
            {
                var all = DigTools.All;
                int i = _dig.Equipped;
                if (i >= 0 && i < all.Length) return all[i].itemId;
            }
            var lighting = GetComponent<PlayerLighting>() ?? GetComponentInParent<PlayerLighting>();
            if (lighting != null && lighting.FlashlightEquipped) return PlayerLighting.FlashlightId;
            return null;
        }

        void Show(string itemId)
        {
            if (itemId == _shown) { SetHeld(itemId != null); return; }
            if (_shown != null && _instances.TryGetValue(_shown, out var prev) && prev != null)
                prev.SetActive(false);
            _shown = itemId;
            if (itemId == null) { SetHeld(false); return; }
            var inst = Ensure(itemId);
            if (inst != null)
            {
                inst.SetActive(true);
                var align = inst.GetComponent<HeldToolAlign>();
                if (align != null) align.ApplyTo(inst.transform);
            }
            SetHeld(true);
        }

        void SetHeld(bool held)
        {
            if (_held == held) return;
            _held = held;
            if (_arms != null) _arms.SetHeld(Hand.Right, held);
        }

        GameObject Ensure(string itemId)
        {
            if (_instances.TryGetValue(itemId, out var existing) && existing != null) return existing;
            var slot = _arms != null ? _arms.RightSlot : null;
            if (slot == null) return null;
            GameObject prefab = _catalog != null ? _catalog.PrefabFor(itemId) : null;
            GameObject inst = SonicHeldModel.Build(itemId);   // sonic excavator / brush: procedural model
            if (inst != null) inst.transform.SetParent(slot, false);
            else if (prefab != null)
            {
                inst = Instantiate(prefab, slot);
                inst.name = prefab.name;
            }
            else
            {
                if (_runtimeMats == null) _runtimeMats = HeldToolMeshFactory.MakeRuntimeMaterials();
                inst = HeldToolMeshFactory.Build(itemId, _runtimeMats);
                if (inst == null) return null;
                inst.transform.SetParent(slot, false);
            }
            inst.transform.SetParent(slot, false);
            var align = inst.GetComponent<HeldToolAlign>();
            if (align != null) align.ApplyTo(inst.transform);
            else
            {
                inst.transform.localPosition = Vector3.zero;
                inst.transform.localRotation = Quaternion.identity;
                inst.transform.localScale = Vector3.one;
            }
            foreach (var col in inst.GetComponentsInChildren<Collider>(true)) col.enabled = false;
            _instances[itemId] = inst;
            return inst;
        }
    }
}
