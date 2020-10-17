using RockSnifferLib.Sniffing;
using System.Collections.Generic;
using System.Linq;

namespace RockSnifferLib.Cache
{
    public class MemoryCache : ICache
    {
        public Dictionary<string, Dictionary<string, SongDetails>> Cache { get; } = new Dictionary<string, Dictionary<string, SongDetails>>();

        public void Add(string filepath, Dictionary<string, SongDetails> allDetails)
        {
            if (Contains(filepath, ""))
            {
                return;
            }

            Cache.Add(filepath, allDetails);
        }

        public void Remove(string filepath, List<string> songIDs)
        {
            Cache.Remove(filepath);
        }

        public bool Contains(string filepath, string fileHash)
        {
            return Cache.ContainsKey(filepath);
        }

        public SongDetails Get(string filepath, string songID)
        {
            if (!Contains(filepath, ""))
            {
                return null;
            }

            return Cache[filepath][songID];
        }

        public SongDetails Get(string SongID)
        {
            try
            {
                var foundin = Cache.First(x => x.Value.ContainsKey(SongID)).Key;

                return Get(foundin, SongID);
            }
            catch
            {
                return null;
            }
        }
    }
}
