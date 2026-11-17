namespace ScreenRecorder.VideoSource
{
    /// <summary>What the capture source should do on one grid tick.</summary>
    internal enum FrameAction
    {
        /// <summary>Emit nothing for this grid index.</summary>
        None,

        /// <summary>Convert and encode the newly captured content.</summary>
        EncodeNew,

        /// <summary>Re-send the previously encoded picture with this grid index as its PTS.</summary>
        RepeatLast,
    }

    /// <summary>
    /// Decides, per grid tick, whether a frame is encoded. The grid itself (QPC ideal grid,
    /// PTS = grid index in a 1/fps time base) is identical in both modes — only the emission
    /// differs:
    /// <list type="bullet">
    /// <item><b>Constant frame rate</b>: exactly one frame per grid index. Unchanged content and
    /// indices skipped by a late wake-up are filled by repeating the last picture.</item>
    /// <item><b>Variable frame rate</b> ("auto"): a frame is emitted only when the content
    /// changed. A static screen costs nothing but a keep-alive repeat every
    /// <c>keepAliveTicks</c>, which bounds sample durations (seek granularity, editors) and
    /// keeps an interrupted recording's tail short. Late indices are simply skipped.</item>
    /// </list>
    /// The first frame is always stamped with index 0 so the video track starts with the audio.
    /// Pure logic — no clock, no I/O — so it is unit tested directly.
    /// </summary>
    internal sealed class FrameEmissionPolicy
    {
        private readonly bool _variableFrameRate;
        private readonly long _keepAliveTicks;
        private long _lastEmittedIndex = -1;

        public FrameEmissionPolicy(bool variableFrameRate, long keepAliveTicks)
        {
            _variableFrameRate = variableFrameRate;
            _keepAliveTicks = keepAliveTicks < 1 ? 1 : keepAliveTicks;
        }

        public bool IsVariableFrameRate => _variableFrameRate;

        /// <summary>True once any frame was emitted (there is a picture to repeat).</summary>
        public bool HasFrame => _lastEmittedIndex >= 0;

        /// <summary>Grid index of the most recently emitted frame, or -1.</summary>
        public long LastEmittedIndex => _lastEmittedIndex;

        /// <summary>Decision for grid index <paramref name="gridIndex"/>.</summary>
        public FrameAction Decide(long gridIndex, bool hasNewContent)
        {
            if (gridIndex <= _lastEmittedIndex)
                return FrameAction.None; // never emit a non-increasing PTS

            if (hasNewContent)
                return FrameAction.EncodeNew;

            if (!HasFrame)
                return FrameAction.None;

            if (!_variableFrameRate)
                return FrameAction.RepeatLast;

            return gridIndex - _lastEmittedIndex >= _keepAliveTicks ? FrameAction.RepeatLast : FrameAction.None;
        }

        /// <summary>
        /// Whether grid indices skipped by a late wake-up are filled with repeats (CFR) or left
        /// out (VFR). False until the first frame exists.
        /// </summary>
        public bool FillsMissedTicks => !_variableFrameRate && HasFrame;

        /// <summary>
        /// The PTS index to stamp on a frame captured at <paramref name="gridIndex"/>: the very
        /// first frame is pulled back to 0, everything after keeps its true grid index.
        /// </summary>
        public long ToPresentationIndex(long gridIndex) => HasFrame ? gridIndex : 0;

        /// <summary>Records that a frame was emitted for <paramref name="gridIndex"/>.</summary>
        public void MarkEmitted(long gridIndex)
        {
            if (gridIndex > _lastEmittedIndex)
                _lastEmittedIndex = gridIndex;
        }

        /// <summary>
        /// At stop: whether one more repeat at <paramref name="gridIndex"/> is needed so the
        /// video track's duration reaches the stop time (otherwise a static tail would be cut
        /// off at the last changed frame).
        /// </summary>
        public bool NeedsFinalFrame(long gridIndex) => HasFrame && gridIndex > _lastEmittedIndex;
    }
}
