using System;

namespace ScreenRecorder.EncoderHarness
{
    /// <summary>
    /// Generates an animated NV12 test pattern: a diagonal gradient that scrolls with the frame
    /// index plus a bright box that moves left-to-right, so motion and frame ordering are visible.
    /// Chroma cycles slowly. Tightly packed (stride == width), exercising the same plane copy as
    /// the padded D3D output that P2 will produce.
    /// </summary>
    internal static unsafe class Nv12TestPattern
    {
        public static void Fill(nint dst, int stride, int width, int height, int frameIndex)
        {
            byte* y = (byte*)dst;
            byte* uv = (byte*)dst + (long)stride * height;

            // Y plane: scrolling diagonal gradient.
            for (int row = 0; row < height; row++)
            {
                byte* line = y + (long)row * stride;
                int rowTerm = row + frameIndex * 4;
                for (int col = 0; col < width; col++)
                    line[col] = (byte)((col + rowTerm) & 0xFF);
            }

            // Y plane: a moving bright box (luma 235).
            int boxSize = Math.Max(16, height / 8);
            int boxX = (frameIndex * 6) % Math.Max(1, width - boxSize);
            int boxY = height / 2 - boxSize / 2;
            for (int row = boxY; row < boxY + boxSize && row < height; row++)
            {
                byte* line = y + (long)row * stride;
                for (int col = boxX; col < boxX + boxSize && col < width; col++)
                    line[col] = 235;
            }

            // UV plane (interleaved, half height): slowly cycling chroma.
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
    }
}
