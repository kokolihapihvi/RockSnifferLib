using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        }

        public string name;
        public string arrangementID;
        public string type;
        public bool isBonusArrangement;
        public List<SectionDetails> sections = new List<SectionDetails>();
    }
}
