using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

/*
 * Class for taking samples at regular intervals (possible async return of sample data) and calculating a rolling average
 */ 
namespace Chetch.Utilities
{
    public interface ISampleSubject
    {
        void RequestSample(Sampler sampler);
        bool Enabled { get; }
    }

    public class Sampler
    {
        public enum SamplingOptions
        {
            MEAN_COUNT,
            MEAN_COUNT_PRUNE_MIN_MAX,
            MEAN_INTERVAL,
            MEAN_INTERVAL_PRUNE_MIN_MAX
        }

        public class SubjectData
        {
            public ISampleSubject Subject;
            public int Interval { get; set; } = 0;
            public int IntervalShift { get; set; } = 0; //can be set by sampler to distribute sample requests
            public int IntervalDeviation { get; set; } = -1; //quality control... can reject samples that deviate too far from the ideal 'Interval'
            public int SampleSize { get; set; } = 0;
            
            public List<double> Samples { get; } = new List<double>();
            public List<long> SampleTimes { get; } = new List<long>();
            private DateTime _lastAddSampleAttempt;
            public List<long> SampleIntervals { get; } = new List<long>();
            

            public double SampleTotal { get; internal set; } = 0; //sum of sample values
            public int SampleCount { get; internal set; } = 0;
            public long DurationTotal { get; internal set; } = 0; //in millis
            public double Average { get; internal set; }
            public SamplingOptions Options;
            public Measurement.Unit MeasurementUnit = Measurement.Unit.NONE;

            
            public SubjectData(ISampleSubject subject, int interval, int sampleSize, SamplingOptions samplingOptions, int intervalDeviation)
            {
                Subject = subject;
                Interval = interval;
                SampleSize = sampleSize;
                Options = samplingOptions;
                IntervalDeviation = intervalDeviation;
            }

            public void AddSample(double sample, long interval = -1)
            {
                long nowInMillis = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;

                //What interval of time is this sample over?  Provided in the function call or to be calculated from previous function calls...
                long interval2add;
                if (interval > 0)
                {
                    interval2add = interval;
                }
                else
                {
                    if(Samples.Count == 0)
                    {
                        interval2add = Interval;
                    } else { 
                        long prevSampleTimeInMillis = _lastAddSampleAttempt.Ticks / TimeSpan.TicksPerMillisecond;
                        interval2add = nowInMillis - prevSampleTimeInMillis;
                    }
                }

                //do soe quality control checks
                bool rejectSample = false;

                //First one is if we care about how much the interval deviates from the expected Interval
                if (interval > 0 && IntervalDeviation >= 0 && System.Math.Abs(interval2add - Interval) > IntervalDeviation)
                {
                    rejectSample = true;
                }

                //Now record the latest sample attempt and if rejecting exit function
                _lastAddSampleAttempt = DateTime.Now;
                if (rejectSample)
                {
                    return;
                }

                //by here the sample is good
                Samples.Add(sample);
                SampleTimes.Add(nowInMillis);
                SampleIntervals.Add(interval2add);

                if (Samples.Count > SampleSize)
                {
                    Samples.RemoveAt(0);
                    SampleTimes.RemoveAt(0);
                    SampleIntervals.RemoveAt(0);
                }

                int minIdx = 0;
                int maxIdx = 0;
                double sampleTotal = 0;
                long durationTotal = 0;
                for(int i = 0; i < Samples.Count; i++)
                {
                    double val = Samples[i];
                    if (i > 0)
                    {
                        if (val < Samples[minIdx]) minIdx = i;
                        if (val > Samples[maxIdx]) maxIdx = i;
                    }
                    sampleTotal += val;
                    durationTotal += SampleIntervals[i];
                }
                
                double average = 0;
                int sampleCount = 0;
                switch (Options)
                {
                    case SamplingOptions.MEAN_COUNT:
                        sampleCount = Samples.Count;
                        average = sampleTotal / (double)sampleCount;
                        break;

                    case SamplingOptions.MEAN_COUNT_PRUNE_MIN_MAX:
                        if(Samples.Count > 2)
                        {
                            sampleTotal = (sampleTotal - Samples[minIdx] - Samples[maxIdx]);
                            sampleCount = Samples.Count - 2;
                            average = sampleTotal / (double)(sampleCount);
                        } else
                        {
                            sampleCount = Samples.Count;
                            average = sampleTotal / (double)sampleCount;
                        }
                        break;

                    case SamplingOptions.MEAN_INTERVAL:
                        sampleCount = Samples.Count;
                        average = sampleTotal * (double)Interval / (double)durationTotal;
                        break;

                    case SamplingOptions.MEAN_INTERVAL_PRUNE_MIN_MAX:
                        if (Samples.Count > 2)
                        {
                            sampleTotal = (sampleTotal - Samples[minIdx] - Samples[maxIdx]);
                            sampleCount = Samples.Count - 2;
                            durationTotal = durationTotal - SampleIntervals[minIdx] - SampleIntervals[maxIdx];
                            average = sampleTotal / (double)durationTotal;
                        }
                        else
                        {
                            sampleCount = Samples.Count;
                            average = sampleTotal / (double)durationTotal;
                        }
                        break;
                }

                //now assign values
                Average = average;
                SampleTotal = sampleTotal; 
                SampleCount = sampleCount;
                DurationTotal = durationTotal;

            }
        } //end SubjectData class

        private static object LockRequestSample = new Object();

        public delegate void SampleProvidedHandler(Sampler sampler, ISampleSubject sampleSubject);
        public delegate void SampleErrorHandler(ISampleSubject sampleSubject, Exception e);

