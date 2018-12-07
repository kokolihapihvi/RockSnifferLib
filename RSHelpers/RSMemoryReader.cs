using RockSnifferLib.Logging;
using RockSnifferLib.SysHelpers;
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RockSnifferLib.Sniffing;

namespace RockSnifferLib.RSHelpers
{
    public class RSMemoryReader
    {
        private const int NOTE_DATA_MAGIC = 111000;
        private RSMemoryReadout readout = new RSMemoryReadout();
        private RSMemoryReadout prevReadout = new RSMemoryReadout();

        public ProcessInfo PInfo = new ProcessInfo();
        IntPtr NoteDataMacAddress = IntPtr.Zero;
        Queue<IntPtr> NDAddressStack = new Queue<IntPtr>();

        public RSMemoryReader(Process rsProcess)
        {
            this.PInfo.rsProcess = rsProcess;

            this.PInfo.rsProcessHandle = rsProcess.Handle;
            this.PInfo.PID = (ulong)rsProcess.Id;

            switch (Environment.OSVersion.Platform)
            {
                case PlatformID.Unix:
                case PlatformID.MacOSX:
                    MacOSAPI.task_for_pid_wrapper(this.PInfo.PID, out this.PInfo.Task);
                    break;
            }
        }

        string lastState = "";
        public void DoPointerScanWin32()
        {
            if (readout.gameState.ToLower().Contains("game"))
            {
                if (this.lastState != readout.gameState)
                {
                    if (Logger.logMemoryReadout)
                        Logger.Log("Scanning for regions");
                    var regions = MemoryHelper.GetAllRegionsWin32(this.PInfo);
                    if (Logger.logMemoryReadout)
                        Logger.Log("Regions Found: " + regions.Count);
                    int regionCounter = 0;
                    Stopwatch s = new Stopwatch();
                    s.Start();
                    Parallel.For(0, regions.Count, (i, ls) =>
                    {
                        Interlocked.Increment(ref regionCounter);
                        var region = regions[i];
                        var address = region.Address;
                        var size = region.Size;
                        if (ls.IsStopped)
                            return;
                        byte[] buffer = MemoryHelper.ReadBytesFromMemory(this.PInfo, (IntPtr)address, (int)size);
                        if (buffer.Length == (int)size)
                        {
                            byte[] hint3 = { 0x00, 0x3A, 0x6C, 0x61, 0x73, 0x5F, 0x67, 0x61, 0x6D, 0x65, 0x00 }; //:LAS_Game
                            byte[] hint4 = { 0x00, 0x3A, 0x4C, 0x41, 0x53, 0x5F, 0x47, 0x61, 0x6D, 0x65, 0x00 }; //:las_game
                            IntPtr fadd = IntPtr.Zero;
                            bool validpid = false;
                            int ret = 0;

                            do
                            {
                                if (ls.IsStopped)
                                    return;
                                ret = MemoryHelper.IndexOfBytes(buffer, hint3, hint4, ret, buffer.Length);
                                if (ret > 0)
                                {
                                    if (ls.IsStopped)
                                        return;
                                    fadd = new IntPtr((int)address + (ret));
                                    string pid = CreateStringFromBytes(IntPtr.Subtract(fadd, 0x20), 0x21);
                                    if (pid != "dependency_scoreattackcomponents")
                                    {
                                        validpid = true;

                                        ls.Stop();
                                        s.Stop();
                                        //Logger.Log("ret: {2} valid pid: {0} Elapsed: {1}", validpid, s.Elapsed.ToString(), ret);
                                    }
                                    else
                                    {
                                        Logger.Log("bad string match : {0} {1}", ret, pid);
                                        ret = ret + hint3.Length;
                                        fadd = IntPtr.Zero;
                                    }
                                }
                                else
                                    break;
                            } while (validpid == false);

                            if (fadd != IntPtr.Zero)
                            {
                                string pid = CreateStringFromBytes(IntPtr.Subtract(fadd, 0x20), 0x21); /* read one byte extra to include null terminating character */
                                if (Logger.logMemoryReadout)
                                    Logger.Log("Region: {0} Address: {1} PersistentID: {2}", i, fadd.ToString("X8"), pid);
                                readout.persistentID = pid;
                            }
                        }
                    });
                    this.lastState = readout.gameState;
                }
                else
                {
                    this.lastState = readout.gameState;
                }
            }
            else
            {
                this.lastState = readout.gameState;
            }
        }
        /* scan memory regions looking for NOTE_DATA_MAGIC */
        public void DoPointerScanMacOS()
        {
            if (CheckForValidNoteDataAddress(NoteDataMacAddress))
                return;
            int itemsFound = 0;
            ulong beginAddress = 0x0;
            ulong endAddress = 0x00007FFFFFE00000;
            ulong dataAlignment = 4;
            var regions = MemoryHelper.GetAllRegionsMacOS(this.PInfo, beginAddress, endAddress);
            regions.Reverse();
            if (Logger.logMemoryReadout)
                Logger.Log("Regions Found: " + regions.Count);
            int regionCounter = 0;
            Parallel.For(0, regions.Count, (i, loopState) =>
            {
                Interlocked.Increment(ref regionCounter);
                var region = regions[i];
                var address = region.Address;
                var size = region.Size;
                ulong dataIndex = 0;

                if (beginAddress < address + size && endAddress > address)
                {
                    if (beginAddress > address)
                    {
                        dataIndex = (beginAddress - address);
                        if (dataIndex % dataAlignment > 0)
                        {
                            dataIndex += dataAlignment - (dataIndex % dataAlignment);
                        }
                    }
                    if (endAddress < address + size)
                    {
                        size = endAddress - address;
                    }
                    if (loopState.IsStopped)
                        return;

                    byte[] hint3 = { 0x00, 0x3A, 0x6C, 0x61, 0x73, 0x5F, 0x67, 0x61, 0x6D, 0x65, 0x00 }; //:LAS_Game
                    byte[] hint4 = { 0x00, 0x3A, 0x4C, 0x41, 0x53, 0x5F, 0x47, 0x61, 0x6D, 0x65, 0x00 }; //:las_game
                    ulong idx = MemoryHelper.ScanMem(this.PInfo, (IntPtr)address, (int)size, dataIndex, NOTE_DATA_MAGIC);
                    if (itemsFound == 0)
                    {
                        ulong idx2 = MemoryHelper.ScanMemChar(this.PInfo, (IntPtr)address, (int)size, dataIndex, hint3, hint4, i);
                        if (idx2 != 0)
                        {
                            IntPtr ptr2 = (IntPtr)(address + idx2);
                            string pid = CreateStringFromBytes(IntPtr.Subtract(ptr2, 0x20), 0x21); /* read one byte extra to include null terminating character */
                            if (Logger.logMemoryReadout)
                                Logger.Log("Region: {0} Address: {1} PersistentID: {2}", i, ptr2.ToString("X8"), pid);
                            Interlocked.Increment(ref itemsFound);
                            readout.persistentID = pid;
                        }
                    }
                    if (idx != 0)
                    {
                        IntPtr ptr = (IntPtr)(address + idx);
                        UInt32 tag = MemoryHelper.GetUserTag(this.PInfo, address, size);
                        if (tag == 2 && CheckForValidNoteDataAddress(ptr)) // VM_MEMORY_MALLOC_SMALL == 2
                        {
                            if (Logger.logMemoryReadout)
                                Logger.Log("Region: {0} Address: {1} Tag: {2}", i, ptr.ToString("X8"), tag);
                            NDAddressStack.Enqueue(ptr);
                            //Interlocked.Increment(ref itemsFound);
                        }
                    }
                }
            });
            if (Logger.logMemoryReadout)
                Logger.Log("Regions Processed: " + regionCounter);
            if (NDAddressStack.Count > 0)
                NoteDataMacAddress = NDAddressStack.Dequeue();
            //var mem = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);
            //Logger.Log(string.Format("Memory@PreGC: {0}mb", mem));
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            //mem = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);
            //Logger.Log(string.Format("Memory@afterGC: {0}mb", mem));
        }

