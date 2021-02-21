using System;

namespace Eliason.AudioVisualizer
{
    public class NoteMovedEventArgs : EventArgs
    {
        public Note Note { get; set; }
        public HitTestArea Area { get; set; }
        public long ByteIndex { get; set; }
    }
}