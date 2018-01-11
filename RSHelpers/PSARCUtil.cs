using RocksmithToolkitLib.PsarcLoader;
using RockSnifferLib.Logging;
using RockSnifferLib.Sniffing;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace RockSnifferLib.RSHelpers
{
    public class PSARCUtil
    {
        /// <summary>
        /// Extracts a 256x256 bitmap album art from PsarcLoader by artFile file path
        /// </summary>
        /// <param name="loader"></param>
        /// <param name="artFile"></param>
        /// <returns></returns>
        internal static Bitmap ExtractAlbumArt(PsarcLoader loader, string artFile)
        {
            //Select the correct entry and load it into the memory stream
            using (MemoryStream ms = (MemoryStream)loader.ExtractEntryData(x => (x.Name == "gfxassets/album_art/" + artFile.Substring(14) + "_256.dds")))
            {
                //Create a Pfim image from memory stream
                Pfim.Dds img = Pfim.Dds.Create(ms);

                //Create bitmap
                Bitmap bm = new Bitmap(img.Width, img.Height);

                //Convert Pfim image to bitmap
                int bytesPerPixel = img.BytesPerPixel;
                for (int i = 0; i < img.Data.Length; i += bytesPerPixel)
                {
                    //Calculate pixel X and Y coordinates
                    int x = (i / bytesPerPixel) % img.Width;
                    int y = (i / bytesPerPixel) / img.Width;

                    //Get color from the Pfim image data array
                    Color c = Color.FromArgb(255, img.Data[i + 2], img.Data[i + 1], img.Data[i]);

                    //Set pixel in bitmap
                    bm.SetPixel(x, y, c);
                }

                //Return bitmap
                return bm;
            }
        }

        /// <summary>
        /// Reads psarc file from filepath and populates details with information
        /// </summary>
        /// <param name="filepath"></param>
        /// <param name="details"></param>
        internal static Dictionary<string, SongDetails> ReadPSARCHeaderData(string filepath)
        {
            //Check that the file exists, just in case
            if (!File.Exists(filepath))
            {
                Logger.LogError("Warning! Psarc file {0} does not exist!", filepath);
                return null;
            }

            //If its big, print a warning
            long size = new FileInfo(filepath).Length;

            //20 MB to trigger warning
            if (size > (1024 * 1024) * 20)
            {
                Logger.LogError("WARNING: Processing a very large PSARC archive! {0} ({1:n0} MB)\nThis will take some crunching!", Path.GetFileName(filepath), size / 1024 / 1024);
            }

            var detailsDict = new Dictionary<string, SongDetails>();

            //var data = new DLCPackageData();

            using (PsarcLoader loader = new PsarcLoader(filepath, true))
            {
                foreach (var v in loader.ExtractJsonManifests())
                {
                    var attr = v.Entries.ToArray()[0].Value.ToArray()[0].Value;

                    if (attr.Phrases != null)
                    {
                        if (!detailsDict.ContainsKey(attr.SongKey))
                        {
                            detailsDict[attr.SongKey] = new SongDetails();
                        }

                        SongDetails details = detailsDict[attr.SongKey];

                        //if (data.SongInfo == null)
                        //{
                        // Fill Package Data
                        //data.Name = attr.DLCKey;
                        //data.Volume = (attr.SongVolume == 0 ? -12 : attr.SongVolume); //FIXME: too low song volume issue, revert to -6 to fix.
                        //data.PreviewVolume = (attr.PreviewVolume ?? data.Volume);

                        // Fill SongInfo
                        //data.SongInfo = new SongInfo { SongDisplayName = attr.SongName, SongDisplayNameSort = attr.SongNameSort, Album = attr.AlbumName, AlbumSort = attr.AlbumNameSort, SongYear = attr.SongYear ?? 0, Artist = attr.ArtistName, ArtistSort = attr.ArtistNameSort, AverageTempo = (int)attr.SongAverageTempo };

                        if (details.albumArt == null)
                        {
                            try
                            {
                                details.albumArt = ExtractAlbumArt(loader, attr.AlbumArt);
                            }
                            catch (Exception e)
                            {
                                Logger.LogError("Warning: couldn't extract album art for {0}: {1}", attr.SongName, e.Message);
                                //Console.WriteLine(e.StackTrace);

                                details.albumArt = new Bitmap(1, 1);
                            }
                        }

                        details.songID = attr.SongKey;
                        details.songLength = (float)(attr.SongLength ?? 0);
                        details.songName = attr.SongName;
                        details.artistName = attr.ArtistName;
                        details.albumName = attr.AlbumName;
                        details.albumYear = attr.SongYear ?? 0;
                        details.numArrangements++;
                        //}
                    }
                }
                return detailsDict;
            }

        }
    }
}