        /* check if the NoteData address is accurate or not */
        public bool CheckForValidNoteDataAddress(IntPtr address)
        {
            if (address == IntPtr.Zero)
                return false;
            int val = MemoryHelper.ReadInt32FromMemory(this.PInfo, address);
            IntPtr newaddress = IntPtr.Subtract(address, 0x0008);
            bool ret = newaddress.ToString("X8").EndsWith("0");
            /* address ends with 0 and has magic number 111000 */
            if (val == NOTE_DATA_MAGIC && ret)
                return true;

            NoteDataMacAddress = IntPtr.Zero;
            return false;
        }

        public string CreateStringFromBytes(IntPtr address, int size)
        {
            byte[] bytes = MemoryHelper.ReadBytesFromMemory(PInfo, address, size);
            int end = Array.IndexOf<byte>(bytes, 0);

            //If there was a 0 in the array
            if (end > 0)
            {
                //Copy into a char array
                char[] chars = new char[end];

                Array.Copy(bytes, chars, end);
                string preview_name = new string(chars);
                return preview_name;
            }
            return string.Empty;
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

            //windows
            //byte[] bytes = MemoryHelper.ReadBytesFromMemory(PInfo, FollowPointers(0x00F5C494, new int[] { 0xBC, 0x0 }), 128);

            //mac
            byte[] bytes;
            switch (Environment.OSVersion.Platform)
            {
                case PlatformID.MacOSX:
                case PlatformID.Unix:
                    /* more info in MacOSAPI.cs */
                    bytes = MemoryHelper.ReadBytesFromMemory(PInfo, FollowPointers(0x0147B678, new int[] { 0xC4, 0x264, 0xBC, 0x0 }), 128);
                    break;
                default:
                    //bytes = MemoryHelper.ReadBytesFromMemory(PInfo, FollowPointers(0x00F5C80C, new int[] { 0x28, 0x10, 0x140 }), 128);
                    bytes = MemoryHelper.ReadBytesFromMemory(PInfo, FollowPointers(0x00F5C494, new int[] { 0xBC, 0x0 }), 128);

                    break;
            }

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


            // CURRENT STATE
            switch (Environment.OSVersion.Platform)
            {
                case PlatformID.MacOSX:
                case PlatformID.Unix:
                    // IMPLEMENT
                    break;
                default:
                    string s = CreateStringFromBytes(FollowPointers(0x00F5C5AC, new int[] { 0x28, 0x8C, 0x0 }), 255);
                    if (!string.IsNullOrEmpty(s))
                        readout.gameState = s;
                    break;
            }


            // SONG TIMER
            //
            switch (Environment.OSVersion.Platform)
            {
                case PlatformID.MacOSX:
                case PlatformID.Unix:
                    /* more info in MacOSAPI.cs */
                    ReadSongTimer(FollowPointers(0x01473BFC, new int[] { 0xC, 0x698, 0xD8 }));
                    IntPtr noteDataRoot = IntPtr.Subtract(NoteDataMacAddress, 0x0008);
                    if (CheckForValidNoteDataAddress(NoteDataMacAddress))
                    {
                        if (!ReadNoteData(noteDataRoot))
                        {
                            if (!ReadScoreAttackNoteData(noteDataRoot))
                            {
                                if (NDAddressStack.Count > 0)
                                {
                                    Logger.Log("trying other address");
                                    NoteDataMacAddress = NDAddressStack.Dequeue();
                                }
                                else
                                {
                                    Logger.Log("queue empty, starting scan");
                                    NoteDataMacAddress = IntPtr.Zero;
                                    NDAddressStack.Clear();
                                }
                                readout.mode = RSMode.UNKNOWN;
                            }
                        }
                    }
                    break;
                default:
                    //Weird static address: FollowPointers(0x01567AB0, new int[]{ 0x80, 0x20, 0x10C, 0x244 })
                    //Candidate #1: FollowPointers(0x00F5C5AC, new int[] { 0xB0, 0x538, 0x8 })
                    //Candidate #2: FollowPointers(0x00F5C4CC, new int[] { 0x5F0, 0x538, 0x8 })
                    ReadSongTimer(FollowPointers(0x00F5C5AC, new int[] { 0xB0, 0x538, 0x8 }));

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
                    if (readout.gameState.ToLower().Contains("learnasong"))
                    {
                        readout.mode = RSMode.LEARNASONG;
                        ReadNoteData(FollowPointers(0x00F5C5AC, new int[] { 0xB0, 0x18, 0x4, 0x84, 0x0 }));
                    }
                    else if (readout.gameState.ToLower().Contains("scoreattack"))
                    {
                        readout.mode = RSMode.SCOREATTACK;
                        ReadScoreAttackNoteData(FollowPointers(0x00F5C5AC, new int[] { 0xB0, 0x18, 0x4, 0x4C, 0x0 }));
                    }
                    else
                        readout.mode = RSMode.UNKNOWN;
                    break;
            }
            //Copy over everything when a song is running
            if (readout.songTimer > 0)
            {
                readout.CopyTo(ref prevReadout);
            }

            //Always copy over important fields
            prevReadout.songID = readout.songID;
            prevReadout.songTimer = readout.songTimer;
            prevReadout.gameState = readout.gameState;

            return prevReadout;
        }

