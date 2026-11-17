using System.IO;
using MediaEncoder;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScreenRecorder.Encoder;

namespace ScreenRecorder.Tests.Support
{
    /// <summary>
    /// Common fixture for recorder integration tests: requires FFmpeg, gives every test its own
    /// temp directory (deleted afterwards) and builds small software-encoder stream parameters.
    /// </summary>
    public abstract class RecorderTestBase
    {
        protected const int Width = 320;
        protected const int Height = 240;

        protected string TempDir { get; private set; }

        public TestContext TestContext { get; set; }

        [TestInitialize]
        public void SetUpRecorderTest()
        {
            FFmpegTestEnvironment.RequireFFmpeg();
            TempDir = TestFiles.CreateTempDirectory();
        }

        [TestCleanup]
        public void TearDownRecorderTest()
        {
            TestFiles.DeleteDirectory(TempDir);
        }

        protected string PathFor(string fileName) => Path.Combine(TempDir, fileName);

        protected static VideoParams SoftwareVideo(int fps, bool variableFrameRate = false, VideoCodec codec = VideoCodec.H264,
            int fpsDenominator = 1, int width = Width, int height = Height, int bitrate = 1_000_000)
        {
            return new VideoParams
            {
                Codec = codec,
                Hw = HwAccel.Software,
                Width = width,
                Height = height,
                FpsNumerator = fps,
                FpsDenominator = fpsDenominator,
                Bitrate = bitrate,
                RateControl = RateControl.Cbr,
                VariableFrameRate = variableFrameRate,
            };
        }

        protected static AudioParams StereoAudio(AudioCodec codec = AudioCodec.Aac, SampleFormat format = SampleFormat.S16)
        {
            return new AudioParams
            {
                Codec = codec,
                SampleRate = 48000,
                Channels = 2,
                Bitrate = 160_000,
                Format = format,
            };
        }

        /// <summary>Opens the file with libavformat and fails the test when that is not possible.</summary>
        internal static MediaProbe ProbeFile(string path)
        {
            Assert.IsTrue(File.Exists(path), $"Output file '{path}' does not exist.");
            MediaProbe probe = MediaProbe.Probe(path);
            Assert.IsTrue(probe.Opened, $"libavformat could not open '{path}'.");
            return probe;
        }
    }
}
