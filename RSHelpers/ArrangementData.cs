using Rocksmith2014PsarcLib.Psarc.Asset;
using Rocksmith2014PsarcLib.Psarc.Models.Sng;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RockSnifferLib.RSHelpers
{
    [Serializable]
    public class ArrangementData
    {
        public class Measure
        {
            public float Time { get; set; }
            public int Number { get; set; }

            public Measure() { }

            public Measure(Bpm bpm)
            {
                Time = bpm.Time;
                Number = bpm.Measure;
            }
        }
        public List<Measure> Measures { get; set; }

        public ArrangementData() { }

        public ArrangementData(SngAsset sng)
        {
            Measures = new List<Measure>();

            if (sng.BPMs.Length > 0)
            {
                Measures.Add(new Measure(sng.BPMs[0]));

                for (int i = 0; i < sng.BPMs.Length; ++i)
                {
                    var bpm = sng.BPMs[i];

                    if (bpm.Measure != Measures.Last().Number)
                    {
                        Measures.Add(new Measure(bpm));
                    }
                }
            }
        }
    }
}
