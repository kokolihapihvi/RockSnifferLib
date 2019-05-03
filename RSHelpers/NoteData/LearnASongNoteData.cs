using System;
using System.Runtime.InteropServices;

namespace RockSnifferLib.RSHelpers.NoteData
{
    [Serializable, StructLayout(LayoutKind.Explicit)]
    public struct LearnASongNoteData : INoteData
    {
        //Basic note data for learn a song:
        //
        //Offsets
        //0030 - total notes hit
        //0034 - current note streak
        //003C - highest note streak
        //0040 - total notes missed
        //0044 - missed note streak

        [FieldOffset(0x30)]
        private readonly int totalNotesHit;

        [FieldOffset(0x34)]
        private readonly int currentHitStreak;

        [FieldOffset(0x3C)]
        private readonly int highestHitStreak;

        [FieldOffset(0x40)]
        private readonly int totalNotesMissed;

        [FieldOffset(0x44)]
        private readonly int currentMissStreak;

        public float Accuracy {
            get {
                if (TotalNotes > 0 && TotalNotesHit > 0)
                {
                    return ((float)TotalNotesHit / (float)TotalNotes) * 100f;
                }

                return 100f;
            }
        }

        public int TotalNotes => TotalNotesMissed + TotalNotesHit;

        public int TotalNotesHit => totalNotesHit;
        public int CurrentHitStreak => currentHitStreak;
        public int HighestHitStreak => highestHitStreak;
        public int TotalNotesMissed => totalNotesMissed;
        public int CurrentMissStreak => currentMissStreak;
    }
}
