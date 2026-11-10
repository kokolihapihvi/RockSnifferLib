namespace RockSnifferLib.RSHelpers
{
    /// <summary>
    /// Path (Lead/Rhythm/Bass) selected in the menus. Values are the raw memory byte.
    /// </summary>
    public enum RSPath
    {
        /// <summary>Unrecognized byte or failed read</summary>
        Unknown = 0x00,
        Lead = 0x01,
        Rhythm = 0x02,
        Bass = 0x04
    }
}
