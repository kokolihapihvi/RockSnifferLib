using System;

namespace RockSnifferLib.RSHelpers;

public static class MemoryOffsets
{
    /// <summary>
    /// Get the pointer to the enumeration flag for the given edition
    /// </summary>
    /// <param name="edition"></param>
    /// <returns>A tuple of (entry address, pointer offsets)</returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public static (int, int[]) GetEnumerationFlagPointer(RSEdition edition)
    {
        return edition switch
        {
            RSEdition.Remastered_Just_In_Case_We_Need_It_Beta => (0xF71E10, [0x8, 0x4]),
            RSEdition.Remastered => (0xF71E10 + 0x3080, [0x8, 0x4]),
            RSEdition.Remastered_Learn_And_Play => (0xF71E10 + 0x4080, [0x8, 0x4]),
            _ => throw new ArgumentOutOfRangeException(nameof(edition), edition, "Unknown edition")
        };
    }

    /// <summary>
    /// Get the pointer to the song ID for the given edition
    /// </summary>
    /// <param name="edition"></param>
    /// <returns>A tuple of (entry address, pointer offsets)</returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public static (int entryAddress, int[] offsets) GetSongIdPointer(RSEdition edition)
    {
        //Candidate #1: (0x00F5C494, [{ 0xBC, 0x0 ]})
        //Candidate #2: (0x00F80CEC, [{ 0x598, 0x1B8, 0x0 ]})
        //Candidate #3: (0x00F5DAFC, [{ 0x608, 0x1B8, 0x0 ]})
        return edition switch
        {
            RSEdition.Remastered_Just_In_Case_We_Need_It_Beta => (0x00F5C494, [0xBC, 0x0]),
            RSEdition.Remastered => (0x00F5C494 + 0x3080, [0xBC, 0x0]),
            RSEdition.Remastered_Learn_And_Play => (0x00F5C494 + 0x4080, [0xBC, 0x0]),
            _ => throw new ArgumentOutOfRangeException(nameof(edition), edition, "Unknown edition")
        };
    }

    /// <summary>
    /// Get the pointer to the song timer for the given edition
    /// </summary>
    /// <param name="edition"></param>
    /// <returns>A tuple of (entry address, pointer offsets)</returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>   
    public static (int entryAddress, int[] offsets) GetSongTimerPointer(RSEdition edition)
    {
        //Weird static address: (0x01567AB0, new int[]{ 0x80, 0x20, 0x10C, 0x244 })
        //Candidate #1: (0x00F5C5AC, [{ 0xB0, 0x538, 0x8 ]})
        //Candidate #2: (0x00F5C4CC, [{ 0x5F0, 0x538, 0x8 ]})
        return edition switch
        {
            RSEdition.Remastered_Just_In_Case_We_Need_It_Beta => (0x00F5C5AC, [0xB0, 0x538, 0x8]),
            RSEdition.Remastered => (0x00F5C5AC + 0x3080, [0xB0, 0x538, 0x8]),
            RSEdition.Remastered_Learn_And_Play => (0x00F5C5AC + 0x4080, [0xB0, 0x538, 0x8]),
            _ => throw new ArgumentOutOfRangeException(nameof(edition), edition, "Unknown edition")
        };
    }

    /// <summary>
    /// Get the pointer to the arrangement hash for the given edition
    /// </summary>
    /// <param name="edition"></param>
    /// <returns>A tuple of (entry address, pointer offsets)</returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public static (int entryAddress, int[] offsets) GetArrangementHashPointer(RSEdition edition)
    {
        return edition switch
        {
            RSEdition.Remastered_Just_In_Case_We_Need_It_Beta => (0x00F5C5AC, [0x18, 0x18, 0xC, 0x1C0, 0x0]),
            RSEdition.Remastered => (0x00F5C5AC + 0x3080, [0x18, 0x18, 0xC, 0x1C0, 0x0]),
            RSEdition.Remastered_Learn_And_Play => (0x00F5C5AC + 0x4080, [0x18, 0x18, 0xC, 0x1C0, 0x0]),
            _ => throw new ArgumentOutOfRangeException(nameof(edition), edition, "Unknown edition")
        };
    }

