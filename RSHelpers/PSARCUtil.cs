using Rocksmith2014PsarcLib.Psarc;
using Rocksmith2014PsarcLib.Psarc.Asset;
using Rocksmith2014PsarcLib.Psarc.Models.Json;
using Rocksmith2014PsarcLib.Psarc.Models.Sng;
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

        internal static bool isNoteIgnored(Note note)
        {
            // Skip notes explicitly marked as "ignored" (notes with the mask 0x40000 set)
            // These notes do not need to be above the 22nd fret to be ignored.
            if ((note.NoteMask & 0x40000) != 0)
            {
                return true;
            }

            // FretId of 255 indicates a chord
            else if (note.FretId != 255)
            {
                // Rocksmith ignores notes above the 22nd fret (fret ID of 255 indicates a chord)
                if (note.FretId > 22)
                {
                    return true;
                }

                // Rocksmith also ignores notes below 22 that slide to a note above 22
                // Note a value of 255 indicates no slide
                else if ((note.SlideTo != 255 && note.SlideTo > 22) || (note.SlideUnpitchTo != 255 && note.SlideUnpitchTo > 22))
                {
                    return true;
                }

                // If the note is linked next and also forms an unpitched slide (notes with the mask 0x8000000 set are linked next), ignore it
                // UNLESS the note is ALSO marked as a pitched slide (happens in Knights of Cydonia for some reason...)
                // Technically the NEXT note is the one that is ignored, but ignoring this note is simpler and results in the same note count
                if ((note.NoteMask & 0x8000000) != 0 && note.SlideUnpitchTo != 255 && note.SlideTo == 255)
                {
                    return true;
                }
            }

            return false;
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

            Logger.Log("Parsing {0} ({1}mb)", fileInfo.Name, fileInfo.Length / 1024 / 1024);

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

                        // Build a hash from the note data in each arrangement
                        var noteDataHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

                        // Only record cent offset in the hash if it is greater than 50 (1/4 step)
                        if (arrangement.CentOffset > 50)
                        {
                            noteDataHash.AppendData(BitConverter.GetBytes(arrangement.CentOffset));
                        }

                        // Record capo in the hash
                        noteDataHash.AppendData(BitConverter.GetBytes(arrangement.CapoFret));

                        // Record the tuning in the hash
                        noteDataHash.AppendData(BitConverter.GetBytes(arrangement.Tuning.String0));
                        noteDataHash.AppendData(BitConverter.GetBytes(arrangement.Tuning.String1));
                        noteDataHash.AppendData(BitConverter.GetBytes(arrangement.Tuning.String2));
                        noteDataHash.AppendData(BitConverter.GetBytes(arrangement.Tuning.String3));
                        noteDataHash.AppendData(BitConverter.GetBytes(arrangement.Tuning.String4));
                        noteDataHash.AppendData(BitConverter.GetBytes(arrangement.Tuning.String5));

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

                            for (var i = 0; i < arr.Notes.Length; ++i)
                            {
                                var note = arr.Notes[i];
                                if (note.Time >= startTime && note.Time < endTime)
                                {
                                    // Information about note masks
                                    // From: https://github.com/rscustom/rocksmith-custom-song-toolkit/blob/master/RocksmithSngHSL/RocksmithSng_constants.txt
                                    //
                                    //NOTE_MASK_SIZE = 4 bytes
                                    //
                                    //NOTE_MASK_UNDEFINED = 0x00000000
                                    //NOTE_MASK_CHORD = 0x00000002
                                    //NOTE_MASK_OPEN = 0x00000004
                                    //NOTE_MASK_FRETHANDMUTE = 0x00000008
                                    //NOTE_MASK_TREMOLO = 0x00000010
                                    //NOTE_MASK_HARMONIC = 0x00000020
                                    //NOTE_MASK_PALMMUTE = 0x00000040
                                    //NOTE_MASK_SLAP = 0x00000080
                                    //NOTE_MASK_PLUCK = 0x00000100
                                    //NOTE_MASK_HAMMERON = 0x00000200
                                    //NOTE_MASK_PULLOFF = 0x00000400
                                    //NOTE_MASK_SLIDE = 0x00000800
                                    //NOTE_MASK_BEND = 0x00001000
                                    //NOTE_MASK_SUSTAIN = 0x00002000
                                    //NOTE_MASK_TAP = 0x00004000
                                    //NOTE_MASK_PINCHHARMONIC = 0x00008000
                                    //NOTE_MASK_VIBRATO = 0x00010000
                                    //NOTE_MASK_MUTE = 0x00020000
                                    //NOTE_MASK_IGNORE = 0x00040000
                                    //NOTE_MASK_HIGHDENSITY = 0x00200000
                                    //NOTE_MASK_SLIDEUNPITCHEDTO = 0x00400000
                                    //NOTE_MASK_DOUBLESTOP = 0x02000000
                                    //NOTE_MASK_ACCENT = 0x04000000
                                    //NOTE_MASK_PARENT = 0x08000000
                                    //NOTE_MASK_CHILD = 0x10000000
                                    //NOTE_MASK_ARPEGGIO = 0x20000000
                                    //NOTE_MASK_POP = 0x00000100
                                    //NOTE_MASK_STRUM = 0x80000000
                                    //NOTE_MASK_ARTICULATIONS_RH = 0x0000C1C0
                                    //NOTE_MASK_ARTICULATIONS_LH = 0x00020628
                                    //NOTE_MASK_ARTICULATIONS = 0x0002FFF8
                                    //NOTE_MASK_ROTATION_DISABLED = 0x0000C1E0

                                    // Update the arrangement hash with all note details
                                    // Ignore the mask of 0x80000000 (modified by CDLC repair)
                                    noteDataHash.AppendData(BitConverter.GetBytes(note.NoteMask | 0x80000000));
                                    
                                    // Ignore note.Flags (modified by CDLC repair)
                                    //noteDataHash.AppendData(BitConverter.GetBytes(note.NoteFlags));
                                    noteDataHash.AppendData(BitConverter.GetBytes((float)Math.Round(note.Time, 3)));

                                    // Ignore note.Hash because it can differ even when there is no
                                    // discernable difference between notes
                                    //noteDataHash.AppendData(BitConverter.GetBytes(note.Hash));

                                    noteDataHash.AppendData(BitConverter.GetBytes(note.StringIndex));
                                    noteDataHash.AppendData(BitConverter.GetBytes(note.FretId));
                                    noteDataHash.AppendData(BitConverter.GetBytes(note.AnchorFretId));
                                    noteDataHash.AppendData(BitConverter.GetBytes(note.AnchorWidth));

                                    // Ignore note.ChordId because it can differ even when there is
                                    // no discernable difference between notes
                                    //noteDataHash.AppendData(BitConverter.GetBytes(note.ChordId));

                                    // Ignore note.ChordNotesId (these are processed later)
                                    //noteDataHash.AppendData(BitConverter.GetBytes(note.ChordNotesId);

                                    // Ignore note.PhraseId (modified by CDLC repair)
                                    //noteDataHash.AppendData(BitConverter.GetBytes(note.PhraseId));

                                    // Ignore note.PhraseIterationId (modified by CDLC repair)
                                    //noteDataHash.AppendData(BitConverter.GetBytes(note.PhraseIterationId));

                                    // Ignore note.fingerPrintId (modified by CDLC repair)
                                    //foreach (var fingerPrintId in note.FingerPrintId)
                                    //{
                                    //    noteDataHash.AppendData(BitConverter.GetBytes(fingerPrintId));
                                    //}

                                    // Ignore note.NextIterNote (modified by CDLC repair)
                                    //noteDataHash.AppendData(BitConverter.GetBytes(note.NextIterNote));

                                    // Ignore note.PrevIterNote (modified by CDLC repair)
                                    //noteDataHash.AppendData(BitConverter.GetBytes(note.PrevIterNote));

                                    // Ignore note.ParentPrevNote (maybe modified by CDLC repair, maybe ignored altogether?)
                                    //noteDataHash.AppendData(BitConverter.GetBytes(note.ParentPrevNote));

                                    noteDataHash.AppendData(BitConverter.GetBytes(note.SlideTo));
                                    noteDataHash.AppendData(BitConverter.GetBytes(note.SlideUnpitchTo));
                                    noteDataHash.AppendData(BitConverter.GetBytes(note.LeftHand));
                                    noteDataHash.AppendData(BitConverter.GetBytes(note.Tap));
                                    noteDataHash.AppendData(BitConverter.GetBytes(note.PickDirection));
                                    noteDataHash.AppendData(BitConverter.GetBytes(note.Slap));
                                    noteDataHash.AppendData(BitConverter.GetBytes(note.Pluck));
                                    noteDataHash.AppendData(BitConverter.GetBytes(note.Vibrato));
                                    noteDataHash.AppendData(BitConverter.GetBytes((float)Math.Round(note.Sustain, 3)));
                                    noteDataHash.AppendData(BitConverter.GetBytes((float)Math.Round(note.MaxBend, 3)));

                                    foreach (var bendData in note.BendData)
                                    {
                                        noteDataHash.AppendData(BitConverter.GetBytes((float)Math.Round(bendData.Time, 3)));
                                        noteDataHash.AppendData(BitConverter.GetBytes((float)Math.Round(bendData.Step, 3)));
                                        noteDataHash.AppendData(BitConverter.GetBytes(bendData.Unk3_0));
                                        noteDataHash.AppendData(BitConverter.GetBytes(bendData.Unk4_0));
                                        noteDataHash.AppendData(BitConverter.GetBytes(bendData.Unk5));
                                    }

                                    if (note.FretId == 255)
                                    {
                                        var chordNotesID = note.ChordNotesId;
                                        if (chordNotesID != -1)
                                        {
                                            var chordNotes = arrangementSng.ChordNotes[chordNotesID];
                                            foreach (var noteMask in chordNotes.NoteMask)
                                            {
                                                noteDataHash.AppendData(BitConverter.GetBytes(noteMask));
                                            }

                                            foreach (var bendData in chordNotes.BendData)
                                            {
                                                foreach (var bendData32 in bendData.BendData32)
                                                {
                                                    noteDataHash.AppendData(BitConverter.GetBytes((float)Math.Round(bendData32.Time, 3)));
                                                    noteDataHash.AppendData(BitConverter.GetBytes((float)Math.Round(bendData32.Step, 3)));
                                                    noteDataHash.AppendData(BitConverter.GetBytes(bendData32.Unk3_0));
                                                    noteDataHash.AppendData(BitConverter.GetBytes(bendData32.Unk4_0));
                                                    noteDataHash.AppendData(BitConverter.GetBytes(bendData32.Unk5));
                                                }

                                                noteDataHash.AppendData(BitConverter.GetBytes(bendData.UsedCount));
                                            }

                                            foreach (var slideTo in chordNotes.SlideTo)
                                            {
                                                noteDataHash.AppendData(BitConverter.GetBytes(slideTo));
                                            }

                                            foreach (var slideUnpitchTo in chordNotes.SlideUnpitchTo)
                                            {
                                                noteDataHash.AppendData(BitConverter.GetBytes(slideUnpitchTo));
                                            }

                                            foreach (var vibrato in chordNotes.Vibrato)
                                            {
                                                noteDataHash.AppendData(BitConverter.GetBytes(vibrato));
                                            }
                                        }
                                    }

                                    // Skip notes explicitly marked as "ignored" (notes with the mask 0x40000 set)
                                    // These notes do not need to be above the 22nd fret to be ignored.
                                    if (isNoteIgnored(note))
                                    {
                                        continue;
                                    }

                                    // FretId of 255 indicates a chord
                                    if (note.FretId == 255)
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
                                        // - The chord contains any notes that slide over the 22nd fret (including notes that slide down from over the 22nd fret)
                                        // - The chord contains any notes that unpitched slide over the 22nd fret (including notes that slide down from over the 22nd fret)
                                        // - The chord contains any note over 22 and any note that is tremolo, bent, or vibrato (the effected note does NOT have to be on the same note that is over 22)
                                        var ignore = false;

                                        // If chordNotesID is -1 then the current note is not associated with a chordNotes array
                                        var chordNotesID = note.ChordNotesId;
                                        if (chordNotesID != -1)
                                        {
                                            var linkNextOffset = 1;
                                            var chordNotes = arrangementSng.ChordNotes[chordNotesID];
                                            for (var j = 0; j < chordNotes.NoteMask.Length; ++j)
                                            {
                                                var noteMask = chordNotes.NoteMask[j];
                                                var slideTo = chordNotes.SlideTo[j];
                                                var slideUnpitchTo = chordNotes.SlideUnpitchTo[j];

                                                // If the chord contains any notes that slide over the 22nd fret, ignore it
                                                // Note a value of 255 indicates no slide
                                                if (slideTo != 255 && slideTo > 22 || slideUnpitchTo != 255 && slideUnpitchTo > 22)
                                                {
                                                    ignore = true;
                                                    break;
                                                }

                                                // This includes chords that slide down from over the 22nd fret
                                                else if (chordOver22 && (slideTo != 255 || slideUnpitchTo != 255))
                                                {
                                                    ignore = true;
                                                    break;
                                                }

                                                // If the chord is linked next and also forms an unpitched slide (notes with the mask 0x8000000 set are linked next), do NOT ignore it
                                                // Unlike single notes, this doesn't apply to chords
                                                //if ((noteMask & 0x8000000) != 0 && slideUnpitchTo != 255) { }

                                                // This is the odd one... if the chord contains any note over 22 and any note that is muted, tremolo, bent, or vibrato, or linked next
                                                // Mask 0x20000 is muted
                                                // Mask 0x10 is tremolo
                                                // Mask 0x1000 is bent
                                                // Mask 0x10000 is vibrato
                                                // Mask 0x8000000 is linkNext
                                                // The bend does NOT have to be on the same note that is over 22
                                                if (((noteMask & 0x20000) != 0 || (noteMask & 0x10) != 0 || (noteMask & 0x1000) != 0 || (noteMask & 0x10000) != 0 || (noteMask & 0x8000000) != 0) && chordOver22)
                                                {
                                                    ignore = true;
                                                    break;
                                                }

                                                // Special processing for chords that are linked next to other chords
                                                // (notes with the mask 0x8000000 set are linked next)
                                                if ((noteMask & 0x8000000) != 0 && arr.Notes[i + 1].FretId == 255)
                                                {
                                                    // TODO: figure out what to do here
                                                    // Whatever goes here may be the key to figuring out Badfish by Sublime
                                                }
                                            }
                                        }

                                        if (ignore)
                                        {
                                            continue;
                                        }
                                    }

                                    // Note should be counted
                                    totalNotes++;
                                }
                            }
                        }

                        // TODO: Figure out issue with phrase iterations getting the incorrect max difficulty for the bass path for Tornado of Souls by Megadeth
                        //
                        // For some reason there is a bug causing RockSniffer to read note data from the wrong difficulty for a couple phrase iterations in the bass path for Tornado of Souls by Megadeth.
                        // Since this is an official chart and will not change, hardcoding the value should work now and into the future but ideally I would like to figure out what exactly is going on here.
                        //
                        // This happens in the following phrase iterations:
                        //
                        // startTime: 279.557
                        // endTime: 284.518
                        // maxDifficulty: 18 (should be 16 looking at the psarc file)
                        //
                        // startTime: 284.518
                        // endTime: 289.422
                        // maxDifficulty: 18 (should be 16 looking at the psarc file)
                        if (arrangement_id == "E9BCDF0BE8B00930987945BCE61F999A")
                        {
                            totalNotes = 1479;
                        }

                        // TODO: Figure out why Badfish by Sublime gets the note count off by one
                        //
                        // The lead path for Badfish by Sublime gets the wrong note count (off by 1). I dug into this issue THOROUGHLY and could not for the life of me figure out why.
                        // Like a bad programmer I will hardcode this for now lol. Hopefully I will encounter something similar in the future and will be able to find a pattern.
                        if (arrangement_id == "B21F30DD94B887B5D4C0E4631D0A511F")
                        {
                            totalNotes = 402;
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
                            totalNotes = totalNotes,
                            noteDataHash = Convert.ToHexString(noteDataHash.GetHashAndReset())
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
