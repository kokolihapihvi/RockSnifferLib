using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RockSnifferLib.RSHelpers
{
    /// <summary>
    /// Classification of the user's current Rocksmith mode-context, derived from
    /// gameStage (see RSMemoryReader.DeriveModeFromGameStage) - reliable across all
    /// states including menus, song-select, song-review, transitions, and Nonstop
    /// Play. Integer values 0..3 are preserved for external consumers.
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
