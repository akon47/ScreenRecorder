using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScreenRecorder.DirectX;

namespace ScreenRecorder.Tests.DirectX
{
    [TestClass]
    public sealed class GeometryUtilsTests
    {
        private const double Epsilon = 1e-9;

        private static void AssertRect(Rect expected, Rect actual)
        {
            Assert.AreEqual(expected.X, actual.X, Epsilon, "X");
            Assert.AreEqual(expected.Y, actual.Y, Epsilon, "Y");
            Assert.AreEqual(expected.Width, actual.Width, Epsilon, "Width");
            Assert.AreEqual(expected.Height, actual.Height, Epsilon, "Height");
        }

        private static void AssertSize(double expectedX, double expectedY, Size actual)
        {
            Assert.AreEqual(expectedX, actual.Width, Epsilon, "scale X");
            Assert.AreEqual(expectedY, actual.Height, Epsilon, "scale Y");
        }

        #region ComputeUniformBounds

        [TestMethod]
        public void UniformBounds_SameAspect_FillsTheTarget()
        {
            Rect bounds = GeometryUtils.ComputeUniformBounds(new Rect(0, 0, 1920, 1080), new Size(1280, 720));

            AssertRect(new Rect(0, 0, 1920, 1080), bounds);
        }

        [TestMethod]
        public void UniformBounds_SameSize_IsIdentity()
        {
            Rect bounds = GeometryUtils.ComputeUniformBounds(new Rect(0, 0, 2560, 1440), new Size(2560, 1440));

            AssertRect(new Rect(0, 0, 2560, 1440), bounds);
        }

        [TestMethod]
        public void UniformBounds_NarrowerContent_IsPillarboxed()
        {
            // 4:3 into 16:9 → bars left and right.
            Rect bounds = GeometryUtils.ComputeUniformBounds(new Rect(0, 0, 1920, 1080), new Size(640, 480));

            AssertRect(new Rect(240, 0, 1440, 1080), bounds);
        }

        [TestMethod]
        public void UniformBounds_WiderContent_IsLetterboxed()
        {
            // 21:9 into 16:9 → bars top and bottom.
            Rect bounds = GeometryUtils.ComputeUniformBounds(new Rect(0, 0, 1920, 1080), new Size(2560, 1080));

            AssertRect(new Rect(0, 135, 1920, 810), bounds);
        }

        [TestMethod]
        public void UniformBounds_PortraitContent_IsScaledDownAndCentered()
        {
            Rect bounds = GeometryUtils.ComputeUniformBounds(new Rect(0, 0, 1920, 1080), new Size(1080, 1920));

            AssertRect(new Rect(656.25, 0, 607.5, 1080), bounds);
        }

        [TestMethod]
        public void UniformBounds_SmallContent_IsScaledUp()
        {
            Rect bounds = GeometryUtils.ComputeUniformBounds(new Rect(0, 0, 1000, 1000), new Size(10, 5));

            AssertRect(new Rect(0, 250, 1000, 500), bounds);
        }

        [TestMethod]
        public void UniformBounds_OffsetTarget_KeepsTheOffset()
        {
            Rect bounds = GeometryUtils.ComputeUniformBounds(new Rect(100, 50, 1920, 1080), new Size(640, 480));

            AssertRect(new Rect(340, 50, 1440, 1080), bounds);
        }

        [TestMethod]
        public void UniformBounds_ResultAlwaysFitsInsideAndKeepsTheAspectRatio()
        {
            var targets = new[] { new Rect(0, 0, 1920, 1080), new Rect(10, 20, 1280, 1024), new Rect(-500, 300, 333, 777) };
            var contents = new[] { new Size(1, 1), new Size(3840, 2160), new Size(1366, 768), new Size(500, 2000), new Size(1707, 960) };

            foreach (Rect target in targets)
            {
                foreach (Size content in contents)
                {
                    Rect bounds = GeometryUtils.ComputeUniformBounds(target, content);

                    Assert.IsTrue(bounds.Left >= target.Left - Epsilon && bounds.Right <= target.Right + 1e-6, $"{content} in {target}: horizontal overflow");
                    Assert.IsTrue(bounds.Top >= target.Top - Epsilon && bounds.Bottom <= target.Bottom + 1e-6, $"{content} in {target}: vertical overflow");
                    Assert.AreEqual(content.Width / content.Height, bounds.Width / bounds.Height, 1e-9, $"{content} in {target}: aspect ratio");

                    // Touches the target on at least one axis, centred on the other.
                    bool fillsWidth = System.Math.Abs(bounds.Width - target.Width) < 1e-6;
                    bool fillsHeight = System.Math.Abs(bounds.Height - target.Height) < 1e-6;
                    Assert.IsTrue(fillsWidth || fillsHeight, $"{content} in {target}: does not touch the target");
                    Assert.AreEqual(target.X + target.Width / 2, bounds.X + bounds.Width / 2, 1e-6, "horizontal centre");
                    Assert.AreEqual(target.Y + target.Height / 2, bounds.Y + bounds.Height / 2, 1e-6, "vertical centre");
                }
            }
        }

        [TestMethod]
        public void UniformBounds_EmptyContent_CollapsesToTheCentre()
        {
            Rect bounds = GeometryUtils.ComputeUniformBounds(new Rect(0, 0, 1920, 1080), new Size(0, 0));

            AssertRect(new Rect(960, 540, 0, 0), bounds);
        }