    /// <summary>
    /// Get the address of the current gameStage string for the given edition.
    /// </summary>
    /// <remarks>
    /// Static .data-section buffer at Rocksmith2014.exe+0xF5F7C9 (Remastered) - the
    /// canonical cell Rocksmith's UI writes for the current stage. Tracks correctly
    /// across all observed states, including all three modes' pause stages and the
    /// SA song-select / song-options / tuner screens. Found via Cheat Engine
    /// string-scan ("gcpre"), keeping the hit that round-trips across mode/menu
    /// transitions; stored as a literal string buffer ("main\0...", "las_songs\0...").
    ///
    /// Returned as (entryAddress, []) so the FollowPointers codepath handles it
    /// uniformly: empty offsets means a direct read at base+entry.
    ///
    /// KNOWN ENGINE BEHAVIOR (not a reader bug, do not "correct" here): Rocksmith
    /// does not update this cell on pause->resume or pause->restart for ANY mode; it
    /// keeps reading "*_pause" until the user navigates to a menu or starts another
    /// song. Consumers needing play/pause state should use game_state (SnifferState).
    ///
    /// EDITION SHIFTS (Beta / LaP): back-derived via the +0x3080 / +0x4080 shifts
    /// used by every other pointer in this file; not independently verified. If
    /// those editions read garbage/empty gameStage with otherwise-working behavior,
    /// suspect this address first.
    /// </remarks>
    /// <param name="edition"></param>
    /// <returns>A tuple of (entry address, pointer offsets) - offsets is empty
    /// for a direct static read.</returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public static (int entryAddress, int[] offsets) GetCurrentMenuPointer(RSEdition edition)
    {
        return edition switch
        {
            RSEdition.Remastered_Just_In_Case_We_Need_It_Beta => (0x00F5C749, []),
            RSEdition.Remastered => (0x00F5C749 + 0x3080, []),
            RSEdition.Remastered_Learn_And_Play => (0x00F5C749 + 0x4080, []),
            _ => throw new ArgumentOutOfRangeException(nameof(edition), edition, "Unknown edition")
        };
    }

    /// <summary>
    /// Get the pointer to the note data when in learn a song mode for the given edition
    /// </summary>
    /// <param name="edition"></param>
    /// <returns>A tuple of (entry address, pointer offsets)</returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public static (int entryAddress, int[] offsets) GetLearnASongNoteDataPointer(RSEdition edition)
    {
        return edition switch
        {
            RSEdition.Remastered_Just_In_Case_We_Need_It_Beta => (0x00F5C5AC, [0xB0, 0x18, 0x4, 0x84, 0x0]),
            RSEdition.Remastered => (0x00F5C5AC + 0x3080, [0xB0, 0x18, 0x4, 0x84, 0x0]),
            RSEdition.Remastered_Learn_And_Play => (0x00F5C5AC + 0x4080, [0xB0, 0x18, 0x4, 0x84, 0x0]),
            _ => throw new ArgumentOutOfRangeException(nameof(edition), edition, "Unknown edition")
        };
    }

    /// <summary>
    /// Get the pointer to the note data when in score attack mode for the given edition
    /// </summary>
    /// <param name="edition"></param>
    /// <returns>A tuple of (entry address, pointer offsets)</returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public static (int entryAddress, int[] offsets) GetScoreAttackNoteDataPointer(RSEdition edition)
    {
        return edition switch
        {
            RSEdition.Remastered_Just_In_Case_We_Need_It_Beta => (0x00F5C5AC, [0xB0, 0x18, 0x4, 0x4C, 0x0]),
            RSEdition.Remastered => (0x00F5C5AC + 0x3080, [0xB0, 0x18, 0x4, 0x4C, 0x0]),
            RSEdition.Remastered_Learn_And_Play => (0x00F5C5AC + 0x4080, [0xB0, 0x18, 0x4, 0x4C, 0x0]),
            _ => throw new ArgumentOutOfRangeException(nameof(edition), edition, "Unknown edition")
        };
    }

    /// <summary>
    /// Get the pointer to the current Path (arrangement type) byte for the given edition.
    /// </summary>
    /// <remarks>
    /// 1-byte enum at a stable menu-level address - populated from launch (defaults
    /// 1 = Lead), mutated only when the user switches Path in options or
    /// song-select, persistent through every gameStage. Encodes only the path type,
    /// so it is invariant to bonus/alternate arrangements. Works in Nonstop Play,
    /// where the arrangement_hash pointer does not populate.
    ///
    /// Value mapping: 0x01 -> Lead, 0x02 -> Rhythm, 0x04 -> Bass, else Unknown.
    /// </remarks>
    public static (int entryAddress, int[] offsets) GetCurrentPathPointer(RSEdition edition)
    {
        // CE table entry: Rocksmith2014.exe+00F5F570, offsets [0x1FC, 0x10] (CE display
        // order: outermost first), read as Byte. Walk order (FollowPointers) is the
        // reverse: [0x10, 0x1FC].
        //
        // Beta / LaP bases are back-derived via the standard +0x3080 / +0x4080 shifts;
        // not independently verified - if those editions read 0x00 for Path with
        // otherwise-working behavior, suspect this address first.
        return edition switch
        {
            RSEdition.Remastered_Just_In_Case_We_Need_It_Beta => (0x00F5C4F0, [0x10, 0x1FC]),
            RSEdition.Remastered => (0x00F5C4F0 + 0x3080, [0x10, 0x1FC]),
            RSEdition.Remastered_Learn_And_Play => (0x00F5C4F0 + 0x4080, [0x10, 0x1FC]),
            _ => throw new ArgumentOutOfRangeException(nameof(edition), edition, "Unknown edition")
        };
    }

