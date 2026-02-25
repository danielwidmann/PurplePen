/* Copyright (c) 2006-2008, Peter Golde
 * All rights reserved.
 * 
 * Redistribution and use in source and binary forms, with or without 
 * modification, are permitted provided that the following conditions are 
 * met:
 * 
 * 1. Redistributions of source code must retain the above copyright
 * notice, this list of conditions and the following disclaimer.
 * 
 * 2. Redistributions in binary form must reproduce the above copyright
 * notice, this list of conditions and the following disclaimer in the
 * documentation and/or other materials provided with the distribution.
 * 
 * 3. Neither the name of Peter Golde, nor "Purple Pen", nor the names
 * of its contributors may be used to endorse or promote products
 * derived from this software without specific prior written permission.
 * 
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND
 * CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES,
 * INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF
 * MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR
 * CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
 * SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING,
 * BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
 * SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
 * INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY,
 * WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING
 * NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE
 * USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY
 * OF SUCH DAMAGE.
 */

using System;
using System.Drawing;
using System.IO;
using PurplePen.Graphics2D;
using PurplePen.MapModel;

namespace PurplePen.MapConverter
{
    /// <summary>
    /// Command line utility to convert orienteering map files (.ocd, .omap) to PNG images
    /// using the Skia rendering backend for cross-platform support.
    /// 
    /// Usage: MapConverter [--dpi &lt;dpi&gt;] &lt;source_map&gt; &lt;destination_png&gt;
    /// </summary>
    class Program
    {
        static int Main(string[] args)
        {
            float dpi = 200;
            string sourceFile = null;
            string destFile = null;

            // Parse command line arguments.
            int i = 0;
            while (i < args.Length) {
                if (args[i] == "--dpi" && i + 1 < args.Length) {
                    if (!float.TryParse(args[i + 1], out dpi) || dpi <= 0) {
                        Console.Error.WriteLine("Error: Invalid DPI value '{0}'.", args[i + 1]);
                        return 1;
                    }
                    i += 2;
                }
                else if (args[i].StartsWith("-")) {
                    Console.Error.WriteLine("Error: Unknown option '{0}'.", args[i]);
                    PrintUsage();
                    return 1;
                }
                else if (sourceFile == null) {
                    sourceFile = args[i];
                    i++;
                }
                else if (destFile == null) {
                    destFile = args[i];
                    i++;
                }
                else {
                    Console.Error.WriteLine("Error: Too many arguments.");
                    PrintUsage();
                    return 1;
                }
            }

            if (sourceFile == null || destFile == null) {
                PrintUsage();
                return 1;
            }

            if (!File.Exists(sourceFile)) {
                Console.Error.WriteLine("Error: Source file '{0}' not found.", sourceFile);
                return 1;
            }

            string extension = Path.GetExtension(sourceFile).ToLowerInvariant();
            if (extension != ".ocd" && extension != ".omap") {
                Console.Error.WriteLine("Error: Source file must be an .ocd or .omap file.");
                return 1;
            }

            try {
                ConvertMapToPng(sourceFile, destFile, dpi);
                Console.WriteLine("Successfully converted '{0}' to '{1}' at {2} DPI.", sourceFile, destFile, dpi);
                return 0;
            }
            catch (Exception ex) {
                Console.Error.WriteLine("Error converting map: {0}", ex.Message);
                return 1;
            }
        }

        /// <summary>
        /// Prints usage information for the command line utility.
        /// </summary>
        static void PrintUsage()
        {
            Console.Error.WriteLine("Usage: MapConverter [--dpi <dpi>] <source_map> <destination_png>");
            Console.Error.WriteLine();
            Console.Error.WriteLine("  --dpi <dpi>       Output resolution in dots per inch (default: 200)");
            Console.Error.WriteLine("  <source_map>      Input map file (.ocd or .omap)");
            Console.Error.WriteLine("  <destination_png>  Output PNG file path");
        }

        /// <summary>
        /// Converts a map file to a PNG image using the Skia rendering backend.
        /// </summary>
        /// <param name="sourceFile">Path to the source map file (.ocd or .omap).</param>
        /// <param name="destFile">Path to the output PNG file.</param>
        /// <param name="dpi">Resolution in dots per inch.</param>
        static void ConvertMapToPng(string sourceFile, string destFile, float dpi)
        {
            string sourceDir = Path.GetDirectoryName(Path.GetFullPath(sourceFile));

            // Load the map using the Skia text metrics and file loader.
            Map map = new Map(new Skia_TextMetrics(), new Skia_FileLoader(sourceDir));
            InputOutput.ReadFile(sourceFile, map);

            RectangleF mapBounds;
            using (map.Read()) {
                mapBounds = map.Bounds;
            }

            if (mapBounds.Width <= 0 || mapBounds.Height <= 0) {
                throw new InvalidOperationException("Map has no visible content.");
            }

            // Convert map coordinates (mm) to pixels at the requested DPI.
            // Map coordinates are in mm, so 1 mm = dpi / 25.4 pixels.
            float mmToPixel = dpi / 25.4f;
            int pixelWidth = (int)Math.Ceiling(mapBounds.Width * mmToPixel);
            int pixelHeight = (int)Math.Ceiling(mapBounds.Height * mmToPixel);

            // Set up render options.
            RenderOptions renderOptions = new RenderOptions();
            renderOptions.usePatternBitmaps = true;
            renderOptions.renderTemplates = RenderTemplateOption.MapAndTemplates;
            renderOptions.minResolution = mapBounds.Width / (float)pixelWidth;

            // Render the map to a bitmap using the Skia backend.
            CmykColor white = CmykColor.FromCmyk(0, 0, 0, 0);
            using (Skia_BitmapGraphicsTarget grTarget = new Skia_BitmapGraphicsTarget(pixelWidth, pixelHeight, false, white, mapBounds, true)) {
                grTarget.PushAntiAliasing(true);

                using (map.Read()) {
                    map.Draw(grTarget, mapBounds, renderOptions, null);
                }

                using (Skia_Bitmap skiaBitmap = (Skia_Bitmap)grTarget.FinishBitmap()) {
                    // Write the bitmap to a PNG file.
                    using (FileStream stream = new FileStream(destFile, FileMode.Create, FileAccess.Write)) {
                        skiaBitmap.WritePngToStream(0, 0, skiaBitmap.PixelWidth, skiaBitmap.PixelHeight, stream);
                    }
                }
            }
        }
    }
}
