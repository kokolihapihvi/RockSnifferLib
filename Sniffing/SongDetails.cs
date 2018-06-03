using RockSnifferLib.Logging;
using System;
using System.Drawing;

namespace RockSnifferLib.Sniffing
{
    [Serializable]
    public class SongDetails
    {
        public string songID;
        public string songName;
        public string artistName;
        public string albumName;

        public float songLength = 0;

        public int albumYear = 0;
        public int numArrangements = 0;

        public ToolkitDetails toolkit;

        [NonSerialized]
        public Image albumArt;

        public void Print()
        {
            //Print details into the console if they are valid
            if (Logger.logSongDetails && IsValid())
            {
                Logger.Log("{6}: {0} - {1}, album:{2}, yr:{3}, len:{4}, art:{5}", artistName, songName, albumName, albumYear, songLength, (albumArt != null) ? "Y" : "N", songID);
            }

            //Print warning if there are more than 6 arrangements (RS crash)
            if (numArrangements >= 6)
            {
                Logger.LogError("WARNING: {0} - {1} has too many ({2}) arrangements", artistName, songName, numArrangements);
            }
        }

        /// <summary>
        /// Returns true if this SongDetails object seems valid (has valid field values)
        /// </summary>
        /// <returns>True if SongDetails seems valid</returns>
        public bool IsValid()
        {
            return !(songLength == 0 && albumYear == 0 && numArrangements == 0);
        }
    }
}
