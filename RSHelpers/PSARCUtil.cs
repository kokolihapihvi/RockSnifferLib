﻿using Rocksmith2014PsarcLib.Psarc;
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

                        // NOTE: Rocksmith COMPLETELY ignores all notes above the 22nd fret. To get a proper "totalNotes" count we need to ignore them too.
                        // Additionally there are some other caveats to how Rocksmith tracks notes and these are handled here
                        var totalNotes = 0;

                        // Due to the way note data is stored, we have to go through the song data phrase by phrase
                        foreach (var phrI in phraseIterations)
                        {
                            var startTime = phrI.startTime;
                            var endTime = phrI.endTime;
                            var maxDifficulty = phrI.maxDifficulty;

                            var arr = arrangementSng.Arrangements[maxDifficulty];

                            foreach (var note in arr.Notes)
                            {
                                if (note.Time >= startTime && note.Time < endTime)
                                {
                                    // Skip notes explicitly marked as "ignored" (notes with the mask 0x40000 set)
                                    // These notes do not need to be above the 22nd fret to be ignored.
                                    if ((note.NoteMask & 0x40000) != 0)
                                    {
                                        continue;
                                    }

                                    // FretId of 255 indicates a chord
                                    else if (note.FretId == 255)
                                    {
                                        // If the chord contains any notes over the 22nd fret this does NOT mean it will be ignored
                                        // This definitely seems like a bug in Rocksmith itself, but this seems to be the way the game works
                                        // We do still need to track if chords contain any notes over the 22nd fret for the odd bend logic below
                                        var chordOver22 = false;

                                        var chordID = note.ChordId;
                                        var chordFrets = arrangementSng.Chords[chordID].Frets;
                                        foreach (var fret in chordFrets)
                                        {
                                            // A value of 255 means the string is not used
                                            if (fret == 255)
                                            {
                                                continue;
                                            }

                                            // The chord contains a note over the 22nd fret
                                            if (fret > 22)
                                            {
                                                chordOver22 = true;
                                                break;
                                            }
                                        }

                                        // Chords are ignored in the following scenarios (yes I know this is a bit odd)
                                        // - The chord contains any notes that slide over the 22nd fret
                                        // - The chord contains any notes that unpitched slide over the 22nd fret
                                        // - The chord is linked next and also forms an unpitched slide
                                        // - The chord contains any note over 22 and any note that is bent (the bend does NOT have to be on the same note that is over 22)
                                        var ignore = false;

                                        // If chordNotesID is -1 then the current note is not associated with a chordNotes array
                                        var chordNotesID = note.ChordNotesId;
                                        if (chordNotesID != -1)
                                        {
                                            var chordNotes = arrangementSng.ChordNotes[chordNotesID];
                                            for (var i = 0; i < chordNotes.NoteMask.Length; i++)
                                            {
                                                var noteMask = chordNotes.NoteMask[i];
                                                var slideTo = chordNotes.SlideTo[i];
                                                var slideUnpitchTo = chordNotes.SlideUnpitchTo[i];

                                                // If the chord contains any notes that slide over the 22nd fret, ignore it
                                                // Note a value of 255 indicates no slide
                                                if (slideTo != 255 && slideTo > 22 || slideUnpitchTo != 255 && slideUnpitchTo > 22)
                                                {
                                                    ignore = true;
                                                    break;
                                                }

                                                // If the chord is linked next and also forms an unpitched slide (notes with the mask 0x8000000 set are linked next), do NOT ignore it
                                                // Unlike single notes, this doesn't apply to chords
                                                //if ((noteMask & 0x8000000) != 0 && slideUnpitchTo != 255) { }

                                                // This is the odd one... if the chord contains any note over 22 and any note that is bent (notes with the mask 0x1000 set are bent), ignore it
                                                // The bend does NOT have to be on the same note that is over 22
                                                if ((noteMask & 0x1000) != 0 && chordOver22)
                                                {
                                                    ignore = true;
                                                    break;
                                                }
                                            }
                                        }

                                        if (ignore)
                                        {
                                            continue;
                                        }
                                    }

                                    // Rocksmith ignores notes above the 22nd fret (fret ID of 255 indicates a chord)
                                    else if (note.FretId > 22)
                                    {
                                        continue;
                                    }

                                    // Rocksmith also ignores notes below 22 that slide to a note above 22
                                    // Note a value of 255 indicates no slide
                                    else if ((note.SlideTo != 255 && note.SlideTo > 22) || (note.SlideUnpitchTo != 255 && note.SlideUnpitchTo > 22))
                                    {
                                        continue;
                                    }

                                    // If the note is linked next and also forms an unpitched slide (notes with the mask 0x8000000 set are linked next), ignore it
                                    // UNLESS the note is ALSO marked as a pitched slide (happens in Knights of Cydonia for some reason...)
                                    // Technically the NEXT note is the one that is ignored, but ignoring this note is simpler and results in the same note count
                                    if ((note.NoteMask & 0x8000000) != 0 && note.SlideUnpitchTo != 255 && note.SlideTo == 255)
                                    {
                                        continue;
                                    }

                                    // Note should be counted
                                    totalNotes++;
                                }
                            }
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
                            isAlternateArrangement = (arrangement.ArrangementProperties.Represent == 0),
                            totalNotes = totalNotes
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
