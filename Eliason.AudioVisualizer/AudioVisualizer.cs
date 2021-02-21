using System;
using System.IO;
using System.Speech.AudioFormat;
using System.Windows.Forms;
using ManagedBass;
using ManagedBass.Fx;

namespace Eliason.AudioVisualizer
{
    public partial class AudioVisualizer : UserControl
    {
        private readonly TimeSpan _attributeChangeAnimationLength = TimeSpan.FromMilliseconds(2000);

        private long _bytesTotal;
        private double _caretOffset = double.NaN;

        private IntPtr _handle;
        private int _maxFrequency = 2000;

        private long _mouseDragByteIndexStart;
        private bool _pauseAborted;

        private long _pauseStart;
        private int _playChannel;

        private Timer _playTimer;
        private double _repeatBackwards;

        private long _repeatCurrent;
        private double _repeatLength;
        private double _repeatPause;
        private double _repeatPauseRemaining;

        private DateTime _timestampLastAttributeChange = DateTime.MinValue;

        private long _viewPortStart;

        private double _zoomRatio;
        private double _zoomSeconds;

        public AudioVisualizer()
        {
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            SmallStep = 1;
            LargeStep = 5;
            ZoomSeconds = 15;

            InitializeComponent();
        }

        public double ZoomSeconds
        {
            get => _zoomSeconds;
            set
            {
                _zoomSeconds = value;
                _zoomRatio = 0;
                ClearWork();
                QueueWork(new Work(GetCurrentViewPortStart(), GetCurrentViewPortEnd()));
            }
        }

        public double RepeatLength
        {
            get => _repeatLength;
            set
            {
                _repeatLength = Math.Max(0, Math.Min(20, value));
                RepeatBackwards = RepeatBackwards;
            }
        }

        public double RepeatBackwards
        {
            get => _repeatBackwards;
            set => _repeatBackwards = Math.Max(0, Math.Min(RepeatLength, value));
        }

        public double RepeatPause
        {
            get => _repeatPause;
            set => _repeatPause = Math.Max(0, Math.Min(20, value));
        }

        public string CurrentFilePath { get; private set; }

        public double SmallStep { get; set; }

        public double LargeStep { get; set; }

        public bool IsPlayable => CurrentFilePath != null;

        protected override void OnLoad(EventArgs e)
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);

            base.OnLoad(e);

            var form = FindForm();
            if (form == null)
                throw new InvalidOperationException(
                    "Could not find a parent Form. It must be attached to a form to work properly.");

            var currentlyResizing = false;
            form.Resize += delegate { currentlyResizing = true; };

            form.ResizeEnd += delegate
            {
                if (currentlyResizing)
                {
                    ClearWork();
                    QueueWork(new Work(GetCurrentViewPortStart(), GetCurrentViewPortEnd()));
                    currentlyResizing = false;
                }
            };

            try
            {
                _handle = Handle;
                if (Bass.Init(-1, 44100, DeviceInitFlags.Default, _handle) == false)
                    Console.WriteLine("Could not initialize the BASS channel for handle '{0}'", _handle);

                StartConsumerThreads();

                if (_playTimer == null)
                {
                    _playTimer = new Timer
                    {
                        Interval = 25
                    };
                    _playTimer.Tick += PlayTimer_OnTick;
                }

                _playTimer.Start();

                // Every X second(s) we should check if we should queue another work,
                // so that while we scroll through the timeline we'll likely not encounter blank space.
                var workTimer = new Timer
                {
                    Interval = 2000
                };
                workTimer.Tick += WorkTimer_OnTick;
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
            if (_playChannel != 0)
                QueueWork(new Work(GetCurrentViewPortStart(), GetCurrentViewPortEnd() + GetCurrentViewPortDistance()));
        }

        private void PlayTimer_OnTick(object sender, EventArgs e)
        {
            switch (Bass.ChannelIsActive(_playChannel))
            {
                case PlaybackState.Playing:

                    if (RepeatLength > 0)
                    {
                        // We're on repeat.
                        // Let's check if we have surpassed the current repeat range.

                        var max = _repeatCurrent + Bass.ChannelSeconds2Bytes(_playChannel, RepeatLength);
                        if (GetCurrentBytePosition() >= max /*- (this.GetBytesPerPixel() * this.GetZoomRatio()) * 2*/)
                        {
                            var destinationAfterPause = Math.Max(0,
                                max - Bass.ChannelSeconds2Bytes(_playChannel, RepeatBackwards));
                            var pauseDuration = RepeatPause;

                            var pauseMilliseconds = pauseDuration * 1000d;
                            StartTemporaryPause(destinationAfterPause, pauseMilliseconds);
                        }
                    }

                    // Invalidate it all, instead of just where the cursor was and now will be.
                    Invalidate();
                    break;
                default:
                    if (_timestampLastAttributeChange != DateTime.MinValue)
                    {
                        if (DateTime.Now - _timestampLastAttributeChange <= _attributeChangeAnimationLength)
                        {
                            // There's an ongoing animation, so we should invalidate so that it can render itself
                            Invalidate();
                        }
                        else
                        {
                            _timestampLastAttributeChange = DateTime.MinValue;
                            Invalidate();
                        }
                    }

                    break;
            }
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            if (_playChannel != 0)
            {
                Bass.ChannelStop(_playChannel);
                Bass.StreamFree(_playChannel);
                _playChannel = 0;
            }

            Bass.Stop();
            Bass.Free();

            base.OnHandleDestroyed(e);
        }

