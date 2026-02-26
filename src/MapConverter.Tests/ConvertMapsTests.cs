using NUnit.Framework;
using System;
using System.IO;
using SkiaSharp;

namespace MapConverter.Tests
{
    /// <summary>
    /// Tests for the MapConverter command line utility.
    /// Verifies that map files (.ocd, .omap) can be converted to image files
    /// with various options.
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
        /// Helper that runs the converter and validates the output image file.
        /// </summary>
        private void CheckMapConversion(string relativeMapPath, int expectedMinWidth, int expectedMinHeight, params string[] extraArgs)
        {
            string mapFile = Path.Combine(testFilesDir, relativeMapPath);
            Assert.IsTrue(File.Exists(mapFile), "Map file not found: " + mapFile);

            // Determine output extension from --format arg or default to png.
            string outputExt = ".png";
            for (int j = 0; j < extraArgs.Length; j++) {
                if (extraArgs[j] == "--format" && j + 1 < extraArgs.Length) {
                    string fmt = extraArgs[j + 1];
                    if (fmt == "jpg") outputExt = ".jpg";
                    else if (fmt == "gif") outputExt = ".gif";
                }
            }

            string outputFile = Path.Combine(tempDir, Path.GetFileNameWithoutExtension(relativeMapPath) + "_" + Guid.NewGuid().ToString("N").Substring(0, 8) + outputExt);

            string[] allArgs = new string[extraArgs.Length + 2];
            Array.Copy(extraArgs, 0, allArgs, 0, extraArgs.Length);
            allArgs[allArgs.Length - 2] = mapFile;
            allArgs[allArgs.Length - 1] = outputFile;

            int returnValue = PurplePen.MapConverter.Program.Main(allArgs);

            Assert.AreEqual(0, returnValue, "MapConverter should return 0 on success.");
            Assert.IsTrue(File.Exists(outputFile), "Output image file should exist.");

            // Verify the output is a valid image with reasonable dimensions.
            FileInfo fi = new FileInfo(outputFile);
            Assert.Greater(fi.Length, 0, "Output image should not be empty.");

            using (SKBitmap bitmap = SKBitmap.Decode(outputFile)) {
                Assert.IsNotNull(bitmap, "Output should be a valid image.");
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
            CheckMapConversion(Path.Combine("courseprinting", "marymoor.ocd"), 1500, 1500, "--dpi", "300");
        }

        [Test]
        public void ConvertToJpeg()
        {
            string mapFile = Path.Combine(testFilesDir, "courseprinting", "marymoor.ocd");
            string outputFile = Path.Combine(tempDir, "marymoor_jpeg.jpg");

            int returnValue = PurplePen.MapConverter.Program.Main(new string[] { "--format", "jpg", "--quality", "50", mapFile, outputFile });

            Assert.AreEqual(0, returnValue);
            Assert.IsTrue(File.Exists(outputFile));

            FileInfo fi = new FileInfo(outputFile);
            Assert.Greater(fi.Length, 0);

            using (SKBitmap bitmap = SKBitmap.Decode(outputFile)) {
                Assert.IsNotNull(bitmap);
                Assert.Greater(bitmap.Width, 0);
            }
        }

        [Test]
        public void ConvertToJpegAutoDetect()
        {
            // Test that .jpg extension auto-detects JPEG format.
            string mapFile = Path.Combine(testFilesDir, "courseprinting", "marymoor.ocd");
            string outputFile = Path.Combine(tempDir, "marymoor_auto.jpg");

            int returnValue = PurplePen.MapConverter.Program.Main(new string[] { mapFile, outputFile });

            Assert.AreEqual(0, returnValue);
            Assert.IsTrue(File.Exists(outputFile));

            // Verify it's a JPEG by checking the file header bytes.
            byte[] header = new byte[2];
            using (FileStream fs = File.OpenRead(outputFile)) {
                fs.ReadExactly(header, 0, 2);
            }
            Assert.AreEqual(0xFF, header[0], "JPEG should start with 0xFF");
            Assert.AreEqual(0xD8, header[1], "JPEG should start with 0xFFD8");
        }

        [Test]
        public void ConvertWithNoAntiAlias()
        {
            CheckMapConversion(Path.Combine("courseprinting", "marymoor.ocd"), 1000, 1000, "--no-anti-alias");
        }

        [Test]
        public void ConvertWithCmyk()
        {
            CheckMapConversion(Path.Combine("courseprinting", "marymoor.ocd"), 1000, 1000, "--cmyk");
        }

        [Test]
        public void ConvertWithQuality()
        {
            // Higher quality should produce a larger JPEG file.
            string mapFile = Path.Combine(testFilesDir, "courseprinting", "marymoor.ocd");
            string lowQFile = Path.Combine(tempDir, "marymoor_q10.jpg");
            string highQFile = Path.Combine(tempDir, "marymoor_q95.jpg");

            PurplePen.MapConverter.Program.Main(new string[] { "--format", "jpg", "--quality", "10", mapFile, lowQFile });
            PurplePen.MapConverter.Program.Main(new string[] { "--format", "jpg", "--quality", "95", mapFile, highQFile });

            FileInfo lowFi = new FileInfo(lowQFile);
            FileInfo highFi = new FileInfo(highQFile);
            Assert.Greater(highFi.Length, lowFi.Length, "Higher quality JPEG should be larger.");
        }

        [Test]
        public void WorldFileCreatedForGeoreferencedMap()
        {
            string mapFile = Path.Combine(testFilesDir, "courseprinting", "LordHill_ver16_2024Jan_scaled.omap");
            string outputFile = Path.Combine(tempDir, "lordhill_wf.png");
            string worldFile = Path.Combine(tempDir, "lordhill_wf.pgw");

            int returnValue = PurplePen.MapConverter.Program.Main(new string[] { "--world-file", mapFile, outputFile });

            Assert.AreEqual(0, returnValue);
            Assert.IsTrue(File.Exists(outputFile), "Output image should exist.");
            Assert.IsTrue(File.Exists(worldFile), "World file should be created for georeferenced map.");

            // World file should have 6 lines.
            string[] lines = File.ReadAllLines(worldFile);
            Assert.AreEqual(6, lines.Length, "World file should have 6 lines.");

            // Each line should be parseable as a double.
            foreach (string line in lines) {
                Assert.IsTrue(double.TryParse(line, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double _),
                    "Each world file line should be a number.");
            }
        }

        [Test]
        public void WorldFileNotCreatedForNonGeoreferencedMap()
        {
            string mapFile = Path.Combine(testFilesDir, "courseprinting", "marymoor.ocd");
            string outputFile = Path.Combine(tempDir, "marymoor_nowf.png");
            string worldFile = Path.Combine(tempDir, "marymoor_nowf.pgw");

            int returnValue = PurplePen.MapConverter.Program.Main(new string[] { "--world-file", mapFile, outputFile });

            Assert.AreEqual(0, returnValue);
            Assert.IsTrue(File.Exists(outputFile), "Output image should exist.");
            Assert.IsFalse(File.Exists(worldFile), "World file should NOT be created for non-georeferenced map.");
        }

        [Test]
        public void WorldFileForJpeg()
        {
            string mapFile = Path.Combine(testFilesDir, "courseprinting", "LordHill_ver16_2024Jan_scaled.omap");
            string outputFile = Path.Combine(tempDir, "lordhill_wf.jpg");
            string worldFile = Path.Combine(tempDir, "lordhill_wf.jgw");

            int returnValue = PurplePen.MapConverter.Program.Main(new string[] { "--world-file", "--format", "jpg", mapFile, outputFile });

            Assert.AreEqual(0, returnValue);
            Assert.IsTrue(File.Exists(worldFile), "World file with .jgw extension should be created for JPEG.");
        }

        [Test]
        public void WorldFileWithEpsgReprojection()
        {
            string mapFile = Path.Combine(testFilesDir, "courseprinting", "LordHill_ver16_2024Jan_scaled.omap");
            string outputFile = Path.Combine(tempDir, "lordhill_epsg3857.png");
            string worldFile = Path.Combine(tempDir, "lordhill_epsg3857.pgw");

            int returnValue = PurplePen.MapConverter.Program.Main(new string[] { "--world-file-epsg", "3857", mapFile, outputFile });

            Assert.AreEqual(0, returnValue);
            Assert.IsTrue(File.Exists(outputFile), "Output image should exist.");
            Assert.IsTrue(File.Exists(worldFile), "World file should be created with EPSG reprojection.");

            // World file should have 6 lines with valid numbers.
            string[] lines = File.ReadAllLines(worldFile);
            Assert.AreEqual(6, lines.Length, "World file should have 6 lines.");

            double[] values = new double[6];
            for (int j = 0; j < 6; j++) {
                Assert.IsTrue(double.TryParse(lines[j], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out values[j]),
                    "Each world file line should be a number.");
            }

            // EPSG:3857 Web Mercator coordinates should be large values (meters at global scale).
            // X offset (line 5) should be large negative for western hemisphere locations.
            Assert.Less(values[4], -1000000, "X coordinate should be large negative for western hemisphere in EPSG:3857.");
            Assert.Greater(values[5], 1000000, "Y coordinate should be large positive in EPSG:3857.");
        }

        [Test]
        public void WorldFileEpsgImpliesWorldFile()
        {
            // Using --world-file-epsg should imply --world-file (no need to pass both).
            string mapFile = Path.Combine(testFilesDir, "courseprinting", "LordHill_ver16_2024Jan_scaled.omap");
            string outputFile = Path.Combine(tempDir, "lordhill_epsg_implied.png");
            string worldFile = Path.Combine(tempDir, "lordhill_epsg_implied.pgw");

            int returnValue = PurplePen.MapConverter.Program.Main(new string[] { "--world-file-epsg", "3857", mapFile, outputFile });

            Assert.AreEqual(0, returnValue);
            Assert.IsTrue(File.Exists(worldFile), "World file should be created when --world-file-epsg is used.");
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

        [Test]
        public void InvalidFormatReturnsError()
        {
            int returnValue = PurplePen.MapConverter.Program.Main(new string[] { "--format", "bmp", "test.ocd", Path.Combine(tempDir, "out.bmp") });
            Assert.AreEqual(1, returnValue);
        }

        [Test]
        public void InvalidQualityReturnsError()
        {
            int returnValue = PurplePen.MapConverter.Program.Main(new string[] { "--quality", "101", "test.ocd", Path.Combine(tempDir, "out.png") });
            Assert.AreEqual(1, returnValue);
        }
    }
}
