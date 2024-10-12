using System;

namespace RockSnifferLib.Events
{
    public class OnPsarcInstalledArgs : EventArgs
    {
        public required string FilePath;
        public required bool ParseSuccess;
    }
}