    /// <summary>
    /// Get the address of the pause-menu mode byte for the given edition.
    /// </summary>
    /// <remarks>
    /// 1-byte static .data cell at module+0xF5F5FC (Remastered) encoding
    /// blocking-overlay depth:
    ///
    ///     0: no blocking overlay (gameplay, menus, song select, loading, review)
    ///     1: sub-overlay (tuner reached from the pause menu or the main menu's
    ///         Tools sub-menu, other sub-prompts)
    ///     2: top-level overlay (in-song pause menu, Mixer, restart confirmation,
    ///         main menu's Tools overlay)
    ///
    /// This is NOT "is the user paused during gameplay" - value 2 also fires for
    /// the main menu's Tools overlay; "paused during a song" requires combining
    /// with a SnifferState check. Because 1 and 2 both mean "in a pause sub-flow,"
    /// the correct paused test is mode != 0, not mode == 2.
    ///
    /// Cross-mode validated (SA, LaS, NSP, Guitarcade), no warmup gate, and
    /// survives relaunch as a true static (verified by memory-neighborhood
    /// inspection and a no-rescan relaunch test).
    ///
    /// EDITION SHIFTS (Beta / LaP): back-derived via the standard +0x3080 /
    /// +0x4080 shifts; not independently verified - if those editions read
    /// constant zero across pause states, suspect this address first.
    ///
    /// Discovery credit: kokolihapihvi shared two named candidates from his RE
    /// project (MustBlockInputsDueToPauseMenu, EnablePauseMenu); both were dead in
    /// the current Remastered build, but the neighborhood they pointed at led
    /// directly to this find.
    /// </remarks>
    /// <param name="edition"></param>
    /// <returns>A tuple of (entry address, pointer offsets) - offsets is empty
    /// for a direct static read.</returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public static (int entryAddress, int[] offsets) GetPauseMenuModePointer(RSEdition edition)
    {
        return edition switch
        {
            RSEdition.Remastered_Just_In_Case_We_Need_It_Beta => (0x00F5C57C, []),
            RSEdition.Remastered => (0x00F5C57C + 0x3080, []),
            RSEdition.Remastered_Learn_And_Play => (0x00F5C57C + 0x4080, []),
            _ => throw new ArgumentOutOfRangeException(nameof(edition), edition, "Unknown edition")
        };
    }

    /// <summary>
    /// Get the pointer to the currently-loaded arrangement GUID (PLAY_arrID) for the given edition.
    /// </summary>
    /// <remarks>
    /// 16-byte cell holding the loaded arrangement's GUID in Microsoft LE layout,
    /// read through a 5-deep chain rooted at the same entry address as the
    /// arrangement_hash chain (Remastered base 0xF5F62C). Converted via
    /// new Guid(bytes).ToString("N").ToUpperInvariant() to the 32-char uppercase
    /// hex form matching songDetails.arrangements[].arrangementID (case-sensitive
    /// comparison downstream).
    ///
    /// STATE COVERAGE:
    ///   las_game / las_pause          yes currently-playing arrangement GUID
    ///   nonstopplaygame / nsp_pause   yes the only chain that resolves arrangements
    ///                                   in Nonstop (arrangement_hash never
    ///                                   populates there), incl. bonus/alternate
    ///   Nonstop carousel / nsp_tuner  no cell not valid until song-load proper
    ///   sa_game / sa_pause            no Score Attack has its own subsystem - keep
    ///                                   using arrangement_hash there
    ///   Menus / transitions / review  no not reliable
    ///
    /// Populates when a song starts loading and stays valid through play and pause.
    /// In LaS its output is byte-for-byte identical to arrangement_hash
    /// (cross-validated by reading both chains simultaneously), so a single chain
    /// spans LaS and Nonstop. Survives process restart and Nonstop entry/exit
    /// cycles; tracks across songs and across arrangements within a song.
    ///
    /// EDITION SHIFTS (Beta / LaP): back-derived via the standard +0x3080 /
    /// +0x4080 shifts; not independently verified - all-zero GUIDs across gameplay
    /// states would point here first.
    /// </remarks>
    /// <param name="edition"></param>
    /// <returns>A tuple of (entry address, pointer offsets).</returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public static (int entryAddress, int[] offsets) GetPlayArrIDPointer(RSEdition edition)
    {
        // CE table entry: Rocksmith2014.exe+00F5F62C, offsets [0x20, 0x84, 0x4, 0x18, 0xB0]
        // (CE display order: outermost first). Walk order (FollowPointers) is the
        // REVERSE: [0xB0, 0x18, 0x4, 0x84, 0x20] - same convention as every other
        // multi-offset chain in this file.
        return edition switch
        {
            RSEdition.Remastered_Just_In_Case_We_Need_It_Beta => (0x00F5C5AC, [0xB0, 0x18, 0x4, 0x84, 0x20]),
            RSEdition.Remastered => (0x00F5C5AC + 0x3080, [0xB0, 0x18, 0x4, 0x84, 0x20]),
            RSEdition.Remastered_Learn_And_Play => (0x00F5C5AC + 0x4080, [0xB0, 0x18, 0x4, 0x84, 0x20]),
            _ => throw new ArgumentOutOfRangeException(nameof(edition), edition, "Unknown edition")
        };
    }
}