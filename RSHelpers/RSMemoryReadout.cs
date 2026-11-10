using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using RockSnifferLib.Logging;
using RockSnifferLib.RSHelpers.NoteData;
using System;

namespace RockSnifferLib.RSHelpers
{
    [Serializable]
    public class RSMemoryReadout
    {
        public float songTimer = 0;

        public string songID = "";
        public string arrangementID = "";
        public string gameStage = "";

        /// <summary>
        /// Path selected in the menus. Unlike arrangementID, this is valid in Nonstop Play.
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public RSPath currentPath = RSPath.Unknown;

        [JsonConverter(typeof(StringEnumConverter))]
        public PauseMenuMode pauseMenuMode = PauseMenuMode.None;

        /// <summary>
        /// Pause overlay is open. Use SnifferState to know if a song is paused.
        /// </summary>
        public bool isPaused = false;

        [JsonConverter(typeof(StringEnumConverter))]
        public RSMode mode = RSMode.UNKNOWN;
        public INoteData noteData;

        /// <summary>
        /// Prints out this readouts details (if Logger.logMemoryOutput is enabled)
        /// </summary>
        public void Print()
        {
            if (Logger.logMemoryReadout)
            {
                Logger.Log("SID: {0}\r\nt: {1}, hits: {2}, misses: {3}\r\nstreak: {4}, hstreak: {5}, mstreak:{6}", songID, songTimer, noteData.TotalNotesHit, noteData.TotalNotesMissed, noteData.CurrentHitStreak, noteData.HighestHitStreak, noteData.CurrentMissStreak);
            }
        }

        /// <summary>
        /// Copy the fields from this readout to another
        /// </summary>
        /// <param name="copy">target readout</param>
        internal void CopyTo(ref RSMemoryReadout copy)
        {
            copy.songTimer = songTimer;

            copy.songID = songID;
            copy.arrangementID = arrangementID;
            copy.gameStage = gameStage;

            copy.currentPath = currentPath;

            copy.pauseMenuMode = pauseMenuMode;
            copy.isPaused = isPaused;

            copy.mode = mode;

            copy.noteData = noteData;
        }

        /// <summary>
        /// Returns a copy of this memory readout
        /// </summary>
        /// <returns></returns>
        public RSMemoryReadout Clone()
        {
            RSMemoryReadout copy = new RSMemoryReadout();

            CopyTo(ref copy);

            return copy;
        }
    }
}
