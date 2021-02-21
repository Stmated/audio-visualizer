using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Eliason.AudioVisualizer
{
    public class DirectBitmap : IDisposable
    {
        public DirectBitmap(int width, int height)
        {
            Width = width;
            Height = height;
            Bits = new byte[width * height * 4];
            BitsHandle = GCHandle.Alloc(Bits, GCHandleType.Pinned);
            Bitmap = new Bitmap(width, height, width * 4, PixelFormat.Format32bppPArgb,
                BitsHandle.AddrOfPinnedObject());
        }

        public Bitmap Bitmap { get; }
        public byte[] Bits { get; }
        public bool Disposed { get; set; }
        public int Height { get; }
        public int Width { get; }

        protected GCHandle BitsHandle { get; }

        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            Bitmap.Dispose();
            BitsHandle.Free();
        }

        public int GetStartOffset(int x, int y)
        {
            var scanSize = Width * 4;
            return x * 4 + scanSize * y;
        }
    }
}