using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;

namespace Eliason.AudioVisualizer
{
    public class Interval
    {
        private long _start;
        private long _end;

        public long Start
        {
            get { return this._start; }
            set { this._start = Math.Min(this.End - 1, value); }
        }

        public long End
        {
            get { return this._end; }
            set { this._end = Math.Max(this.Start + 1, value); }
        }

        public long Length
        {
            get { return this.End - this.Start; }
        }

        public Interval(long start, long end)
        {
            this._start = start;
            this._end = end;
        }

        public bool IsOverlapping(Interval other)
        {
            return this.Start < other.End && other.Start < this.End;
        }

        public long[] GetDistanceFromOverlap(long point)
        {
            var distanceToStart = this.Start - point;
            var distanceToEnd = point - this.End;

            return new[]
            {
                distanceToStart,
                distanceToEnd
            };
        }
    }
}
