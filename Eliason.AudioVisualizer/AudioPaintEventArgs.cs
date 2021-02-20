using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Eliason.AudioVisualizer
{
    public class AudioPaintEventArgs : EventArgs
    {
        public Interval ViewPort { get; private set; }

        public List<Note> Notes { get; private set; }

        public AudioPaintEventArgs(Interval viewport)
        {
            this.Notes = new List<Note>();
            this.ViewPort = viewport;
        }
    }
}
