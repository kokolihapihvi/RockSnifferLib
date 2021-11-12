using RockSnifferLib.RSHelpers;
using System;
using System.Collections.Generic;

namespace RockSnifferLib.Sniffing
{
    [Serializable]
    public class ArrangementDetails
    {
        public class SectionDetails
        {
            public string name;
            public float startTime;
            public float endTime;
        };
 
        public class PhraseIterationDetails
        {
            public string name;
            public int phraseId;
            public int maxDifficulty;
            public float startTime;
            public float endTime;
        };

        public string name;
        public string arrangementID;
        public string type;
        public bool isBonusArrangement;
        public bool isAlternateArrangement;
        public ArrangementTuning tuning;
        public List<SectionDetails> sections = new List<SectionDetails>();
        public List<PhraseIterationDetails> phraseIterations = new List<PhraseIterationDetails>();
        public ArrangementData data;
    }
}