        public long GetCurrentViewPortStart()
        {
            //var bytesPerPixel = (long)Math.Floor(this.GetCurrentViewPortDistance() / (double)this.Width);
            //var modulusValue = trimmedValue - (trimmedValue % bytesPerPixel);

            if (double.IsNaN(_caretOffset)) return _viewPortStart;

            // The viewport start is dynamic depending on the current play position.
            // So let's calculate where the viewport starts depending on the caret offset.
            var currentPosition = GetCurrentBytePosition();
            var pixelDistance = Width * _caretOffset;
            var bytesPerZoomedPixel = GetBytesPerPixel() * GetZoomRatio();
            var byteDistance = (long) Math.Round(pixelDistance * bytesPerZoomedPixel);

            return Math.Max(0, currentPosition - byteDistance);
        }

        public void SetCaretOffset(double value)
        {
            _caretOffset = value;
        }

        public long Normalize(double value)
        {
            var bytesPerPixel = (long) Math.Floor(GetCurrentViewPortDistance() / (double) Width);
            var modulusValue = value - value % bytesPerPixel;
            return (long) Math.Floor(modulusValue);
        }

        public long GetCurrentViewPortEnd()
        {
            return GetCurrentViewPortStart() + GetCurrentViewPortDistance();
        }

        public long GetCurrentViewPortDistance()
        {
            return (long) Math.Floor(_bytesTotal * GetZoomRatio());
        }

        private double GetZoomRatio()
        {
            if (_zoomRatio <= 0)
            {
                if (_bytesTotal <= 0) return 1;

                _zoomRatio = ZoomSeconds / ByteIndexToSeconds(_bytesTotal);
            }

            return _zoomRatio;
        }

        private double GetBytesPerPixel()
        {
            return _bytesTotal / (double) Width;
        }

        public long GetCurrentBytePosition()
        {
            var currentPosition = Bass.ChannelGetPosition(_playChannel);
            if (currentPosition < 0) return 0;

            return currentPosition;
        }

        public long GetCurrentMillisecondPosition()
        {
            var currentBytePosition = GetCurrentBytePosition();
            var seconds = Bass.ChannelBytes2Seconds(_playChannel, currentBytePosition);
            if (seconds < 0) return 0;

            return (long) Math.Round(seconds * 1000);
        }

        public long ClientXToByteIndex(int x)
        {
            var currentViewPortStart = GetCurrentViewPortStart();
            return (long) Math.Floor(currentViewPortStart + x * GetBytesPerPixel() * GetZoomRatio());
        }

        public long SecondsToByteIndex(double seconds)
        {
            return Bass.ChannelSeconds2Bytes(_playChannel, seconds);
        }

        public long ByteIndexToMilliseconds(long byteIndex)
        {
            return (long) Math.Round(ByteIndexToSeconds(byteIndex) * 1000d);
        }

        public double ByteIndexToSeconds(long byteIndex)
        {
            return Bass.ChannelBytes2Seconds(_playChannel, byteIndex);
        }

        private double NormalizedByteIndexToClientX(long byteIndex)
        {
            byteIndex = Normalize(byteIndex);
            var viewPortStart = Normalize(GetCurrentViewPortStart());
            var viewPortEnd = Normalize(GetCurrentViewPortEnd());
            var viewPortDistance = viewPortEnd - viewPortStart;
            var byteDistanceIntoViewPort = byteIndex - viewPortStart;
            var ratioIntoViewPort = byteDistanceIntoViewPort / (double) viewPortDistance;

            return Width * ratioIntoViewPort;
        }

        public double ByteIndexToClientX(long byteIndex, long viewPortStart = long.MinValue)
        {
            if (viewPortStart == long.MinValue) viewPortStart = GetCurrentViewPortStart();

            var viewPortEnd = GetCurrentViewPortEnd();
            var viewPortDistance = viewPortEnd - viewPortStart;
            var byteDistanceIntoViewPort = byteIndex - viewPortStart;
            var ratioIntoViewPort = byteDistanceIntoViewPort / (double) viewPortDistance;

            return Width * ratioIntoViewPort;
        }