        private Dictionary<ISampleSubject, SubjectData> _subjects2data = new Dictionary<ISampleSubject, SubjectData>();
        public event SampleProvidedHandler SampleProvided;
        public event SampleErrorHandler SampleError;

        public int SubjectCount { get { return _subjects2data.Count;  } }
        public bool IsSampling { get; internal set; } = false;
        public bool IsRunning { get; internal set; } = false;

        private System.Timers.Timer _timer;
        public int TimerTicks { get; internal set; } = 0;
        private int _maxTimerInterval = 0;
        public int TimerInterval { get { return _timer == null ? -1 : (int)_timer.Interval;  } }
        public bool DistributeSampleRequests { get; set; } = true; //space out sample requests so they don't all fall on the same 'tick'


        public void Add(ISampleSubject subject, int interval, int sampleSize, SamplingOptions samplingOptions = SamplingOptions.MEAN_COUNT, int intervalDeviation = -1)
        {
           if (!_subjects2data.ContainsKey(subject))
            {
                SubjectData sd = new SubjectData(subject, interval, sampleSize, samplingOptions, intervalDeviation);
                _subjects2data[subject] = sd;
            }
        }

        public void Remove(ISampleSubject subject)
        {
            if(_subjects2data.ContainsKey(subject))
            {
                lock (_subjects2data)
                {
                    _subjects2data.Remove(subject);
                }
            }
        }

        public double ProvideSample(ISampleSubject subject, double sample, long interval = -1)
        {
            if (!_subjects2data.ContainsKey(subject)) return 0;

            SubjectData sd = _subjects2data[subject];
            sd.AddSample(sample, interval);

            SampleProvided?.Invoke(this, subject);

            return sd.Average;
        }

        public List<double> GetSubjectSamples(ISampleSubject subject)
        {
            if (!_subjects2data.ContainsKey(subject)) return null;

            SubjectData sd = _subjects2data[subject];
            return sd.Samples;
        }

        public SubjectData GetSubjectData(ISampleSubject subject)
        {
            if (!_subjects2data.ContainsKey(subject)) return null;

            SubjectData sd = _subjects2data[subject];
            return sd;
        }

        public double GetAverage(ISampleSubject subject)
        {
            return _subjects2data.ContainsKey(subject) ? _subjects2data[subject].Average : 0;
        }

        public double GetSampleTotal(ISampleSubject subject)
        {
            return _subjects2data.ContainsKey(subject) ? _subjects2data[subject].SampleTotal : 0;
        }

        public double GetSampleCount(ISampleSubject subject)
        {
            return _subjects2data.ContainsKey(subject) ? _subjects2data[subject].SampleCount : 0;
        }

        public long GetDurationTotal(ISampleSubject subject)
        {
            return _subjects2data.ContainsKey(subject) ? _subjects2data[subject].DurationTotal : 0;
        }
        public void Start()
        {
            if (_subjects2data.Count == 0) return;
            if (_timer != null)
            {
                throw new Exception("Cannot start sampling as a timer already exists");
            }

            _timer = new System.Timers.Timer();

            List<int> intervals = new List<int>();
            foreach (var sd in _subjects2data.Values)
            {
                if(!intervals.Contains(sd.Interval))intervals.Add(sd.Interval);
            }
            int timerInterval = Math.GCD(intervals.ToArray());

            if (DistributeSampleRequests && _subjects2data.Values.Count > 1)
            {
                //we need the smallest divisor for the time interval >= the number of subjects
                int divisor = _subjects2data.Values.Count;
                while(timerInterval % divisor != 0)
                {
                    if ((timerInterval / divisor) < 20) throw new Exception("Cannot distribute sample subjects as it will result in a timer interval of less than 20.");
                    divisor++;
                }
                timerInterval = timerInterval / divisor;
                
                //now we have a fine-grained enough timer so we can shift all the subjects so that never more than one subject is called per timer tick
                int i = 0;
                foreach(var sd in _subjects2data.Values)
                {
                    sd.IntervalShift = i * timerInterval;
                    i++;
                }
            }

            _timer.Interval = timerInterval;
            _timer.Elapsed += OnTimer;
            _maxTimerInterval = Math.LCM(intervals.ToArray());
            _timer.Start();

            IsRunning = true;
        }

        public void Stop()
        {
            IsRunning = false;
            if (_timer != null)
            {
                _timer.Stop();
                while (IsSampling)
                {
                    System.Threading.Thread.Sleep(250);
                }
                _timer.Dispose();
                _timer = null;
            }

        }

        void OnTimer(Object sender, System.Timers.ElapsedEventArgs eventArgs)
        {
            if (sender is System.Timers.Timer)
            {
                _timer.Stop();
                lock (_subjects2data)
                {
                    IsSampling = true;
                    TimerTicks++;
                    int interval = (int)((System.Timers.Timer)sender).Interval;
                    int timerInterval = TimerTicks * interval;
                    foreach (var sd in _subjects2data.Values)
                    {
                        if (timerInterval % sd.Interval == sd.IntervalShift && sd.Subject.Enabled)
                        {
                            try
                            {
                                sd.Subject.RequestSample(this);
                            }
                            catch (Exception e)
                            {
                                SampleError?.Invoke(sd.Subject, e);
                            }
                        }
                    }

                    if (timerInterval % _maxTimerInterval == 0)
                    {
                        TimerTicks = 0;
                    }

                    IsSampling = false;
                }
                _timer.Start();
            }
        }
    } //end class
}
