using RockSnifferLib.Sniffing;
using System;

namespace RockSnifferLib.Events
{
    public class OnSongSelectedArgs : EventArgs
    {
        public SongDetails song;
    }
}
