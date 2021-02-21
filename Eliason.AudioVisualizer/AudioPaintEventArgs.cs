using System;
using System.Collections.Generic;

namespace Eliason.AudioVisualizer
{
    public class AudioPaintEventArgs : EventArgs
    {
        public AudioPaintEventArgs(Interval viewport)
        {
            Notes = new List<Note>();
            ViewPort = viewport;
        }

        public Interval ViewPort { get; }

        public List<Note> Notes { get; }
    }
}