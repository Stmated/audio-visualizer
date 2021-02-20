using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime;
using System.Speech.AudioFormat;
using System.Threading;
using System.Windows.Forms;
using Eliason.Common;
using Un4seen.Bass;
using Un4seen.Bass.AddOn.Fx;
using Un4seen.Bass.Misc;
using Timer = System.Windows.Forms.Timer;

namespace Eliason.AudioVisualizer
{
    public partial class AudioVisualizer : UserControl
    {
        private String _currentFilePath;

        private long _bytesTotal;
        private int _maxFrequency = 2000;

        private IntPtr _handle;
        private int _playChannel;

        private long _viewPortStart;
        private double _caretOffset = double.NaN;

        private long _mouseDragByteIndexStart;

        private Timer _playTimer;

        private long _repeatCurrent;
        private double _repeatBackwards;
        private double _repeatPause;
        private double _repeatLength;
        private double _repeatPauseRemaining;

        private long _pauseStart;
        private bool _pauseAborted;

        private double _zoomRatio;

        public double ZoomSeconds
        {
            get { return this._zoomSeconds; }
            set
            {
                this._zoomSeconds = value;
                this._zoomRatio = 0;
                this.ClearWork();
                this.QueueWork(new Work(this.GetCurrentViewPortStart(), this.GetCurrentViewPortEnd()));
            }
        }

        public AudioVisualizer()
        {
            this.SetStyle(ControlStyles.Selectable, false);
            this.TabStop = false;
            this.SmallStep = 1;
            this.LargeStep = 5;
            this.ZoomSeconds = 15;

            this.InitializeComponent();
        }

        private readonly TimeSpan _attributeChangeAnimationLength = TimeSpan.FromMilliseconds(2000);

        public double RepeatLength
        {
            get
            {
                return this._repeatLength;
            }
            set
            {
                this._repeatLength = Math.Max(0, Math.Min(20, value));
                this.RepeatBackwards = this.RepeatBackwards;
            }
        }

        public double RepeatBackwards
        {
            get
            {
                return this._repeatBackwards;
            }
            set
            {
                this._repeatBackwards = Math.Max(0, Math.Min(this.RepeatLength, value));
            }
        }

        public double RepeatPause
        {
            get
            {
                return this._repeatPause;
            }
            set { this._repeatPause = Math.Max(0, Math.Min(20, value)); }
        }

        protected override void OnLoad(EventArgs e)
        {
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);

            base.OnLoad(e);

            var form = this.FindForm();
            if (form == null)
            {
                throw new InvalidOperationException("Could not find a parent Form. It must be attached to a form to work properly.");
            }

            var currentlyResizing = false;
            form.Resize += delegate
            {
                currentlyResizing = true;
            };

            form.ResizeEnd += delegate
            {
                if (currentlyResizing)
                {
                    this.ClearWork();
                    this.QueueWork(new Work(this.GetCurrentViewPortStart(), this.GetCurrentViewPortEnd()));
                    currentlyResizing = false;
                }
            };

