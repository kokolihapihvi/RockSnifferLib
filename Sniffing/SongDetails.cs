using Newtonsoft.Json;
using RockSnifferLib.Logging;
using RockSnifferLib.SysHelpers;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace RockSnifferLib.Sniffing
{
    [Serializable]
    public class SongDetails
    {
        public class VocalDetails
        {
            public float Time;
            public int Note;
            public float Length;
            public string? Lyric;
        }
        public List<VocalDetails> vocals = [];

        public string songID;
        public string songName;
        public string artistName;
        public string albumName;

        public float songLength = 0;

        public int albumYear = 0;
        public int NumArrangements { get { return arrangements.Count; } }

        public List<ArrangementDetails> arrangements = new List<ArrangementDetails>();

        public ToolkitDetails toolkit;

        [JsonConverter(typeof(ImageBase64Converter))]
        public Image albumArt;

        public string psarcFileHash;

        public void Print()
        {
            //Print details into the console if they are valid
            if (Logger.logSongDetails && IsValid())
            {
                Logger.Log("{6}: {0} - {1}, album:{2}, yr:{3}, len:{4}, art:{5}", artistName, songName, albumName, albumYear, songLength, (albumArt != null) ? "Y" : "N", songID);
            }

            //Print warning if there are more than 6 arrangements (RS crash)
            if (NumArrangements >= 6)
            {
                Logger.LogError("WARNING: {0} - {1} has too many ({2}) arrangements", artistName, songName, NumArrangements);
            }
        }

        /// <summary>
        /// Returns true if this SongDetails object seems valid (has valid field values)
        /// </summary>
        /// <returns>True if SongDetails seems valid</returns>
        public bool IsValid()
        {
            return !(songLength == 0 && albumYear == 0 && NumArrangements == 0);
        }
    }
}
