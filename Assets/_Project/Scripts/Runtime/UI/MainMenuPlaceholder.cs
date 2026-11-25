using DMI.Xenoarch.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.UI
{
    /// <summary>PLACEHOLDER main menu (UI Toolkit, Resources/UI/MainMenu.uxml): New Game, Continue (quick save), Quit.</summary>
    public sealed class MainMenuPlaceholder : MonoBehaviour
    {
        public const string Uxml = "UI/MainMenu";
        Button _continue;

        void OnEnable()
        {
            UIState.SetOpen(Panels.MainMenu, true);
            var doc = XenoarchUI.CreateDocument(gameObject, Uxml, 20);
            if (doc == null) return;
            var r = doc.rootVisualElement;
            r.Q<Button>("btn-new")?.RegisterCallback<ClickEvent>(_ => { if (!Busy) XenoarchRuntime.Instance.NewGame(); });
            _continue = r.Q<Button>("btn-continue");
            _continue?.RegisterCallback<ClickEvent>(_ =>
            {
                if (Busy) return;
                XenoarchRuntime.Instance.LoadGame(XenoarchRuntime.Session.Config.quickSaveSlot);
            });
            r.Q<Button>("btn-quit")?.RegisterCallback<ClickEvent>(_ => Quit());
        }

        void OnDisable() => UIState.SetOpen(Panels.MainMenu, false);

        static bool Busy => XenoarchRuntime.Instance == null || XenoarchRuntime.Scenes.IsLoading;

        void Update()
        {
            if (_continue == null || XenoarchRuntime.Session == null) return;
            _continue.SetEnabled(XenoarchRuntime.Session.Saves.HasSave(XenoarchRuntime.Session.Config.quickSaveSlot));
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
