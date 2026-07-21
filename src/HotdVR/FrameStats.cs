using System;

namespace HotdVR
{
    /// <summary>
    /// Zero-allocation frame-time accumulator: a ring buffer of per-frame
    /// millisecond samples emitted as percentiles on the periodic state log.
    /// Suspended frames (loading screen / grace period) are counted but
    /// excluded from the percentiles - load hitches are 100-1000ms outliers
    /// that would otherwise drown the gameplay numbers the perf checkpoint
    /// needs; the suspendedFrames count still shows a load happened inside
    /// the window.
    /// </summary>
    internal sealed class FrameStats
    {
        private const int Capacity = 2048; // >15s at 130fps between emissions

        private readonly float[] samples = new float[Capacity];
        private readonly float[] scratch = new float[Capacity];
        private int head;      // next write index (wraps)
        private int count;     // valid samples since last emit (capped)
        private int suspended; // suspended-frame count since last emit
        private float max;     // true max since last emit (survives wrap)

        public void Add(float ms, bool suspendedFrame)
        {
            if (suspendedFrame)
            {
                suspended++;
                return;
            }
            samples[head] = ms;
            head = (head + 1) % Capacity;
            if (count < Capacity)
                count++;
            if (ms > max)
                max = ms;
        }

        /// <summary>Builds the stats line and resets the window. The string
        /// allocation is fine - this only runs on the periodic log cadence.</summary>
        public string EmitAndReset(string label)
        {
            string line;
            if (count == 0)
            {
                line = $"{label}: n=0 suspendedFrames={suspended}";
            }
            else
            {
                Array.Copy(samples, scratch, count);
                Array.Sort(scratch, 0, count); // ring order is irrelevant for percentiles
                float p50 = Percentile(0.50f), p95 = Percentile(0.95f), p99 = Percentile(0.99f);
                float fps = p50 > 0f ? 1000f / p50 : 0f;
                line = $"{label}: n={count} p50={p50:F1}ms p95={p95:F1}ms p99={p99:F1}ms max={max:F1}ms (~{fps:F0} fps) suspendedFrames={suspended}";
            }
            head = 0;
            count = 0;
            suspended = 0;
            max = 0f;
            return line;
        }

        private float Percentile(float p)
        {
            int idx = (int)Math.Ceiling(p * count) - 1;
            if (idx < 0) idx = 0;
            if (idx >= count) idx = count - 1;
            return scratch[idx];
        }
    }
}
