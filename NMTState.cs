/// libCanopenSimple
/// Robin Cornelius <robin.cornelius@gmail.com>

using System;

namespace libCanopenSimple
{
    public class NMTState
    {
        public enum e_NMTState
        {
            BOOT = 0,
            STOPPED = 4,
            OPERATIONAL = 5,
            PRE_OPERATIONAL = 127,
            INVALID = 0xff,

        }

        public e_NMTState state;
        public e_NMTState laststate;
        public DateTime lastping;
        public bool compulsory;

        public Action<e_NMTState> NMT_boot = null;
        public Action<int> NMT_guard = null;

        public NMTState()
        {
            state = e_NMTState.INVALID;
            laststate = e_NMTState.INVALID;
        }

        public void changestate(e_NMTState newstate)
        {
            laststate = state;
            state = newstate;
            lastping = DateTime.Now;

            if (newstate == e_NMTState.BOOT)
            {
                if (state != laststate && NMT_boot != null)
                {
                    NMT_boot(state);
                }
            }
        }
    }
}
