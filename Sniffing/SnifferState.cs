namespace RockSnifferLib.Sniffing
{
    /// <summary>
    /// Sniffer states
    /// </summary>
    public enum SnifferState
    {
        /// <summary>
        /// Unknown state
        /// </summary>
        NONE,

        /// <summary>
        /// In menus
        /// </summary>
        IN_MENUS,

        /// <summary>
        /// Song has been selected
        /// </summary>
        SONG_SELECTED,

        /// <summary>
        /// Song timer is starting
        /// </summary>
        SONG_STARTING,

        /// <summary>
        /// Song is playing
        /// </summary>
        SONG_PLAYING,

        /// <summary>
        /// Song is about to end
        /// </summary>
        SONG_ENDING
    }
}
