using RocksmithToolkitLib.Sng2014HSL;
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

        public ArrangementData(Sng2014File sng)
        {
            Measures = new List<Measure>();

            if (sng.BPMs.Count > 0)
            {
                Measures.Add(new Measure(sng.BPMs.BPMs[0]));

                for (int i = 0; i < sng.BPMs.BPMs.Length; ++i)
                {
                    var bpm = sng.BPMs.BPMs[i];

                    if (bpm.Measure != Measures.Last().Number)
                    {
                        Measures.Add(new Measure(bpm));
                    }
                }
            }
        }
    }
}
