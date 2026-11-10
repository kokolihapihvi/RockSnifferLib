using RockSnifferLib.RSHelpers.NoteData;
using RockSnifferLib.SysHelpers;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RockSnifferLib.RSHelpers
{
    public class RSMemoryReader
    {
        private RSMemoryReadout readout = new RSMemoryReadout();
        private RSMemoryReadout prevReadout = new RSMemoryReadout();

        //Process handles
        private readonly Process rsProcess;
        private readonly RSEdition edition;
        private readonly IntPtr rsProcessHandle;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="rsProcess"></param>
        /// <param name="edition"></param>
        public RSMemoryReader(Process rsProcess, RSEdition edition)
        {
            this.rsProcess = rsProcess;
            this.edition = edition;

            rsProcessHandle = rsProcess.Handle;
        }

        /// <summary>
        /// Sets the enumerate flag to 1, causing rocksmith to start enumerating
        /// </summary>
        public void TriggerEnumeration()
        {
            IntPtr addr = FollowPointers(MemoryOffsets.GetEnumerationFlagPointer(edition));

            MemoryHelper.WriteBytesToMemory(rsProcessHandle, addr, new byte[] { 0x01 });
        }

        /// <summary>
        /// Read song timer and note data from memory
        /// </summary>
        /// <returns></returns>
        public RSMemoryReadout DoReadout()
        {
            // SONG ID
            //
            // Seems to be a zero terminated string in the format: Play_SONGID_Preview
            string preview_name = MemoryHelper.ReadStringFromMemory(rsProcessHandle, FollowPointers(MemoryOffsets.GetSongIdPointer(edition)));

            //If there was string in memory
            if (preview_name != null)
            {
                //Verify Play_ prefix and _Preview or _Invalid suffix
                //_Invalid suffix is applied to all song previews, and replaces _Preview, when a RSMods user has the "Disable Song Preview" mod enabled.
                //_Invalid is used to prevent the song preview from being played in-game, but in this case we want to know when that event is triggered.
                if (preview_name.StartsWith("Play_") && (preview_name.EndsWith("_Preview") || preview_name.EndsWith("_Invalid")))
                {
                    //Remove Play_ prefix and _Preview or _Invalid suffix
                    string song_id = preview_name.Substring(5, preview_name.Length - 13);

                    // readout persists between polls, so drop the previous song's arrangement
                    if (readout.songID != song_id)
                    {
                        readout.arrangementID = null;
                    }

                    //Assign to readout
                    readout.songID = song_id;
                }
            }

            // SONG TIMER
            ReadSongTimer(FollowPointers(MemoryOffsets.GetSongTimerPointer(edition)));

            // GAME STAGE: read before the arrangement ID, which picks its pointer by game stage
            string game_stage = MemoryHelper.ReadStringFromMemory(rsProcessHandle, FollowPointers(MemoryOffsets.GetCurrentMenuPointer(edition)));

            //If we got a game stage
            if (game_stage != null)
            {
                //Verify that it is at least 4 characters long, to filter out more garbage
                if (game_stage.Length >= 4)
                {
                    readout.gameStage = game_stage;
                }
            }

            // MODE: the plain "tuner" stage opens from any mode, so it keeps the previous one
            if (!string.Equals(readout.gameStage, "tuner", StringComparison.OrdinalIgnoreCase))
            {
                readout.mode = DeriveModeFromGameStage(readout.gameStage);
            }

            // ARRANGEMENT ID: the hash isn't set in Nonstop Play, so Learn a Song and Nonstop
            // read the loaded arrangement GUID. Invalid reads keep the previous value.
            bool usePlayArrIDChain = readout.gameStage == "las_game"
                                  || readout.gameStage == "las_pause"
                                  || readout.gameStage == "nonstopplaygame"
                                  || readout.gameStage == "nsp_pause";

            string resolved_arrangement_id;
            if (usePlayArrIDChain)
            {
                resolved_arrangement_id = ReadGuidFromMemory(
                    FollowPointers(MemoryOffsets.GetPlayArrIDPointer(edition)));
            }
            else
            {
                resolved_arrangement_id = MemoryHelper.ReadStringFromMemory(
                    rsProcessHandle,
                    FollowPointers(MemoryOffsets.GetArrangementHashPointer(edition)));
            }

            if (IsValidArrangementHash(resolved_arrangement_id))
            {
                readout.arrangementID = resolved_arrangement_id;
            }

            // CURRENT PATH
            IntPtr pathAddr = FollowPointers(MemoryOffsets.GetCurrentPathPointer(edition));
            if (pathAddr != IntPtr.Zero)
            {
                byte pathByte = MemoryHelper.ReadByteFromMemory(rsProcessHandle, pathAddr);
                readout.currentPath = pathByte switch
                {
                    0x01 => RSPath.Lead,
                    0x02 => RSPath.Rhythm,
                    0x04 => RSPath.Bass,
                    _ => RSPath.Unknown
                };
            }

            // PAUSE MENU
            IntPtr pauseModeAddr = FollowPointers(MemoryOffsets.GetPauseMenuModePointer(edition));
            if (pauseModeAddr != IntPtr.Zero)
            {
                byte modeByte = MemoryHelper.ReadByteFromMemory(rsProcessHandle, pauseModeAddr);
                readout.pauseMenuMode = modeByte switch
                {
                    0 => PauseMenuMode.None,
                    1 => PauseMenuMode.SubOverlay,
                    2 => PauseMenuMode.TopOverlay,
                    _ => PauseMenuMode.Unknown
                };
                readout.isPaused = readout.pauseMenuMode is PauseMenuMode.SubOverlay or PauseMenuMode.TopOverlay;
            }

            // NOTE DATA
            //
            // For learn a song:
            //Candidate #1: FollowPointers(0x00F5C5AC, new int[] {0xB0, 0x18, 0x4, 0x84, 0x0})
            //Candidate #2: FollowPointers(0x00F5C4CC, new int[] {0x5F0, 0x18, 0x4, 0x84, 0x0})
            //
            // For score attack:
            //Candidate #1: FollowPointers(0x00F5C5AC, new int[] { 0xB0, 0x18, 0x4, 0x4C, 0x0 })
            //Candidate #2: FollowPointers(0x00F5C4CC, new int[] { 0x5F0, 0x18, 0x4, 0x4C, 0x0 })

            //If note data is not valid, try the next mode
            //Learn a song
            if (!ReadNoteData(FollowPointers(MemoryOffsets.GetLearnASongNoteDataPointer(edition))))
            {
                //Score attack
                ReadScoreAttackNoteData(FollowPointers(MemoryOffsets.GetScoreAttackNoteDataPointer(edition)));
            }

            //Copy over everything when a song is running
            if (readout.songTimer > 0)
            {
                readout.CopyTo(ref prevReadout);
            }

            //Always copy over important fields
            prevReadout.songID = readout.songID;
            prevReadout.gameStage = readout.gameStage;
            prevReadout.songTimer = readout.songTimer;

            // These are valid outside of songs too
            prevReadout.currentPath = readout.currentPath;
            prevReadout.pauseMenuMode = readout.pauseMenuMode;
            prevReadout.isPaused = readout.isPaused;
            prevReadout.mode = readout.mode;
            prevReadout.arrangementID = readout.arrangementID;

            return prevReadout;
        }

        // Filters out garbage reads while a song loads
        private static bool IsValidArrangementHash(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length != 32)
            {
                return false;
            }
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (!((c >= '0' && c <= '9') ||
                      (c >= 'A' && c <= 'F') ||
                      (c >= 'a' && c <= 'f')))
                {
                    return false;
                }
            }
            return true;
        }

        private static RSMode DeriveModeFromGameStage(string gameStage)
        {
            if (string.IsNullOrEmpty(gameStage))
            {
                return RSMode.UNKNOWN;
            }

            string gs = gameStage.ToLowerInvariant();

            switch (gs)
            {
                case "learnasong":
                case "las_songs":
                case "las_options":
                case "las_tuner":
                case "las_game":
                case "las_pause":
                case "las_songreview":
                    return RSMode.LEARNASONG;

                case "scoreattack":
                case "panel_bib":
                case "scoreattack_presongtuner":
                case "sa_game":
                case "sa_pause":
                case "sa_songreview":
                    return RSMode.SCOREATTACK;

                case "gcpre":
                case "gcade":
                case "gcade_game":
                case "guitarcade_tuner":
                    return RSMode.GUITARCADE;

                case "nonstopplay":
                case "nsp_main":
                case "nonstopplayhub":
                case "nsp_tuner":
                case "nonstopplaygame":
                case "nsp_pause":
                    return RSMode.NONSTOPPLAY;

                case "getuner":
                case "pregametuner":
                    return RSMode.LESSONS;

                case "split_game":
                    return RSMode.MULTIPLAYER;

                case "titlescreen":
                case "profileselect":
                case "main":
                case "mainmenu":
                case "statsmenu":
                case "shop":
                case "contentpanelchord":
                case "sidelist":
                    return RSMode.MENU;
            }

            if (gs.StartsWith("gc_")) return RSMode.GUITARCADE;
            if (gs.StartsWith("sm_")) return RSMode.SESSION;
            if (gs.StartsWith("ge_")) return RSMode.LESSONS;
            if (gs.StartsWith("mp_") || gs.StartsWith("duet_") || gs.StartsWith("h2h_")) return RSMode.MULTIPLAYER;
            if (gs.StartsWith("tonedesigner")) return RSMode.MENU;

            return RSMode.UNKNOWN;
        }

        private IntPtr FollowPointers((int entryAddress, int[] offsets) tuple)
        {
            return FollowPointers(tuple.entryAddress, tuple.offsets);
        }

        private IntPtr FollowPointers(int entryAddress, int[] offsets)
        {
            //If the process has exited, don't try to read memory
            if (rsProcess.HasExited)
            {
                return IntPtr.Zero;
            }

            //Get base address
            IntPtr baseAddress = rsProcess.MainModule.BaseAddress;

            //Add entry address
            IntPtr finalAddress = IntPtr.Add(baseAddress, entryAddress);

            //Add offsets
            foreach (int offset in offsets)
            {
                finalAddress = MemoryHelper.FollowPointer(rsProcessHandle, finalAddress, offset);

                //If any of the offsets points to 0, return zero
                if (finalAddress.ToInt32() == offset)
                {
                    return IntPtr.Zero;
                }
            }

            //Return the final address
            return finalAddress;
        }

        private void ReadSongTimer(IntPtr timerAddress)
        {
            //Read float from memory and assign field on readout
            readout.songTimer = MemoryHelper.ReadFloatFromMemory(rsProcessHandle, timerAddress);
        }

        // Same format as the arrangement IDs in song details
        private string ReadGuidFromMemory(IntPtr address)
        {
            if (address == IntPtr.Zero)
            {
                return null;
            }

            byte[] bytes = MemoryHelper.ReadBytesFromMemory(rsProcessHandle, address, 16);
            return new Guid(bytes).ToString("N").ToUpperInvariant();
        }

        private bool ReadNoteData(IntPtr structAddress)
        {
            //Check validity
            //No null pointers
            if (structAddress == IntPtr.Zero)
            {
                return false;
            }

            //This seems to be a magic number that is at this value when the pointer is valid
            if (MemoryHelper.ReadInt32FromMemory(rsProcessHandle, IntPtr.Add(structAddress, 0x0008)) != 111000)
            {
                return false;
            }

            //Read note data
            readout.noteData = MemoryHelper.ReadStructureFromMemory<LearnASongNoteData>(rsProcessHandle, structAddress);

            return true;
        }

        private bool ReadScoreAttackNoteData(IntPtr structAddress)
        {
            //Check validity
            //No null pointers
            if (structAddress == IntPtr.Zero)
            {
                return false;
            }

            //This seems to be a magic number that is at this value when the pointer is valid
            if (MemoryHelper.ReadInt32FromMemory(rsProcessHandle, IntPtr.Add(structAddress, 0x0008)) != 111000)
            {
                return false;
            }

            //Read note data
            readout.noteData = MemoryHelper.ReadStructureFromMemory<ScoreAttackNoteData>(rsProcessHandle, structAddress);

            return true;
        }
    }
}
