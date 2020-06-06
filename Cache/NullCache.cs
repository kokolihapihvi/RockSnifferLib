using RockSnifferLib.Sniffing;
using System.Collections.Generic;

namespace RockSnifferLib.Cache
{
    public class NullCache : ICache
    {

        public void Add(string filepath, Dictionary<string, SongDetails> allDetails)
        {

        }

        public void Remove(string filepath, List<string> songIDs)
        {
            
        }

        public bool Contains(string filepath, string fileHash)
        {
            return false;
        }

        public SongDetails Get(string filepath, string songID)
        {
            return null;
        }

        public SongDetails Get(string SongID)
        {
            return null;
        }
    }
}
