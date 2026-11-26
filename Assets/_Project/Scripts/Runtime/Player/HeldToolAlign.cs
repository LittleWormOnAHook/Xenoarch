using UnityEngine;

namespace DMI.Xenoarch.Player
{
    /// <summary>
    /// Local pose applied when this tool prefab is parented to HandSlot_R (identity child of hand_r).
    /// Values are a starting grip for the FPS_Starter_Pack right hand and can be tweaked on the prefab
    /// without code changes if the tool sits wrong in the fist.
    /// </summary>
    public sealed class HeldToolAlign : MonoBehaviour
    {
        [Tooltip("Local position on HandSlot_R.")]
        public Vector3 slotPosition = new Vector3(0.03f, 0.01f, 0.04f);
        [Tooltip("Local euler angles on HandSlot_R. Tool +Z is the working end.")]
        public Vector3 slotEuler = new Vector3(0f, 90f, 90f);

        public void ApplyTo(Transform t)
        {
            if (t == null) return;
            t.localPosition = slotPosition;
            t.localRotation = Quaternion.Euler(slotEuler);
            t.localScale = Vector3.one;
        }

        public void ApplyPreset(string itemId)
        {
            // Shaft tools and pistol tools share one grip so they all sit in the same fist.
            // Pistol grips hang along -Y in tool space, which becomes "down out of the palm" after this rotation.
            slotPosition = new Vector3(0.03f, 0.012f, 0.04f);
            slotEuler = new Vector3(0f, 90f, 90f);
            switch (itemId)
            {
                case HeldToolMeshFactory.ScannerId:
                case HeldToolMeshFactory.DrillId:
                    slotPosition = new Vector3(0.025f, 0.008f, 0.035f);
                    break;
                case HeldToolMeshFactory.ShovelId:
                    slotPosition = new Vector3(0.03f, 0.01f, 0.045f);
                    break;
                case HeldToolMeshFactory.ProbeId:
                    slotPosition = new Vector3(0.028f, 0.01f, 0.038f);
                    break;
            }
        }
    }
}
