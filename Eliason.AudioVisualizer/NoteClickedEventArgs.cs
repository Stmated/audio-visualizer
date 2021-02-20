using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Eliason.AudioVisualizer
{
    public class NoteClickedEventArgs : EventArgs
    {
        public Note Note { get; set; }
        public HitTestArea Area { get; set; }
    }
}
