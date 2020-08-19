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

        class SubjectData
        {
            public ISampleSubject Subject;
            public int Interval = 0;
            public int SampleSize = 0;
            List<double> Samples = new List<double>();
            public double Average { get; internal set; }
            public Measurement.Unit MeasurementUnit = Measurement.Unit.NONE;

            public SubjectData(ISampleSubject subject, int interval, int sampleSize, Measurement.Unit measurementUnit)
            {
                Subject = subject;
                Interval = interval;
                SampleSize = sampleSize;
                MeasurementUnit = measurementUnit;
            }

            public void AddSample(double sample)
            {
                Samples.Add(sample);

                if (Samples.Count > SampleSize)
                {
                    Average = Average + (sample - Samples[0]) / (double)SampleSize;
                    Samples.RemoveAt(0);
                }
                else
                {
                    Average = ((Average * (double)(Samples.Count - 1)) + sample) / (double)Samples.Count;
                }
            }
        } //end SubjectData class


        private Dictionary<int, List<SubjectData>> _subjects = new Dictionary<int, List<SubjectData>>();
        private List<System.Timers.Timer> _timers = new List<System.Timers.Timer>();
        private Dictionary<ISampleSubject, SubjectData> _subjects2data = new Dictionary<ISampleSubject, SubjectData>();

        public void Add(ISampleSubject subject, int interval, int sampleSize, Measurement.Unit measurementUnit = Measurement.Unit.NONE)
        {
            if (!_subjects.ContainsKey(interval))
            {
                _subjects[interval] = new List<SubjectData>();
            }

            if (!_subjects2data.ContainsKey(subject))
            {
                SubjectData sd = new SubjectData(subject, interval, sampleSize, measurementUnit);
                _subjects[interval].Add(sd);
                _subjects2data[subject] = sd;
            }
        }

        public void Remove(ISampleSubject subject)
        {
            foreach (List<SubjectData> subjects in _subjects.Values)
            {
                foreach (SubjectData sd in subjects)
                {
                    if (sd.Subject == subject)
                    {
                        subjects.Remove(sd);
                        _subjects2data.Remove(subject);
                    }
                }
            }
        }

        public double ProvideSample(ISampleSubject subject, double sample)
        {
            if (!_subjects2data.ContainsKey(subject)) return 0;

            SubjectData sd = _subjects2data[subject];
            sd.AddSample(sample);
            return sd.Average;
        }

        public double GetAverage(ISampleSubject subject)
        {
            return _subjects2data.ContainsKey(subject) ? _subjects2data[subject].Average : 0;
        }

        public void Start()
        {
            foreach (int interval in _subjects.Keys)
            {
                var timer = new System.Timers.Timer();
                timer.Interval = interval;
                timer.Elapsed += OnTimer;
                _timers.Add(timer);
                timer.Start();
            }
        }

        public void Stop()
        {
            foreach (var timer in _timers)
            {
                timer.Stop();
            }
        }

        void OnTimer(Object sender, System.Timers.ElapsedEventArgs eventArgs)
        {
            if (sender is System.Timers.Timer)
            {
                int interval = (int)((System.Timers.Timer)sender).Interval;
                var subjects = _subjects[interval];
                foreach (SubjectData sd in subjects)
                {
                    sd.Subject.RequestSample(this);
                }
            }
        }
    }
}
