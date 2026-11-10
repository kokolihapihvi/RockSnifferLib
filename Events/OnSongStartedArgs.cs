using RockSnifferLib.Sniffing;
using System;

namespace RockSnifferLib.Events
{
    public class OnSongStartedArgs : EventArgs
    {
        public SongDetails song;
        public DateTime timestamp;

        // If no arrangement could be resolved, arrangementID is null and path and
        // tuning are "unknown"
        public string arrangementID;
        public string path;
        public string tuning;

        public bool wasNonstopMode;

        // Note data is single-player only, so multiplayer runs aren't usable for history
        public bool wasMultiplayerMode;
    }
}
