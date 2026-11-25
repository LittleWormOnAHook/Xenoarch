using System.IO;
using UnityEditor;

namespace DMI.Xenoarch.EditorTools
{
    /// <summary>
    /// One-shot: the first time these scripts compile in a project without the Bootstrap scene, run the Phase 1A setup
    /// (additive scene creation; the open scene is untouched) and the smoke check. Afterwards it does nothing; use the
    /// Xenoarch menu to rerun. Delete Library/XenoarchFirstRun.done to allow it again.
    /// </summary>
    [InitializeOnLoad]
    static class XenoarchFirstRun
    {
        const string Marker = "Library/XenoarchFirstRun.done";
        /// <summary>Create this (empty) file to have the smoke check run once after the next script reload (tooling/CI hook).</summary>
        const string SmokeTrigger = "Temp/xenoarch_smoke.trigger";

        /// <summary>Create this (empty) file to re-run "Create Foundation Assets" (idempotent) after the next script reload.</summary>
        const string AssetsTrigger = "Temp/xenoarch_assets.trigger";

        /// <summary>Create this (empty) file to build the first-person player and swap it into the scenes after the next reload.</summary>
        const string PlayerTrigger = "Temp/xenoarch_player.trigger";

        /// <summary>Create this (empty) file to convert Assets/_Creepy_Cat materials to URP after the next reload.</summary>
        const string ConvertTrigger = "Temp/xenoarch_convert.trigger";

        /// <summary>Create this (empty) file to add the Kade body + hand slots to XA_Player (prefab only, no scenes) after the next reload.</summary>
        const string BodyTrigger = "Temp/xenoarch_body.trigger";

        static XenoarchFirstRun() => EditorApplication.delayCall += TryRun;

        static void TryRun()
        {
            if (File.Exists(AssetsTrigger) && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                File.Delete(AssetsTrigger);
                XenoarchSetup.CreateFoundationAssets();
            }
            if (File.Exists(ConvertTrigger) && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                File.Delete(ConvertTrigger);
                Art.VendorMaterialConverter.ConvertCreepyCat();
            }
            if (File.Exists(PlayerTrigger) && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                File.Delete(PlayerTrigger);
                XenoarchPlayerBuilder.BuildAll();
            }
            if (File.Exists(BodyTrigger) && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                File.Delete(BodyTrigger);
                XenoarchBodyBuilder.BuildBodyAndHands();
            }
            if (File.Exists(SmokeTrigger) && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                File.Delete(SmokeTrigger);
                XenoarchSmokeCheck.Run();
            }
            if (File.Exists(Marker) || File.Exists(XenoarchSetup.ScenePath(Core.SceneNames.Bootstrap))) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += TryRun;
                return;
            }
            File.WriteAllText(Marker, System.DateTime.Now.ToString("o"));
            UnityEngine.Debug.Log("[Xenoarch] First run: running Phase 1A setup + smoke check");
            XenoarchSetup.RunAll();
            XenoarchSmokeCheck.Run();
        }
    }
}
