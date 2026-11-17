using System;

namespace ScreenRecorder.Tests.Support
{
    /// <summary>
    /// Synthetic NV12 pictures (Y plane followed by the interleaved UV plane, both with the
    /// same stride). Adapted from the encoder harness.
    /// </summary>
    internal static unsafe class Nv12TestPattern
    {
        /// <summary>Animated pattern: a scrolling diagonal gradient plus a moving bright box.</summary>
        public static void Fill(nint dst, int stride, int width, int height, int frameIndex)
        {
            byte* y = (byte*)dst;
            byte* uv = (byte*)dst + (long)stride * height;

            for (int row = 0; row < height; row++)
            {
                byte* line = y + (long)row * stride;
                int rowTerm = row + frameIndex * 4;
                for (int col = 0; col < width; col++)
                    line[col] = (byte)((col + rowTerm) & 0xFF);
            }

            int boxSize = Math.Max(16, height / 8);
            int boxX = (frameIndex * 6) % Math.Max(1, width - boxSize);
            int boxY = height / 2 - boxSize / 2;
            for (int row = boxY; row < boxY + boxSize && row < height; row++)
            {
                byte* line = y + (long)row * stride;
                for (int col = boxX; col < boxX + boxSize && col < width; col++)
                    line[col] = 235;
            }

            byte u = (byte)(128 + (int)(Math.Sin(frameIndex / 15.0) * 60));
            byte v = (byte)(128 + (int)(Math.Cos(frameIndex / 15.0) * 60));
            int chromaRows = height / 2;
            int pairs = width / 2;
            for (int row = 0; row < chromaRows; row++)
            {
                byte* line = uv + (long)row * stride;
                for (int p = 0; p < pairs; p++)
                {
                    line[p * 2] = u;
                    line[p * 2 + 1] = v;
                }
            }
        }

        /// <summary>
        /// Deterministic luma noise with neutral chroma: incompressible content, so the encoder
        /// is limited by its rate control rather than by how simple the picture is.
        /// </summary>
        public static void FillNoise(nint dst, int stride, int width, int height, int seed)
        {
            byte* y = (byte*)dst;
            new Random(seed).NextBytes(new Span<byte>(y, stride * height));
            new Span<byte>(y + (long)stride * height, stride * (height / 2)).Fill(128);
        }

        /// <summary>
        /// Mid-gray with a little deterministic luma grain (±<paramref name="amplitude"/>):
        /// content that always costs bits but that an encoder can still squeeze into a target
        /// rate. (Full-range noise cannot be: at its coarsest quantizer a hardware encoder still
        /// needs more than the target, so the result says nothing about rate control.)
        /// </summary>
        public static void FillGrain(nint dst, int stride, int width, int height, int seed, int amplitude)
        {
            byte* y = (byte*)dst;
            var luma = new Span<byte>(y, stride * height);
            new Random(seed).NextBytes(luma);
            int range = 2 * amplitude + 1;
            for (int i = 0; i < luma.Length; i++)
                luma[i] = (byte)(128 - amplitude + luma[i] % range);
            new Span<byte>(y + (long)stride * height, stride * (height / 2)).Fill(128);
        }

        /// <summary>
        /// Two flat vertical halves (left/right luma) with constant chroma. Bytes between
        /// <paramref name="width"/> and <paramref name="stride"/> are filled with 0xFF so a
        /// stride mix-up shows up in the decoded picture.
        /// </summary>
        public static void FillSplit(nint dst, int stride, int width, int height, byte leftLuma, byte rightLuma, byte u, byte v)
        {
            byte* y = (byte*)dst;
            int total = stride * height * 3 / 2;
            new Span<byte>(y, total).Fill(0xFF);

            for (int row = 0; row < height; row++)
            {
                byte* line = y + (long)row * stride;
                for (int col = 0; col < width; col++)
                    line[col] = col < width / 2 ? leftLuma : rightLuma;
            }

            byte* uv = y + (long)stride * height;
            for (int row = 0; row < height / 2; row++)
            {
                byte* line = uv + (long)row * stride;
                for (int p = 0; p < width / 2; p++)
                {
                    line[p * 2] = u;
                    line[p * 2 + 1] = v;
                }
            }
        }
    }
}
