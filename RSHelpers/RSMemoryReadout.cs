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
        /// The user's currently-selected Path (arrangement type) at the menu level.
        /// Read from a stable byte pointer (see MemoryOffsets.GetCurrentPathPointer).
        /// Persistent across game stages, populated from launch, only changes when the
        /// user actively switches Path in options or song-select. Crucially works in
        /// Nonstop Play, where the per-song arrangement_hash pointer fails.
        /// Serialized as the member name ("Lead", "Rhythm", "Bass", "Unknown").
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public RSPath currentPath = RSPath.Unknown;

        /// <summary>
        /// Blocking-overlay state, serialized as the member name ("None",
        /// "SubOverlay", "TopOverlay", "Unknown"). See the PauseMenuMode enum for the value
        /// documentation and caveats.
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public PauseMenuMode pauseMenuMode = PauseMenuMode.None;

        /// <summary>
        /// True when pauseMenuMode is SubOverlay or TopOverlay.
        /// Raw engine signal: for "paused during a song" use SnifferState (game_state),
        /// which interprets this flag against the player's state-machine context.
        /// </summary>
        public bool isPaused = false;

        /// <summary>
        /// Current mode (LearnASong, ScoreAttack, ...) serialized as its member name in
        /// the JSON output. Addons should compare against the string form.
        /// </summary>
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
