using System.IO;
using UnityEditor;
using UnityEngine;

namespace DMI.Xenoarch.EditorTools
{
    /// <summary>
    /// DEV / TESTING: Xenoarch/Dev menu and Temp/xenoarch_devlaunch.trigger ("planet", "rovertest" or either with ":bodyId")
    /// start Play and hand off to the runtime XenoarchDevLaunch (skip menus + ship, land on a planet, call the rover).
    /// Uses the normal Play start scene; nothing is saved (XenoarchRuntime.SuppressSaves).
    /// </summary>
    [InitializeOnLoad]
    static class XenoarchDevLaunchMenu
    {
        const string Trigger = "Temp/xenoarch_devlaunch.trigger";
        const string Key = "Xenoarch.DevLaunch";

        static XenoarchDevLaunchMenu() => EditorApplication.update += Poll;

        [MenuItem("Xenoarch/Dev/Planet + Rover (skip menus and ship)")]
        static void Planet() => Start("planet");

        [MenuItem("Xenoarch/Dev/Rover Test (skip menus, scripted drive, log CSV)")]
        static void Test() => Start("rovertest");

        [MenuItem("Xenoarch/Dev/Rover Inclinometer logging (CSV, F10 readout)")]
        static void ToggleInc() => SessionState.SetBool("Xenoarch.RoverDiag", !SessionState.GetBool("Xenoarch.RoverDiag", false));
        [MenuItem("Xenoarch/Dev/Rover Inclinometer logging (CSV, F10 readout)", true)]
        static bool ToggleIncValidate() { Menu.SetChecked("Xenoarch/Dev/Rover Inclinometer logging (CSV, F10 readout)", SessionState.GetBool("Xenoarch.RoverDiag", false)); return true; }

        static void Start(string cfg)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[DevLaunch] already in Play."); return; }
            SessionState.SetString(Key, cfg);
            Debug.Log("[DevLaunch] entering Play: " + cfg);
            EditorApplication.isPlaying = true;
        }

        static void Poll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (!File.Exists(Trigger)) return;
            string cfg = File.ReadAllText(Trigger).Trim();
            File.Delete(Trigger);
            Start(string.IsNullOrEmpty(cfg) ? "rovertest" : cfg);   // "rovertest|maxAirUpSpeed=5.5,damper=9000" applies profile overrides to a throw-away clone
        }
    }
}
