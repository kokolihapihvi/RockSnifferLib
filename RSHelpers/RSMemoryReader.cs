using RockSnifferLib.SysHelpers;
using System;
using System.Diagnostics;

namespace RockSnifferLib.RSHelpers
{
    public class RSMemoryReader
    {
        private RSMemoryReadout readout = new RSMemoryReadout();
        private RSMemoryReadout prevReadout = new RSMemoryReadout();

        //Process handles
        public Process rsProcess;
        private readonly IntPtr rsProcessHandle;

        public RSMemoryReader(Process rsProcess)
        {
            this.rsProcess = rsProcess;

            rsProcessHandle = rsProcess.Handle;
        }

        /// <summary>
        /// Read song timer and note data from memory
        /// </summary>
        /// <returns></returns>
        public RSMemoryReadout DoReadout()
        {
            // SONG ID
            //
            // Seems to be a zero terminated string in the format: Song_SONGID_Preview
            //
            //Candidate #1: FollowPointers(0x00F5C494, new int[] { 0xBC, 0x0 })
            //Candidate #2: FollowPointers(0x00F80CEC, new int[] { 0x598, 0x1B8, 0x0 })
            //Candidate #3: FollowPointers(0x00F5DAFC, new int[] { 0x608, 0x1B8, 0x0 })
            string preview_name = MemoryHelper.ReadStringFromMemory(rsProcessHandle, FollowPointers(0x00F5C494, new int[] { 0xBC, 0x0 }));

            //If there was string in memory
            if (preview_name != null)
            {
                //Verify Play_ prefix and _Preview suffix
                if (preview_name.StartsWith("Play_") && preview_name.EndsWith("_Preview"))
                {
                    //Remove Play_ prefix and _Preview suffix
                    string song_id = preview_name.Substring(5, preview_name.Length - 13);

                    //Assign to readout
                    readout.songID = song_id;
                }
            }

            // SONG TIMER
            //
            //Weird static address: FollowPointers(0x01567AB0, new int[]{ 0x80, 0x20, 0x10C, 0x244 })
            //Candidate #1: FollowPointers(0x00F5C5AC, new int[] { 0xB0, 0x538, 0x8 })
            //Candidate #2: FollowPointers(0x00F5C4CC, new int[] { 0x5F0, 0x538, 0x8 })
            ReadSongTimer(FollowPointers(0x00F5C5AC, new int[] { 0xB0, 0x538, 0x8 }));

            // ARRANGEMENT HASH
            //
            string arrangement_hash = MemoryHelper.ReadStringFromMemory(rsProcessHandle, FollowPointers(0x00F5C5AC, new int[] { 0x18, 0x18, 0xC, 0x1C0, 0x0 }));
            if (arrangement_hash != null)
            {
                readout.arrangementID = arrangement_hash;
            }

            string game_stage = MemoryHelper.ReadStringFromMemory(rsProcessHandle, FollowPointers(0x00F5C5AC, new int[] { 0x18, 0x18, 0xC, 0x27C }));
            if(game_stage == null)
            {
                game_stage = MemoryHelper.ReadStringFromMemory(rsProcessHandle, FollowPointers(0x00F5C5AC, new int[] { 0x18, 0x18, 0xC, 0x14 }));
            }

            if (game_stage != null)
            {
                readout.gameStage = game_stage;
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
            if (!ReadNoteData(FollowPointers(0x00F5C5AC, new int[] { 0xB0, 0x18, 0x4, 0x84, 0x0 })))
            {
                //Score attack
                if (!ReadScoreAttackNoteData(FollowPointers(0x00F5C5AC, new int[] { 0xB0, 0x18, 0x4, 0x4C, 0x0 })))
                {
                    readout.mode = RSMode.UNKNOWN;
                }
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

            return prevReadout;
        }

        private IntPtr FollowPointers(int entryAddress, int[] offsets)
        {
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

            //Assign mode
            readout.mode = RSMode.LEARNASONG;

            //Riff repeater data:
            //
            //Offsets
            //0030 - total notes hit
            //0034 - current note streak
            //003C - highest note streak
            //0040 - total notes missed
            //0044 - missed note streak

            //Read and assign all fields
            readout.totalNotesHit = MemoryHelper.ReadInt32FromMemory(rsProcessHandle, IntPtr.Add(structAddress, 0x0030));
            readout.currentHitStreak = MemoryHelper.ReadInt32FromMemory(rsProcessHandle, IntPtr.Add(structAddress, 0x0034));
            readout.highestHitStreak = MemoryHelper.ReadInt32FromMemory(rsProcessHandle, IntPtr.Add(structAddress, 0x003C));
            readout.totalNotesMissed = MemoryHelper.ReadInt32FromMemory(rsProcessHandle, IntPtr.Add(structAddress, 0x0040));
            readout.currentMissStreak = MemoryHelper.ReadInt32FromMemory(rsProcessHandle, IntPtr.Add(structAddress, 0x0044));

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

            readout.mode = RSMode.SCOREATTACK;

            //Score attack data:
            //
            //Offsets
            //003C - current hit streak
            //0040 - current miss streak
            //0044 - highest hit streak
            //0048 - highest miss streak
            //004C - total notes hit
            //0050 - total notes missed
            //0054 - current hit streak
            //0058 - current miss streak
            //0074 - current perfect hit streak
            //0078 - total perfect hits
            //007C - current late hit streak
            //0080 - total late hits
            //0084 - perfect phrases
            //0088 - good phrases
            //008C - passed phrases
            //0090 - failed phrases
            //0094 - current perfect phrase streak
            //0098 - current good phrase streak
            //009C - current passed phrase streak
            //00A0 - current failed phrase streak
            //00A4 - highest perfect phrase streak
            //00A8 - highest good phrase streak
            //00AC - highest passed phrase streak
            //00B0 - highest failed phrase streak
            //00E4 - current score
            //00E8 - current multiplier
            //00EC - highest multiplier
            //01D0 - current path ("Lead"/"Rhythm"/"Bass")

            readout.totalNotesHit = MemoryHelper.ReadInt32FromMemory(rsProcessHandle, IntPtr.Add(structAddress, 0x004C));
            readout.currentHitStreak = MemoryHelper.ReadInt32FromMemory(rsProcessHandle, IntPtr.Add(structAddress, 0x003C));
            readout.highestHitStreak = MemoryHelper.ReadInt32FromMemory(rsProcessHandle, IntPtr.Add(structAddress, 0x0044));
            readout.totalNotesMissed = MemoryHelper.ReadInt32FromMemory(rsProcessHandle, IntPtr.Add(structAddress, 0x0050));
            readout.currentMissStreak = MemoryHelper.ReadInt32FromMemory(rsProcessHandle, IntPtr.Add(structAddress, 0x0040));

            return true;
        }
    }
}
