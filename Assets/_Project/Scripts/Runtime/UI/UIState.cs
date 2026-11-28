using System;
using System.Collections.Generic;
using UnityEngine;

namespace DMI.Xenoarch.UI
{
    /// <summary>
    /// Which cursor-requiring panels are open. Adapted from ASH ASHUIState (token set gating gameplay input and cursor).
    /// Cursor handling is optional so a vendor controller that manages the cursor itself can turn it off.
    /// </summary>
    public static class UIState
    {
        static readonly HashSet<string> Open = new HashSet<string>();
        public static event Action Changed;
        public static bool ManageCursor = true;
        /// <summary>Seated at the cockpit: free cursor for clicking the consoles, without blocking stand-up (E / Space).</summary>
        static bool _consoleCursor;
        public static bool ConsoleCursor
        {
            get => _consoleCursor;
            set
            {
                if (_consoleCursor == value) return;
                _consoleCursor = value;
                ApplyCursor();
            }
        }

        public static bool BlocksGameplay => Open.Count > 0;
        public static bool IsOpen(string panel) => Open.Contains(panel);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Open.Clear(); Changed = null; ManageCursor = true; _consoleCursor = false; }

        public static void SetOpen(string panel, bool open)
        {
            bool changed = open ? Open.Add(panel) : Open.Remove(panel);
            if (!changed) return;
            ApplyCursor();
            Changed?.Invoke();
        }

        public static void CloseAll()
        {
            if (Open.Count == 0) return;
            Open.Clear();
            ApplyCursor();
            Changed?.Invoke();
        }

        public static void ApplyCursor()
        {
            if (!ManageCursor) return;
            bool free = BlocksGameplay || ConsoleCursor;
            Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = free;
        }
    }

    public static class Panels
    {
        public const string MainMenu = "main_menu";
        public const string Pause = "pause";
        public const string Loading = "loading";
    }
}
