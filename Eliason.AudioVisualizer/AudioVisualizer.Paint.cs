using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Eliason.Common;
using ManagedBass;

namespace Eliason.AudioVisualizer
{
    public partial class AudioVisualizer
    {
        public event EventHandler<AudioPaintEventArgs> NoteRequest;
        public event EventHandler<NoteMovedEventArgs> NoteMoved;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.Clear(Color.Black);

            try
            {
                var renderRequest = new RenderRequest
                {
                    Graphics = e.Graphics,
                    CursorPoint = PointToClient(Cursor.Position),
                    IsHitTest = false
                };

                Render(renderRequest);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        public void Render(RenderRequest request)
        {
            var g = request.Graphics;

            // Draw the image, with stretching.
            // Might be very ugly, but the thread will improve its resolution step by step.
            var viewPortStart = Normalize(GetCurrentViewPortStart());
            var viewPortEnd = Normalize(GetCurrentViewPortEnd());

            if (NoteRequest != null)
            {
                var viewPortInterval = new Interval(viewPortStart, viewPortEnd);

                var args = new AudioPaintEventArgs(viewPortInterval);
                NoteRequest(this, args);
                request.Notes = args.Notes;
            }

            if (request.IsRendering)
            {
                for (var i = 0; i < _work.Count; i++)
                {
                    var work = _work[i];
                    if (work.Bitmap == null) continue;

                    if (work.To < viewPortStart)
                        // We've yet to get to a work that is visible.
                        continue;

                    if (work.From > viewPortEnd)
                        // We've past the last visible work, so we can abort now.
                        break;

                    var x = NormalizedByteIndexToClientX(work.From);
                    g.DrawImageUnscaled(work.Bitmap.Bitmap, (int) Math.Round(x), 0);
                }

                if (RepeatLength > 0)
                {
                    var repeatStartX = (float) ByteIndexToClientX(_repeatCurrent);
                    if (float.IsNaN(repeatStartX) == false)
                    {
                        var endByteIndex = _repeatCurrent + Bass.ChannelSeconds2Bytes(_playChannel, _repeatLength);
                        var repeatEndX = (float) ByteIndexToClientX(endByteIndex);
                        var repeatMiddleX =
                            (float) ByteIndexToClientX(endByteIndex -
                                                       Bass.ChannelSeconds2Bytes(_playChannel, _repeatBackwards));

                        const int repeatMarkerHeight = 20;
                        var outlinePen = Pens.Black;
                        var y1 = ClientRectangle.Height - repeatMarkerHeight;
                        var y2 = ClientRectangle.Height;

                        {
                            g.DrawLine(outlinePen, repeatStartX - 1, y1, repeatStartX - 1, y2);
                            g.DrawLine(outlinePen, repeatStartX - 1, y1 - 1, repeatStartX + 1, y1 - 1);
                            g.DrawLine(outlinePen, repeatStartX + 1, y1, repeatStartX + 1, y2);
                            g.DrawLine(Pens.LawnGreen, repeatStartX, y1, repeatStartX, y2);
                        }

                        {
                            g.DrawLine(outlinePen, repeatMiddleX - 1, y1, repeatMiddleX - 1, y2);
                            g.DrawLine(outlinePen, repeatMiddleX - 1, y1 - 1, repeatMiddleX + 1, y1 - 1);
                            g.DrawLine(outlinePen, repeatMiddleX + 1, y1, repeatMiddleX + 1, y2);
                            g.DrawLine(Pens.Orange, repeatMiddleX, y1, repeatMiddleX, y2);
                        }

                        {
                            g.DrawLine(outlinePen, repeatEndX - 1, y1, repeatEndX - 1, y2);
                            g.DrawLine(outlinePen, repeatEndX - 1, y1 - 1, repeatEndX + 1, y1 - 1);
                            g.DrawLine(outlinePen, repeatEndX + 1, y1, repeatEndX + 1, y2);
                            g.DrawLine(Pens.Red, repeatEndX, y1, repeatEndX, y2);
                        }

                        if (_repeatPauseRemaining > 0)
                        {
                            var timeString = "" + Math.Round(_repeatPauseRemaining / 1000d, 1);
                            var textSize = g.MeasureString(timeString, SystemFonts.StatusFont);
                            g.DrawString(timeString, SystemFonts.StatusFont, Brushes.Red, Width - textSize.Width - 20,
                                5);
                        }
                    }
                }
            }

            float paddingBottom = ClientRectangle.Bottom;
            if (IsPlayable)
            {
                var millisecondsStart =
                    (long) Math.Round(Bass.ChannelBytes2Seconds(_playChannel, viewPortStart) * 1000d);
                var millisecondsEnd = (long) Math.Round(Bass.ChannelBytes2Seconds(_playChannel, viewPortEnd) * 1000d);

                var millisecondsDistance = millisecondsEnd - millisecondsStart;

                // Resolution is one large marker every 50 pixels.
                var preferredResolution = millisecondsDistance / (Width / 50d);
                const long startingResolution = 100L;
                var actualResolution = startingResolution;

                var multipliers = new long[]
                {
                    10, // 1 second
                    20, // 2 seconds
                    50, // 5 seconds
                    100, // 10 seconds
                    150, // 15 seconds
                    300, // 30 seconds
                    600, // 1 minute
                    1200, // 2 minutes
                    3000, // 5 minutes
                    6000, // 10 minutes
                    9000, // 15 minutes
                    18000, // 30 minutes
                    36000 // 1 hour
                };

                for (var i = 0; i < multipliers.Length && actualResolution < preferredResolution; i++)
                    actualResolution = startingResolution * multipliers[i];

                var milliseconds = millisecondsStart - millisecondsStart % actualResolution;
                while (milliseconds < millisecondsEnd)
                {
                    var offsetMilliseconds = milliseconds - millisecondsStart;
                    var x = (int) Math.Floor(offsetMilliseconds * (Width / (double) millisecondsDistance));

                    var stringTime = "" + getTimeString(milliseconds / 1000d / 60) + ":" +
                                     getTimeString(milliseconds / 1000d % 60);

                    var timeLineTop = ClientRectangle.Bottom - 10;
                    paddingBottom = Math.Min(paddingBottom, timeLineTop);

                    if (request.IsRendering)
                    {
                        g.DrawLine(Pens.White, x, timeLineTop, x, ClientRectangle.Bottom);

                        using (var f = new Font(FontFamily.GenericSansSerif, 7f))
                        {
                            var textSize = g.MeasureString(stringTime, f);
                            g.DrawString(stringTime, f, Brushes.White, x + 1, ClientRectangle.Bottom - textSize.Height);
                        }
                    }

                    milliseconds += actualResolution;
                }
            }

            if (request.Notes != null)
            {
                if (_mouseDraggingObject is Note)
                {
                    request.FocusedObject = _mouseDraggingObject;
                    request.HitTestArea = _mouseDraggingArea;
                }

                const int hoverPadding = 10;
                foreach (var note in request.Notes)
                {
                    var x1 = (float) ByteIndexToClientX(note.Interval.Start);
                    var x2 = (float) ByteIndexToClientX(note.Interval.End);
                    var top = paddingBottom - 45f;
                    var height = 40f;

                    var noteRectangle = new RectangleF(x1, top, x2 - x1, height);

                    if (request.FocusedObject == null)
                    {
                        var ht = request.CursorPoint;
                        if (ht.Y >= noteRectangle.Top && ht.Y <= noteRectangle.Bottom)
                        {
                            if (ht.X >= noteRectangle.Left && ht.X <= noteRectangle.Left + hoverPadding)
                            {
                                request.HitTestArea = HitTestArea.NoteLeft;
                                request.CursorResult = Cursors.SizeWE;
                                request.FocusedObject = note;
                            }
                            else if (ht.X >= noteRectangle.Right - hoverPadding && ht.X <= noteRectangle.Right)
                            {
                                request.HitTestArea = HitTestArea.NoteRight;
                                request.CursorResult = Cursors.SizeWE;
                                request.FocusedObject = note;
                            }
                            else if (noteRectangle.Contains(ht))
                            {
                                request.HitTestArea = HitTestArea.NoteCenter;
                                request.CursorResult = Cursors.Hand;
                                request.FocusedObject = note;
                            }
                        }
                    }

                    if (request.IsHitTest == false)
                        if (noteRectangle.Width > 1)
                        {
                            using (var b = new SolidBrush(Color.FromArgb(150, Color.GhostWhite)))
                            {
                                g.FillRectangle(b, noteRectangle);
                            }

                            if (note.Equals(request.FocusedObject))
                                using (var hatch = new HatchBrush(HatchStyle.Percent20, Color.LightGray,
                                    Color.Transparent))
                                {
                                    g.FillRectangle(hatch, noteRectangle.Left, noteRectangle.Top, hoverPadding,
                                        noteRectangle.Height);
                                    g.FillRectangle(hatch, noteRectangle.Right - hoverPadding, noteRectangle.Top,
                                        hoverPadding, noteRectangle.Height);
                                }

                            g.DrawRectangle(note.Equals(request.FocusedObject) ? Pens.LightGray : Pens.Gray,
                                noteRectangle.Left, noteRectangle.Top, noteRectangle.Width, noteRectangle.Height);

                            var stringFormat = new StringFormat();
                            stringFormat.FormatFlags = StringFormatFlags.NoWrap;

                            using (var f = new Font(FontFamily.GenericSansSerif, 8f))
                            {
                                var measure = g.MeasureString(note.Text, f, noteRectangle.Size, stringFormat);
                                var offsetX = noteRectangle.Width / 2f - measure.Width / 2f;
                                var offsetY = noteRectangle.Height / 2f - measure.Height / 2f;

                                noteRectangle.Offset(offsetX, offsetY);

                                g.DrawString(note.Text, f, Brushes.Black, noteRectangle, stringFormat);
                            }
                        }
                }
            }

            if (request.IsRendering == false) return;

            if (_mouseDraggingZoom)
            {
                var xLow = Math.Min(_mouseDragStartX, _mouseDragEndX);
                var xHigh = Math.Max(_mouseDragStartX, _mouseDragEndX);

                using (var b = new SolidBrush(Color.FromArgb(50, Color.LightBlue)))
                {
                    g.FillRectangle(b, new Rectangle(xLow, 0, xHigh - xLow, ClientRectangle.Height));
                }

                g.DrawLine(Pens.Gray, xLow, 0, xLow, ClientRectangle.Height);
                g.DrawLine(Pens.Gray, xHigh, 0, xHigh, ClientRectangle.Height);
            }

            const int infoPadding = 5;
            var overviewRectangle = new RectangleF(
                infoPadding,
                infoPadding,
                ClientRectangle.Width * 0.10f,
                10);

            using (var dimBrush = new SolidBrush(Color.FromArgb(150, Color.Gray)))
            {
                var timeSinceAnimation = DateTime.Now - _timestampLastAttributeChange;
                if (timeSinceAnimation <= _attributeChangeAnimationLength)
                {
                    var volumeWidth = Math.Max(20, Width * 0.20f);
                    var volumeHeight = Math.Min(20, Math.Max(5, Height * 0.10f));
                    var rectVolume = new RectangleF(
                        Width * 0.5f - volumeWidth / 2f, Height * 0.5f - volumeHeight / 2f,
                        volumeWidth, volumeHeight);
                    var volumeX = rectVolume.X + rectVolume.Width * GetVolume();

                    var c = new HsvColor((int) (360 * GetVolume()), 75, 75).ToColor();

                    g.FillRectangle(Brushes.DarkGray, rectVolume);

                    using (var intensityBrush = new SolidBrush(c))
                    {
                        g.FillRectangle(intensityBrush, rectVolume.Left, rectVolume.Top, volumeX - rectVolume.Left,
                            rectVolume.Height);
                    }

                    g.DrawRectangle(Pens.WhiteSmoke, rectVolume.X, rectVolume.Y, rectVolume.Width, rectVolume.Height);
                    g.DrawLine(Pens.WhiteSmoke, volumeX, rectVolume.Top, volumeX, rectVolume.Bottom);

                    var percentageString = Math.Round(GetVolume() * 100) + "%";
                    var size = g.MeasureString(percentageString, SystemFonts.StatusFont);
                    g.DrawString(percentageString, SystemFonts.StatusFont, Brushes.WhiteSmoke, rectVolume.Right + 4f,
                        rectVolume.Top + rectVolume.Height / 2 - size.Height / 2f);
                }

                long bytePosition;
                if (IsPlayable)
                {
                    bytePosition = Normalize(Bass.ChannelGetPosition(_playChannel));

                    var timePosition = Bass.ChannelBytes2Seconds(_playChannel, bytePosition);
                    var stringTimeCurrent =
                        "" + getTimeString(timePosition / 60) + ":" + getTimeString(timePosition % 60);
                    g.DrawString(stringTimeCurrent, SystemFonts.DefaultFont, Brushes.White, overviewRectangle.Right,
                        overviewRectangle.Top);
                }
                else
                {
                    bytePosition = 0;
                }

                if (_bytesTotal > 0)
                {
                    g.FillRectangle(dimBrush, overviewRectangle.X, overviewRectangle.Y, overviewRectangle.Width,
                        overviewRectangle.Height);

                    var bytesPerOverviewPixel = _bytesTotal / overviewRectangle.Width;
                    var overviewStartX = overviewRectangle.Left + viewPortStart / bytesPerOverviewPixel;

                    using (var overviewFocusBrush = new SolidBrush(Color.FromArgb(150, Color.LightBlue)))
                    {
                        var overviewWidth = (long) Math.Max(1, overviewRectangle.Width * GetZoomRatio());
                        g.FillRectangle(overviewFocusBrush, overviewStartX, overviewRectangle.Y, overviewWidth,
                            overviewRectangle.Height);
                    }

                    if (bytePosition >= 0)
                        using (var caretPen = new Pen(Color.FromArgb(150, Color.Red)))
                        {
                            var globalCaretX = (float) ByteIndexToClientX(bytePosition);
                            var overviewCaretX = overviewRectangle.Left + bytePosition / bytesPerOverviewPixel;

                            g.DrawLine(caretPen, globalCaretX, 0, globalCaretX, ClientRectangle.Height);
                            g.DrawLine(caretPen, overviewCaretX, overviewRectangle.Top, overviewCaretX,
                                overviewRectangle.Bottom);
                        }
                }
            }
        }

        private string getTimeString(double number)
        {
            if (number < 1) return "00";

            if (number < 10) return "0" + Math.Floor(number);

            return "" + Math.Floor(number);
        }
    }
}