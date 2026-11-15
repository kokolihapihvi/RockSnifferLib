using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RockSnifferLib.RSHelpers
{
    /// <summary>
    /// Game mode, derived from gameStage.
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
