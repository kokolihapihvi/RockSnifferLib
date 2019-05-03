using System;
using System.Runtime.InteropServices;

namespace RockSnifferLib.RSHelpers.NoteData
{
    [Serializable, StructLayout(LayoutKind.Explicit)]
    public struct ScoreAttackNoteData : INoteData
    {
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

        [FieldOffset(0x3C)]
        private readonly int currentHitStreak;

        [FieldOffset(0x40)]
        private readonly int currentMissStreak;

        [FieldOffset(0x44)]
        private readonly int highestHitStreak;

        [FieldOffset(0x48)]
        private readonly int highestMissStreak;

        [FieldOffset(0x4C)]
        private readonly int totalNotesHit;

        [FieldOffset(0x50)]
        private readonly int totalNotesMissed;

        [FieldOffset(0x74)]
        private readonly int currentPerfectHitStreak;

        [FieldOffset(0x78)]
        private readonly int totalPerfectHits;

        [FieldOffset(0x7C)]
        private readonly int currentLateHitStreak;

        [FieldOffset(0x80)]
        private readonly int totalLateHits;

        [FieldOffset(0x84)]
        private readonly int perfectPhrases;

        [FieldOffset(0x88)]
        private readonly int goodPhrases;

        [FieldOffset(0x8C)]
        private readonly int passedPhrases;

        [FieldOffset(0x90)]
        private readonly int failedPhrases;

        [FieldOffset(0x94)]
        private readonly int currentPerfectPhraseStreak;

        [FieldOffset(0x98)]
        private readonly int currentGoodPhraseStreak;

        [FieldOffset(0x9C)]
        private readonly int currentPassedPhraseStreak;

        [FieldOffset(0xA0)]
        private readonly int currentFailedPhraseStreak;

        [FieldOffset(0xA4)]
        private readonly int highestPerfectPhraseStreak;

        [FieldOffset(0xA8)]
        private readonly int highestGoodPhraseStreak;

        [FieldOffset(0xAC)]
        private readonly int highestPassedPhraseStreak;

        [FieldOffset(0xB0)]
        private readonly int highestFailedPhraseStreak;

        [FieldOffset(0xE4)]
        private readonly int currentScore;

        [FieldOffset(0xE8)]
        private readonly int currentMultiplier;

        [FieldOffset(0xEC)]
        private readonly int highestMultiplier;

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

        public int HighestMissStreak => highestMissStreak;
        public int CurrentPerfectHitStreak => currentPerfectHitStreak;
        public int TotalPerfectHits => totalPerfectHits;
        public int CurrentLateHitStreak => currentLateHitStreak;
        public int TotalLateHits => totalLateHits;
        public int PerfectPhrases => perfectPhrases;
        public int GoodPhrases => goodPhrases;
        public int PassedPhrases => passedPhrases;
        public int FailedPhrases => failedPhrases;
        public int CurrentPerfectPhraseStreak => currentPerfectPhraseStreak;
        public int CurrentGoodPhraseStreak => currentGoodPhraseStreak;
        public int CurrentPassedPhraseStreak => currentPassedPhraseStreak;
        public int CurrentFailedPhraseStreak => currentFailedPhraseStreak;
        public int HighestPerfectPhraseStreak => highestPerfectPhraseStreak;
        public int HighestGoodPhraseStreak => highestGoodPhraseStreak;
        public int HighestPassedPhraseStreak => highestPassedPhraseStreak;
        public int HighestFailedPhraseStreak => highestFailedPhraseStreak;
        public int CurrentScore => currentScore;
        public int CurrentMultiplier => currentMultiplier;
        public int HighestMultiplier => highestMultiplier;
    }
}
