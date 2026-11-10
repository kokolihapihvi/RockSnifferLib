namespace RockSnifferLib.RSHelpers
{
    /// <summary>
    /// Blocking-overlay state read from Rocksmith's pause-menu mode byte (see
    /// MemoryOffsets.GetPauseMenuModePointer).
    ///
    /// NOTE: TopOverlay also fires for the main menu's Tools overlay, so this is
    /// not "paused during gameplay" by itself; combine with a SnifferState check
    /// for that. Because SubOverlay and TopOverlay both mean "in a pause
    /// sub-flow", the paused test is RSMemoryReadout.isPaused (either of the two),
    /// not == TopOverlay.
    /// </summary>
    public enum PauseMenuMode
    {
        /// <summary>No blocking overlay (gameplay, menus, song select, loading, review).</summary>
        None = 0,
        /// <summary>Sub-overlay: tuner reached from the pause menu or the main menu's Tools sub-menu, other sub-prompts.</summary>
        SubOverlay = 1,
        /// <summary>Top-level overlay: in-song pause menu, Mixer, restart confirmation, main menu's Tools overlay.</summary>
        TopOverlay = 2,
        /// <summary>Byte held a value outside 0-2. Never observed; not treated as paused.</summary>
        Unknown = 255
    }
}
