using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Eliason.AudioVisualizer
{
    public class NoteMovedEventArgs : EventArgs
    {
        public Note Note { get; set; }
        public HitTestArea Area { get; set; }
        public long ByteIndex { get; set; }

        public NoteMovedEventArgs()
        {
        }
    }
}
