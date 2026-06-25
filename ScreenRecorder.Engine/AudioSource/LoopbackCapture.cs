using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace ScreenRecorder.AudioSource
{
    /// <summary>
    /// System-audio (loopback) capture off the render endpoint. Uses an event-synced
    /// WasapiCapture with the Loopback stream flag (NAudio's stock WasapiLoopbackCapture forces
    /// eventSync=false; this keeps low-latency event callbacks AND loopback together).
    /// </summary>
    internal sealed class LoopbackCapture : WasapiCaptureBase
    {
        public LoopbackCapture(string deviceId = null)
            : base(DataFlow.Render, useDefaultDevice: deviceId == null, deviceId)
        {
        }

        protected override WasapiCapture CreateCapture(MMDevice device) => new EventSyncLoopbackCapture(device);

        private sealed class EventSyncLoopbackCapture : WasapiCapture
        {
            public EventSyncLoopbackCapture(MMDevice device) : base(device, useEventSync: true) { }

            protected override AudioClientStreamFlags GetAudioClientStreamFlags()
                => AudioClientStreamFlags.Loopback | base.GetAudioClientStreamFlags();
        }
    }
}
