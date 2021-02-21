namespace Eliason.AudioVisualizer
{
    public class Work
    {
        public Work(long from, long to)
        {
            From = from;
            To = to;
        }

        public long From { get; set; }
        public long To { get; set; }
        public DirectBitmap Bitmap { get; set; }

        public bool IsSame(Work other)
        {
            return From == other.From && To == other.To;
        }

        public bool IsOverlapping(Work other)
        {
            return From < other.To && other.From < To;
        }

        public bool IsLaterThan(Work other)
        {
            return From > other.To;
        }

        public bool IsEarlierThan(Work other)
        {
            return To < other.From;
        }

        public bool IsStartingBefore(Work other)
        {
            return From < other.From;
        }

        public bool IsEndingAfter(Work other)
        {
            return To > other.To;
        }
    }
}