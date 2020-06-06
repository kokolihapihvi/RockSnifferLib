using System;

namespace RockSnifferLib.Configuration
{
    [Serializable]
    public class SnifferSettings
    {
        public bool enableAutoEnumeration = true;
        public int parallelism = 0;
    }
}
