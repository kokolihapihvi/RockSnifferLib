using RockSnifferLib.SysHelpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace RockSnifferLib.RSHelpers
{
    public class RSMemoryReader
    {
        private RSMemoryReadout readout = new RSMemoryReadout();
        private RSMemoryReadout prevReadout = new RSMemoryReadout();

        //Pointers
        private IntPtr HIRCPtr = IntPtr.Zero;

        //List of invalid pointers
        List<IntPtr> invalidHIRCPtrs = new List<IntPtr>();

        //Search pattern for "HIRC"
        private readonly static byte[] hircPattern = new byte[] { 0x48, 0x49, 0x52, 0x43 };

        //Process handles
        public Process rsProcess;
        private IntPtr rsProcessHandle;

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
            byte[] bytes = MemoryHelper.ReadBytesFromMemory(rsProcessHandle, FollowPointers(0x00F5C494, new int[] { 0xBC, 0x0 }), 128);

            //Find the first 0 in the array
            int end = Array.IndexOf<byte>(bytes, 0);

            //If there was a 0 in the array
            if (end > 0)
            {
                //Copy into a char array
                char[] chars = new char[end];

                Array.Copy(bytes, chars, end);

                //Create string from char array
                string preview_name = new string(chars);

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

            // NOTE DATA
            //
            //Candidate #1: FollowPointers(0x00F5C5AC, new int[] {0xB0, 0x18, 0x4, 0x84, 0x30})
            //Candidate #2: FollowPointers(0x00F5C4CC, new int[] {0x5F0, 0x18, 0x4, 0x84, 0x30})
            ReadNoteData(FollowPointers(0x00F5C5AC, new int[] { 0xB0, 0x18, 0x4, 0x84, 0x30 }));

            //Copy over everything when a song is running
            if (readout.songTimer > 0)
            {
                readout.CopyTo(ref prevReadout);
            }

            //Always copy over important fields
            prevReadout.songID = readout.songID;
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
            }

            //Return the final address
            return finalAddress;
        }

        private void ReadSongTimer(IntPtr timerAddress)
        {
            //Read float from memory and assign field on readout
            readout.songTimer = MemoryHelper.ReadFloatFromMemory(rsProcessHandle, timerAddress);
        }

        private void ReadNoteData(IntPtr structAddress)
        {
            //Riff repeater data:
            //
            //Offsets
            //0000 - total notes hit
            //0004 - current note streak
            //0008 - unknown
            //000C - highest note streak
            //0010 - total notes missed
            //0014 - missed note streak

            //Read and assign all fields
            readout.totalNotesHit = MemoryHelper.ReadInt32FromMemory(rsProcessHandle, structAddress);
            readout.currentHitStreak = MemoryHelper.ReadInt32FromMemory(rsProcessHandle, IntPtr.Add(structAddress, 0x0004));
            readout.unknown = MemoryHelper.ReadInt32FromMemory(rsProcessHandle, IntPtr.Add(structAddress, 0x0008));
            readout.highestHitStreak = MemoryHelper.ReadInt32FromMemory(rsProcessHandle, IntPtr.Add(structAddress, 0x000C));
            readout.totalNotesMissed = MemoryHelper.ReadInt32FromMemory(rsProcessHandle, IntPtr.Add(structAddress, 0x0010));
            readout.currentMissStreak = MemoryHelper.ReadInt32FromMemory(rsProcessHandle, IntPtr.Add(structAddress, 0x0014));
        }
    }
}