        #endregion

        #region ComputeScaleFactor

        [TestMethod]
        public void ScaleFactor_None_IsOne()
        {
            AssertSize(1, 1, GeometryUtils.ComputeScaleFactor(new Size(1920, 1080), new Size(640, 480), Stretch.None));
        }

        [TestMethod]
        public void ScaleFactor_Uniform_UsesTheSmallerAxisScale()
        {
            AssertSize(2.25, 2.25, GeometryUtils.ComputeScaleFactor(new Size(1920, 1080), new Size(640, 480), Stretch.Uniform));
            AssertSize(0.5, 0.5, GeometryUtils.ComputeScaleFactor(new Size(1920, 1080), new Size(3840, 2160), Stretch.Uniform));
        }

        [TestMethod]
        public void ScaleFactor_UniformToFill_UsesTheLargerAxisScale()
        {
            AssertSize(3, 3, GeometryUtils.ComputeScaleFactor(new Size(1920, 1080), new Size(640, 480), Stretch.UniformToFill));
        }

        [TestMethod]
        public void ScaleFactor_Fill_ScalesEachAxisIndependently()
        {
            AssertSize(3, 2.25, GeometryUtils.ComputeScaleFactor(new Size(1920, 1080), new Size(640, 480), Stretch.Fill));
        }

        [TestMethod]
        public void ScaleFactor_UnconstrainedWidth_FollowsTheHeight()
        {
            AssertSize(2.25, 2.25, GeometryUtils.ComputeScaleFactor(new Size(double.PositiveInfinity, 1080), new Size(640, 480), Stretch.Uniform));
        }

        [TestMethod]
        public void ScaleFactor_UnconstrainedHeight_FollowsTheWidth()
        {
            AssertSize(3, 3, GeometryUtils.ComputeScaleFactor(new Size(1920, double.PositiveInfinity), new Size(640, 480), Stretch.Uniform));
        }

        [TestMethod]
        public void ScaleFactor_FullyUnconstrained_IsOne()
        {
            var infinite = new Size(double.PositiveInfinity, double.PositiveInfinity);

            AssertSize(1, 1, GeometryUtils.ComputeScaleFactor(infinite, new Size(640, 480), Stretch.Uniform));
            AssertSize(1, 1, GeometryUtils.ComputeScaleFactor(infinite, new Size(640, 480), Stretch.Fill));
        }

        [TestMethod]
        public void ScaleFactor_ZeroContent_IsZeroNotInfinity()
        {
            AssertSize(0, 0, GeometryUtils.ComputeScaleFactor(new Size(1920, 1080), new Size(0, 0), Stretch.Uniform));
            AssertSize(0, 2.25, GeometryUtils.ComputeScaleFactor(new Size(1920, 1080), new Size(0, 480), Stretch.Fill));
        }

        [TestMethod]
        public void ScaleFactor_UpOnly_NeverShrinks()
        {
            AssertSize(1, 1, GeometryUtils.ComputeScaleFactor(new Size(1920, 1080), new Size(3840, 2160), Stretch.Uniform, StretchDirection.UpOnly));
            AssertSize(2.25, 2.25, GeometryUtils.ComputeScaleFactor(new Size(1920, 1080), new Size(640, 480), Stretch.Uniform, StretchDirection.UpOnly));
        }

        [TestMethod]
        public void ScaleFactor_DownOnly_NeverEnlarges()
        {
            AssertSize(1, 1, GeometryUtils.ComputeScaleFactor(new Size(1920, 1080), new Size(640, 480), Stretch.Uniform, StretchDirection.DownOnly));
            AssertSize(0.5, 0.5, GeometryUtils.ComputeScaleFactor(new Size(1920, 1080), new Size(3840, 2160), Stretch.Uniform, StretchDirection.DownOnly));
        }

        #endregion

        #region EvenFloor

        [TestMethod]
        [DataRow(0.0, 0)]
        [DataRow(0.99, 0)]
        [DataRow(1.0, 0)]
        [DataRow(1.99, 0)]
        [DataRow(2.0, 2)]
        [DataRow(3.0, 2)]
        [DataRow(3.999, 2)]
        [DataRow(4.0, 4)]
        [DataRow(1080.0, 1080)]
        [DataRow(1080.5, 1080)]
        [DataRow(1081.0, 1080)]
        [DataRow(1081.99, 1080)]
        [DataRow(1919.9, 1918)]
        [DataRow(1920.0, 1920)]
        [DataRow(2559.999999, 2558)]
        [DataRow(3840.0, 3840)]
        public void EvenFloor_RoundsDownToAnEvenInteger(double value, int expected)
        {
            Assert.AreEqual(expected, GeometryUtils.EvenFloor(value));
        }

        [TestMethod]
        public void EvenFloor_ResultIsEvenAndWithinTwoBelowTheInput()
        {
            for (double value = 0; value < 5000; value += 0.37)
            {
                int result = GeometryUtils.EvenFloor(value);

                Assert.AreEqual(0, result % 2, $"EvenFloor({value}) = {result} is odd");
                Assert.IsTrue(result <= value && value - result < 2, $"EvenFloor({value}) = {result}");
            }
        }

        #endregion
    }
}
