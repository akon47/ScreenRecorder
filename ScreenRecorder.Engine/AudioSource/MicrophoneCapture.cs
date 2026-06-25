using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace ScreenRecorder.AudioSource
{
    /// <summary>Microphone capture off the default (or named) capture endpoint, event-synced.</summary>
    internal sealed class MicrophoneCapture : WasapiCaptureBase
    {
        public MicrophoneCapture(string deviceId = null)
            : base(DataFlow.Capture, useDefaultDevice: deviceId == null, deviceId)
        {
        }

        protected override WasapiCapture CreateCapture(MMDevice device) => new WasapiCapture(device, useEventSync: true);
    }
}
