using RockSnifferLib.Sniffing;
using System;

namespace RockSnifferLib.Events
{
    public class OnSongStartedArgs : EventArgs
    {
        public SongDetails song;
    }
}
