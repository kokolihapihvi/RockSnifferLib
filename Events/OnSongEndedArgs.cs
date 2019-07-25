using RockSnifferLib.Sniffing;
using System;

namespace RockSnifferLib.Events
{
    public class OnSongEndedArgs : EventArgs
    {
        public SongDetails song;
    }
}
