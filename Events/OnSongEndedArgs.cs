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

        // Same values as the run's OnSongStarted. Kept from the start because in
        // Nonstop Play the live readout can already point at the next song.
        public string arrangementID;
        public string path;
        public string tuning;
        public bool wasNonstopMode;
        public bool wasMultiplayerMode;

        // Last readout of the run. On a restart the live readout is already zeroed
        // for the next attempt, so read end-of-run stats from here.
        public RSMemoryReadout readout;
    }
}
