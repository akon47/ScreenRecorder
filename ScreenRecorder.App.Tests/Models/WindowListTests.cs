using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScreenRecorder.Region;

namespace ScreenRecorder.AppTests.Models
{
    [TestClass]
    public sealed class WindowListTests
    {
        private const long ToolWindow = 0x00000080;
        private const long AppWindow = 0x00040000;
        private const long NoActivate = 0x08000000;

        [TestMethod]
        public void OrdinaryTitledWindow_IsListed()
        {
            Assert.IsTrue(WindowList.IsListed("Document - Editor", 0, hasOwner: false));
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public void WindowWithoutATitle_IsNotListed(string title)
        {
            Assert.IsFalse(WindowList.IsListed(title, 0, hasOwner: false));
            Assert.IsFalse(WindowList.IsListed(title, AppWindow, hasOwner: false), "nothing to show for it in a list");
        }

        [TestMethod]
        public void ToolWindowsAndOwnedWindows_AreNotListed()
        {
            Assert.IsFalse(WindowList.IsListed("Palette", ToolWindow, hasOwner: false));
            Assert.IsFalse(WindowList.IsListed("Overlay", NoActivate, hasOwner: false));
            Assert.IsFalse(WindowList.IsListed("Find and Replace", 0, hasOwner: true));
        }

        [TestMethod]
        public void AWindowThatAsksForATaskbarButton_IsListedRegardless()
        {
            Assert.IsTrue(WindowList.IsListed("Palette", ToolWindow | AppWindow, hasOwner: false));
            Assert.IsTrue(WindowList.IsListed("Dialog", AppWindow, hasOwner: true));
        }

        [TestMethod]
        public void ShortTitle_IsKeptAsItIs()
        {
            Assert.AreEqual("Notes", WindowList.ShortenTitle("Notes"));
            Assert.AreEqual(new string('a', WindowList.MaximumTitleLength), WindowList.ShortenTitle(new string('a', WindowList.MaximumTitleLength)));
        }

        [TestMethod]
        public void LongTitle_IsCutWithAnEllipsis()
        {
            string shortened = WindowList.ShortenTitle(new string('a', 200));

            Assert.AreEqual(WindowList.MaximumTitleLength, shortened.Length);
            StringAssert.EndsWith(shortened, "…");
        }

        [TestMethod]
        public void Title_IsOneLineAndTrimmed()
        {
            Assert.AreEqual("first second third", WindowList.ShortenTitle("  first\r\nsecond\tthird  ".Replace("\r\n", "\n")));
            Assert.AreEqual(string.Empty, WindowList.ShortenTitle(null));
        }

        [TestMethod]
        public void GetWindows_NeverListsThisProcess_AndEveryEntryIsUsable()
        {
            foreach (WindowListItem window in WindowList.GetWindows())
            {
                Assert.AreNotEqual(System.IntPtr.Zero, window.Handle);
                Assert.IsFalse(string.IsNullOrWhiteSpace(window.Title));
                Assert.IsTrue(window.Title.Length <= WindowList.MaximumTitleLength);
            }
        }
    }
}
