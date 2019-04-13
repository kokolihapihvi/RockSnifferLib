using RockSnifferLib.Sniffing;
using System.Collections.Generic;

namespace RockSnifferLib.Cache
{
    public interface ICache
    {
        /// <summary>
        /// Checks if the cache contains a cached version of a file
        /// </summary>
        /// <param name="filepath"></param>
        /// <returns></returns>
        bool Contains(string filepath);

        /// <summary>
        /// Loads a cached version of a file
        /// </summary>
        /// <param name="filepath"></param>
        /// <returns></returns>
        SongDetails Get(string filepath, string songID);

        /// <summary>
        /// Gets cached song details by searching the cache
        /// </summary>
        /// <param name="SongID"></param>
        /// <returns></returns>
        SongDetails Get(string SongID);

        /// <summary>
        /// Adds songdetails of a file to the cache
        /// </summary>
        /// <param name="filepath"></param>
        /// <param name="details"></param>
        void Add(string filepath, Dictionary<string, SongDetails> allDetails);
    }
}
