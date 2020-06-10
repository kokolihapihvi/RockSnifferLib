using RocksmithToolkitLib.XML;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RockSnifferLib.Sniffing
{
    [Serializable]
    public class ArrangementTuning
    {
        private static readonly Dictionary<string, ArrangementTuning> _TuningNames = new Dictionary<string, ArrangementTuning>()
        {

            ["Eb Standard"] = new ArrangementTuning(11, 11, 11, 11, 11, 11, -999, -999),
            ["D Standard"] = new ArrangementTuning(10, 10, 10, 10, 10, 10, -999, -999),
            ["C# Standard"] = new ArrangementTuning(9, 9, 9, 9, 9, 9, -999, -999),
            ["C Standard"] = new ArrangementTuning(8, 8, 8, 8, 8, 8, -999, -999),
            ["B Standard"] = new ArrangementTuning(7, 7, 7, 7, 7, 7, -999, -999),
            ["Bb Standard"] = new ArrangementTuning(6, 6, 6, 6, 6, 6, -999, -999),
            ["A Standard"] = new ArrangementTuning(5, 5, 5, 5, 5, 5, -999, -999),
            ["Ab Standard"] = new ArrangementTuning(4, 4, 4, 4, 4, 4, -999, -999),
            ["G Standard"] = new ArrangementTuning(3, 3, 3, 3, 3, 3, -999, -999),
            ["F# Standard"] = new ArrangementTuning(2, 2, 2, 2, 2, 2, -999, -999),
            ["F Standard"] = new ArrangementTuning(1, 1, 1, 1, 1, 1, -999, -999),
            ["E Standard"] = new ArrangementTuning(0, 0, 0, 0, 0, 0, -999, -999),
            ["Eb Standard"] = new ArrangementTuning(-1, -1, -1, -1, -1, -1, -999, -999),
            ["D Standard"] = new ArrangementTuning(-2, -2, -2, -2, -2, -2, -999, -999),
            ["C# Standard"] = new ArrangementTuning(-3, -3, -3, -3, -3, -3, -999, -999),
            ["C Standard"] = new ArrangementTuning(-4, -4, -4, -4, -4, -4, -999, -999),
            ["B Standard"] = new ArrangementTuning(-5, -5, -5, -5, -5, -5, -999, -999),
            ["Bb Standard"] = new ArrangementTuning(-6, -6, -6, -6, -6, -6, -999, -999),
            ["A Standard"] = new ArrangementTuning(-7, -7, -7, -7, -7, -7, -999, -999),
            ["Ab Standard"] = new ArrangementTuning(-8, -8, -8, -8, -8, -8, -999, -999),
            ["G Standard"] = new ArrangementTuning(-9, -9, -9, -9, -9, -9, -999, -999),
            ["F# Standard"] = new ArrangementTuning(-10, -10, -10, -10, -10, -10, -999, -999),
            ["F Standard"] = new ArrangementTuning(-11, -11, -11, -11, -11, -11, -999, -999),

            ["Eb Standard"] = new ArrangementTuning(11, 11, 11, 11, 0, 0, -999, -999),
            ["D Standard"] = new ArrangementTuning(10, 10, 10, 10, 0, 0, -999, -999),
            ["C# Standard"] = new ArrangementTuning(9, 9, 9, 9, 0, 0, -999, -999),
            ["C Standard"] = new ArrangementTuning(8, 8, 8, 8, 0, 0, -999, -999),
            ["B Standard"] = new ArrangementTuning(7, 7, 7, 7, 0, 0, -999, -999),
            ["Bb Standard"] = new ArrangementTuning(6, 6, 6, 6, 0, 0, -999, -999),
            ["A Standard"] = new ArrangementTuning(5, 5, 5, 5, 0, 0, -999, -999),
            ["Ab Standard"] = new ArrangementTuning(4, 4, 4, 4, 0, 0, -999, -999),
            ["G Standard"] = new ArrangementTuning(3, 3, 3, 3, 0, 0, -999, -999),
            ["F# Standard"] = new ArrangementTuning(2, 2, 2, 2, 0, 0, -999, -999),
            ["F Standard"] = new ArrangementTuning(1, 1, 1, 1, 0, 0, -999, -999),
            ["Eb Standard"] = new ArrangementTuning(-1, -1, -1, -1, 0, 0, -999, -999),
            ["D Standard"] = new ArrangementTuning(-2, -2, -2, -2, 0, 0, -999, -999),
            ["C# Standard"] = new ArrangementTuning(-3, -3, -3, -3, 0, 0, -999, -999),
            ["C Standard"] = new ArrangementTuning(-4, -4, -4, -4, 0, 0, -999, -999),
            ["B Standard"] = new ArrangementTuning(-5, -5, -5, -5, 0, 0, -999, -999),
            ["Bb Standard"] = new ArrangementTuning(-6, -6, -6, -6, 0, 0, -999, -999),
            ["A Standard"] = new ArrangementTuning(-7, -7, -7, -7, 0, 0, -999, -999),
            ["Ab Standard"] = new ArrangementTuning(-8, -8, -8, -8, 0, 0, -999, -999),
            ["G Standard"] = new ArrangementTuning(-9, -9, -9, -9, 0, 0, -999, -999),
            ["F# Standard"] = new ArrangementTuning(-10, -10, -10, -10, 0, 0, -999, -999),
            ["F Standard"] = new ArrangementTuning(-11, -11, -11, -11, 0, 0, -999, -999),

            ["F Drop Eb"] = new ArrangementTuning(11, 13, 13, 13, 13, 13, -999, -999),
            ["Drop D"] = new ArrangementTuning(10, 12, 12, 12, 12, 12, -999, -999),
            ["Eb Drop Db"] = new ArrangementTuning(9, 11, 11, 11, 11, 11, -999, -999),
            ["D Drop C"] = new ArrangementTuning(8, 10, 10, 10, 10, 10, -999, -999),
            ["C# Drop B"] = new ArrangementTuning(7, 9, 9, 9, 9, 9, -999, -999),
            ["C Drop Bb"] = new ArrangementTuning(6, 8, 8, 8, 8, 8, -999, -999),
            ["B Drop A"] = new ArrangementTuning(5, 7, 7, 7, 7, 7, -999, -999),
            ["Bb Drop Ab"] = new ArrangementTuning(4, 6, 6, 6, 6, 6, -999, -999),
            ["A Drop G"] = new ArrangementTuning(3, 5, 5, 5, 5, 5, -999, -999),
            ["Ab Drop F#"] = new ArrangementTuning(2, 4, 4, 4, 4, 4, -999, -999),
            ["G Drop F"] = new ArrangementTuning(1, 3, 3, 3, 3, 3, -999, -999),
            ["F# Drop E"] = new ArrangementTuning(0, 2, 2, 2, 2, 2, -999, -999),
            ["F Drop Eb"] = new ArrangementTuning(-1, 1, 1, 1, 1, 1, -999, -999),
            ["Drop D"] = new ArrangementTuning(-2, 0, 0, 0, 0, 0, -999, -999),
            ["Eb Drop Db"] = new ArrangementTuning(-3, -1, -1, -1, -1, -1, -999, -999),
            ["D Drop C"] = new ArrangementTuning(-4, -2, -2, -2, -2, -2, -999, -999),
            ["C# Drop B"] = new ArrangementTuning(-5, -3, -3, -3, -3, -3, -999, -999),
            ["C Drop Bb"] = new ArrangementTuning(-6, -4, -4, -4, -4, -4, -999, -999),
            ["B Drop A"] = new ArrangementTuning(-7, -5, -5, -5, -5, -5, -999, -999),
            ["Bb Drop Ab"] = new ArrangementTuning(-8, -6, -6, -6, -6, -6, -999, -999),
            ["A Drop G"] = new ArrangementTuning(-9, -7, -7, -7, -7, -7, -999, -999),
            ["Ab Drop F#"] = new ArrangementTuning(-10, -8, -8, -8, -8, -8, -999, -999),
            ["G Drop F"] = new ArrangementTuning(-11, -9, -9, -9, -9, -9, -999, -999),
            ["F# Drop E"] = new ArrangementTuning(-12, -10, -10, -10, -10, -10, -999, -999),
            ["F Drop Eb"] = new ArrangementTuning(-13, -11, -11, -11, -11, -11, -999, -999),

            ["F Drop Eb"] = new ArrangementTuning(11, 13, 13, 13, 0, 0, -999, -999),
            ["Drop D"] = new ArrangementTuning(10, 12, 12, 12, 0, 0, -999, -999),
            ["Eb Drop Db"] = new ArrangementTuning(9, 11, 11, 11, 0, 0, -999, -999),
            ["D Drop C"] = new ArrangementTuning(8, 10, 10, 10, 0, 0, -999, -999),
            ["C# Drop B"] = new ArrangementTuning(7, 9, 9, 9, 0, 0, -999, -999),
            ["C Drop Bb"] = new ArrangementTuning(6, 8, 8, 8, 0, 0, -999, -999),
            ["B Drop A"] = new ArrangementTuning(5, 7, 7, 7, 0, 0, -999, -999),
            ["Bb Drop Ab"] = new ArrangementTuning(4, 6, 6, 6, 0, 0, -999, -999),
            ["A Drop G"] = new ArrangementTuning(3, 5, 5, 5, 0, 0, -999, -999),
            ["Ab Drop F#"] = new ArrangementTuning(2, 4, 4, 4, 0, 0, -999, -999),
            ["G Drop F"] = new ArrangementTuning(1, 3, 3, 3, 0, 0, -999, -999),
            ["F# Drop E"] = new ArrangementTuning(0, 2, 2, 2, 0, 0, -999, -999),
            ["F Drop Eb"] = new ArrangementTuning(-1, 1, 1, 1, 0, 0, -999, -999),
            ["Drop D"] = new ArrangementTuning(-2, 0, 0, 0, 0, 0, -999, -999),
            ["Eb Drop Db"] = new ArrangementTuning(-3, -1, -1, -1, 0, 0, -999, -999),
            ["D Drop C"] = new ArrangementTuning(-4, -2, -2, -2, 0, 0, -999, -999),
            ["C# Drop B"] = new ArrangementTuning(-5, -3, -3, -3, 0, 0, -999, -999),
            ["C Drop Bb"] = new ArrangementTuning(-6, -4, -4, -4, 0, 0, -999, -999),
            ["B Drop A"] = new ArrangementTuning(-7, -5, -5, -5, 0, 0, -999, -999),
            ["Bb Drop Ab"] = new ArrangementTuning(-8, -6, -6, -6, 0, 0, -999, -999),
            ["A Drop G"] = new ArrangementTuning(-9, -7, -7, -7, 0, 0, -999, -999),
            ["Ab Drop F#"] = new ArrangementTuning(-10, -8, -8, -8, 0, 0, -999, -999),
            ["G Drop F"] = new ArrangementTuning(-11, -9, -9, -9, 0, 0, -999, -999),
            ["F# Drop E"] = new ArrangementTuning(-12, -10, -10, -10, 0, 0, -999, -999),
            ["F Drop Eb"] = new ArrangementTuning(-13, -11, -11, -11, 0, 0, -999, -999),

            ["Open A"] = new ArrangementTuning(0, 0, 2, 2, 2, 0, -999, -999),
            ["Open B"] = new ArrangementTuning(-5, -3, -3, -1, 0, -1, -999, -999),
            ["Open C"] = new ArrangementTuning(-4, -2, -2, 0, 1, 0, -999, -999),
            ["Open D"] = new ArrangementTuning(-2, 0, 0, -1, -2, -2, -999, -999),
            ["Open E"] = new ArrangementTuning(0, 2, 2, 1, 0, 0, -999, -999),
            ["Open F"] = new ArrangementTuning(-4, -4, -2, -2, -2, 1, -999, -999),
            ["Open G"] = new ArrangementTuning(-2, -2, 0, 0, 0, -2, -999, -999),

            ["DADGAD"] = new ArrangementTuning(-2, 0, 0, 0, -2, -2, -999, -999),
            ["Minor Third"] = new ArrangementTuning(-4, -6, -8, -10, -11, -13, -999, -999),
            ["Major Third"] = new ArrangementTuning(-8, -9, -10, -11, -11, -12, -999, -999),
            ["Fourths"] = new ArrangementTuning(0, 0, 0, 0, 1, 1, -999, -999),
            ["Aug Fourths"] = new ArrangementTuning(-4, -3, -2, -1, 1, 2, -999, -999),
            ["Fifths"] = new ArrangementTuning(-4, -2, 0, 2, 5, 7, -999, -999),
            ["Double Drop D"] = new ArrangementTuning(-2, 0, 0, 0, 0, -2, -999, -999),
            ["Nick Drake"] = new ArrangementTuning(-4, -2, -2, -2, 1, 0, -999, -999),
            ["C6 Modal"] = new ArrangementTuning(-4, 0, -2, 0, 1, 0, -999, -999),
            ["Nashville"] = new ArrangementTuning(12, 12, 12, 12, 0, 0, -999, -999)
        };

        public string TuningName
        {
            get
            {
                string name = "Custom Tuning";

                //Find the tuning name in the dictionary
                KeyValuePair<string, ArrangementTuning> tuningNamePair = _TuningNames.FirstOrDefault(kvp => kvp.Value.Equals(this));
                if (!tuningNamePair.Equals(default(KeyValuePair<string, ArrangementTuning>))) name = tuningNamePair.Key;

                //Calculate estimated hz offset from cents offset, if nonzero
                if (CentsOffset != 0)
                {
                    name = $"{name}: A{Math.Floor(440d * Math.Pow(2d, CentsOffset / 1200d))}";
                }

                //Add capo fret
                if (CapoFret != 0)
                {
                    name = $"{name} (Capo Fret {CapoFret})";
                }

                return name;
            }
        }

        /// <summary>
        /// Invalid tuning
        /// </summary>
        public ArrangementTuning()
        {
            String0 = -999;
            String1 = -999;
            String2 = -999;
            String3 = -999;
            String4 = -999;
            String5 = -999;
            CentsOffset = -999;
            CapoFret = -999;
        }

        public ArrangementTuning(int string0, int string1, int string2, int string3, int string4, int string5, int centsOffset, int capoFret)
        {
            String0 = string0;
            String1 = string1;
            String2 = string2;
            String3 = string3;
            String4 = string4;
            String5 = string5;
            CentsOffset = centsOffset;
            CapoFret = capoFret;
        }

        public ArrangementTuning(TuningStrings tuning, int centsOffset, int capoFret)
        {
            String0 = tuning.String0;
            String1 = tuning.String1;
            String2 = tuning.String2;
            String3 = tuning.String3;
            String4 = tuning.String4;
            String5 = tuning.String5;
            CentsOffset = centsOffset;
            CapoFret = capoFret;
        }

        public int CentsOffset;
        public int CapoFret;
        public int String0;
        public int String1;
        public int String2;
        public int String3;
        public int String4;
        public int String5;

        /// <summary>
        /// Check if two tunings are equal, capo fret and cents offset are ignored
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public override bool Equals(object obj)
        {
            if (obj is ArrangementTuning)
            {
                var t = obj as ArrangementTuning;
                return t.String0 == String0 &&
                    t.String1 == String1 &&
                    t.String2 == String2 &&
                    t.String3 == String3 &&
                    t.String4 == String4 &&
                    t.String5 == String5;
            }

            return base.Equals(obj);
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }
    }
}