        public int CreateNewChannel()
        {
            return Bass.CreateStream(CurrentFilePath, 0, 0, BassFlags.Default | BassFlags.Prescan | BassFlags.Decode);
        }

        public int CreateNewChannel(long offset, long length)
        {
            // Remove BASSFlag.BASS_DEFAULT and replace with BASSFlag.BASS_SAMPLE_FLOAT
            return Bass.CreateStream(CurrentFilePath, 0, 0, BassFlags.Float | BassFlags.Prescan | BassFlags.Decode);
        }

        public void Open(string filePath)
        {
            CurrentFilePath = filePath;

            if (string.IsNullOrEmpty(CurrentFilePath) == false)
            {
                // BASS_CONFIG_BUFFER

                var decodingChannel = CreateNewChannel();
                var resamplingChannel = BassFx.TempoCreate(decodingChannel, BassFlags.FxTempoAlgorithmCubic);

                _playChannel = resamplingChannel;
                _bytesTotal = Bass.ChannelGetLength(_playChannel);

                //Bass.BASS_ChannelSetAttribute(this._playChannel, BASSAttribute.BASS_ATTRIB_TEMPO_OPTION_USE_AA_FILTER, 0);
                Bass.ChannelSetAttribute(_playChannel, ChannelAttribute.TempoSequenceMilliseconds, 1); // default 82
                Bass.ChannelSetAttribute(_playChannel, ChannelAttribute.TempoSeekWindowMilliseconds, 36); // default 14
                Bass.ChannelSetAttribute(_playChannel, ChannelAttribute.TempoOverlapMilliseconds,
                    6); // default 12 -- decrease if decrease SEQUENCE_MS,

                //var bytesInPreferredTime = Bass.BASS_ChannelSeconds2Bytes(this._playChannel, 15);
                //var preferredZoomRatio = bytesInPreferredTime / (double)this._bytesTotal;
                _zoomRatio = 0; // Math.Min(1, preferredZoomRatio);
                _caretOffset = 0.5d;

                ClearWork();
                QueueWork(new Work(GetCurrentViewPortStart(), GetCurrentViewPortEnd()));

                Invalidate();
            }
        }

        public void SetFrequencyRange(int value)
        {
            if (value != _maxFrequency)
            {
                _maxFrequency = value;
                ClearWork();
                Invalidate();
            }
        }

        public int GetFrequencyRange()
        {
            return _maxFrequency;
        }

        public void TogglePlayPause()
        {
            switch (Bass.ChannelIsActive(_playChannel))
            {
                case PlaybackState.Paused:
                    StartPlaying();
                    break;
                case PlaybackState.Playing:
                    StopPlaying();
                    break;
                case PlaybackState.Stalled:
                    StartPlaying();
                    break;
                case PlaybackState.Stopped:
                    StartPlaying();
                    break;
            }
        }

        public void StopPlaying()
        {
            switch (Bass.ChannelIsActive(_playChannel))
            {
                case PlaybackState.Playing:
                    Bass.ChannelPause(_playChannel);
                    break;
            }
        }

        private void StartPlaying(long? location = null)
        {
            if (string.IsNullOrEmpty(CurrentFilePath))
                // Won't play anything if we have no file to play ;)
                return;

            switch (Bass.ChannelIsActive(_playChannel))
            {
                case PlaybackState.Playing:

                    // First pause the channel, 
                    // so we don't play on multiple locations for the same channel.
                    Bass.ChannelPause(_playChannel);
                    break;
            }

            switch (Bass.ChannelIsActive(_playChannel))
            {
                case PlaybackState.Paused:
                    Bass.ChannelPlay(_playChannel);
                    break;
                case PlaybackState.Stopped:
                    Bass.ChannelSetPosition(_playChannel, location ?? GetCurrentBytePosition());
                    Bass.ChannelPlay(_playChannel);
                    Bass.ChannelSetAttribute(_playChannel, ChannelAttribute.Volume, 0.5f);
                    break;
                case PlaybackState.Stalled:
                    Bass.ChannelPlay(_playChannel, true);
                    break;
            }

            if (location != null) SetLocation(location.Value);
        }

        public void SeekBackward(bool large = false)
        {
            var seconds = Bass.ChannelBytes2Seconds(_playChannel, GetCurrentBytePosition()) -
                          (large ? LargeStep : SmallStep);
            var byteIndex = Bass.ChannelSeconds2Bytes(_playChannel, seconds);
            SetLocation(byteIndex);
        }

        public void SeekForward(bool large = false)
        {
            var seconds = Bass.ChannelBytes2Seconds(_playChannel, GetCurrentBytePosition()) +
                          (large ? LargeStep : SmallStep);
            var byteIndex = Bass.ChannelSeconds2Bytes(_playChannel, seconds);
            SetLocation(byteIndex);
        }

