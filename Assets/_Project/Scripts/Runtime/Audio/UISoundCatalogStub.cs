using UnityEngine;

namespace DMI.Xenoarch.Audio
{
    /// <summary>Resources stub that points at the project-side UI and footstep catalogs.</summary>
    [CreateAssetMenu(menuName = "Xenoarch/Audio/UI Sound Catalog Stub", fileName = "UISoundCatalogStub")]
    public sealed class UISoundCatalogStub : ScriptableObject
    {
        public const string ResourcesPath = "Audio/UISoundCatalogStub";
        public UISoundCatalog ui;
        public ProSoundFootstepCatalog footsteps;
    }
}
