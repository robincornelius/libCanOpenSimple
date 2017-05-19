/*
    This file is part of libCanopenSimple.
    libCanopenSimple is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.
    libCanopenSimple is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.
    You should have received a copy of the GNU General Public License
    along with libCanopenSimple.  If not, see <http://www.gnu.org/licenses/>.
 
    Copyright(c) 2017 Robin Cornelius <robin.cornelius@gmail.com>
*/

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
