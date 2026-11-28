using System;

namespace DMI.Xenoarch.Audio
{
    /// <summary>UI and inventory cue kinds played through <see cref="UISound"/>.</summary>
    public enum UISoundEvent
    {
        Click,
        Hover,
        Tab,
        WindowOpen,
        WindowClose,
        Confirm,
        Cancel,
        Error,
        Notification,
        Pickup,
        Drop,
        SlotDrop,
        Slider,
        Typing,
        MapPing,
        TerminalPowerOn
    }
}
