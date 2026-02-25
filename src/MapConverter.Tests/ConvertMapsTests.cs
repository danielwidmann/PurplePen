using NUnit.Framework;
using System;
using System.IO;
using SkiaSharp;

namespace MapConverter.Tests
{
    /// <summary>
    /// Tests for the MapConverter command line utility.
    /// Verifies that map files (.ocd, .omap) can be converted to PNG images.
    /// </summary>
    [TestFixture]
    public class ConvertMapsTests
    {
        // Path to the TestFiles directory, relative to the test binary output.
        private string testFilesDir;
        private string tempDir;

        [OneTimeSetUp]
        public void Setup()
        {
            // TestFiles is located at src/TestFiles. The test binary is at
            // src/MapConverter.Tests/bin/<config>/net10.0/. Navigate up to src/.
            string binDir = AppDomain.CurrentDomain.BaseDirectory;
            testFilesDir = Path.GetFullPath(Path.Combine(binDir, "..", "..", "..", "..", "TestFiles"));
            if (!Directory.Exists(testFilesDir)) {
                Assert.Inconclusive("TestFiles directory not found at: " + testFilesDir);
            }

            tempDir = Path.Combine(Path.GetTempPath(), "MapConverterTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            if (Directory.Exists(tempDir)) {
                Directory.Delete(tempDir, true);
            }
        }

        /// <summary>
        /// Helper that runs the converter and validates the output PNG file.
        /// </summary>
        private void CheckMapConversion(string relativeMapPath, int expectedMinWidth, int expectedMinHeight, int dpi = 200)
        {
            string mapFile = Path.Combine(testFilesDir, relativeMapPath);
            Assert.IsTrue(File.Exists(mapFile), "Map file not found: " + mapFile);

            string outputFile = Path.Combine(tempDir, Path.GetFileNameWithoutExtension(relativeMapPath) + ".png");

            string[] args;
            if (dpi != 200) {
                args = new string[] { "--dpi", dpi.ToString(), mapFile, outputFile };
            }
            else {
                args = new string[] { mapFile, outputFile };
            }

            int returnValue = PurplePen.MapConverter.Program.Main(args);

            Assert.AreEqual(0, returnValue, "MapConverter should return 0 on success.");
            Assert.IsTrue(File.Exists(outputFile), "Output PNG file should exist.");

            // Verify the output is a valid PNG with reasonable dimensions.
            FileInfo fi = new FileInfo(outputFile);
            Assert.Greater(fi.Length, 0, "Output PNG should not be empty.");

            using (SKBitmap bitmap = SKBitmap.Decode(outputFile)) {
                Assert.IsNotNull(bitmap, "Output should be a valid PNG image.");
                Assert.GreaterOrEqual(bitmap.Width, expectedMinWidth, "Image width should meet minimum.");
                Assert.GreaterOrEqual(bitmap.Height, expectedMinHeight, "Image height should meet minimum.");
            }
        }

        [Test]
        public void ConvertOcdFile()
        {
            CheckMapConversion(Path.Combine("courseprinting", "marymoor.ocd"), 1000, 1000);
        }

        [Test]
        public void ConvertOmapFile()
        {
            CheckMapConversion(Path.Combine("courseprinting", "McHugh Creek.omap"), 1000, 1000);
        }

        [Test]
        public void ConvertWithCustomDpi()
        {
            CheckMapConversion(Path.Combine("courseprinting", "marymoor.ocd"), 1500, 1500, 300);
        }

        [Test]
        public void MissingSourceFileReturnsError()
        {
            int returnValue = PurplePen.MapConverter.Program.Main(new string[] { "nonexistent.ocd", Path.Combine(tempDir, "out.png") });
            Assert.AreEqual(1, returnValue);
        }

        [Test]
        public void NoArgumentsReturnsError()
        {
            int returnValue = PurplePen.MapConverter.Program.Main(new string[] { });
            Assert.AreEqual(1, returnValue);
        }

        [Test]
        public void InvalidExtensionReturnsError()
        {
            // Create a temp file with wrong extension.
            string tempFile = Path.Combine(tempDir, "test.txt");
            File.WriteAllText(tempFile, "not a map");

            int returnValue = PurplePen.MapConverter.Program.Main(new string[] { tempFile, Path.Combine(tempDir, "out.png") });
            Assert.AreEqual(1, returnValue);
        }
    }
}
