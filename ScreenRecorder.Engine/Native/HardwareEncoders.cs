using System;

namespace MediaEncoder
{
    /// <summary>
    /// Which hardware video encoders actually work on this PC. "Works" is established by
    /// opening the encoder (not by looking at what the FFmpeg build contains): a driver that is
    /// missing or too old, or a GPU without that codec, simply fails to open. Probing takes a
    /// moment, so <see cref="Detect"/> is meant to run once on a background thread at startup;
    /// the getters run it on demand if nobody did.
    /// </summary>
    public static class HardwareEncoders
    {
        private static readonly object Gate = new object();
        private static readonly HwAccel[] Backends = { HwAccel.Nvenc, HwAccel.Qsv, HwAccel.Amf };
        private static readonly VideoCodec[] Codecs = { VideoCodec.H264, VideoCodec.Hevc, VideoCodec.Av1 };

        // [codec index, backend index]
        private static bool[,] _usable;

        /// <summary>Probes every hardware encoder (again). Slow — call off the UI thread.</summary>
        public static void Detect()
        {
            var usable = new bool[Codecs.Length, Backends.Length];
            for (int c = 0; c < Codecs.Length; c++)
            {
                for (int b = 0; b < Backends.Length; b++)
                {
                    foreach (string name in FFmpegVideoEncoder.ResolveEncoderNames(Codecs[c], Backends[b]))
                    {
                        if (FFmpegHelper.IsEncoderUsable(name))
                        {
                            usable[c, b] = true;
                            break;
                        }
                    }
                }
            }

            lock (Gate)
                _usable = usable;
        }

        /// <summary>True when <paramref name="backend"/> can encode <paramref name="codec"/> here.</summary>
        public static bool IsAvailable(VideoCodec codec, HwAccel backend)
        {
            int c = Array.IndexOf(Codecs, Normalize(codec));
            int b = Array.IndexOf(Backends, backend);
            if (c < 0 || b < 0)
                return false;

            return Table()[c, b];
        }

        /// <summary>True when any hardware encoder can encode <paramref name="codec"/> here.</summary>
        public static bool IsAvailable(VideoCodec codec) => GetBest(codec) != HwAccel.Software;

        /// <summary>
        /// The backend to use for <paramref name="codec"/>: NVENC, then Quick Sync, then AMF,
        /// else <see cref="HwAccel.Software"/>. Quick Sync is skipped for constant-quality
        /// recording (its ICQ mode is unverified).
        /// </summary>
        public static HwAccel GetBest(VideoCodec codec, RateControl rateControl = RateControl.Cbr)
        {
            foreach (HwAccel backend in Backends)
            {
                if (backend == HwAccel.Qsv && rateControl == RateControl.Cq)
                    continue;

                if (IsAvailable(codec, backend))
                    return backend;
            }

            return HwAccel.Software;
        }

        private static VideoCodec Normalize(VideoCodec codec) => codec == VideoCodec.H265 ? VideoCodec.Hevc : codec;

        private static bool[,] Table()
        {
            lock (Gate)
            {
                if (_usable != null)
                    return _usable;
            }

            Detect();
            lock (Gate)
                return _usable;
        }
    }
}
