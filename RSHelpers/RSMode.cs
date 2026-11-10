using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RockSnifferLib.RSHelpers
{
    /// <summary>
    /// Game mode, derived from gameStage. Values 0-3 keep their original numbers.
    /// </summary>
    public enum RSMode
    {
        UNKNOWN,
        LEARNASONG,
        SCOREATTACK,
        MULTIPLAYER,
        NONSTOPPLAY,
        GUITARCADE,
        SESSION,
        LESSONS,
        MENU
    }
}
