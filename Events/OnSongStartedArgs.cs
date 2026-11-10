using RockSnifferLib.Sniffing;
using System;

namespace RockSnifferLib.Events
{
    public class OnSongStartedArgs : EventArgs
    {
        public SongDetails song;
        public DateTime timestamp;

        // Arrangement resolved at the start of the run. arrangementID is null when
        // only a fallback could pick the arrangement; path and tuning are "unknown"
        // when nothing could.
        public string arrangementID;
        public string path;
        public string tuning;

        // Run started in a Nonstop Play gameStage. Informational.
        public bool wasNonstopMode;

        // Run started in a Multiplayer gameStage. Multi-user note data isn't
        // tracked, so history consumers skip these runs.
        public bool wasMultiplayerMode;
    }
}
