using System.Drawing;
using System.IO;
using Klocman.Tools;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BulkCrapUninstallerTests
{
    [TestClass]
    public class DrawingToolsTests
    {
        [TestMethod]
        public void CreateOwnedIconFromHandle_ReturnsUsableClone()
        {
            using var sourceIcon = SystemIcons.Application;

            // Icon has no GetHicon(); it exposes a shared Handle. CreateOwnedIconFromHandle takes
            // ownership and calls DestroyIcon, so it must be given a handle we own - destroying the
            // shared SystemIcons handle would corrupt it for the rest of the process. Round-tripping
            // through a Bitmap produces a fresh HICON that is safe to hand over.
            using var sourceBitmap = sourceIcon.ToBitmap();
            var handle = sourceBitmap.GetHicon();

            using var ownedIcon = DrawingTools.CreateOwnedIconFromHandle(handle);
            using var stream = new MemoryStream();

            ownedIcon.Save(stream);

            Assert.IsTrue(stream.Length > 0);
            Assert.AreEqual(sourceIcon.Size, ownedIcon.Size);
        }
    }
}
