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
            public bool CanRequest { get; set; } = true;
            public int Interval { get; set; } = 0;
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
                if (IntervalDeviation >= 0 && System.Math.Abs(interval2add - Interval) > IntervalDeviation)
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

        public delegate void SampleProvidedHandler(ISampleSubject sampleSubject);
        public delegate void SampleErrorHandler(ISampleSubject sampleSubject, Exception e);

        private Dictionary<ISampleSubject, SubjectData> _subjects2data = new Dictionary<ISampleSubject, SubjectData>();
        public event SampleProvidedHandler SampleProvided;
        public event SampleErrorHandler SampleError;

        private System.Timers.Timer _timer;
        private int _timerCount = 0;
        private int _maxTimerInterval = 0;

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
                _subjects2data.Remove(subject);
            }
        }

        public double ProvideSample(ISampleSubject subject, double sample, long interval = -1)
        {
            if (!_subjects2data.ContainsKey(subject)) return 0;

            SubjectData sd = _subjects2data[subject];
            sd.AddSample(sample, interval);

            SampleProvided?.Invoke(subject);

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
            _timer = new System.Timers.Timer();
            List<int> intervals = new List<int>();
            foreach(var sd in _subjects2data.Values)
            {
                intervals.Add(sd.Interval);
            }
            _timer.Interval = Math.GCD(intervals.ToArray());
            _timer.Elapsed += OnTimer;
            _timer.Start();
            _maxTimerInterval = Math.LCM(intervals.ToArray());
        }

        public void Stop()
        {
            if(_timer != null)_timer.Stop();
        }

        void OnTimer(Object sender, System.Timers.ElapsedEventArgs eventArgs)
        {
            if (sender is System.Timers.Timer)
            {
                _timerCount++;
                int interval = (int)((System.Timers.Timer)sender).Interval;
                int timerInterval = _timerCount * interval;
                foreach(var sd in _subjects2data.Values)
                {
                    if (timerInterval % sd.Interval == 0 && sd.CanRequest)
                    {
                        try
                        {
                            lock (LockRequestSample)
                            {
                                sd.Subject.RequestSample(this);
                            }
                        } catch (Exception e)
                        {
                            sd.CanRequest = false;
                            SampleError?.Invoke(sd.Subject, e);
                        }
                    }
                }

                if (timerInterval % _maxTimerInterval == 0)
                {
                    _timerCount = 0;
                }
            }
        }
    } //end class
}
