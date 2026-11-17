using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using ScreenRecorder.AudioSource;

namespace ScreenRecorder.Controls
{
    /// <summary>Something whose audio level can be read: the loudest moment since the last call, 0..1.</summary>
    public interface IAudioLevelSource
    {
        float ReadAudioPeak();
    }

    /// <summary>
    /// The numbers behind the meter, separate from the drawing so they can be tested:
    /// a decibel scale from the floor to full scale, an instant rise, a steady fall, and a
    /// marker that stays at the recent maximum for a moment before it falls too.
    /// </summary>
    public struct LevelMeterState
    {
        /// <summary>How fast the bar falls back, in dB per second.</summary>
        public const double FallRate = 24.0;

        /// <summary>How long the peak marker stays put before it starts to fall.</summary>
        public const double HoldSeconds = 1.5;

        /// <summary>From here the level is close to the limit (drawn in the caution color).</summary>
        public const double CautionDecibels = -6.0;

        /// <summary>From here the sound is at the limit and likely clipped (critical color).</summary>
        public const double CriticalDecibels = -1.0;

        /// <summary>The level the bar shows, in dB.</summary>
        public double Level;

        /// <summary>The level of the peak marker, in dB.</summary>
        public double Peak;

        private double _heldFor;

        public static LevelMeterState Silent => new LevelMeterState { Level = AudioPeakMeter.FloorDecibels, Peak = AudioPeakMeter.FloorDecibels };

        /// <summary>
        /// Advances by <paramref name="seconds"/> with <paramref name="input"/> (dB) being the
        /// loudest level heard in that time.
        /// </summary>
        public void Advance(double input, double seconds)
        {
            double floor = AudioPeakMeter.FloorDecibels;
            if (double.IsNaN(input) || input < floor)
                input = floor;
            if (input > 0)
                input = 0;
            if (!(seconds > 0))
                seconds = 0;

            double fall = FallRate * seconds;

            Level = input >= Level ? input : Math.Max(input, Level - fall);

            if (input >= Peak)
            {
                Peak = input;
                _heldFor = 0;
            }
            else
            {
                _heldFor += seconds;
                if (_heldFor > HoldSeconds)
                    Peak = Math.Max(Level, Peak - fall);
            }
        }

        /// <summary>Where a level sits on the meter: 0 at the floor, 1 at full scale.</summary>
        public static double ToPosition(double decibels)
        {
            double floor = AudioPeakMeter.FloorDecibels;
            if (double.IsNaN(decibels) || decibels <= floor)
                return 0;
            return decibels >= 0 ? 1 : 1 - decibels / floor;
        }
    }

    /// <summary>
    /// A thin horizontal audio level meter. It shows what is going into the recording, so a
    /// silent recording is noticed while it happens rather than afterwards.
    ///
    /// While visible it asks <see cref="Source"/> for the level once per rendered frame. Only
    /// the meter's own drawing is replaced when the level changes — no layout pass, no
    /// re-rendering of anything around it.
    /// </summary>
    public sealed class LevelMeter : FrameworkElement
    {
        public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
            nameof(Source), typeof(IAudioLevelSource), typeof(LevelMeter), new PropertyMetadata(null, OnSourceChanged));

