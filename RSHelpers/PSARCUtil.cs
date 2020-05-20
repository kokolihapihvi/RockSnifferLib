using RocksmithToolkitLib.DLCPackage.Manifest2014;
using RockSnifferLib.Logging;
using RockSnifferLib.Sniffing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

namespace RockSnifferLib.RSHelpers
{
    public static class PSARCUtil
    {
        /// <summary>
        /// Waits for a file to exist and be available for reading
        /// </summary>
        /// <param name="fileInfo"></param>
        private static void WaitForFile(FileInfo fileInfo)
        {
            //Check that the file exists, just in case
            if (!fileInfo.Exists)
            {
                //If it doesn't exist, wait for a bit to see if it magically starts existing
                //If you download the file directly from your browser, it might not exist
                //immediately (though we get the notification about it early?)
                for (int tries = 0; tries < 10; tries++)
                {
                    Thread.Sleep(1000);
                    fileInfo.Refresh();
                    if (fileInfo.Exists) break;
                }
            }

            //Try to open the file for reading, to detect if we are able to read it
            for (int tries = 0; tries < 10; tries++)
            {
                try
                {
                    using (FileStream stream = fileInfo.OpenRead())
                    {
                        stream.Close();
                    }
                }
                catch
                {
                    Thread.Sleep(100);
                }
            }
        }

        /// <summary>
        /// Reads psarc file from filepath and populates details with information
        /// </summary>
        /// <param name="filepath"></param>
        /// <param name="details"></param>
        internal static Dictionary<string, SongDetails> ReadPSARCHeaderData(FileInfo fileInfo)
        {
            //Wait for the file to exist
            WaitForFile(fileInfo);

            if (!fileInfo.Exists)
            {
                Logger.LogError("Warning! Psarc file {0} does not exist!", fileInfo.FullName);
                return null;
            }

            var sw = new Stopwatch();
            sw.Start();

            string fileHash = GetFileHash(fileInfo);

            var detailsDict = new Dictionary<string, SongDetails>();

            using (LazyPsarcLoader loader = new LazyPsarcLoader(fileInfo))
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
                    Logger.LogError("Warning! Could not parse psarc file {0}: {1}", fileInfo.Name, e.Message);
                    return null;
                }

                //Extract all arrangements
                foreach (var v in manifests)
                {
                    if (v == null)
                    {
                        Logger.LogError("Unable to process JSON manifest for {0}", fileInfo.Name);
                        continue;
                    }

                    var arrangement = v.Entries.First();
                    var arrangement_id = arrangement.Key;
                    var attr = arrangement.Value.First().Value;

                    ArrangementData arrangementData = loader.ExtractArrangementData(attr);

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
                                details.albumArt = loader.ExtractAlbumArt(attr);
                            }
                            catch (Exception e)
                            {
                                Logger.LogError("Warning: couldn't extract album art for {0}", attr.SongName);
#if DEBUG
                                Logger.LogException(e);
#endif

                                details.albumArt = new Bitmap(1, 1);
                            }
                        }

                        //Get a list of all sections
                        var sections = new List<ArrangementDetails.SectionDetails>();
                        Dictionary<string, int> sectionCounts = new Dictionary<string, int>();

                        foreach (var sect in attr.Sections)
                        {
                            if (!sectionCounts.ContainsKey(sect.Name))
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
                            data = arrangementData,
                            isBonusArrangement = (attr.ArrangementProperties.BonusArr == 1),
                            isAlternateArrangement = (attr.ArrangementProperties.Represent == 0)
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

                        arrangementDetails.tuning = new ArrangementTuning(attr.Tuning, (int)attr.CentOffset, (int)attr.CapoFret);

                        //file hash
                        details.psarcFileHash = fileHash;

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

                Logger.Log("Parsed {0} ({1}mb) in {2}ms and found {3} songs", fileInfo.Name, fileInfo.Length / 1024 / 1024, sw.ElapsedMilliseconds, detailsDict.Count);

                return detailsDict;
            }

        }

        public static string GetFileHash(FileInfo fileInfo)
        {
            WaitForFile(fileInfo);

            //Calculate file hash
            using (var stream = fileInfo.OpenRead())
            {
                return Convert.ToBase64String(MD5.Create().ComputeHash(stream));
            }
        }
    }
}
