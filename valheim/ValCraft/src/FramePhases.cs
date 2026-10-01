using System.Diagnostics;
using UnityEngine;

namespace ValCraft
{
    // Diagnostics: where a whole Unity frame goes. Unity runs (fixed updates + physics)*, updates,
    // late updates, then renders: stamps at the start of the frame, before the first Update and after
    // the last LateUpdate split it into physics, scripts (Valheim and every mod) and rendering.
    [DefaultExecutionOrder(-32000)]
    class PhaseEarly : MonoBehaviour
    {
        public static long FrameStart, LateEnd;
        static int _fixedFrame = -1;

        void FixedUpdate()
        {
            if (_fixedFrame == Time.frameCount) return;
            _fixedFrame = Time.frameCount;
            BeginFrame();
        }

        void Update()
        {
            if (_fixedFrame != Time.frameCount) BeginFrame();
            long now = Stopwatch.GetTimestamp();
            Prof.Add("phase/physics", Ms(FrameStart, now));
            _updateStart = now;
        }

        static long _updateStart;
        public static long UpdateStart => _updateStart;

        static void BeginFrame()
        {
            long now = Stopwatch.GetTimestamp();
            if (LateEnd != 0) Prof.Add("phase/render", Ms(LateEnd, now));  // the previous frame's rendering
            FrameStart = now;
        }

        public static double Ms(long a, long b) => (b - a) * 1000.0 / Stopwatch.Frequency;
    }

    [DefaultExecutionOrder(32000)]
    class PhaseLate : MonoBehaviour
    {
        void LateUpdate()
        {
            long now = Stopwatch.GetTimestamp();
            Prof.Add("phase/scripts", PhaseEarly.Ms(PhaseEarly.UpdateStart, now));
            PhaseEarly.LateEnd = now;
            Prof.EndFrame();  // render time arrives with the next frame (it's measured once it's over)
        }
    }
}
