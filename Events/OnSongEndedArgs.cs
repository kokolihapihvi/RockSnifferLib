using RockSnifferLib.RSHelpers;
using RockSnifferLib.Sniffing;
using System;

namespace RockSnifferLib.Events
{
    public class OnSongEndedArgs : EventArgs
    {
        public SongDetails song;
        public DateTime timestamp;
        public bool completed;
        public bool paused;

        // Copied from the start of the run; in Nonstop Play the live readout may
        // already be on the next song.
        public string arrangementID;
        public string path;
        public string tuning;
        public bool wasNonstopMode;
        public bool wasMultiplayerMode;

        // Last readout of the run; on a restart the live one is already reset
        public RSMemoryReadout readout;
    }
}
