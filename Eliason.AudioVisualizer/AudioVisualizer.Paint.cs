using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Eliason.Common;
using Un4seen.Bass;

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
                var renderRequest = new RenderRequest()
                {
                    Graphics = e.Graphics,
                    CursorPoint = this.PointToClient(Cursor.Position),
                    IsHitTest = false
                };

                this.Render(renderRequest);
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
            var viewPortStart = this.Normalize(this.GetCurrentViewPortStart());
            var viewPortEnd = this.Normalize(this.GetCurrentViewPortEnd());

            if (this.NoteRequest != null)
            {
                var viewPortInterval = new Interval(viewPortStart, viewPortEnd);

                var args = new AudioPaintEventArgs(viewPortInterval);
                this.NoteRequest(this, args);
                request.Notes = args.Notes;
            }

            if (request.IsRendering)
            {
                for (int i = 0; i < this._work.Count; i++)
                {
                    var work = this._work[i];
                    if (work.Bitmap == null)
                    {
                        continue;
                    }

                    if (work.To < viewPortStart)
                    {
                        // We've yet to get to a work that is visible.
                        continue;
                    }

                    if (work.From > viewPortEnd)
                    {
                        // We've past the last visible work, so we can abort now.
                        break;
                    }

                    var x = this.NormalizedByteIndexToClientX(work.From);
                    g.DrawImageUnscaled(work.Bitmap.Bitmap, (int)Math.Round(x), 0);
                }

                if (this.RepeatLength > 0)
                {
                    var repeatStartX = (float)this.ByteIndexToClientX(this._repeatCurrent);
                    if (float.IsNaN(repeatStartX) == false)
                    {
                        var endByteIndex = this._repeatCurrent + Bass.BASS_ChannelSeconds2Bytes(this._playChannel, this._repeatLength);
                        var repeatEndX = (float)this.ByteIndexToClientX(endByteIndex);
                        var repeatMiddleX = (float)this.ByteIndexToClientX(endByteIndex - Bass.BASS_ChannelSeconds2Bytes(this._playChannel, this._repeatBackwards));

                        const int repeatMarkerHeight = 20;
                        var outlinePen = Pens.Black;
                        var y1 = this.ClientRectangle.Height - repeatMarkerHeight;
                        var y2 = this.ClientRectangle.Height;

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

                        if (this._repeatPauseRemaining > 0)
                        {
                            var timeString = "" + Math.Round(this._repeatPauseRemaining / 1000d, 1);
                            var textSize = g.MeasureString(timeString, SystemFonts.StatusFont);
                            g.DrawString(timeString, SystemFonts.StatusFont, Brushes.Red, (this.Width - textSize.Width) - 20, 5);
                        }
                    }
                }
            }

            float paddingBottom = this.ClientRectangle.Bottom;
            if (this.IsPlayable)
            {
                var millisecondsStart = (long)Math.Round(Bass.BASS_ChannelBytes2Seconds(this._playChannel, viewPortStart) * 1000d);
                var millisecondsEnd = (long)Math.Round(Bass.BASS_ChannelBytes2Seconds(this._playChannel, viewPortEnd) * 1000d);

                var millisecondsDistance = millisecondsEnd - millisecondsStart;

                // Resolution is one large marker every 50 pixels.
                var preferredResolution = millisecondsDistance / (this.Width / 50d);
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

                for (int i = 0; i < multipliers.Length && actualResolution < preferredResolution; i++)
                {
                    actualResolution = startingResolution * multipliers[i];
                }

                var milliseconds = millisecondsStart - (millisecondsStart % actualResolution);
                while (milliseconds < millisecondsEnd)
                {
                    var offsetMilliseconds = (milliseconds - millisecondsStart);
                    var x = (int)Math.Floor(offsetMilliseconds * (this.Width / (double)millisecondsDistance));

                    var stringTime = "" + this.getTimeString((milliseconds / 1000d) / 60) + ":" + this.getTimeString((milliseconds / 1000d) % 60);

                    var timeLineTop = this.ClientRectangle.Bottom - 10;
                    paddingBottom = Math.Min(paddingBottom, timeLineTop);

                    if (request.IsRendering)
                    {
                        g.DrawLine(Pens.White, x, timeLineTop, x, this.ClientRectangle.Bottom);

                        using (var f = new Font(FontFamily.GenericSansSerif, 7f))
                        {
                            var textSize = g.MeasureString(stringTime, f);
                            g.DrawString(stringTime, f, Brushes.White, x + 1, this.ClientRectangle.Bottom - textSize.Height);
                        }
                    }

                    milliseconds += actualResolution;
                }
            }

            if (request.Notes != null)
            {
                if (this._mouseDraggingObject is Note)
                {
                    request.FocusedObject = this._mouseDraggingObject;
                    request.HitTestArea = this._mouseDraggingArea;
                }

                const int hoverPadding = 10;
                foreach (var note in request.Notes)
                {
                    var x1 = (float)this.ByteIndexToClientX(note.Interval.Start);
                    var x2 = (float)this.ByteIndexToClientX(note.Interval.End);
                    var top = paddingBottom - 45f;
                    var height = 40f;

                    var noteRectangle = new RectangleF(x1, top, x2 - x1, height);

                    if (request.FocusedObject == null)
                    {
                        var ht = request.CursorPoint;
                        if (ht.Y >= noteRectangle.Top && ht.Y <= noteRectangle.Bottom)
                        {
                            if (ht.X >= (noteRectangle.Left) && ht.X <= (noteRectangle.Left + hoverPadding))
                            {
                                request.HitTestArea = HitTestArea.NoteLeft;
                                request.CursorResult = Cursors.SizeWE;
                                request.FocusedObject = note;
                            }
                            else if (ht.X >= (noteRectangle.Right - hoverPadding) && ht.X <= (noteRectangle.Right))
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
                    {
                        if (noteRectangle.Width > 1)
                        {
                            using (var b = new SolidBrush(Color.FromArgb(150, Color.GhostWhite)))
                            {
                                g.FillRectangle(b, noteRectangle);
                            }

                            if (note.Equals(request.FocusedObject))
                            {
                                using (var hatch = new HatchBrush(HatchStyle.Percent20, Color.LightGray, Color.Transparent))
                                {
                                    g.FillRectangle(hatch, noteRectangle.Left, noteRectangle.Top, hoverPadding, noteRectangle.Height);
                                    g.FillRectangle(hatch, noteRectangle.Right - hoverPadding, noteRectangle.Top, hoverPadding, noteRectangle.Height);
                                }
                            }

                            g.DrawRectangle(note.Equals(request.FocusedObject) ? Pens.LightGray : Pens.Gray, noteRectangle.Left, noteRectangle.Top, noteRectangle.Width, noteRectangle.Height);

                            var stringFormat = new StringFormat();
                            stringFormat.FormatFlags = StringFormatFlags.NoWrap;

                            using (var f = new Font(FontFamily.GenericSansSerif, 8f))
                            {
                                var measure = g.MeasureString(note.Text, f, noteRectangle.Size, stringFormat);
                                var offsetX = (noteRectangle.Width / 2f) - (measure.Width / 2f);
                                var offsetY = (noteRectangle.Height / 2f) - (measure.Height / 2f);

                                noteRectangle.Offset(offsetX, offsetY);

                                g.DrawString(note.Text, f, Brushes.Black, noteRectangle, stringFormat);
                            }
                        }
                    }
                }
            }

            if (request.IsRendering == false)
            {
                return;
            }

            if (this._mouseDraggingZoom)
            {
                var xLow = Math.Min(this._mouseDragStartX, this._mouseDragEndX);
                var xHigh = Math.Max(this._mouseDragStartX, this._mouseDragEndX);

                using (var b = new SolidBrush(Color.FromArgb(50, Color.LightBlue)))
                {
                    g.FillRectangle(b, new Rectangle(xLow, 0, xHigh - xLow, this.ClientRectangle.Height));
                }

                g.DrawLine(Pens.Gray, xLow, 0, xLow, this.ClientRectangle.Height);
                g.DrawLine(Pens.Gray, xHigh, 0, xHigh, this.ClientRectangle.Height);
            }

            const int infoPadding = 5;
            var overviewRectangle = new RectangleF(
                infoPadding,
                infoPadding,
                this.ClientRectangle.Width * 0.10f,
                10);

            using (var dimBrush = new SolidBrush(Color.FromArgb(150, Color.Gray)))
            {
                var timeSinceAnimation = (DateTime.Now - this._timestampLastAttributeChange);
                if (timeSinceAnimation <= this._attributeChangeAnimationLength)
                {
                    var volumeWidth = Math.Max(20, this.Width * 0.20f);
                    var volumeHeight = Math.Min(20, Math.Max(5, this.Height * 0.10f));
                    var rectVolume = new RectangleF(
                        (this.Width * 0.5f) - (volumeWidth / 2f), (this.Height * 0.5f) - (volumeHeight / 2f),
                        volumeWidth, volumeHeight);
                    var volumeX = rectVolume.X + (rectVolume.Width * this.GetVolume());

                    var c = new HsvColor((int)(360 * this.GetVolume()), 75, 75).ToColor();

                    g.FillRectangle(Brushes.DarkGray, rectVolume);

                    using (var intensityBrush = new SolidBrush(c))
                    {
                        g.FillRectangle(intensityBrush, rectVolume.Left, rectVolume.Top, volumeX - rectVolume.Left, rectVolume.Height);
                    }

                    g.DrawRectangle(Pens.WhiteSmoke, rectVolume.X, rectVolume.Y, rectVolume.Width, rectVolume.Height);
                    g.DrawLine(Pens.WhiteSmoke, volumeX, rectVolume.Top, volumeX, rectVolume.Bottom);

                    var percentageString = Math.Round(this.GetVolume() * 100) + "%";
                    var size = g.MeasureString(percentageString, SystemFonts.StatusFont);
                    g.DrawString(percentageString, SystemFonts.StatusFont, Brushes.WhiteSmoke, rectVolume.Right + 4f, rectVolume.Top + (rectVolume.Height / 2) - (size.Height / 2f));
                }

                long bytePosition;
                if (this.IsPlayable)
                {
                    bytePosition = this.Normalize(Bass.BASS_ChannelGetPosition(this._playChannel));

                    var timePosition = Bass.BASS_ChannelBytes2Seconds(this._playChannel, bytePosition);
                    var stringTimeCurrent = "" + this.getTimeString(timePosition / 60) + ":" + this.getTimeString(timePosition % 60);
                    g.DrawString(stringTimeCurrent, SystemFonts.DefaultFont, Brushes.White, overviewRectangle.Right, overviewRectangle.Top);
                }
                else
                {
                    bytePosition = 0;
                }

                if (this._bytesTotal > 0)
                {
                    g.FillRectangle(dimBrush, overviewRectangle.X, overviewRectangle.Y, overviewRectangle.Width, overviewRectangle.Height);

                    var bytesPerOverviewPixel = this._bytesTotal / overviewRectangle.Width;
                    var overviewStartX = (overviewRectangle.Left + (viewPortStart / bytesPerOverviewPixel));

                    using (var overviewFocusBrush = new SolidBrush(Color.FromArgb(150, Color.LightBlue)))
                    {
                        var overviewWidth = (long)Math.Max(1, (overviewRectangle.Width * this.GetZoomRatio()));
                        g.FillRectangle(overviewFocusBrush, overviewStartX, overviewRectangle.Y, overviewWidth, overviewRectangle.Height);
                    }

                    if (bytePosition >= 0)
                    {
                        using (var caretPen = new Pen(Color.FromArgb(150, Color.Red)))
                        {
                            var globalCaretX = (float)this.ByteIndexToClientX(bytePosition);
                            var overviewCaretX = overviewRectangle.Left + (bytePosition / bytesPerOverviewPixel);

                            g.DrawLine(caretPen, globalCaretX, 0, globalCaretX, this.ClientRectangle.Height);
                            g.DrawLine(caretPen, overviewCaretX, overviewRectangle.Top, overviewCaretX, overviewRectangle.Bottom);
                        }
                    }
                }
            }
        }

        private String getTimeString(double number)
        {
            if (number < 1)
            {
                return "00";
            }

            if (number < 10)
            {
                return "0" + Math.Floor(number);
            }

            return "" + Math.Floor(number);
        }
    }
}
