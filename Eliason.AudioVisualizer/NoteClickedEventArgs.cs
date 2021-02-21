using System;

namespace Eliason.AudioVisualizer
{
    public class NoteClickedEventArgs : EventArgs
    {
        public Note Note { get; set; }
        public HitTestArea Area { get; set; }
    }
}