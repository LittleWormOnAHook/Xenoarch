using System.Collections.Generic;
using DMI.Xenoarch.Core;
using UnityEngine;

namespace DMI.Xenoarch.Galaxy
{
    /// <summary>Environment of a place: gravity, atmosphere, lighting/fog and an optional URP Volume profile.</summary>
    [CreateAssetMenu(menuName = "Xenoarch/Galaxy/Environment Profile", fileName = "Env_")]
    public sealed class EnvironmentProfile : DefinitionAsset
    {
        protected override string DefaultIdPrefix => "env.";
        public string displayName;
        [Tooltip("Downward acceleration, m/s^2 (Earth 9.81).")] public float gravity = 9.81f;
        public string atmosphere = "None";
        public bool breathable;
        public float surfaceTemperatureC;
        public bool applyLighting = true;
        public Color ambientColor = new Color(0.25f, 0.25f, 0.28f);
        public bool fog;
        public Color fogColor = Color.gray;
        [Min(0f)] public float fogDensity = 0.01f;
        [Tooltip("Optional URP Volume profile applied by a global volume while in this environment.")]
        public UnityEngine.Rendering.VolumeProfile volumeProfile;
        [Tooltip("Optional sky for this environment's scenes (surfaces). Ship/orbit scenes use the body's orbit skybox instead.")]
        public Material skybox;
    }
}

