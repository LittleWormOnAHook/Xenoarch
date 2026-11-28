using DMI.Xenoarch.Player;
using UnityEngine;

namespace DMI.Xenoarch.Excavation
{
    /// <summary>
    /// First-person models for the Sonic Excavator and Ultrasonic Brush: dark gunmetal body, emissive emitter ring and
    /// energy strip, and a "SonicMuzzle" transform the beam starts from. Built from primitives (no imported art); tool
    /// space follows HeldToolMeshFactory (origin at the grip, +Z the working end, grip hangs along -Y). Placeholder look
    /// until a modelled pass; the pose in the hand is unverified.
    /// </summary>
    public static class SonicHeldModel
    {
        static HeldToolMeshFactory.Materials _m;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _m = null; }

        public static bool Handles(string itemId) => SonicTools.KindOf(itemId) != SonicToolKind.None && itemId != SonicTools.LegacyPrecisionId;

        public static GameObject Build(string itemId)
        {
            if (!Handles(itemId)) return null;
            if (_m == null) _m = HeldToolMeshFactory.MakeRuntimeMaterials();
            var kind = SonicTools.KindOf(itemId);
            bool brush = kind == SonicToolKind.Brush, burster = kind == SonicToolKind.Burster;
            var root = new GameObject(brush ? "Held_SonicBrush" : burster ? "Held_SonicBurster" : "Held_SonicExcavator");
            if (burster)
            {
                Part(root, PrimitiveType.Cube, new Vector3(0f, 0.025f, 0.11f), new Vector3(0.08f, 0.09f, 0.26f), Vector3.zero, _m.metalDark);
                Part(root, PrimitiveType.Cube, new Vector3(0f, -0.055f, 0.0f), new Vector3(0.045f, 0.12f, 0.055f), new Vector3(-10f, 0f, 0f), _m.rubber);
                Part(root, PrimitiveType.Cube, new Vector3(0f, 0.073f, 0.11f), new Vector3(0.014f, 0.006f, 0.22f), Vector3.zero, _m.emit);
                Part(root, PrimitiveType.Cylinder, new Vector3(0.05f, 0.025f, 0.30f), new Vector3(0.025f, 0.07f, 0.025f), new Vector3(90f, 0f, 0f), _m.metal);
                Part(root, PrimitiveType.Cylinder, new Vector3(-0.05f, 0.025f, 0.30f), new Vector3(0.025f, 0.07f, 0.025f), new Vector3(90f, 0f, 0f), _m.metal);
                Part(root, PrimitiveType.Cylinder, new Vector3(0f, 0.025f, 0.33f), new Vector3(0.035f, 0.09f, 0.035f), new Vector3(90f, 0f, 0f), _m.emit);
                Muzzle(root, new Vector3(0f, 0.025f, 0.43f));
            }
            else if (brush)
            {
                Part(root, PrimitiveType.Cube, new Vector3(0f, 0.015f, 0.09f), new Vector3(0.04f, 0.045f, 0.2f), Vector3.zero, _m.metalDark);
                Part(root, PrimitiveType.Cube, new Vector3(0f, -0.04f, 0.0f), new Vector3(0.035f, 0.09f, 0.045f), new Vector3(-8f, 0f, 0f), _m.rubber);
                Part(root, PrimitiveType.Cube, new Vector3(0f, 0.04f, 0.09f), new Vector3(0.01f, 0.005f, 0.16f), Vector3.zero, _m.emit);
                Part(root, PrimitiveType.Sphere, new Vector3(0f, 0.015f, 0.205f), new Vector3(0.05f, 0.05f, 0.05f), Vector3.zero, _m.emit);
                Muzzle(root, new Vector3(0f, 0.015f, 0.235f));
            }
            else
            {
                Part(root, PrimitiveType.Cube, new Vector3(0f, 0.02f, 0.10f), new Vector3(0.065f, 0.075f, 0.24f), Vector3.zero, _m.metalDark);
                Part(root, PrimitiveType.Cube, new Vector3(0f, 0.062f, 0.10f), new Vector3(0.012f, 0.006f, 0.2f), Vector3.zero, _m.emit);
                Part(root, PrimitiveType.Cube, new Vector3(0f, -0.05f, 0.0f), new Vector3(0.04f, 0.11f, 0.05f), new Vector3(-10f, 0f, 0f), _m.rubber);
                Part(root, PrimitiveType.Cube, new Vector3(0.038f, 0.025f, 0.06f), new Vector3(0.012f, 0.04f, 0.09f), Vector3.zero, _m.emit);
                Part(root, PrimitiveType.Cylinder, new Vector3(0f, 0.02f, 0.27f), new Vector3(0.045f, 0.06f, 0.045f), new Vector3(90f, 0f, 0f), _m.metal);
                Part(root, PrimitiveType.Cylinder, new Vector3(0f, 0.02f, 0.335f), new Vector3(0.068f, 0.008f, 0.068f), new Vector3(90f, 0f, 0f), _m.emit);
                Muzzle(root, new Vector3(0f, 0.02f, 0.36f));
            }
            var align = root.AddComponent<HeldToolAlign>();
            align.ApplyPreset(HeldToolMeshFactory.ScannerId);   // pistol grip pose, same as the scanner
            return root;
        }

        static void Muzzle(GameObject root, Vector3 pos)
        {
            var m = new GameObject("SonicMuzzle");
            m.transform.SetParent(root.transform, false);
            m.transform.localPosition = pos;
        }

        static void Part(GameObject root, PrimitiveType type, Vector3 pos, Vector3 scale, Vector3 euler, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            var col = go.GetComponent<Collider>(); if (col != null) Object.Destroy(col);
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = pos; go.transform.localEulerAngles = euler; go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>(); r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
