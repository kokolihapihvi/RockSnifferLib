namespace RockSnifferLib.RSHelpers
{
    /// <summary>
    /// Rocksmith's pause-menu overlay byte. TopOverlay also covers the main menu's
    /// Tools overlay, so only treat it as a pause together with the song state.
    /// </summary>
    public enum PauseMenuMode
    {
        /// <summary>No overlay</summary>
        None = 0,
        /// <summary>Second-level overlay, e.g. the tuner opened from the pause menu</summary>
        SubOverlay = 1,
        /// <summary>Pause menu, mixer, restart confirmation</summary>
        TopOverlay = 2,
        /// <summary>Byte didn't match a known value</summary>
        Unknown = 255
    }
}
