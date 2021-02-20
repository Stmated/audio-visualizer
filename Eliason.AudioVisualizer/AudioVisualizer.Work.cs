using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ManagedBass;
using ManagedBass.Mix;

//using Un4seen.Bass;
//using Un4seen.Bass.AddOn.Mix;
//using Un4seen.Bass.Misc;

namespace Eliason.AudioVisualizer
{
    public partial class AudioVisualizer
    {
        private static int staticWorkConsumerThreadInstanceCounter = 0;

        private readonly Queue<Work> _workQueue = new Queue<Work>();
        private readonly List<Work> _work = new List<Work>();

        private readonly SyncEvents _syncEvents = new SyncEvents();

        private readonly Object _workSyncLock = new Object();

        public void QueueWork(Work work)
        {
            // If there is already work being done for that area, then it should be merged.
            // If there is a done work for that area, then it should be added to that.

            // TODO: Den här behöver gå igenom all existerande och inte köa om redan finns för det området
            // TODO: Ta bort kö. Använd samma lista, men sätt Bitmap när klar
            // TODO: Hitta delar av en start -> end som har hål i sig, och skapa separata Work för det
            // TODO: Ha någon form av Timer som lägger ihop flera sammankopplade Work varannan X sekunder (spara lite overhead-minne och CPU för listor)
            // TODO: Ha någon form av Timer som tar bort Work när de har försvunnit en bra bit bort i historiken
            // TODO: Rita ut ett riktigt jävla enkelt histogram först, och byt sedan ut mot spectrogrammet
            // TODO: Volymkontroll (ctrl+mwheel)

            lock (((ICollection) this._workQueue).SyncRoot)
            {
                this._workQueue.Enqueue(work);
                this._syncEvents.NewItemEvent.Set();
            }
        }

        public void ClearWork()
        {
            lock (((ICollection) this._workQueue).SyncRoot)
            {
                this._workQueue.Clear();
            }

            lock (this._workSyncLock)
            {
                this._work.Clear();
            }
        }

        private const int COLOR_CACHE_PRECISION = 0;

        private void StartConsumerThreads()
        {
            // Start 2 threads for consuming work

            var stepsPerHue = (int) Math.Pow(10, COLOR_CACHE_PRECISION);
            var hue = 360 * 0.70;
            var steps = (int) Math.Floor(hue * stepsPerHue);
            this._cachedColors = new Color[steps];
            var step = 1d / stepsPerHue;
            for (var i = 0; i < steps; i++)
            {
                var color = ColorFromHSV(hue, 0.8, 0.8);
                this._cachedColors[i] = color;
                hue -= step;
            }

            for (int i = 0; i < 2; i++)
            {
                var thread = new Thread(this.WorkConsumerThread)
                {
                    IsBackground = true
                };
                thread.Start();
            }
        }

        private void WorkConsumerThread()
        {
            Thread.CurrentThread.Name = "AV-Cons-Thread-" + (staticWorkConsumerThreadInstanceCounter++);
            var visualChannel = 0;
            try
            {
                while (WaitHandle.WaitAny(this._syncEvents.EventArray) != 1)
                {
                    if (visualChannel == 0)
                    {
                        visualChannel = this.CreateNewChannel();
                    }

                    Work work;
                    lock (((ICollection) this._workQueue).SyncRoot)
                    {
                        if (this._workQueue.Count == 0)
                        {
                            // We've already been dequeued by another thread or something.
                            continue;
                        }

                        work = this._workQueue.Dequeue();
                    }

                    if (visualChannel == 0)
                    {
                        // Could not create the visual channel.
                        // Most likely the underlying file is corrupt, or not loaded.
                        // We dequeue the received work, so the queue does not grow endlessly.
                        // But we do nothing with the work.
                        continue;
                    }

                    if (String.IsNullOrEmpty(this._currentFilePath))
                    {
                        continue;
                    }

                    var clientRectangle = Rectangle.Empty;
                    Invoke(new Action(() => { clientRectangle = this.ClientRectangle; }));

                    if (this.DoWork(this, visualChannel, work, clientRectangle) > 0)
                    {
                        // Call the AudoVisualizer to invalidate its rendering!
                        BeginInvoke(new Action(Invalidate));
                    }
                }
            }
            finally
            {
                if (visualChannel != 0)
                {
                    Bass.ChannelStop(visualChannel);
                    Bass.StreamFree(visualChannel);
                }
            }
        }