        public void SetLocation(long location)
        {
            //this._playTimerPreviousMarker = location;
            var result = Bass.ChannelSetPosition(_playChannel, location);
            _pauseAborted = true;
        }

        public void SetLocationMs(long ms)
        {
            var byteIndex = Bass.ChannelSeconds2Bytes(_playChannel, ms / 1000L);
            SetLocation(byteIndex);
        }

        public float GetVolume()
        {
            var currentVolume = 0f;
            Bass.ChannelGetAttribute(_playChannel, ChannelAttribute.Volume, out currentVolume);

            return currentVolume;
        }

        public void VolumeIncrease()
        {
            Bass.ChannelSetAttribute(_playChannel, ChannelAttribute.Volume, Math.Min(GetVolume() + 0.025f, 1));

            _timestampLastAttributeChange = DateTime.Now;
            Invalidate();
        }

        public void VolumeDecrease()
        {
            Bass.ChannelSetAttribute(_playChannel, ChannelAttribute.Volume, Math.Max(GetVolume() - 0.025f, 0));

            _timestampLastAttributeChange = DateTime.Now;
            Invalidate();
        }

        public float GetTempo()
        {
            var currentSpeed = 0f;
            //Bass.BASS_ChannelGetAttribute(this._playChannel, BASSAttribute.BASS_ATTRIB_TEMPO_FREQ, ref currentSpeed);
            Bass.ChannelGetAttribute(_playChannel, ChannelAttribute.Tempo, out currentSpeed);
            return currentSpeed;
        }

        public bool SetTempo(float value)
        {
            //var newSpeed = Math.Min(50000f, this.GetTempo() + 200f);
            //var result = Bass.BASS_ChannelSetAttribute(this._playChannel, BASSAttribute.BASS_ATTRIB_TEMPO_FREQ, newSpeed);

            var newSpeed = Math.Max(-90f, Math.Min(500f, value));
            return Bass.ChannelSetAttribute(_playChannel, ChannelAttribute.Tempo, newSpeed);
        }

        public void StartTemporaryPause(long destinationAfterPause, double pauseMilliseconds, bool restartable = false)
        {
            if (_repeatPauseRemaining > 0)
            {
                // There's already a pause going on.
                // So we'll quit from here until that pause has been completed.
                // TODO: Might want to replace the destination with this new one?
                if (restartable)
                    // We're restartable, though.
                    // So let's reset the remaining pause time.
                    _pauseStart = Environment.TickCount;

                return;
            }

            switch (Bass.ChannelIsActive(_playChannel))
            {
                case PlaybackState.Paused:
                case PlaybackState.Stalled:
                case PlaybackState.Stopped:

                    // If we're not currently playing, then there's no point in starting a pause.
                    return;
            }

            _pauseAborted = false;
            _repeatPauseRemaining = pauseMilliseconds;
            if (_repeatPauseRemaining > 0)
            {
                StopPlaying();
                _pauseStart = Environment.TickCount;
                var pauseTimer = new Timer();
                pauseTimer.Tick += delegate
                {
                    if (_pauseAborted)
                    {
                        _repeatPauseRemaining = 0;
                        _pauseAborted = false;
                        pauseTimer.Stop();
                        pauseTimer.Dispose();
                        return;
                    }

                    var msElapsed = Environment.TickCount - _pauseStart;
                    _repeatPauseRemaining = Math.Max(0, pauseMilliseconds - msElapsed);

                    if (_repeatPauseRemaining <= 0)
                    {
                        _repeatCurrent = destinationAfterPause;
                        StartPlaying(destinationAfterPause);
                        pauseTimer.Stop();
                        pauseTimer.Dispose();
                    }
                    else
                    {
                        Invalidate();
                    }
                };

                pauseTimer.Interval = 100;
                pauseTimer.Start();
            }
            else
            {
                _repeatCurrent = destinationAfterPause;
                SetLocation(destinationAfterPause);
            }

            Invalidate();
        }

        public Stream GetAudioStream()
        {
            return null;
        }

        public SpeechAudioFormatInfo GetSpeechAudioFormat()
        {
            //WaveWriter wv = new WaveWriter("asd.wav", 1, 44100, 16, true);

            var samplesPerSecond = 0;
            var bitsPerSample = 0;
            var channelCount = 0;
            var averageBytesPerSecond = 0;
            var blockAlign = 0;
            var formatSpecificData = new byte[0];

            return new SpeechAudioFormatInfo(EncodingFormat.Pcm, samplesPerSecond, bitsPerSample, channelCount,
                averageBytesPerSecond, blockAlign, formatSpecificData);
        }
    }
}