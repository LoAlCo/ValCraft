using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace ValCraft
{
    // Where ValCraft's main-thread time goes (Diagnostics = true): total, worst frame and count per
    // part, logged every 10 s, plus every frame over SpikeMs with its parts.
    public static class Prof
    {
        const double SpikeMs = 12.0;
        static readonly Dictionary<string, (double total, double max, int count)> _parts = new Dictionary<string, (double, double, int)>();
        static readonly Dictionary<string, double> _frame = new Dictionary<string, double>();
        static readonly Stopwatch _clock = Stopwatch.StartNew();
        static double _windowStart;
        static int _frames, _spikesLogged, _slowFrames;
        static double _wholeTotal, _wholeMax;

        public static bool On => Plugin.Diagnostics != null && Plugin.Diagnostics.Value;

        public static long Start() => On ? Stopwatch.GetTimestamp() : 0;

        public static void Stop(string part, long start)
        {
            if (start == 0) return;
            double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            _frame.TryGetValue(part, out double f);
            _frame[part] = f + ms;
        }

        public static void Add(string part, double ms)
        {
            if (!On) return;
            _frame.TryGetValue(part, out double f);
            _frame[part] = f + ms;
        }

        // End of ValCraft's frame.
        public static void EndFrame()
        {
            if (!On) return;
            _frames++;
            double frameTotal = 0;
            foreach (var kv in _frame)
            {
                _parts.TryGetValue(kv.Key, out var p);
                _parts[kv.Key] = (p.total + kv.Value, System.Math.Max(p.max, kv.Value), p.count + 1);
                if (!kv.Key.Contains("/")) frameTotal += kv.Value;  // "a/b" parts are inside "a"
            }
            double whole = UnityEngine.Time.unscaledDeltaTime * 1000.0;
            _wholeMax = System.Math.Max(_wholeMax, whole);
            _wholeTotal += whole;
            if (whole > 40.0) _slowFrames++;
            if ((frameTotal > SpikeMs || whole > 50.0) && _spikesLogged < 40)
            {
                _spikesLogged++;
                var sb = new StringBuilder($"prof spike: Valheim frame {whole:F0} ms, ValCraft {frameTotal:F1} ms:");
                foreach (var kv in _frame) if (kv.Value > 0.3) sb.Append($" {kv.Key}={kv.Value:F1}");
                Plugin.Log(sb.ToString());
            }
            _frame.Clear();
            double now = _clock.Elapsed.TotalSeconds;
            if (now - _windowStart < 10.0) return;
            var line = new StringBuilder($"prof {_frames} frames, Valheim frame avg {_wholeTotal / _frames:F1} ms max {_wholeMax:F0} ms, {_slowFrames} over 40 ms; ValCraft:");
            _wholeTotal = _wholeMax = 0;
            _slowFrames = 0;
            foreach (var kv in _parts)
                line.Append($" {kv.Key} avg {kv.Value.total / _frames:F2} max {kv.Value.max:F1} ({kv.Value.count}x);");
            Plugin.Log(line.ToString());
            _parts.Clear();
            _frames = 0;
            _spikesLogged = 0;
            _windowStart = now;
        }
    }
}
