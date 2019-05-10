using RocksmithToolkitLib.DLCPackage.Manifest2014;
using RockSnifferLib.Logging;
using RockSnifferLib.Sniffing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        internal static Bitmap ExtractAlbumArt(LazyPsarcLoader loader, string artFile)
        {
            //Select the correct entry and load it into the memory stream
            using (MemoryStream ms = loader.ExtractEntryData(x => (x.Name == "gfxassets/album_art/" + artFile.Substring(14) + "_256.dds")))
            {
                //Create a Pfim image from memory stream
                Pfim.Dds img = Pfim.Dds.Create(ms, new Pfim.PfimConfig());

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

            var sw = new Stopwatch();
            sw.Start();

            //If its big, print a warning
            var fileinfo = new FileInfo(filepath);
            long size = fileinfo.Length;

            var detailsDict = new Dictionary<string, SongDetails>();

            using (LazyPsarcLoader loader = new LazyPsarcLoader(filepath))
            {
                //Extract toolkit info
                var tkInfo = loader.ExtractToolkitInfo();
                List<Manifest2014<Attributes2014>> manifests = null;

                try
                {
                    manifests = loader.ExtractJsonManifests();
                }
                catch (Exception e)
                {
                    Logger.LogError("Warning! Could not parse psarc file {0}: {1}", Path.GetFileName(filepath), e.Message);
                    return null;
                }

                //Extract all arrangements
                foreach (var v in manifests)
                {
                    if (v == null)
                    {
                        Logger.LogError("Unable to process JSON manifest for {0}", Path.GetFileName(filepath));
                        continue;
                    }

                    var arrangement = v.Entries.First();
                    var arrangement_id = arrangement.Key;
                    var attr = arrangement.Value.First().Value;

                    if (attr.Phrases != null)
                    {
                        if (!detailsDict.ContainsKey(attr.SongKey))
                        {
                            detailsDict[attr.SongKey] = new SongDetails();
                        }

                        SongDetails details = detailsDict[attr.SongKey];

                        if (details.albumArt == null)
                        {
                            try
                            {
                                details.albumArt = ExtractAlbumArt(loader, attr.AlbumArt);
                            }
                            catch (Exception)
                            {
                                Logger.LogError("Warning: couldn't extract album art for {0}", attr.SongName);

                                details.albumArt = new Bitmap(1, 1);
                            }
                        }

                        //Get a list of all sections
                        var sections = new List<ArrangementDetails.SectionDetails>();
                        Dictionary<string, int> sectionCounts = new Dictionary<string, int>();

                        foreach (var sect in attr.Sections)
                        {
                            if(!sectionCounts.ContainsKey(sect.Name))
                            {
                                sectionCounts[sect.Name] = 1;
                            }

                            var sectionDetails = new ArrangementDetails.SectionDetails
                            {
                                name = $"{sect.Name} {sectionCounts[sect.Name]}",
                                startTime = sect.StartTime,
                                endTime = sect.EndTime
                            };

                            sections.Add(sectionDetails);

                            sectionCounts[sect.Name]++;
                        }

                        //Build arrangement details
                        var arrangementDetails = new ArrangementDetails
                        {
                            name = attr.ArrangementName,
                            arrangementID = arrangement_id,
                            sections = sections,
                            isBonusArrangement = (attr.ArrangementProperties.BonusArr == 1)
                        };

                        //Determine path type
                        if (attr.ArrangementProperties.PathLead == 1)
                        {
                            arrangementDetails.type = "Lead";
                        }
                        else if (attr.ArrangementProperties.PathRhythm == 1)
                        {
                            arrangementDetails.type = "Rhythm";
                        }
                        else if (attr.ArrangementProperties.PathBass == 1)
                        {
                            arrangementDetails.type = "Bass";
                        }

                        //Get general song information
                        details.songID = attr.SongKey;
                        details.songLength = (float)(attr.SongLength ?? 0);
                        details.songName = attr.SongName;
                        details.artistName = attr.ArtistName;
                        details.albumName = attr.AlbumName;
                        details.albumYear = attr.SongYear ?? 0;
                        details.arrangements.Add(arrangementDetails);

                        //Apply toolkit information
                        details.toolkit = new ToolkitDetails
                        {
                            version = tkInfo.PackageVersion,
                            author = tkInfo.PackageAuthor,
                            comment = tkInfo.PackageComment,
                            package_version = tkInfo.PackageVersion
                        };
                    }
                }

                sw.Stop();

                Logger.Log("Parsed {0} ({1}mb) in {2}ms and found {3} songs", fileinfo.Name, fileinfo.Length / 1024 / 1024, sw.ElapsedMilliseconds, detailsDict.Count);

                return detailsDict;
            }

        }
    }
}