            try
            {
                this._handle = this.Handle;
                if (Bass.BASS_Init(-1, 44100, BASSInit.BASS_DEVICE_DEFAULT, this._handle) == false)
                {
                    Console.WriteLine("Could not initialize the BASS channel for handle '{0}'", this._handle);
                }

                this.StartConsumerThreads();

                if (this._playTimer == null)
                {
                    this._playTimer = new Timer
                    {
                        Interval = 25
                    };
                    this._playTimer.Tick += this.PlayTimer_OnTick;
                }

                this._playTimer.Start();

                // Every X second(s) we should check if we should queue another work,
                // so that while we scroll through the timeline we'll likely not encounter blank space.
                var workTimer = new Timer
                {
                    Interval = 2000
                };
                workTimer.Tick += this.WorkTimer_OnTick;
                workTimer.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        private void WorkTimer_OnTick(object sender, EventArgs args)
        {
            // Let's queue a work that will calculate the spectrogram of X seconds into the future
            if (this._playChannel != 0)
            {
                this.QueueWork(new Work(this.GetCurrentViewPortStart(), this.GetCurrentViewPortEnd() + this.GetCurrentViewPortDistance()));
            }
        }

        private void PlayTimer_OnTick(object sender, EventArgs e)
        {
            switch (Bass.BASS_ChannelIsActive(this._playChannel))
            {
                case BASSActive.BASS_ACTIVE_PLAYING:

                    if (this.RepeatLength > 0)
                    {
                        // We're on repeat.
                        // Let's check if we have surpassed the current repeat range.

                        var max = this._repeatCurrent + Bass.BASS_ChannelSeconds2Bytes(this._playChannel, this.RepeatLength);
                        if (this.GetCurrentBytePosition() >= max /*- (this.GetBytesPerPixel() * this.GetZoomRatio()) * 2*/)
                        {
                            var destinationAfterPause = Math.Max(0, max - Bass.BASS_ChannelSeconds2Bytes(this._playChannel, this.RepeatBackwards));
                            var pauseDuration = this.RepeatPause;

                            var pauseMilliseconds = pauseDuration * 1000d;
                            this.StartTemporaryPause(destinationAfterPause, pauseMilliseconds);
                        }
                    }

                    // Invalidate it all, instead of just where the cursor was and now will be.
                    this.Invalidate();
                    break;
                default:
                    if (this._timestampLastAttributeChange != DateTime.MinValue)
                    {
                        if ((DateTime.Now - this._timestampLastAttributeChange) <= this._attributeChangeAnimationLength)
                        {
                            // There's an ongoing animation, so we should invalidate so that it can render itself
                            this.Invalidate();
                        }
                        else
                        {
                            this._timestampLastAttributeChange = DateTime.MinValue;
                            this.Invalidate();
                        }
                    }
                    break;
            }
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            if (this._playChannel != 0)
            {
                Bass.BASS_ChannelStop(this._playChannel);
                Bass.BASS_StreamFree(this._playChannel);
                this._playChannel = 0;
            }

            Bass.BASS_Stop();
            Bass.BASS_Free();

            base.OnHandleDestroyed(e);
        }

        public long GetCurrentViewPortStart()
        {
            //var bytesPerPixel = (long)Math.Floor(this.GetCurrentViewPortDistance() / (double)this.Width);
            //var modulusValue = trimmedValue - (trimmedValue % bytesPerPixel);

            if (double.IsNaN(this._caretOffset))
            {
                return this._viewPortStart;
            }

            // The viewport start is dynamic depending on the current play position.
            // So let's calculate where the viewport starts depending on the caret offset.
            var currentPosition = this.GetCurrentBytePosition();
            var pixelDistance = this.Width * this._caretOffset;
            var bytesPerZoomedPixel = this.GetBytesPerPixel() * this.GetZoomRatio();
            var byteDistance = (long)Math.Round(pixelDistance * bytesPerZoomedPixel);

            return Math.Max(0, currentPosition - byteDistance);
        }

        public void SetCaretOffset(double value)
        {
            this._caretOffset = value;
        }

        public long Normalize(double value)
        {
            var bytesPerPixel = (long)Math.Floor(this.GetCurrentViewPortDistance() / (double)this.Width);
            var modulusValue = value - (value % bytesPerPixel);
            return (long)Math.Floor(modulusValue);
        }

        public long GetCurrentViewPortEnd()
        {
            return this.GetCurrentViewPortStart() + this.GetCurrentViewPortDistance();
        }

        public long GetCurrentViewPortDistance()
        {
            return (long)Math.Floor(this._bytesTotal * this.GetZoomRatio());
        }

        private double GetZoomRatio()
        {
            if (this._zoomRatio <= 0)
            {
                if (this._bytesTotal <= 0)
                {
                    return 1;
                }

                this._zoomRatio = this.ZoomSeconds / this.ByteIndexToSeconds(this._bytesTotal);
            }

            return this._zoomRatio;
        }

        private double GetBytesPerPixel()
        {
            return this._bytesTotal / (double)this.Width;
        }

        public long GetCurrentBytePosition()
        {
            long currentPosition = Bass.BASS_ChannelGetPosition(this._playChannel);
            if (currentPosition < 0)
            {
                return 0;
            }

            return currentPosition;
        }

        public long GetCurrentMillisecondPosition()
        {
            var currentBytePosition = this.GetCurrentBytePosition();
            var seconds = Bass.BASS_ChannelBytes2Seconds(this._playChannel, currentBytePosition);
            if (seconds < 0)
            {
                return 0;
            }

            return (long)Math.Round(seconds * 1000);
        }

        public long ClientXToByteIndex(int x)
        {
            var currentViewPortStart = this.GetCurrentViewPortStart();
            return (long)Math.Floor(currentViewPortStart + (x * this.GetBytesPerPixel()) * this.GetZoomRatio());
        }

        public long SecondsToByteIndex(double seconds)
        {
            return Bass.BASS_ChannelSeconds2Bytes(this._playChannel, seconds);
        }

        public long ByteIndexToMilliseconds(long byteIndex)
        {
            return (long)Math.Round(this.ByteIndexToSeconds(byteIndex) * 1000d);
        }

        public double ByteIndexToSeconds(long byteIndex)
        {
            return Bass.BASS_ChannelBytes2Seconds(this._playChannel, byteIndex);
        }

        private double NormalizedByteIndexToClientX(long byteIndex)
        {
            byteIndex = this.Normalize(byteIndex);
            var viewPortStart = this.Normalize(this.GetCurrentViewPortStart());
            var viewPortEnd = this.Normalize(this.GetCurrentViewPortEnd());
            var viewPortDistance = viewPortEnd - viewPortStart;
            var byteDistanceIntoViewPort = byteIndex - viewPortStart;
            var ratioIntoViewPort = byteDistanceIntoViewPort / (double)(viewPortDistance);

            return this.Width * ratioIntoViewPort;
        }

        public double ByteIndexToClientX(long byteIndex, long viewPortStart = long.MinValue)
        {
            if (viewPortStart == long.MinValue)
            {
                viewPortStart = this.GetCurrentViewPortStart();
            }

            var viewPortEnd = this.GetCurrentViewPortEnd();
            var viewPortDistance = viewPortEnd - viewPortStart;
            var byteDistanceIntoViewPort = byteIndex - viewPortStart;
            var ratioIntoViewPort = byteDistanceIntoViewPort / (double)(viewPortDistance);

            return this.Width * ratioIntoViewPort;
        }

        public int CreateNewChannel()
        {
            return Bass.BASS_StreamCreateFile(this._currentFilePath, 0, 0, BASSFlag.BASS_DEFAULT | BASSFlag.BASS_STREAM_PRESCAN | BASSFlag.BASS_STREAM_DECODE);
        }

        public int CreateNewChannel(long offset, long length)
        {
            // Remove BASSFlag.BASS_DEFAULT and replace with BASSFlag.BASS_SAMPLE_FLOAT
            return Bass.BASS_StreamCreateFile(this._currentFilePath, 0, 0, BASSFlag.BASS_SAMPLE_FLOAT | BASSFlag.BASS_STREAM_PRESCAN | BASSFlag.BASS_STREAM_DECODE);
        }

        public String CurrentFilePath
        {
            get
            {
                return this._currentFilePath;
            }
        }

        public void Open(String filePath)
        {
            this._currentFilePath = filePath;

            if (String.IsNullOrEmpty(this._currentFilePath) == false)
            {
                // BASS_CONFIG_BUFFER

                var decodingChannel = this.CreateNewChannel();
                var resamplingChannel = BassFx.BASS_FX_TempoCreate(decodingChannel, BASSFlag.BASS_FX_TEMPO_ALGO_CUBIC);

                this._playChannel = resamplingChannel;
                this._bytesTotal = Bass.BASS_ChannelGetLength(this._playChannel, BASSMode.BASS_POS_BYTES);

                //Bass.BASS_ChannelSetAttribute(this._playChannel, BASSAttribute.BASS_ATTRIB_TEMPO_OPTION_USE_AA_FILTER, 0);
                Bass.BASS_ChannelSetAttribute(this._playChannel, BASSAttribute.BASS_ATTRIB_TEMPO_OPTION_SEQUENCE_MS, 1); // default 82
                Bass.BASS_ChannelSetAttribute(this._playChannel, BASSAttribute.BASS_ATTRIB_TEMPO_OPTION_SEEKWINDOW_MS, 36); // default 14
                Bass.BASS_ChannelSetAttribute(this._playChannel, BASSAttribute.BASS_ATTRIB_TEMPO_OPTION_OVERLAP_MS, 6); // default 12 -- decrease if decrease SEQUENCE_MS,

                //var bytesInPreferredTime = Bass.BASS_ChannelSeconds2Bytes(this._playChannel, 15);
                //var preferredZoomRatio = bytesInPreferredTime / (double)this._bytesTotal;
                this._zoomRatio = 0; // Math.Min(1, preferredZoomRatio);
                this._caretOffset = 0.5d;

                this.ClearWork();
                this.QueueWork(new Work(this.GetCurrentViewPortStart(), this.GetCurrentViewPortEnd()));

                this.Invalidate();
            }
        }

        public void SetFrequencyRange(int value)
        {
            if (value != this._maxFrequency)
            {
                this._maxFrequency = value;
                this.ClearWork();
                this.Invalidate();
            }
        }

        public int GetFrequencyRange()
        {
            return this._maxFrequency;
        }

        public void TogglePlayPause()
        {
            switch (Bass.BASS_ChannelIsActive(this._playChannel))
            {
                case BASSActive.BASS_ACTIVE_PAUSED:
                    this.StartPlaying();
                    break;
                case BASSActive.BASS_ACTIVE_PLAYING:
                    this.StopPlaying();
                    break;
                case BASSActive.BASS_ACTIVE_STALLED:
                    this.StartPlaying();
                    break;
                case BASSActive.BASS_ACTIVE_STOPPED:
                    this.StartPlaying();
                    break;
            }
        }

        public void StopPlaying()
        {
            switch (Bass.BASS_ChannelIsActive(this._playChannel))
            {
                case BASSActive.BASS_ACTIVE_PLAYING:
                    Bass.BASS_ChannelPause(this._playChannel);
                    break;
            }
        }

        private void StartPlaying(long? location = null)
        {
            if (String.IsNullOrEmpty(this._currentFilePath))
            {
                // Won't play anything if we have no file to play ;)
                return;
            }

            switch (Bass.BASS_ChannelIsActive(this._playChannel))
            {
                case BASSActive.BASS_ACTIVE_PLAYING:

                    // First pause the channel, 
                    // so we don't play on multiple locations for the same channel.
                    Bass.BASS_ChannelPause(this._playChannel);
                    break;
            }

            switch (Bass.BASS_ChannelIsActive(this._playChannel))
            {
                case BASSActive.BASS_ACTIVE_PAUSED:
                    Bass.BASS_ChannelPlay(this._playChannel, false);
                    break;
                case BASSActive.BASS_ACTIVE_STOPPED:
                    Bass.BASS_ChannelSetPosition(this._playChannel, location ?? this.GetCurrentBytePosition());
                    Bass.BASS_ChannelPlay(this._playChannel, false);
                    Bass.BASS_ChannelSetAttribute(this._playChannel, BASSAttribute.BASS_ATTRIB_VOL, 0.5f);
                    break;
                case BASSActive.BASS_ACTIVE_STALLED:
                    Bass.BASS_ChannelPlay(this._playChannel, true);
                    break;
            }

            if (location != null)
            {
                this.SetLocation(location.Value);
            }
        }

        public double SmallStep { get; set; }

        public double LargeStep { get; set; }

        public void SeekBackward(bool large = false)
        {
            var seconds = Bass.BASS_ChannelBytes2Seconds(this._playChannel, this.GetCurrentBytePosition()) - (large ? this.LargeStep : this.SmallStep);
            var byteIndex = Bass.BASS_ChannelSeconds2Bytes(this._playChannel, seconds);
            this.SetLocation(byteIndex);
        }

        public void SeekForward(bool large = false)
        {
            var seconds = Bass.BASS_ChannelBytes2Seconds(this._playChannel, this.GetCurrentBytePosition()) + (large ? this.LargeStep : this.SmallStep);
            var byteIndex = Bass.BASS_ChannelSeconds2Bytes(this._playChannel, seconds);
            this.SetLocation(byteIndex);
        }

        public void SetLocation(long location)
        {
            //this._playTimerPreviousMarker = location;
            var result = Bass.BASS_ChannelSetPosition(this._playChannel, location);
            this._pauseAborted = true;
        }

        public void SetLocationMs(long ms)
        {
            var byteIndex = Bass.BASS_ChannelSeconds2Bytes(this._playChannel, (long)(ms / 1000L));
            this.SetLocation(byteIndex);
        }

        private DateTime _timestampLastAttributeChange = DateTime.MinValue;
        private double _zoomSeconds;

        public float GetVolume()
        {
            float currentVolume = 0f;
            Bass.BASS_ChannelGetAttribute(this._playChannel, BASSAttribute.BASS_ATTRIB_VOL, ref currentVolume);

            return currentVolume;
        }

        public void VolumeIncrease()
        {
            Bass.BASS_ChannelSetAttribute(this._playChannel, BASSAttribute.BASS_ATTRIB_VOL, Math.Min(this.GetVolume() + 0.025f, 1));

            this._timestampLastAttributeChange = DateTime.Now;
            this.Invalidate();
        }

        public void VolumeDecrease()
        {
            Bass.BASS_ChannelSetAttribute(this._playChannel, BASSAttribute.BASS_ATTRIB_VOL, Math.Max(this.GetVolume() - 0.025f, 0));

            this._timestampLastAttributeChange = DateTime.Now;
            this.Invalidate();
        }

        public Boolean IsPlayable
        {
            get { return this._currentFilePath != null; }
        }

        public float GetTempo()
        {
            float currentSpeed = 0f;
            //Bass.BASS_ChannelGetAttribute(this._playChannel, BASSAttribute.BASS_ATTRIB_TEMPO_FREQ, ref currentSpeed);
            Bass.BASS_ChannelGetAttribute(this._playChannel, BASSAttribute.BASS_ATTRIB_TEMPO, ref currentSpeed);
            return currentSpeed;
        }

        public bool SetTempo(float value)
        {
            //var newSpeed = Math.Min(50000f, this.GetTempo() + 200f);
            //var result = Bass.BASS_ChannelSetAttribute(this._playChannel, BASSAttribute.BASS_ATTRIB_TEMPO_FREQ, newSpeed);

            var newSpeed = Math.Max(-90f, Math.Min(500f, value));
            return Bass.BASS_ChannelSetAttribute(this._playChannel, BASSAttribute.BASS_ATTRIB_TEMPO, newSpeed);
        }

        public void StartTemporaryPause(long destinationAfterPause, double pauseMilliseconds, bool restartable = false)
        {
            if (this._repeatPauseRemaining > 0)
            {
                // There's already a pause going on.
                // So we'll quit from here until that pause has been completed.
                // TODO: Might want to replace the destination with this new one?
                if (restartable)
                {
                    // We're restartable, though.
                    // So let's reset the remaining pause time.
                    this._pauseStart = Environment.TickCount;
                }

                return;
            }

            switch (Bass.BASS_ChannelIsActive(this._playChannel))
            {
                case BASSActive.BASS_ACTIVE_PAUSED:
                case BASSActive.BASS_ACTIVE_STALLED:
                case BASSActive.BASS_ACTIVE_STOPPED:

                    // If we're not currently playing, then there's no point in starting a pause.
                    return;
            }

            this._pauseAborted = false;
            this._repeatPauseRemaining = pauseMilliseconds;
            if (this._repeatPauseRemaining > 0)
            {
                this.StopPlaying();
                this._pauseStart = Environment.TickCount;
                var pauseTimer = new Timer();
                pauseTimer.Tick += delegate
                {
                    if (this._pauseAborted)
                    {
                        this._repeatPauseRemaining = 0;
                        this._pauseAborted = false;
                        pauseTimer.Stop();
                        pauseTimer.Dispose();
                        return;
                    }

                    var msElapsed = Environment.TickCount - this._pauseStart;
                    this._repeatPauseRemaining = Math.Max(0, (pauseMilliseconds - msElapsed));

                    if (this._repeatPauseRemaining <= 0)
                    {
                        this._repeatCurrent = destinationAfterPause;
                        this.StartPlaying(destinationAfterPause);
                        pauseTimer.Stop();
                        pauseTimer.Dispose();
                    }
                    else
                    {
                        this.Invalidate();
                    }
                };

                pauseTimer.Interval = 100;
                pauseTimer.Start();
            }
            else
            {
                this._repeatCurrent = destinationAfterPause;
                this.SetLocation(destinationAfterPause);
            }

            this.Invalidate();
        }

        public Stream GetAudioStream()
        {
            return null;
        }

        public SpeechAudioFormatInfo GetSpeechAudioFormat()
        {
            //EncoderWAV.
            WaveWriter wv = new WaveWriter("asd.wav", 1, 44100, 16, true);

            var samplesPerSecond = 0;
            var bitsPerSample = 0;
            var channelCount = 0;
            var averageBytesPerSecond = 0;
            int blockAlign = 0;
            var formatSpecificData = new byte[0];

            return new SpeechAudioFormatInfo(EncodingFormat.Pcm, samplesPerSecond, bitsPerSample, channelCount, averageBytesPerSecond, blockAlign, formatSpecificData);
        }
    }
}
