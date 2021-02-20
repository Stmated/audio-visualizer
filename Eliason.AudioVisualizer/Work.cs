using System;
using System.Drawing;

namespace Eliason.AudioVisualizer
{
    public class Work
    {
        public long From { get; set; }
        public long To { get; set; }
        public DirectBitmap Bitmap { get; set; }

        public Work(long @from, long to)
        {
            this.From = @from;
            this.To = to;
        }

        public bool IsSame(Work other)
        {
            return this.From == other.From && this.To == other.To;
        }

        public bool IsOverlapping(Work other)
        {
            return this.From < other.To && other.From < this.To;
        }

        public bool IsLaterThan(Work other)
        {
            return this.From > other.To;
        }

        public bool IsEarlierThan(Work other)
        {
            return this.To < other.From;
        }

        public bool IsStartingBefore(Work other)
        {
            return this.From < other.From;
        }

        public bool IsEndingAfter(Work other)
        {
            return this.To > other.To;
        }
    }
}