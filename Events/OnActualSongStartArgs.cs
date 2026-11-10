using RockSnifferLib.Sniffing;
using System;

namespace RockSnifferLib.Events
{
    public class OnActualSongStartArgs : EventArgs
    {
        public SongDetails song;
        public DateTime timestamp;
        public string arrangementID;  // Resolved arrangement ID (may be null if unresolved)
        public string path;           // Arrangement type (Lead/Rhythm/Bass)
        public string tuning;         // Tuning (e.g., "E Standard", "D Standard (Capo Fret 2)")
        // True if the song started in a Nonstop Play gameStage (nsp_main /
        // nonstopplayhub / nonstopplaygame). Set by Sniffer.cs at song start.
        // Informational only.
        public bool wasNonstopMode;

        // True if the song started in a Multiplayer gameStage (split_game, mp_*,
        // duet_*, h2h_* - RSMode.MULTIPLAYER). Used to gate playthrough tracking:
        // multi-user note data isn't tracked. See Sniffer.cs.
        public bool wasMultiplayerMode;
    }
}
