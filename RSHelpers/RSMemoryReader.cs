using RockSnifferLib.Logging;
using RockSnifferLib.SysHelpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

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
        /// Look for HIRC pointer and read if possible
        /// </summary>
        public void DoHIRCReadout()
        {
            // SONG ID
            //
            //This one is super weird, I couldn't find any pointers to any of the instances in memory
            //So we're going to have to improvise by looking for BINK .bnk data in memory that contains the filename of the preview audio
            //(And possibly the song audio itself)
            //First we need to find the header "HIRC", followed by the length of the data that follows
            //There are two instances of "HIRC" in memory at all times, one of which does not have a length byte
            //The one that is in static address space: "HIRCt" 48 49 52 43 74
            //And one that is in heap presumably: "HIRC?" 48 49 52 43 BF/C4

            //Try to find the HIRC header in memory if we don't have a reference to it
            //And only search for HIRC headers when a song isnt playing
            //since rocksmith doesn't maintain them in memory during songs
            if (HIRCPtr == IntPtr.Zero && prevReadout.songTimer == 0)
            {
                try
                {
                    HIRCPtr = FindHIRC();

                    if (Logger.logHIRCScan && HIRCPtr != IntPtr.Zero)
                    {
                        Logger.Log("HIRC addr: 0x{0:X}", HIRCPtr.ToInt32());
                    }
                }
                catch
                {
                    if (Logger.logHIRCScan)
                    {
                        Logger.Log("HIRC addr fetch failed");
                    }
                }
            }
            try
            {
                //If the pointer isn't zero
                if (HIRCPtr != IntPtr.Zero)
                {
                    //Try to read the HIRC data
                    ReadHIRC();
                }
            }
            catch
            {
                //If there was an error, assume that the pointer is not valid and needs to be re-fetched
                RevalidateHIRC();
            }
        }

        /// <summary>
        /// Read song timer and note data from memory
        /// </summary>
        /// <returns></returns>
        public RSMemoryReadout DoReadout()
        {
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

        /// <summary>
        /// Causes HIRC pointer to be revalidated
        /// </summary>
        public void RevalidateHIRC()
        {
            //Return if pointer is already invalidated
            if (HIRCPtr == IntPtr.Zero)
            {
                return;
            }

            if (Logger.logHIRCScan)
            {
                Logger.Log("Revalidating HIRC pointer");
            }

            //Add the current pointer to the invalid list
            invalidHIRCPtrs.Add(new IntPtr(HIRCPtr.ToInt32()));

            HIRCPtr = IntPtr.Zero;
        }

        /// <summary>
        /// Scans for the HIRC data structure in memory
        /// </summary>
        /// <returns></returns>
        private IntPtr FindHIRC()
        {
            //Get base address
            IntPtr baseAddress = rsProcess.MainModule.BaseAddress;

            //Read memory in chunks
            int chunkSize = 1024 * 32; //32kb chunk

            byte[] scan = new byte[chunkSize];
            byte[] segment = new byte[hircPattern.Length];

            int i = 0;
            int i2 = 0;

            List<IntPtr> hircPointers = new List<IntPtr>();

            //Start at an arbitrary point in memory, after most of the static stuff
            //End at an arbitrary point in memory, mostly a guess, hopefully after the heap where RS allocates HIRC data
            //Start next chunk 1kb before the previous chunks end in case the HIRC pattern is on the chunks border
            for (i = 0x10000000; i < 0x40000000; i += (chunkSize - 1024))
            {
                //Read chunkSize bytes
                MemoryHelper.ReadBytesFromMemory(rsProcessHandle, IntPtr.Add(baseAddress, i), chunkSize, ref scan);

                //Search for the HIRC byte array pattern
                i2 = Array.IndexOf(scan, hircPattern[0], 0);
                while (i2 >= 0 && i2 <= scan.Length - hircPattern.Length)
                {
                    Buffer.BlockCopy(scan, i2, segment, 0, hircPattern.Length);

                    if (segment.SequenceEqual(hircPattern))
                    {
                        IntPtr newPtr = IntPtr.Add(baseAddress, i + i2);

                        //Add value to the list
                        if (!hircPointers.Contains(newPtr))
                        {
                            hircPointers.Add(newPtr);
                        }
                    }

                    //We need to only skip ahead 1 byte, because a 0x48 within 4 bytes before the HIRC pattern will fool us
                    i2 = Array.IndexOf(scan, hircPattern[0], i2 + 1);
                }
            }

            //If no pointer was good, print error
            if (Logger.logHIRCScan && hircPointers.Count > 0)
            {
                Logger.Log("Found {0} candidate HIRC pointers, {1} invalid", hircPointers.Count, invalidHIRCPtrs.Count);
            }

            //Pick the first valid pointer
            foreach (IntPtr ptr in hircPointers)
            {
                //Skip all invalidated pointers
                if (invalidHIRCPtrs.Contains(ptr))
                {
                    continue;
                }

                if (IsValidHIRCPointer(ptr))
                {
                    return ptr;
                }
            }

            //If no pointer was good, print error
            if (Logger.logHIRCScan)
            {
                Logger.LogError("Warning! Could not find valid HIRC pointer!");
            }

            //If there was no valid pointer, maybe it's worth revisiting the old ones
            invalidHIRCPtrs.Clear();

            return IntPtr.Zero;
        }

        //HIRC data:
        //
        //4 bytes - "HIRC"
        //1 byte - HIRC data length
        //Contents unknown
        //
        //4 bytes - "STID"
        //1 byte - STID data length
        //Contains some version information and a string filename

        private bool IsValidHIRCPointer(IntPtr ptr)
        {
            //Call the function discarding out parameters
            return IsValidHIRCPointer(ptr, out int _, out int _);
        }

        private bool IsValidHIRCPointer(IntPtr ptr, out int hLen, out int sLen)
        {
            //TODO: Read bigger chunk into byte array and do validity checks on that to avoid multiple memory reads

            if (Logger.logHIRCValidation)
            {
                Logger.Log("Checking validity of HIRC pointer 0x{0:X}", ptr.ToInt32());
            }

            hLen = 0;
            sLen = 0;

            //Verify that we have "HIRC" at the pointer
            byte[] hirc = MemoryHelper.ReadBytesFromMemory(rsProcessHandle, ptr, 4);
            if (!hirc.SequenceEqual(hircPattern))
            {
                return false;
            }

            //Read HIRC data length
            hLen = MemoryHelper.ReadInt32FromMemory(rsProcessHandle, IntPtr.Add(ptr, 4));
            //Add "HIRC" to length
            hLen += 4;

            //Verify that the length of the HIRC data is not too long
            if (hLen > 1000)
            {
                return false;
            }

            //Read STID data length
            sLen = MemoryHelper.ReadInt32FromMemory(rsProcessHandle, IntPtr.Add(ptr, 8 + hLen));
            //Add "STID" to length
            sLen += 4;

            //Verity that the length of the STID data is not too long
            if (sLen > 1000)
            {
                return false;
            }

            //Verify that the length of the STID data is not too short
            if (sLen <= 4)
            {
                return false;
            }

            //Verify that containing STID data name starts with "Song_", and ends with "_Preview"
            string STIDName = ReadSTIDName(ptr, hLen, sLen);
            if (!(STIDName.StartsWith("Song_") && STIDName.EndsWith("_Preview")))
            {
                return false;
            }

            //If all checks passed
            return true;
        }

        private void ReadHIRC()
        {
            int hLen = 0;
            int sLen = 0;

            //If our current HIRC pointer is no longer valid, don't try to read
            if (!IsValidHIRCPointer(HIRCPtr, out hLen, out sLen))
            {
                HIRCPtr = IntPtr.Zero;
                return;
            }

            /*
                53 54 49 44 -- STID
                uint32: length of section
                uint32: unknown integer, always 01 00 00 00
                uint32: number of SoundBanks
                FOR EACH (SoundBank) {
                    uint32: SoundBank id
                    byte: length of SoundBank name
                    char[]: String with given length, e.g. "music". Can be used to find the SoundBank file name: "music.bnk".
                } END FOR
            */

            //Read STID name
            string s = ReadSTIDName(HIRCPtr, hLen, sLen);

            //If it starts with Song_ assume it's correct
            if (s.StartsWith("Song_"))
            {
                //Take a substring from the STID name, removing "Song_" from the beginning, and "_Preview" from the end
                readout.songID = s.Substring(5, s.Length - 13);
            }
            else
            {
                //Otherwise revalidate the pointer
                RevalidateHIRC();
            }
        }

        private string ReadSTIDName(IntPtr ptr, int hLen, int sLen)
        {
            //Read STID data to byte buffer
            byte[] stid = MemoryHelper.ReadBytesFromMemory(rsProcessHandle, IntPtr.Add(ptr, 8 + hLen), sLen);

            //Skip irrelevant data to us and just read length byte
            byte len = stid[16];

            //Read string
            char[] strBuf = new char[len];
            //Copy string from stid buffer
            Array.Copy(stid, 17, strBuf, 0, len);

            string s = new string(strBuf);

            if (Logger.logHIRCValidation)
            {
                Logger.Log("HIRC->STID->name = '{0}'", s);
            }

            return s;
        }
    }
}
