using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Eliason.AudioVisualizer
{
    public class RenderRequest
    {
        public Graphics Graphics { get; set; }

        public bool IsHitTest { get; set; }
        public Point CursorPoint { get; set; }
        public Cursor CursorResult { get; set; }
        public object FocusedObject { get; set; }
        public HitTestArea HitTestArea { get; set; }
        public List<Note> Notes { get; set; }

        public bool IsRendering => !IsHitTest;
    }
}