using RockSnifferLib.Logging;
using System;

namespace RockSnifferLib.RSHelpers
{
    [Serializable]
    public class RSMemoryReadout
    {
        public float songTimer = 0;

        public string songID = "";

        public int totalNotesHit = 0;
        public int currentHitStreak = 0;
        public int highestHitStreak = 0;
        public int totalNotesMissed = 0;
        public int currentMissStreak = 0;
        public RSMode mode = RSMode.UNKNOWN;

        public int TotalNotes {
            get {
                return totalNotesMissed + totalNotesHit;
            }
        }

        /// <summary>
        /// Prints out this readouts details (if Logger.logMemoryOutput is enabled)
        /// </summary>
        public void Print()
        {
            if (Logger.logMemoryReadout)
            {
                Logger.Log("SID: {0}\r\nt: {1}, hits: {2}, misses: {3}\r\nstreak: {4}, hstreak: {5}, mstreak:{6}", songID, songTimer, totalNotesHit, totalNotesMissed, currentHitStreak, highestHitStreak, currentMissStreak);
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

            copy.mode = mode;

            copy.totalNotesHit = totalNotesHit;
            copy.currentHitStreak = currentHitStreak;
            copy.highestHitStreak = highestHitStreak;
            copy.totalNotesMissed = totalNotesMissed;
            copy.currentMissStreak = currentMissStreak;
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