        ulong Offset = 0;
        private IntPtr FollowPointers(int entryAddress, int[] offsets)
        {
            //Get base address
            IntPtr baseAddress = IntPtr.Zero;
            switch (Environment.OSVersion.Platform)
            {
                case PlatformID.MacOSX:
                case PlatformID.Unix:
                    if (Offset == 0)
                    {
                        int ret = MacOSAPI.find_main_binary_wrapper(PInfo.PID, out Offset);
                        if (ret != 0)
                        {
                            Logger.Log("Unable to find address of Rocksmith2014, try running with sudo");
                            System.Environment.Exit(ret);
                        }
                    }
                    baseAddress = (IntPtr)Offset;
                    break;
                default:
                    baseAddress = PInfo.rsProcess.MainModule.BaseAddress;
                    break;
            }

            //Add entry address
            IntPtr finalAddress = IntPtr.Add(baseAddress, entryAddress);

            //Add offsets
            foreach (int offset in offsets)
            {
                finalAddress = MemoryHelper.FollowPointer(PInfo, finalAddress, offset);

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
            readout.songTimer = MemoryHelper.ReadFloatFromMemory(PInfo, timerAddress);
        }

        private bool ReadNoteData(IntPtr structAddress)
        {
            //Check validity
            //No null pointers
            if (structAddress == IntPtr.Zero)
            {
                return false;
            }

            if (MemoryHelper.ReadInt32FromMemory(PInfo, IntPtr.Add(structAddress, 0x0008)) != 111000)
            {
                return false;
            }
            //This seems to be a magic number that is at this value when the pointer is valid

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
            var tnh = MemoryHelper.ReadInt32FromMemory(PInfo, IntPtr.Add(structAddress, 0x0030));
            var chs = MemoryHelper.ReadInt32FromMemory(PInfo, IntPtr.Add(structAddress, 0x0034));
            var hhs = MemoryHelper.ReadInt32FromMemory(PInfo, IntPtr.Add(structAddress, 0x003C));
            var tnm = MemoryHelper.ReadInt32FromMemory(PInfo, IntPtr.Add(structAddress, 0x0040));
            var cms = MemoryHelper.ReadInt32FromMemory(PInfo, IntPtr.Add(structAddress, 0x0044));
            readout.totalNotesHit = tnh;
            readout.currentHitStreak = chs;
            readout.highestHitStreak = hhs;
            readout.totalNotesMissed = tnm;
            readout.currentMissStreak = cms;

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
            if (MemoryHelper.ReadInt32FromMemory(PInfo, IntPtr.Add(structAddress, 0x0008)) != 111000)
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
            //0074 - current perfect hit streak - STORE
            //0078 - total perfect hits - STORE
            //007C - current late hit streak - STORE
            //0080 - total late hits - STORE
            //0084 - perfect phrases - STORE
            //0088 - good phrases - STORE
            //008C - passed phrases - STORE
            //0090 - failed phrases - STORE
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

            var tnh = MemoryHelper.ReadInt32FromMemory(PInfo, IntPtr.Add(structAddress, 0x004C));
            var chs = MemoryHelper.ReadInt32FromMemory(PInfo, IntPtr.Add(structAddress, 0x003C));
            var hhs = MemoryHelper.ReadInt32FromMemory(PInfo, IntPtr.Add(structAddress, 0x0044));
            var tnm = MemoryHelper.ReadInt32FromMemory(PInfo, IntPtr.Add(structAddress, 0x0050));
            var cms = MemoryHelper.ReadInt32FromMemory(PInfo, IntPtr.Add(structAddress, 0x0040));
            var cphs = MemoryHelper.ReadInt32FromMemory(PInfo, IntPtr.Add(structAddress, 0x0074));
            var tph = MemoryHelper.ReadInt32FromMemory(PInfo, IntPtr.Add(structAddress, 0x0078));
            var clhs = MemoryHelper.ReadInt32FromMemory(PInfo, IntPtr.Add(structAddress, 0x007c));
            var tlh = MemoryHelper.ReadInt32FromMemory(PInfo, IntPtr.Add(structAddress, 0x0080));
            var pp = MemoryHelper.ReadInt32FromMemory(PInfo, IntPtr.Add(structAddress, 0x0084));
            var gp = MemoryHelper.ReadInt32FromMemory(PInfo, IntPtr.Add(structAddress, 0x0088));
            var passedp = MemoryHelper.ReadInt32FromMemory(PInfo, IntPtr.Add(structAddress, 0x008C));
            var fp = MemoryHelper.ReadInt32FromMemory(PInfo, IntPtr.Add(structAddress, 0x0090));
            readout.totalNotesHit = tnh;
            readout.currentHitStreak = chs;
            readout.highestHitStreak = hhs;
            readout.totalNotesMissed = tnm;
            readout.currentMissStreak = cms;
            readout.currentPerfectHitStreak = cphs;
            readout.totalPerfectHits = tph;
            readout.currentLateHitStreak = clhs;
            readout.totalLateHits = tlh;
            readout.perfectPhrases = pp;
            readout.goodPhrases = gp;
            readout.passedPhrases = passedp;
            readout.failedPhrases = fp;

            return true;
        }
    }
}
