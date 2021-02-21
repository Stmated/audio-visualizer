using System;
using System.Windows.Forms;
using ManagedBass;

namespace Eliason.AudioVisualizer
{
    public partial class AudioVisualizer
    {
        private bool _mouseDown;
        private int _mouseDragEndX;
        private HitTestArea _mouseDraggingArea;

        private object _mouseDraggingObject;
        private bool _mouseDraggingPan;

        private bool _mouseDraggingZoom;
        private int _mouseDragStartX;

        private bool IsCaretInsideViewPort
        {
            get
            {
                var currentPosition = GetCurrentBytePosition();
                if (currentPosition < GetCurrentViewPortStart()) return false;

                if (currentPosition > GetCurrentViewPortEnd()) return false;

                return true;
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            switch (e.Button)
            {
                case MouseButtons.Right:

                    // Set the caret position, so that we'll start following with the viewport
                    _viewPortStart = GetCurrentViewPortStart();

                    // Start playing at the location that was clicked.
                    var clickedByteIndex = ClientXToByteIndex(e.Location.X);

                    switch (Bass.ChannelIsActive(_playChannel))
                    {
                        case PlaybackState.Playing:
                            StartPlaying(clickedByteIndex);
                            break;
                        default:
                            SetLocation(clickedByteIndex);
                            break;
                    }

                    if (double.IsNaN(_caretOffset) == false) _caretOffset = e.X / (double) ClientRectangle.Width;
                    break;
                case MouseButtons.Middle:
                    _mouseDown = true;
                    _mouseDragStartX = e.Location.X;
                    break;
                case MouseButtons.Left:

                    var request = new RenderRequest
                    {
                        IsHitTest = true,
                        CursorPoint = e.Location
                    };

                    Render(request);

                    _mouseDown = true;
                    _mouseDragStartX = e.Location.X;
                    _mouseDraggingArea = HitTestArea.None;

                    var note = request.FocusedObject as Note;
                    if (note != null)
                    {
                        _mouseDraggingArea = request.HitTestArea;
                        _mouseDraggingObject = request.FocusedObject;
                        _viewPortStart = GetCurrentViewPortStart();

                        switch (Bass.ChannelIsActive(_playChannel))
                        {
                            case PlaybackState.Playing:
                                _caretOffset = double.NaN;
                                break;
                        }

                        switch (request.HitTestArea)
                        {
                            case HitTestArea.NoteLeft:
                                _mouseDragByteIndexStart = note.Interval.Start;
                                break;
                            case HitTestArea.NoteRight:
                                _mouseDragByteIndexStart = note.Interval.End;
                                break;
                            case HitTestArea.NoteCenter:
                                _mouseDragByteIndexStart = note.Interval.Start;
                                break;
                        }

                        Cursor = request.CursorResult ?? Cursors.Default;
                    }
                    else
                    {
                        _mouseDragByteIndexStart = GetCurrentViewPortStart();
                        _viewPortStart = _mouseDragByteIndexStart;
                        _caretOffset = double.NaN;
                    }

                    break;
            }
        }

        public event EventHandler<NoteClickedEventArgs> NoteClicked;

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            var renderRequest = new RenderRequest
            {
                IsHitTest = true,
                CursorPoint = e.Location
            };

            Render(renderRequest);

            if (_mouseDown)
            {
                _mouseDragEndX = e.Location.X;
                var distance = Math.Max(_mouseDragStartX, _mouseDragEndX) - Math.Min(_mouseDragStartX, _mouseDragEndX);

                // If we've dragged more than a few pixels, so we'll count it as a drag/zoom action
                var isDragging = distance > 3;
                switch (e.Button)
                {
                    case MouseButtons.Middle:
                        _mouseDraggingZoom = isDragging;
                        break;
                    case MouseButtons.Left:
                        var previous = _mouseDraggingPan;
                        _mouseDraggingPan = isDragging;
                        if (previous != _mouseDraggingPan && _mouseDraggingObject == null)
                            Cursor = _mouseDraggingPan ? Cursors.Hand : Cursors.Default;
                        break;
                }
            }

            if (_mouseDraggingPan)
            {
                var startByteIndex = ClientXToByteIndex(_mouseDragStartX);
                var currentByteIndex = ClientXToByteIndex(e.Location.X);
                var byteDistance = startByteIndex - currentByteIndex;

                if (_mouseDraggingObject is Note)
                {
                    var note = (Note) _mouseDraggingObject;

                    var capStart = 0L;
                    var capEnd = _bytesTotal;
                    if (renderRequest.Notes != null)
                    {
                        var foundIndex = -1;
                        for (var i = 0; i < renderRequest.Notes.Count; i++)
                        {
                            var otherNote = renderRequest.Notes[i];
                            if (otherNote.Equals(note) == false) continue;

                            switch (_mouseDraggingArea)
                            {
                                case HitTestArea.NoteLeft:
                                    capEnd = otherNote.Interval.End -
                                             (long) Math.Round(GetBytesPerPixel() * GetZoomRatio() * 2);
                                    break;
                                case HitTestArea.NoteRight:
                                    capStart = otherNote.Interval.Start +
                                               (long) Math.Round(GetBytesPerPixel() * GetZoomRatio() * 2);
                                    break;
                            }

                            foundIndex = i;
                            break;
                        }

                        if (foundIndex != -1)
                        {
                            long previousEnd;
                            long nextStart;
                            if (renderRequest.HitTestArea == HitTestArea.NoteCenter)
                            {
                                previousEnd = foundIndex > 0
                                    ? renderRequest.Notes[foundIndex - 1].Interval.End
                                    : capStart;
                                nextStart = foundIndex < renderRequest.Notes.Count - 1
                                    ? renderRequest.Notes[foundIndex + 1].Interval.Start -
                                      renderRequest.Notes[foundIndex + 1].Interval.Length
                                    : capEnd;
                            }
                            else
                            {
                                previousEnd = foundIndex > 0
                                    ? renderRequest.Notes[foundIndex - 1].Interval.End
                                    : capStart;
                                nextStart = foundIndex < renderRequest.Notes.Count - 1
                                    ? renderRequest.Notes[foundIndex + 1].Interval.Start
                                    : capEnd;
                            }

                            capStart = Math.Max(capStart, previousEnd);
                            capEnd = Math.Min(capEnd, nextStart);
                        }
                    }

                    var args = new NoteMovedEventArgs
                    {
                        Note = note,
                        Area = _mouseDraggingArea,
                        ByteIndex = Math.Max(capStart, Math.Min(capEnd, _mouseDragByteIndexStart - byteDistance))
                    };

                    NoteMoved(this, args);
                }
                else
                {
                    _viewPortStart = Math.Max(0, _mouseDragByteIndexStart + byteDistance);
                }
            }
            else
            {
                if (_mouseDown == false) Cursor = renderRequest.CursorResult ?? Cursors.Default;
            }

            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            var renderRequest = new RenderRequest
            {
                IsHitTest = true,
                CursorPoint = e.Location
            };

            Render(renderRequest);

            switch (e.Button)
            {
                case MouseButtons.XButton1:
                case MouseButtons.XButton2:

                    // TODO: Redo this so that the paragraph selected in the text is the selected one
                    // TODO: Mouse4 changes start, mouse5 changes end
                    // TODO: The note should not have to be visible; remake the NoteMoved so the note is not required


                    // Middle mouse button should resize the note according to the most closely clicked location, in relevance to the location that the user is in the text
                    var clickedByteIndex = ClientXToByteIndex(e.X);
                    var clickedInterval = new Interval(clickedByteIndex, clickedByteIndex);
                    long[] closestDistances = null;
                    var closestDistance = long.MaxValue;
                    Note closestNote = null;
                    foreach (var note in renderRequest.Notes)
                        if (note.IsFocused)
                        {
                            closestNote = note;
                            closestDistances = note.Interval.GetDistanceFromOverlap(clickedByteIndex);
                            break;
                        }

                    if (closestNote == null)
                        foreach (var note in renderRequest.Notes)
                        {
                            var distance = note.Interval.GetDistanceFromOverlap(clickedByteIndex);
                            if (note.IsFocused || note.Interval.IsOverlapping(clickedInterval))
                            {
                                closestNote = note;
                                closestDistances = distance;
                                break;
                            }

                            var actualDistance = Math.Min(Math.Abs(distance[0]), Math.Abs(distance[1]));
                            if (actualDistance < closestDistance)
                            {
                                closestNote = note;
                                closestDistances = distance;
                                closestDistance = actualDistance;
                            }
                        }

                    if (closestNote != null)
                    {
                        var isStart = Math.Abs(closestDistances[0]) < Math.Abs(closestDistances[1]);

                        if (NoteMoved != null)
                        {
                            var args = new NoteMovedEventArgs
                            {
                                Area = isStart ? HitTestArea.NoteLeft : HitTestArea.NoteRight,
                                ByteIndex = clickedByteIndex,
                                Note = closestNote
                            };

                            NoteMoved(this, args);
                        }
                    }

                    break;
                case MouseButtons.Middle:
                    break;

                #region old zoom code

                /*
            if (this._mouseDraggingZoom)
            {
                // Let's set the time zoom!
                var low = Math.Max(0, Math.Min(this._mouseDragStartX, this._mouseDragEndX));
                var high = Math.Min(this.ClientRectangle.Width, Math.Max(this._mouseDragStartX, this._mouseDragEndX));

                // Set as approximative as we can.
                var timeStart = this.ClientXToByteIndex(low);
                var timeEnd = this.ClientXToByteIndex(high);

                this._zoomStack.Push((timeEnd - timeStart) / (double)this._bytesTotal);

                // Restart the processing, since we've now zoomed in.
                this.ClearWork();
                this.QueueWork(new Work(this.GetCurrentViewPortStart(), this.GetCurrentViewPortEnd()));
            }
            else
            {
                // The user clicked Middle Mouse, but had not selected anything. Let's zoom out.
                // Zoom out to the previous zoom level.
                if (this._zoomStack.Count > 0)
                {
                    this._zoomStack.Pop();
                    this.ClearWork();
                    this.QueueWork(new Work(this.GetCurrentViewPortStart(), this.GetCurrentViewPortEnd()));
                }
            }
            */

                #endregion

                case MouseButtons.Left:
                    if (_mouseDraggingPan == false)
                    {
                        var note = renderRequest.FocusedObject as Note;
                        if (note != null)
                            if (NoteClicked != null)
                                NoteClicked(this, new NoteClickedEventArgs
                                {
                                    Note = note,
                                    Area = renderRequest.HitTestArea
                                });
                    }

                    break;
            }

            if (_mouseDraggingPan)
            {
                if (_mouseDraggingObject == null && IsCaretInsideViewPort)
                {
                    var distanceIntoViewPort = GetCurrentBytePosition() - GetCurrentViewPortStart();
                    _caretOffset = distanceIntoViewPort / (double) GetCurrentViewPortDistance();
                }
                else
                {
                    // Since we stopped panning outside of the currently playing viewport, we'll set the caret offset to NaN.
                    // This means that the viewport will stay where it is, while the track is playing.
                    switch (Bass.ChannelIsActive(_playChannel))
                    {
                        case PlaybackState.Playing:
                            _caretOffset = double.NaN;
                            break;
                    }
                }

                // Let's get the new area that should be rendered, and queue the work.
                // We'll add the whole visible viewport, and it will automatically be split and merged properly.
                var work = new Work(GetCurrentViewPortStart(), GetCurrentViewPortEnd());
                QueueWork(work);
            }

            Cursor = Cursors.Default;
            _mouseDraggingZoom = false;
            _mouseDraggingPan = false;
            _mouseDown = false;
            _mouseDraggingObject = null;
            _mouseDraggingArea = HitTestArea.None;

            Invalidate();
        }
    }
}