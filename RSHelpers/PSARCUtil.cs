using Rocksmith2014PsarcLib.Psarc;
using Rocksmith2014PsarcLib.Psarc.Asset;
using Rocksmith2014PsarcLib.Psarc.Models.Json;
using RockSnifferLib.Logging;
using RockSnifferLib.Sniffing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
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
                        break; // break when file was successfully opened
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

            using (PsarcFile loader = new PsarcFile(fileInfo))
            {
                //Extract toolkit info
                var tkInfo = loader.ExtractToolkitInfo();

                List<SongArrangement> manifests;

                try
                {
                    manifests = loader.ExtractArrangementManifests();
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

                    var arrangement = v.Attributes;
                    var arrangement_id = arrangement.PersistentID;

                    var sngPath = $"songs/bin/generic/{arrangement.SongXml.Substring(20)}.sng";
                    var arrangementSng = loader.InflateEntry<SngAsset>(a => a.Path.Equals(sngPath));
                    ArrangementData arrangementData = new ArrangementData(arrangementSng);

                    if (arrangement.Phrases != null)
                    {
                        if (!detailsDict.ContainsKey(arrangement.SongKey))
                        {
                            detailsDict[arrangement.SongKey] = new SongDetails();
                        }

                        SongDetails details = detailsDict[arrangement.SongKey];

                        if (details.albumArt == null)
                        {
                            try
                            {
                                details.albumArt = loader.ExtractAlbumArt(arrangement).Bitmap;
                            }
                            catch (Exception e)
                            {
                                Logger.LogError("Warning: couldn't extract album art for {0}", arrangement.SongName);
#if DEBUG
                                Logger.LogException(e);
#endif

                                details.albumArt = new Bitmap(1, 1);
                            }
                        }

                        //Get a list of all sections
                        var sections = new List<ArrangementDetails.SectionDetails>();
                        Dictionary<string, int> sectionCounts = new Dictionary<string, int>();

                        foreach (var sect in arrangement.Sections)
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


                        //Get a list of all phraseIterations
                        var phraseIterations = new List<ArrangementDetails.PhraseIterationDetails>();
                        Dictionary<string, int> phraseIterationCounts = new Dictionary<string, int>();

                        foreach (var phrI in arrangement.PhraseIterations)
                        {
                            if (!phraseIterationCounts.ContainsKey(phrI.Name))
                            {
                                phraseIterationCounts[phrI.Name] = 1;
                            }

                            var phraseIterationDetails = new ArrangementDetails.PhraseIterationDetails
                            {
                                name = $"{phrI.Name} {phraseIterationCounts[phrI.Name]}",
                                phraseId = phrI.PhraseIndex,
                                maxDifficulty = phrI.MaxDifficulty,
                                startTime = phrI.StartTime,
                                endTime = phrI.EndTime
                            };

                            phraseIterations.Add(phraseIterationDetails);

                            phraseIterationCounts[phrI.Name]++;
                        }

                        //Build arrangement details
                        var arrangementDetails = new ArrangementDetails
                        {
                            name = arrangement.ArrangementName,
                            arrangementID = arrangement_id,
                            sections = sections,
                            phraseIterations = phraseIterations,
                            data = arrangementData,
                            isBonusArrangement = (arrangement.ArrangementProperties.BonusArr == 1),
                            isAlternateArrangement = (arrangement.ArrangementProperties.Represent == 0)
                        };

                        //Determine path type
                        if (arrangement.ArrangementProperties.PathLead == 1)
                        {
                            arrangementDetails.type = "Lead";
                        }
                        else if (arrangement.ArrangementProperties.PathRhythm == 1)
                        {
                            arrangementDetails.type = "Rhythm";
                        }
                        else if (arrangement.ArrangementProperties.PathBass == 1)
                        {
                            arrangementDetails.type = "Bass";
                        }

                        arrangementDetails.tuning = new ArrangementTuning(arrangement.Tuning, (int)arrangement.CentOffset, (int)arrangement.CapoFret);


                        //file hash
                        details.psarcFileHash = fileHash;

                        //Get general song information
                        details.songID = arrangement.SongKey;
                        details.songLength = arrangement.SongLength;
                        details.songName = arrangement.SongName;
                        details.artistName = arrangement.ArtistName;
                        details.albumName = arrangement.AlbumName;
                        details.albumYear = arrangement.SongYear;
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
            using (var md5 = MD5.Create())
            {
                using (var stream = fileInfo.OpenRead())
                {
                    var hash = Convert.ToBase64String(md5.ComputeHash(stream));
                    return hash;
                }
            }

        }
    }
}