        private bool HasPotentialHumanSpeech(int visualChannel, long pos)
        {
            Bass.ChannelSetPosition(visualChannel, pos);

            const int scaleFactor = _scaleFactorSqr * ushort.MaxValue;
            var buffer = new float[2048];
            var bufferResult = this._isMixerUsed
                ? BassMix.ChannelGetData(visualChannel, buffer, (int) this._maxFft)
                : Bass.ChannelGetData(visualChannel, buffer, (int) this._maxFft);

            int verticalSamplesWithSound = 0;
            if (bufferResult > 0)
            {
                for (int n = 0; n < buffer.Length; n++)
                {
                    // TODO: Don't convert to percentage, just check the threshold raw from the buffer
                    // TODO: Skip parts of the spectrogram that cannot be human speech
                    float percentage = (float) Math.Min(1d, (Math.Sqrt(buffer[n]) * scaleFactor) / ushort.MaxValue);
                    if (percentage > 0.35)
                    {
                        verticalSamplesWithSound++;
                        if (verticalSamplesWithSound > 4)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        public long GetPositionOfClosestSilence(long pos)
        {
            var buffer = new float[2048];

            int visualChannel = 0;
            try
            {
                long originalPos = pos;
                visualChannel = this.CreateNewChannel();

                // 10 millisecond increments
                long increment = Bass.ChannelSeconds2Bytes(visualChannel, 0.01);

                // And we increment enough to check for 1/5th of a second in each direction
                int numberOfSteps = 20;

                var currentlyOnSpeechCounter =
                    (this.HasPotentialHumanSpeech(visualChannel, pos - increment) ? 1 : 0)
                    + (this.HasPotentialHumanSpeech(visualChannel, pos) ? 1 : 0)
                    + (this.HasPotentialHumanSpeech(visualChannel, pos + increment) ? 1 : 0);
                var currentlyOnSpeech = (currentlyOnSpeechCounter >= 2);

                // TODO: If on speech, then check if is close to forward-search NOT SPEECH -> Speech within timeframe.
                //       Then it's probably the start of a new quick sentence and the next subtitle should start there.

                long lastNonHit = pos;
                int consequtiveHits = 0;
                if (currentlyOnSpeech)
                {
                    // If we're currently on speech, then search backwards for a moment without speech.
                    for (int i = 0; i < numberOfSteps; i++)
                    {
                        long currentPos = pos - (i * increment);
                        if (this.HasPotentialHumanSpeech(visualChannel, currentPos) == false && ++consequtiveHits == 3)
                            return lastNonHit;
                        else lastNonHit = currentPos;
                    }
                }
                else
                {
                    // If we're currently NOT on speech, then search forwards for a moment with speech.
                    for (int i = 0; i < numberOfSteps; i++)
                    {
                        long currentPos = pos + (i * increment);
                        if (this.HasPotentialHumanSpeech(visualChannel, currentPos) == true && ++consequtiveHits == 3)
                            return lastNonHit;
                        else lastNonHit = currentPos;
                    }
                }

                return pos;
            }
            finally
            {
                if (visualChannel != 0)
                {
                    Bass.ChannelStop(visualChannel);
                    Bass.StreamFree(visualChannel);
                }
            }
        }

        private int DoWork(AudioVisualizer audioVisualizer, int visualChannel, Work queued, Rectangle clientRectangle)
        {
            int changes = 0;
            var todoWorkList = this.SplitAndMerge(queued);

            var buffer = new float[2048]; // Move back inside loop if behavior is odd
            foreach (var work in todoWorkList)
            {
                try
                {
                    // TODO: Replace all this code with custom BASS_ChannelGetData instead, and paint a heatmap rather than a 3DVoicePrint
                    this._maxFrequencySpectrum = FFTFrequency2Index(audioVisualizer.GetFrequencyRange(), 4096, 44100);

                    var clientXFrom = audioVisualizer.ByteIndexToClientX(work.From);
                    var clientXTo = audioVisualizer.ByteIndexToClientX(work.To);
                    var clientWidth = (int) (clientXTo - clientXFrom);
                    if (clientWidth <= 0)
                    {
                        continue;
                    }

                    var bitmapWidth = clientWidth;
                    var bitmapHeight = clientRectangle.Height;
                    var bitmap = new DirectBitmap(bitmapWidth, bitmapHeight);
                    work.Bitmap = bitmap;
                    changes++;

                    var clipRectangle = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
                    if (visualChannel == 0 || clipRectangle.Width <= 1 || clipRectangle.Height <= 1)
                    {
                        continue;
                    }

                    var bytesPerPixel = (double) (work.To - work.From) / bitmap.Width;

                    var verticalSamplesPerPixel = (double) this._maxFrequencySpectrum / clipRectangle.Height;
                    var verticalSampleCount = this._maxFrequencySpectrum + 1;

                    const int scaleFactor = _scaleFactorSqr * ushort.MaxValue;

                    for (var pos = 0; pos < clipRectangle.Width; pos++)
                    {
                        Bass.ChannelSetPosition(visualChannel, (long) Math.Floor(work.From + (pos * bytesPerPixel)));

                        var bufferResult = this._isMixerUsed
                            ? BassMix.ChannelGetData(visualChannel, buffer, (int) this._maxFft)
                            : Bass.ChannelGetData(visualChannel, buffer, (int) this._maxFft);

                        var y1 = 0;
                        var highest = 0f;

                        for (var index = 1; index < verticalSampleCount; ++index)
                        {
                            if (highest < buffer[index])
                            {
                                // By only printing the pixel if we've passed the current pixel's frequencies,
                                // and keeping the highest frequency of the pixel, we get a better representation.
                                highest = Math.Max(0, buffer[index]);
                            }

                            // TODO: Should not need to divide each time, should be able to increment on each loop.
                            var currentY = (int) (Math.Round(index / verticalSamplesPerPixel) - 1);
                            if (currentY > y1)
                            {
                                var dbIndex = bitmap.GetStartOffset(pos, y1);

                                // From near-purple blue (0.70) until red (0.0) in HSV hue wheel.
                                var percentage = Math.Min(1d, (Math.Sqrt(highest) * scaleFactor) / ushort.MaxValue);

                                var cacheIndex = (int) Math.Floor((this._cachedColors.Length - 1) * percentage);
                                var currentColor = this._cachedColors[cacheIndex];

                                bitmap.Bits[dbIndex + 0] = currentColor.B; // B
                                bitmap.Bits[dbIndex + 1] = currentColor.G; // G
                                bitmap.Bits[dbIndex + 2] = currentColor.R; // R
                                bitmap.Bits[dbIndex + 3] = 255; // A

                                y1 = currentY;
                                highest = 0;
                            }
                        }

                        if (pos < clipRectangle.Width - 1)
                        {
                            for (int y = 0; y < clipRectangle.Height; y++)
                            {
                                var dbIndex = bitmap.GetStartOffset(pos + 1, y);
                                bitmap.Bits[dbIndex + 0] = 0;
                                bitmap.Bits[dbIndex + 1] = 0;
                                bitmap.Bits[dbIndex + 2] = 0;
                                bitmap.Bits[dbIndex + 3] = 255;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }

            return changes;
        }

        private static int FFTFrequency2Index(int frequency, int length, int sampleRate)
        {
            var bin = (int) Math.Round((double) length * frequency / sampleRate);
            var lengthMidpoint = length / 2 - 1;
            return bin > lengthMidpoint ? lengthMidpoint : bin;
        }

        private IEnumerable<Work> SplitAndMerge(Work queued)
        {
            var todoWorkList = new List<Work>();
            lock (this._workSyncLock)
            {
                // Let's go through all the existing works, and find the gaps that should be added.
                // This might result in a smaller work, several split segments, or of the originally queued size.
                //long startFrom = -1;
                int workIndex = 0;
                for (; workIndex < this._work.Count; workIndex++)
                {
                    var existing = this._work[workIndex];
                    if (existing.IsLaterThan(queued))
                    {
                        // We're no longer in the range of the queued work. Let's abort.
                        this._work.Insert(workIndex, queued);
                        todoWorkList.Add(queued);
                        break;
                    }

                    if (existing.IsOverlapping(queued))
                    {
                        // We've encountered our first overlapping, existing work.
                        // We should start keeping track of the ranges and fill the gaps.
                        var existingStartingBefore = existing.IsStartingBefore(queued);
                        var existingEndingAfter = existing.IsEndingAfter(queued);
                        if (existingStartingBefore)
                        {
                            // The existing starts before the queued.
                            // So the minimum From should be the To of the existing.
                            queued.From = existing.To;
                        }

                        if (existingEndingAfter)
                        {
                            // The existing ends after the queued.
                            // So the minimum To should be the From of the existing.
                            queued.To = existing.From;

                            var distance = queued.To - queued.From;
                            if (distance > 0)
                            {
                                var newWork = new Work(queued.From, queued.To);
                                this._work.Insert(workIndex, newWork);
                                todoWorkList.Add(newWork);
                            }
                        }

                        if (existingStartingBefore == false && existingEndingAfter == false)
                        {
                            // The existing is smack in the middle of the queued.
                            // So we should split the queued into two parts; one that is added to work queue,
                            // and the other which will replace the From and To of the currently queued, 
                            // to be handled by subsequent existing works.
                            var preWork = new Work(queued.From, existing.From);
                            if (preWork.To - preWork.From > 0)
                            {
                                this._work.Insert(workIndex, preWork);
                                todoWorkList.Add(preWork);
                            }

                            var postWork = new Work(existing.To, queued.To);
                            queued = postWork;
                        }
                    }
                    else if (queued.IsEarlierThan(existing))
                    {
                        // The queued is earlier than the next existing work.
                        // So we can safely just add this one and break out.
                        this._work.Insert(workIndex, queued);
                        todoWorkList.Add(queued);
                        break;
                    }
                }

                if (queued.To - queued.From > 0)
                {
                    // There's still some left of what was originally queued.
                    // Let's add it to the work queue.
                    this._work.Insert(workIndex, queued);
                    todoWorkList.Add(queued);
                }
            }

            return todoWorkList;
        }

        public void GenerateSpeechDiarization()
        {
            var filePath = this.CurrentFilePath;
        }

        private bool _isMixerUsed;
        private const int _scaleFactorSqr = 4;
        private const int _scaleFactorLinear = 9;
        private int _maxHz = 4096;
        private int _maxFftSampleIndex = 2047;
        private int _maxFrequencySpectrum = 2047;
        private Color[] _cachedColors;

        private DataFlags _maxFft = DataFlags.FFT4096; // BASSData.BASS_DATA_FFT4096;

        private void SetMaxFFT(DataFlags value)
        {
            switch (value)
            {
                case DataFlags.FFT512:
                    this._maxHz = 1024;
                    this._maxFft = value;
                    this._maxFftSampleIndex = byte.MaxValue;
                    break;
                case DataFlags.FFT1024:
                    this._maxHz = 1024;
                    this._maxFft = value;
                    this._maxFftSampleIndex = 511;
                    break;
                case DataFlags.FFT2048:
                    this._maxHz = 2048;
                    this._maxFft = value;
                    this._maxFftSampleIndex = 1023;
                    break;
                case DataFlags.FFT4096:
                    this._maxHz = 4096;
                    this._maxFft = value;
                    this._maxFftSampleIndex = 2047;
                    break;
                case DataFlags.FFT8192:
                    this._maxHz = 8192;
                    this._maxFft = value;
                    this._maxFftSampleIndex = 4095;
                    break;
                default:
                    this._maxHz = 4096;
                    this._maxFft = DataFlags.FFT4096;
                    this._maxFftSampleIndex = 2047;
                    break;
            }

            if (this._maxFrequencySpectrum <= this._maxFftSampleIndex)
            {
                return;
            }

            this._maxFrequencySpectrum = this._maxFftSampleIndex;
        }

        /*
        public void SetMaxFrequencySpectrum(int value)
        {
            this._maxFrequencySpectrum = Math.Max(1, Math.Min(this._maxFftSampleIndex, value));
        }
        */

        public static Color ColorFromHSV(double hue, double saturation, double value)
        {
            var hi = Convert.ToInt32(Math.Floor(hue / 60)) % 6;
            var f = hue / 60 - Math.Floor(hue / 60);

            value = value * 255;
            var v = Convert.ToInt32(value);
            var p = Convert.ToInt32(value * (1 - saturation));
            var q = Convert.ToInt32(value * (1 - f * saturation));
            var t = Convert.ToInt32(value * (1 - (1 - f) * saturation));

            if (hi == 0) return Color.FromArgb(255, v, t, p);
            if (hi == 1) return Color.FromArgb(255, q, v, p);
            if (hi == 2) return Color.FromArgb(255, p, v, t);
            if (hi == 3) return Color.FromArgb(255, p, q, v);
            if (hi == 4) return Color.FromArgb(255, t, p, v);

            return Color.FromArgb(255, v, p, q);
        }
    }
}