using RockSnifferLib.Sniffing;

namespace RockSnifferLib.Events
{
    public class OnStateChangedArgs
    {
        public SnifferState oldState;
        public SnifferState newState;
    }
}
