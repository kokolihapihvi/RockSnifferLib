using Newtonsoft.Json;
using RockSnifferLib.Logging;
using RockSnifferLib.Sniffing;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace RockSnifferLib.Cache
{
    public class FileCache : ICache
    {
        public string cachedir;

        public FileCache(string dir)
        {
            cachedir = dir;

            //Create the directory if it doesn't exist
            Directory.CreateDirectory(cachedir);
        }

        /// <summary>
        /// Checks if the cache contains a cached version of a file
        /// </summary>
        /// <param name="filepath"></param>
        /// <returns></returns>
        public bool Contains(string filepath)
        {
            string cachedfiledir = cachedir + Path.DirectorySeparatorChar + SanitizeFilename(Path.GetFileNameWithoutExtension(filepath)) + Path.DirectorySeparatorChar;
            return Directory.Exists(cachedfiledir);
        }

        /// <summary>
        /// Loads a cached version of a file
        /// </summary>
        /// <param name="filepath"></param>
        /// <returns></returns>
        public SongDetails Get(string filepath, string songID)
        {
            string cachedfile = cachedir + Path.DirectorySeparatorChar + SanitizeFilename(Path.GetFileNameWithoutExtension(filepath)) + Path.DirectorySeparatorChar + SanitizeFilename(songID);

            if (File.Exists(cachedfile + ".json"))
            {
                //Read song details from json
                SongDetails details = JsonConvert.DeserializeObject<SongDetails>(File.ReadAllText(cachedfile + ".json"));

                //Read album art from jpeg
                details.albumArt = Image.FromFile(cachedfile + ".jpeg");

                if (Logger.logCache)
                {
                    Logger.Log("Read from cache: {0}/{1}", Path.GetFileName(filepath), songID);
                }

                //Return loaded object
                return details;
            }
            else if (Logger.logCache)
            {
                Logger.Log("Cache for {0} does not contain {1}", SanitizeFilename(Path.GetFileNameWithoutExtension(filepath)), SanitizeFilename(songID));
            }

            return null;
        }

        /// <summary>
        /// Get a cached songs details by searching the cache
        /// </summary>
        /// <param name="songID"></param>
        /// <returns></returns>
        public SongDetails Get(string songID)
        {
            var folder = Search(songID);

            if (folder == null)
            {
                if (Logger.logCache)
                {
                    Logger.Log("Cache does not contain {0}", songID);
                }

                return null;
            }

            var cachedfile = folder + "/" + songID;

            SongDetails details = JsonConvert.DeserializeObject<SongDetails>(File.ReadAllText(cachedfile + ".json"));

            details.albumArt = Image.FromFile(cachedfile + ".jpeg");

            if (Logger.logCache)
            {
                Logger.Log("Read from cache: {0}/{1}", Path.GetFileName(folder), songID);
            }

            return details;
        }

        private string Search(string songID)
        {
            foreach (string folder in Directory.EnumerateDirectories(cachedir))
            {
                if (File.Exists(folder + "/" + songID + ".json"))
                {
                    return folder;
                }
            }

            return null;
        }

        /// <summary>
        /// Adds songdetails of a file to the cache
        /// </summary>
        /// <param name="filepath"></param>
        /// <param name="details"></param>
        public void Add(string filepath, Dictionary<string, SongDetails> allDetails)
        {
            string cachedfileDir = cachedir + Path.DirectorySeparatorChar + SanitizeFilename(Path.GetFileNameWithoutExtension(filepath)) + Path.DirectorySeparatorChar;

            Directory.CreateDirectory(cachedfileDir);

            foreach (SongDetails details in allDetails.Values)
            {
                string cachedfile = cachedfileDir + SanitizeFilename(details.songID);

                //Write details object as json
                File.WriteAllText(cachedfile + ".json", JsonConvert.SerializeObject(details));

                //Write album art as jpeg
                details.albumArt.Save(cachedfile + ".jpeg", ImageFormat.Jpeg);

            }

            if (Logger.logCache)
            {
                Logger.Log("Cached {0}", Path.GetFileName(filepath));
            }
        }

        private string SanitizeFilename(string filename)
        {
            //Remove invalid file name characters
            foreach (char character in Path.GetInvalidFileNameChars())
            {
                filename = filename.Replace(character.ToString(), "_");
            }

            //Remove invalid path characters
            foreach (char character in Path.GetInvalidPathChars())
            {
                filename = filename.Replace(character.ToString(), "_");
            }

            return filename;
        }
    }
}
