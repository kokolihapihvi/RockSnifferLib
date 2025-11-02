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
    /// Get the pointer to the current menu for the given edition
    /// </summary>
    /// <param name="edition"></param>
    /// <returns>A tuple of (entry address, pointer offsets)</returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>   
    public static (int entryAddress, int[] offsets) GetCurrentMenuPointer(RSEdition edition)
    {
        return edition switch
        {
            RSEdition.Remastered_Just_In_Case_We_Need_It_Beta => (0x00F5C5AC, [0x18, 0x18, 0xC, 0x14]),
            RSEdition.Remastered => (0x00F5C5AC + 0x3080, [0x18, 0x18, 0xC, 0x14]),
            RSEdition.Remastered_Learn_And_Play => (0x00F5C5AC + 0x4080, [0x18, 0x18, 0xC, 0x14]),
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
}