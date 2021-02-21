using System;

namespace Eliason.AudioVisualizer
{
    public class Interval
    {
        private long _end;
        private long _start;

        public Interval(long start, long end)
        {
            _start = start;
            _end = end;
        }

        public long Start
        {
            get => _start;
            set => _start = Math.Min(End - 1, value);
        }

        public long End
        {
            get => _end;
            set => _end = Math.Max(Start + 1, value);
        }

        public long Length => End - Start;

        public bool IsOverlapping(Interval other)
        {
            return Start < other.End && other.Start < End;
        }

        public long[] GetDistanceFromOverlap(long point)
        {
            var distanceToStart = Start - point;
            var distanceToEnd = point - End;

            return new[]
            {
                distanceToStart,
                distanceToEnd
            };
        }
    }
}