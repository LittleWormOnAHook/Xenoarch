using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace DMI.Xenoarch.Ship
{
    /// <summary>
    /// Wraps a Creepy Cat trigger door (DoorDoubleSlide / DoorDoubleRotate on the same GameObject): reads its door
    /// leaves and settings by reflection (the kit lives in Assembly-CSharp), disables it, and drives the leaves itself
    /// with Xenoarch rules: player-only trigger (no props/physics objects), lock and power state, occupancy count
    /// (no flicker with several bodies) and a null-safe AudioSource. Vendor files stay untouched.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class XenoarchDoor : MonoBehaviour
    {
        public enum Motion { Slide, Rotate }
        public enum Axis { X, Y, Z }

        [Tooltip("Stable id for later persistence (locks set by progression).")] public string doorId;
        public bool locked;
        public bool powered = true;
        [Tooltip("Only the player (CharacterController) opens the door.")] public bool playerOnly = true;

        [Header("Leaves (copied from the vendor component on Awake if empty)")]
        public Motion motion = Motion.Slide;
        public Transform doorL;
        public Transform doorR;
        public Axis axis = Axis.X;
        public float speed = 2f;
        [Tooltip("Slide distance (m) or open angle (deg).")] public float openAmount = 2f;
        public float closeAngle;

        readonly HashSet<Collider> _inside = new HashSet<Collider>();
        Vector3 _restL, _restR;
        Quaternion _rotL, _rotR;
        float _point;
        AudioSource _audio;
        bool _wasOpen;

        public bool IsOpen => _point > 0.5f;
        public bool WantsOpen => !locked && powered && _inside.Count > 0;

        void Awake()
        {
            AdoptVendor("DoorDoubleSlide", Motion.Slide);
            AdoptVendor("DoorDoubleRotate", Motion.Rotate);
            _audio = GetComponent<AudioSource>();
            if (doorL) { _restL = doorL.localPosition; _rotL = doorL.localRotation; }
            if (doorR) { _restR = doorR.localPosition; _rotR = doorR.localRotation; }
            if (string.IsNullOrEmpty(doorId)) doorId = $"door.{gameObject.scene.name}.{name}".ToLowerInvariant().Replace(' ', '_');
        }

        void AdoptVendor(string typeName, Motion m)
        {
            var c = GetComponent(typeName) as Behaviour;
            if (c == null) return;
            var t = c.GetType();
            T Get<T>(string f, T fallback) { var fi = t.GetField(f, BindingFlags.Public | BindingFlags.Instance); return fi != null && fi.GetValue(c) is T v ? v : fallback; }
            motion = m;
            if (doorL == null) doorL = Get<Transform>("doorL", null);
            if (doorR == null) doorR = Get<Transform>("doorR", null);
            var dirField = t.GetField("directionType");
            if (dirField != null) axis = (Axis)(int)dirField.GetValue(c);
            if (m == Motion.Slide) { speed = Get("speed", speed); openAmount = Get("openDistance", openAmount); }
            else { speed = Get("openSmooth", speed); openAmount = Get("doorOpenAngle", 90f); closeAngle = Get("doorCloseAngle", 0f); }
            c.enabled = false;
        }

        bool Accepts(Collider other)
        {
            if (other == null || other.isTrigger) return false;
            return !playerOnly || other is CharacterController || other.GetComponentInParent<CharacterController>() != null;
        }

        void OnTriggerEnter(Collider other) { if (Accepts(other)) _inside.Add(other); }
        void OnTriggerExit(Collider other) { _inside.Remove(other); }

        public void SetLocked(bool value) => locked = value;
        public void SetPowered(bool value) => powered = value;

        void Update()
        {
            _inside.RemoveWhere(c => c == null || !c.enabled || !c.gameObject.activeInHierarchy);
            bool open = WantsOpen;
            if (open != _wasOpen) { _wasOpen = open; if (_audio != null && _audio.clip != null) _audio.Play(); }
            _point = Mathf.Lerp(_point, open ? 1f : 0f, Time.deltaTime * speed);

            if (motion == Motion.Slide)
            {
                Vector3 dir = axis == Axis.X ? Vector3.right : axis == Axis.Y ? Vector3.up : Vector3.back;
                if (doorL) doorL.localPosition = _restL + dir * _point * openAmount;
                if (doorR) doorR.localPosition = _restR - dir * _point * openAmount;
            }
            else
            {
                float a = Mathf.Lerp(closeAngle, openAmount, _point);
                Vector3 e = axis == Axis.X ? Vector3.right : axis == Axis.Y ? Vector3.up : Vector3.forward;
                if (doorL) doorL.localRotation = _rotL * Quaternion.Euler(e * (a - closeAngle));
                if (doorR) doorR.localRotation = _rotR * Quaternion.Euler(-e * (a - closeAngle));
            }
        }
    }
}
