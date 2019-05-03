using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RockSnifferLib.RSHelpers.NoteData
{
    public interface INoteData
    {
        int TotalNotesHit { get; }
        int CurrentHitStreak { get; }
        int HighestHitStreak { get; }
        int TotalNotesMissed { get; }
        int CurrentMissStreak { get; }
        float Accuracy { get; }
        int TotalNotes { get; }
    }
}
