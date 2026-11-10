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

                    // Reset arrangementID on songID change: `readout` persists across DoReadout
                    // calls, and a valid hash from the previous song would otherwise linger and
                    // produce stale-arrangement reports during menu browsing.
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

            // GAME STAGE: must be resolved before ARRANGEMENT ID (the arrangement read
            // dispatches by gameStage). Static-address read; the buffer at module+0xF5F7C9
            // (Remastered) is Rocksmith's canonical gameStage cell - see
            // MemoryOffsets.GetCurrentMenuPointer.
            //
            // Length >= 4 guard filters transient sub-4-char writes during stage transitions.
            //
            // KNOWN: gameStage does NOT update on pause->resume or pause->restart for any
            // mode (engine behavior, not a reader bug). Consumers needing play/pause state
            // should use game_state (SnifferState).
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

            // MODE: derived from gameStage (reliable across all states); see
            // DeriveModeFromGameStage for the mapping table.
            //
            // SPECIAL CASE: bare "tuner": fires when the universal tuner is invoked from
            // any parent context (pause menu, main menu, Session, ...), unlike the
            // mode-specific tuners (las_tuner, nsp_tuner, scoreattack_presongtuner,
            // getuner, pregametuner, guitarcade_tuner), which classify under their parent
            // mode. The bare stage is stateless, so persist the previous poll's mode
            // instead of reclassifying. Edge case: if the first observed gameStage after
            // attach is the bare tuner, mode stays UNKNOWN until the user navigates away.
            if (!string.Equals(readout.gameStage, "tuner", StringComparison.OrdinalIgnoreCase))
            {
                readout.mode = DeriveModeFromGameStage(readout.gameStage);
            }

            // ARRANGEMENT ID: dispatch by gameStage. Two chains expose arrangement-id data:
            //
            //   PLAY_arrID chain (MemoryOffsets.GetPlayArrIDPointer): 16-byte raw GUID in
            //   Microsoft LE layout, converted via new Guid(bytes).ToString("N")
            //   .ToUpperInvariant() to match songDetails.arrangements[].arrangementID.
            //   Used for las_game / las_pause / nonstopplaygame / nsp_pause. In LaS it is
            //   cross-validated identical to arrangement_hash; in Nonstop it is the only
            //   chain that populates.
            //
            //   arrangement_hash chain (legacy): 32-char ASCII hex string. Used for
            //   sa_game / sa_pause (Score Attack has its own subsystem - PLAY_arrID does
            //   not track it) and all other gameStages, where it may return junk or stale
            //   values.
            //
            // FORMAT VALIDATION: IsValidArrangementHash rejects null/empty/wrong-length
            // and non-hex - catches uninitialized-memory garbage (song titles, URN
            // fragments) and all-zero / unresolved PLAY_arrID reads (e.g. Nonstop carousel).
            //
            // CANDIDATE VALIDATION (Sniffer.cs cross-reference): nulls format-valid but
            // song-mismatched IDs. Chain-agnostic.
            //
            // PERSISTENCE: readout.arrangementID persists across DoReadout calls until the
            // songID changes (reset at the top of DoReadout) or a fresh valid read
            // overwrites it: a bad read retains the prior good value and retries next poll.
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

            // CURRENT PATH: the user's currently-selected Path (arrangement type) at the
            // menu level. 1-byte enum at a stable address, populated from launch (defaults
            // 0x01/Lead), mutated only when the user switches Path. Works in Nonstop Play,
            // where arrangement_hash fails. Mapping: 0x01=Lead, 0x02=Rhythm, 0x04=Bass,
            // else Unknown (Sniffer.cs then falls through to heuristics).
            try
            {
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
            }
            catch
            {
                // Best-effort read - leave currentPath at its default value.
                // This shouldn't happen in practice (pointer chain has been observed stable),
                // but defensive coding keeps a transient memory hiccup from killing the poll.
            }

            // PAUSE MENU MODE: direct read of the static byte at module+0xF5F5FC
            // (Remastered): 0=no overlay, 1=sub-overlay (tuner-from-pause), 2=top-level
            // overlay (pause menu, Mixer, Tools). Cross-mode validated, survives relaunch
            // as a true static. Used by Sniffer.UpdateState for flag-driven SONG_PLAYING <->
            // SONG_PAUSED transitions. See MemoryOffsets.GetPauseMenuModePointer for the
            // full table and caveats.
            try
            {
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
            }
            catch
            {
                // Best-effort read - leave pauseMenuMode/isPaused at their default / prior values.
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
                // No UNKNOWN fallback here: mode is derived from gameStage in DoReadout, and
                // note-data dispatch only decides which struct shape to read - an UNKNOWN
                // write would clobber a gameStage-derived menu mode.
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

            // currentPath is a menu-level setting that's stable across all game states:
            // always propagate, same as songID/gameStage. Without this, prevReadout would
            // only get the path during active gameplay (songTimer > 0), and consumers
            // querying `prevReadout.currentPath` while in song-select would see stale data.
            prevReadout.currentPath = readout.currentPath;

            // pauseMenuMode reflects engine overlay state and can flip on user input
            // (pause button) at any songTimer value, including songTimer == 0 during
            // loading. Propagate every poll regardless of songTimer, same rationale
            // as currentPath above; otherwise consumers would see stale pause state
            // during the brief window when pause is first registered.
            prevReadout.pauseMenuMode = readout.pauseMenuMode;
            prevReadout.isPaused = readout.isPaused;

            // Always propagate mode: every gameStage (menus, transitions, ...) has a
            // meaningful classification, so mode gets the same always-propagate treatment
            // as gameStage / currentPath / pauseMenuMode. Otherwise prevReadout.mode would
            // retain the last in-song value through every menu state.
            prevReadout.mode = readout.mode;

            // Always propagate arrangementID: gating on songTimer > 0 (a) missed
            // arrangement picks made in song-options (timer 0), letting START fire with
            // stale data, and (b) left the field null after a cross-reference clear until
            // the next in-song poll. Always propagating makes the clear per-poll only.
            prevReadout.arrangementID = readout.arrangementID;

            return prevReadout;
        }

        /// <summary>
        /// Validates that a string is a 32-character hexadecimal hash matching the format of
        /// Rocksmith arrangement IDs (MD5 hashes serialized as hex). Returns false for null,
        /// empty, wrong length, or any non-hex character.
        ///
        /// Used to filter out junk reads from the arrangement_hash memory pointer when the
        /// game hasn't yet populated that location with a valid hash (e.g. during song-load
        /// transitions, especially in Nonstop Play).
        /// </summary>
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

        /// <summary>
        /// Classifies a Rocksmith gameStage string into an RSMode value. gameStage is
        /// the canonical source of truth for what the user is doing in the game (see
        /// MemoryOffsets.GetCurrentMenuPointer); this maps the observed stages into
        /// mode buckets consumers can reason about.
        ///
        /// MAPPING TABLE (exact-match first, then prefix fallback):
        ///
        ///   LEARNASONG
        ///     exact: learnasong, las_songs, las_options, las_tuner,
        ///            las_game, las_pause, las_songreview
        ///
        ///   SCOREATTACK
        ///     exact: scoreattack, panel_bib, scoreattack_presongtuner,
        ///            sa_game, sa_pause, sa_songreview
        ///
        ///   GUITARCADE  (SA is conceptually a subset of Guitarcade, but classified
        ///                separately above when in an SA-specific stage; Guitarcade
        ///                catches the hub and its other minigames)
        ///     exact: gcpre, gcade, gcade_game, guitarcade_tuner
        ///     prefix: gc_
        ///
        ///   NONSTOPPLAY
        ///     exact: nonstopplay, nsp_main, nonstopplayhub, nsp_tuner,
        ///            nonstopplaygame, nsp_pause
        ///
        ///   SESSION
        ///     prefix: sm_   (e.g. sm_game, sm_pause, sm_bandsettings)
        ///
        ///   LESSONS
        ///     exact: getuner, pregametuner
        ///     prefix: ge_   (e.g. ge_techniquehub, ge_game, ge_pause)
        ///
        ///   MULTIPLAYER  (classification tag only - full MP support is a separate
        ///                 larger effort)
        ///     exact: split_game
        ///     prefix: mp_, duet_, h2h_
        ///
        ///   MENU  (top-level / utility screens not tied to any single gameplay mode)
        ///     exact: titlescreen, profileselect, main, mainmenu, statsmenu,
        ///            shop, contentpanelchord, sidelist
        ///     prefix: tonedesigner
        ///
        ///   UNKNOWN  (everything else, defensive default)
        ///
        /// Input is lowercased once at the top; match tables are lowercase. The bare
        /// "tuner" stage is NOT handled here - DoReadout persists the prior mode for
        /// it; if it reaches this method it falls through to UNKNOWN.
        /// </summary>
        private static RSMode DeriveModeFromGameStage(string gameStage)
        {
            if (string.IsNullOrEmpty(gameStage))
            {
                return RSMode.UNKNOWN;
            }

            string gs = gameStage.ToLowerInvariant();

            switch (gs)
            {
                // LEARNASONG
                case "learnasong":
                case "las_songs":
                case "las_options":
                case "las_tuner":
                case "las_game":
                case "las_pause":
                case "las_songreview":
                    return RSMode.LEARNASONG;

                // SCOREATTACK
                case "scoreattack":
                case "panel_bib":
                case "scoreattack_presongtuner":
                case "sa_game":
                case "sa_pause":
                case "sa_songreview":
                    return RSMode.SCOREATTACK;

                // GUITARCADE (also caught by gc_ prefix below for minigame variants)
                case "gcpre":
                case "gcade":
                case "gcade_game":
                case "guitarcade_tuner":
                    return RSMode.GUITARCADE;

                // NONSTOPPLAY
                case "nonstopplay":
                case "nsp_main":
                case "nonstopplayhub":
                case "nsp_tuner":
                case "nonstopplaygame":
                case "nsp_pause":
                    return RSMode.NONSTOPPLAY;

                // LESSONS (also caught by ge_ prefix below)
                case "getuner":
                case "pregametuner":
                    return RSMode.LESSONS;

                // MULTIPLAYER (also caught by mp_/duet_/h2h_ prefixes below)
                case "split_game":
                    return RSMode.MULTIPLAYER;

                // MENU (also caught by tonedesigner prefix below)
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

            // Prefix matches (after exact-match fall-through).
            // Each family's exact members are listed in the switch above for
            // documentation visibility; the prefix catches any unenumerated
            // member of the same family (e.g. new minigame variants, new
            // session-mode sub-screens, etc.).
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

        /// <summary>
        /// Reads 16 raw bytes from the PLAY_arrID chain and converts them to the
        /// 32-char uppercase hex form matching songDetails.arrangements[].arrangementID
        /// (Microsoft GUID layout via the Guid(byte[]) constructor; ToString("N") +
        /// ToUpperInvariant for the case-sensitive cross-reference in Sniffer.cs).
        /// Returns null when the chain is broken or the bytes are unreadable.
        /// </summary>
        private string ReadGuidFromMemory(IntPtr address)
        {
            if (address == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                byte[] bytes = MemoryHelper.ReadBytesFromMemory(rsProcessHandle, address, 16);
                if (bytes == null || bytes.Length != 16)
                {
                    return null;
                }
                return new Guid(bytes).ToString("N").ToUpperInvariant();
            }
            catch
            {
                // Best-effort read - null return leaves readout.arrangementID at its
                // prior value, next poll retries. Same defensive pattern as currentPath
                // and pauseMenuMode reads above.
                return null;
            }
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

            // mode is derived from gameStage in DoReadout; this method only reads the LaS
            // note-data struct.

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

            // mode is derived from gameStage in DoReadout; this method only reads the SA
            // note-data struct.

            //Read note data
            readout.noteData = MemoryHelper.ReadStructureFromMemory<ScoreAttackNoteData>(rsProcessHandle, structAddress);

            return true;
        }
    }
}