        /// <summary>The level in dB to show when there is no <see cref="Source"/> to ask.</summary>
        public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
            nameof(Level), typeof(double), typeof(LevelMeter),
            new FrameworkPropertyMetadata(AudioPeakMeter.FloorDecibels, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty TrackBrushProperty = RegisterBrush(nameof(TrackBrush));
        public static readonly DependencyProperty LevelBrushProperty = RegisterBrush(nameof(LevelBrush));
        public static readonly DependencyProperty CautionBrushProperty = RegisterBrush(nameof(CautionBrush));
        public static readonly DependencyProperty CriticalBrushProperty = RegisterBrush(nameof(CriticalBrush));

        private const double MarkerWidth = 2;

        private readonly DrawingVisual _drawing = new DrawingVisual();
        private LevelMeterState _state = LevelMeterState.Silent;
        private long _lastFrame;
        private double _drawnLevel = double.NaN;
        private double _drawnPeak = double.NaN;
        private bool _listening;
        private RectangleGeometry _clip;

        public LevelMeter()
        {
            AddVisualChild(_drawing);
            IsHitTestVisible = false;
            SnapsToDevicePixels = true;

            IsVisibleChanged += (s, e) => UpdateListening();
            Loaded += (s, e) => UpdateListening();
            Unloaded += (s, e) => UpdateListening();
        }

        public IAudioLevelSource Source
        {
            get => (IAudioLevelSource)GetValue(SourceProperty);
            set => SetValue(SourceProperty, value);
        }

        public double Level
        {
            get => (double)GetValue(LevelProperty);
            set => SetValue(LevelProperty, value);
        }

        public Brush TrackBrush
        {
            get => (Brush)GetValue(TrackBrushProperty);
            set => SetValue(TrackBrushProperty, value);
        }

        public Brush LevelBrush
        {
            get => (Brush)GetValue(LevelBrushProperty);
            set => SetValue(LevelBrushProperty, value);
        }

        public Brush CautionBrush
        {
            get => (Brush)GetValue(CautionBrushProperty);
            set => SetValue(CautionBrushProperty, value);
        }

        public Brush CriticalBrush
        {
            get => (Brush)GetValue(CriticalBrushProperty);
            set => SetValue(CriticalBrushProperty, value);
        }

        protected override int VisualChildrenCount => 1;

        protected override Visual GetVisualChild(int index) => _drawing;

        private static DependencyProperty RegisterBrush(string name)
            => DependencyProperty.Register(name, typeof(Brush), typeof(LevelMeter),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((LevelMeter)d).UpdateListening();

        /// <summary>Follows the source only while there is something to show and somewhere to show it.</summary>
        private void UpdateListening()
        {
            bool listen = IsLoaded && IsVisible && Source != null;
            if (listen == _listening)
                return;

            _listening = listen;
            if (listen)
            {
                _state = LevelMeterState.Silent;
                _lastFrame = Stopwatch.GetTimestamp();
                CompositionTarget.Rendering += OnRendering;
            }
            else
            {
                CompositionTarget.Rendering -= OnRendering;
                _state = LevelMeterState.Silent;
            }

            Draw();
        }

        private void OnRendering(object sender, EventArgs e)
        {
            IAudioLevelSource source = Source;
            if (source == null)
                return;

            long now = Stopwatch.GetTimestamp();
            double seconds = (now - _lastFrame) / (double)Stopwatch.Frequency;
            _lastFrame = now;

            _state.Advance(AudioPeakMeter.ToDecibels(source.ReadAudioPeak()), seconds);

            // A meter at rest costs nothing: redraw only when the picture would differ.
            if (_state.Level != _drawnLevel || _state.Peak != _drawnPeak)
                Draw();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            // Size, brushes or the bound level changed.
            Draw();
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            _clip = null;
            Draw();
        }

        private void Draw()
        {
            double level = _listening ? _state.Level : Level;
            double peak = _listening ? _state.Peak : Level;
            _drawnLevel = level;
            _drawnPeak = peak;

            double width = ActualWidth;
            double height = ActualHeight;

            using DrawingContext context = _drawing.RenderOpen();
            if (width <= 0 || height <= 0)
                return;

            if (_clip == null)
            {
                // Rounded ends. Built once per size: this runs for every frame with sound.
                _clip = new RectangleGeometry(new Rect(0, 0, width, height), height / 2, height / 2);
                _clip.Freeze();
            }

            context.PushClip(_clip);

            if (TrackBrush != null)
                context.DrawRectangle(TrackBrush, null, new Rect(0, 0, width, height));

            double end = width * LevelMeterState.ToPosition(level);
            double caution = width * LevelMeterState.ToPosition(LevelMeterState.CautionDecibels);
            double critical = width * LevelMeterState.ToPosition(LevelMeterState.CriticalDecibels);

            // The bar: normal up to the caution mark, then caution, then critical.
            Segment(context, LevelBrush, 0, Math.Min(end, caution), height);
            Segment(context, CautionBrush, caution, Math.Min(end, critical), height);
            Segment(context, CriticalBrush, critical, end, height);

            // The peak marker, in the color of the zone it is in.
            double marker = width * LevelMeterState.ToPosition(peak);
            if (marker - MarkerWidth > end)
            {
                Brush brush = peak >= LevelMeterState.CriticalDecibels ? CriticalBrush
                    : peak >= LevelMeterState.CautionDecibels ? CautionBrush
                    : LevelBrush;
                Segment(context, brush, marker - MarkerWidth, marker, height);
            }

            context.Pop();
        }

        private static void Segment(DrawingContext context, Brush brush, double from, double to, double height)
        {
            if (brush != null && to > from)
                context.DrawRectangle(brush, null, new Rect(from, 0, to - from, height));
        }
    }
}
