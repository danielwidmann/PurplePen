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
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using DotSpatial.Projections;
using PurplePen.Graphics2D;
using PurplePen.MapModel;
using SkiaSharp;

namespace PurplePen.MapConverter
{
    /// <summary>
    /// Command line utility to convert orienteering map files (.ocd, .omap) to image files
    /// using the Skia rendering backend for cross-platform support.
    /// 
    /// Usage: MapConverter [options] &lt;source_map&gt; &lt;destination_image&gt;
    /// </summary>
    public class Program
    {
        public static int Main(string[] args)
        {
            float dpi = 200;
            int quality = 80;
            bool antiAlias = true;
            bool worldFile = false;
            bool cmyk = false;
            bool kmz = false;
            int worldFileEpsg = 0;
            string format = null;
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
                else if (args[i] == "--quality" && i + 1 < args.Length) {
                    if (!int.TryParse(args[i + 1], out quality) || quality < 1 || quality > 100) {
                        Console.Error.WriteLine("Error: Quality must be between 1 and 100.");
                        return 1;
                    }
                    i += 2;
                }
                else if (args[i] == "--format" && i + 1 < args.Length) {
                    format = args[i + 1].ToLowerInvariant();
                    if (format != "png" && format != "jpg" && format != "gif") {
                        Console.Error.WriteLine("Error: Format must be png, jpg, or gif.");
                        return 1;
                    }
                    i += 2;
                }
                else if (args[i] == "--no-anti-alias") {
                    antiAlias = false;
                    i++;
                }
                else if (args[i] == "--world-file") {
                    worldFile = true;
                    i++;
                }
                else if (args[i] == "--world-file-epsg" && i + 1 < args.Length) {
                    if (!int.TryParse(args[i + 1], out worldFileEpsg) || worldFileEpsg <= 0) {
                        Console.Error.WriteLine("Error: Invalid EPSG code '{0}'.", args[i + 1]);
                        return 1;
                    }
                    worldFile = true;
                    i += 2;
                }
                else if (args[i] == "--cmyk") {
                    cmyk = true;
                    i++;
                }
                else if (args[i] == "--kmz") {
                    kmz = true;
                    i++;
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

            string sourceExtension = Path.GetExtension(sourceFile).ToLowerInvariant();
            if (sourceExtension != ".ocd" && sourceExtension != ".omap") {
                Console.Error.WriteLine("Error: Source file must be an .ocd or .omap file.");
                return 1;
            }

            // Determine output format from --format flag or destination file extension.
            if (format == null) {
                string destExtension = Path.GetExtension(destFile).ToLowerInvariant();
                if (destExtension == ".jpg" || destExtension == ".jpeg") {
                    format = "jpg";
                }
                else if (destExtension == ".gif") {
                    format = "gif";
                }
                else {
                    format = "png";
                }
            }

            // Auto-detect KMZ from the destination file extension.
            if (!kmz && Path.GetExtension(destFile).ToLowerInvariant() == ".kmz") {
                kmz = true;
            }

            try {
                ConvertMap(sourceFile, destFile, dpi, format, quality, antiAlias, worldFile, cmyk, worldFileEpsg, kmz);
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
            Console.Error.WriteLine("Usage: MapConverter [options] <source_map> <destination_image>");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Options:");
            Console.Error.WriteLine("  --dpi <dpi>         Output resolution in dots per inch (default: 200)");
            Console.Error.WriteLine("  --format <fmt>      Output format: png, jpg, gif (default: from file extension)");
            Console.Error.WriteLine("  --quality <1-100>   Image quality for JPEG output (default: 80)");
            Console.Error.WriteLine("  --no-anti-alias     Disable anti-aliasing");
            Console.Error.WriteLine("  --world-file        Create a world file for georeferencing");
            Console.Error.WriteLine("  --world-file-epsg <epsg>  Create a world file reprojected to the given EPSG");
            Console.Error.WriteLine("                      (e.g., 3857 for Web Mercator)");
            Console.Error.WriteLine("  --cmyk              Use CMYK color mode with overprint blending");
            Console.Error.WriteLine("  --kmz               Create a KMZ file with the map as a ground overlay");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Arguments:");
            Console.Error.WriteLine("  <source_map>        Input map file (.ocd or .omap)");
            Console.Error.WriteLine("  <destination_image>  Output image file path");
        }

        /// <summary>
        /// Converts a map file to an image using the Skia rendering backend.
        /// </summary>
        /// <param name="sourceFile">Path to the source map file (.ocd or .omap).</param>
        /// <param name="destFile">Path to the output image file.</param>
        /// <param name="dpi">Resolution in dots per inch.</param>
        /// <param name="format">Output format: "png", "jpg", or "gif".</param>
        /// <param name="quality">Image quality for JPEG output (1-100).</param>
        /// <param name="antiAlias">Whether to enable anti-aliasing.</param>
        /// <param name="worldFile">Whether to create a world file for georeferencing.</param>
        /// <param name="cmyk">Whether to use CMYK color mode with overprint blending.</param>
        /// <param name="worldFileEpsg">EPSG code for world file reprojection (0 = use map's native CRS).</param>
        /// <param name="kmz">Whether to create a KMZ file with the map as a ground overlay.</param>
        static void ConvertMap(string sourceFile, string destFile, float dpi, string format, int quality, bool antiAlias, bool worldFile, bool cmyk, int worldFileEpsg, bool kmz)
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
            renderOptions.blendOverprintedColors = cmyk;

            // Render the map to a bitmap using the Skia backend.
            CmykColor white = CmykColor.FromCmyk(0, 0, 0, 0);
            byte[] imageBytes = null;
            using (Skia_BitmapGraphicsTarget grTarget = new Skia_BitmapGraphicsTarget(pixelWidth, pixelHeight, false, white, mapBounds, true)) {
                grTarget.PushAntiAliasing(antiAlias);

                using (map.Read()) {
                    map.Draw(grTarget, mapBounds, renderOptions, null);
                }

                using (Skia_Bitmap skiaBitmap = (Skia_Bitmap)grTarget.FinishBitmap()) {
                    imageBytes = EncodeImage(skiaBitmap, format, quality);

                    // If the output is a .kmz file, don't write the raw image to the .kmz path;
                    // the KMZ creation below will handle the output file.
                    bool destIsKmz = Path.GetExtension(destFile).ToLowerInvariant() == ".kmz";
                    if (!destIsKmz) {
                        using (FileStream stream = new FileStream(destFile, FileMode.Create, FileAccess.Write)) {
                            stream.Write(imageBytes, 0, imageBytes.Length);
                        }
                    }
                }
            }

            // Create world file if requested and the map has real-world coordinates.
            if (worldFile) {
                CreateWorldFile(map, destFile, mapBounds, pixelWidth, pixelHeight, worldFileEpsg);
            }

            // Create KMZ file if requested.
            if (kmz) {
                CreateKmzFile(map, destFile, mapBounds, imageBytes, format);
            }
        }

        /// <summary>
        /// Encodes a Skia bitmap to a byte array in the specified format.
        /// </summary>
        /// <param name="skiaBitmap">The rendered bitmap.</param>
        /// <param name="format">Image format: "png", "jpg", or "gif".</param>
        /// <param name="quality">Encoding quality (1-100), used for JPEG.</param>
        /// <returns>The encoded image data as a byte array.</returns>
        static byte[] EncodeImage(Skia_Bitmap skiaBitmap, string format, int quality)
        {
            SKEncodedImageFormat skFormat;
            switch (format) {
                case "jpg":
                    skFormat = SKEncodedImageFormat.Jpeg;
                    break;
                case "gif":
                    skFormat = SKEncodedImageFormat.Gif;
                    break;
                default:
                    skFormat = SKEncodedImageFormat.Png;
                    break;
            }

            SKBitmap bitmap = skiaBitmap.Bitmap;
            using (SKImage image = SKImage.FromBitmap(bitmap)) {
                using (SKData data = image.Encode(skFormat, quality)) {
                    if (data == null) {
                        throw new InvalidOperationException(
                            string.Format("Failed to encode image as {0}. This format may not be supported on this platform.", format));
                    }
                    return data.ToArray();
                }
            }
        }

        /// <summary>
        /// Creates a world file for georeferencing, using the map's real-world coordinates.
        /// The world file contains six parameters that define the affine transformation
        /// from pixel coordinates to real-world coordinates.
        /// See https://en.wikipedia.org/wiki/World_file
        /// </summary>
        /// <param name="map">The loaded map with coordinate information.</param>
        /// <param name="imageFile">Path to the output image file (world file is created alongside).</param>
        /// <param name="mapBounds">The map area that was rendered.</param>
        /// <param name="pixelWidth">Width of the rendered image in pixels.</param>
        /// <param name="pixelHeight">Height of the rendered image in pixels.</param>
        /// <param name="targetEpsg">EPSG code for the target coordinate system (0 = use map's native CRS).</param>
        static void CreateWorldFile(Map map, string imageFile, RectangleF mapBounds, int pixelWidth, int pixelHeight, int targetEpsg)
        {
            RealWorldCoords realWorldCoords;
            float mapScale;
            using (map.Read()) {
                realWorldCoords = map.RealWorldCoords;
                mapScale = map.MapScale;
            }

            if (!realWorldCoords.RealWorldOn &&
                realWorldCoords.RealWorldAngle == 0 &&
                realWorldCoords.RealWorldOffsetX == 0 &&
                realWorldCoords.RealWorldOffsetY == 0) {
                Console.Error.WriteLine("Warning: Map has no real-world coordinates. World file not created.");
                return;
            }

            // Compute the world file extension based on the image file extension.
            // Standard convention: 2nd char, 4th char, then 'w'. E.g. .png -> .pgw, .jpg -> .jgw
            string imageExtension = Path.GetExtension(imageFile);
            string worldExtension;
            if (imageExtension.Length == 4) {
                // Standard convention: 2nd char, 4th char, then 'w'. E.g. .png -> .pgw, .jpg -> .jgw
                worldExtension = "." + imageExtension[1] + imageExtension[3] + "w";
            }
            else {
                worldExtension = imageExtension + "w";
            }
            string worldFileName = Path.ChangeExtension(imageFile, worldExtension);

            // Compute transformation from pixel coords to map coords using the rectangle transform.
            Matrix transform = Geometry.CreateInvertedRectangleTransform(
                new RectangleF(0, 0, pixelWidth, pixelHeight), mapBounds);

            PointF[] transformedPoints = Geometry.TransformPoints(
                new PointF[] { new PointF(0, 0), new PointF(1, 0), new PointF(0, 1) }, transform);

            // Convert map coords to real-world coords.
            double gridScaleFactor = realWorldCoords.GridScaleFactor;
            double scaleFactor = gridScaleFactor * mapScale / 1000.0;
            double angRad = (-realWorldCoords.RealWorldAngle * Math.PI) / 180.0;

            double[] realX = new double[3];
            double[] realY = new double[3];
            for (int idx = 0; idx < 3; idx++) {
                double x = transformedPoints[idx].X * scaleFactor;
                double y = transformedPoints[idx].Y * scaleFactor;
                realX[idx] = x * Math.Cos(angRad) - y * Math.Sin(angRad) + realWorldCoords.RealWorldOffsetX - realWorldCoords.RealWorldLocalOffsetX;
                realY[idx] = x * Math.Sin(angRad) + y * Math.Cos(angRad) + realWorldCoords.RealWorldOffsetY - realWorldCoords.RealWorldLocalOffsetY;
            }

            // If a target EPSG is specified, reproject the coordinates.
            if (targetEpsg > 0) {
                if (realWorldCoords.ProjectionType != MapProjectionType.Known) {
                    Console.Error.WriteLine("Warning: Map projection is unknown. Cannot reproject to EPSG:{0}. World file not created.", targetEpsg);
                    return;
                }

                string targetProj4 = PurplePen.MapModel.Projections.AuthorityCodeHandler.Instance["EPSG:" + targetEpsg.ToString()];
                if (targetProj4 == null) {
                    Console.Error.WriteLine("Warning: EPSG:{0} is not recognized. World file not created.", targetEpsg);
                    return;
                }

                ProjectionInfo sourceProj = ProjectionInfo.FromProj4String(realWorldCoords.Proj4String);
                ProjectionInfo destProj = ProjectionInfo.FromProj4String(targetProj4);

                // Reproject the three reference points from the map's CRS to the target CRS.
                // Elevation is 0 since orienteering maps use 2D planar coordinates.
                for (int idx = 0; idx < 3; idx++) {
                    double[] xy = { realX[idx], realY[idx] };
                    double[] z = { 0 };
                    Reproject.ReprojectPoints(xy, z, sourceProj, destProj, 0, 1);
                    realX[idx] = xy[0];
                    realY[idx] = xy[1];
                }
            }

            // World file format: a, d, b, e, c, f
            // where (c,f) is the real-world coord of pixel (0,0),
            // (a,d) is the change per pixel moving right,
            // (b,e) is the change per pixel moving down.
            double a = realX[1] - realX[0];
            double d = realY[1] - realY[0];
            double b = realX[2] - realX[0];
            double e = realY[2] - realY[0];
            double c = realX[0];
            double f = realY[0];

            using (TextWriter writer = new StreamWriter(worldFileName)) {
                writer.WriteLine(a.ToString("F10", CultureInfo.InvariantCulture));
                writer.WriteLine(d.ToString("F10", CultureInfo.InvariantCulture));
                writer.WriteLine(b.ToString("F10", CultureInfo.InvariantCulture));
                writer.WriteLine(e.ToString("F10", CultureInfo.InvariantCulture));
                writer.WriteLine(c.ToString("F5", CultureInfo.InvariantCulture));
                writer.WriteLine(f.ToString("F5", CultureInfo.InvariantCulture));
            }

            if (targetEpsg > 0) {
                Console.WriteLine("Created world file '{0}' (EPSG:{1}).", worldFileName, targetEpsg);
            }
            else {
                Console.WriteLine("Created world file '{0}'.", worldFileName);
            }
        }

        /// <summary>
        /// Converts a paper coordinate (in mm) to WGS84 latitude/longitude using the map's
        /// real-world coordinates and projection.
        /// </summary>
        /// <param name="paperCoord">The paper coordinate in mm.</param>
        /// <param name="realWorldCoords">The map's real-world coordinate settings.</param>
        /// <param name="mapScale">The map scale.</param>
        /// <param name="sourceProj">The source projection info.</param>
        /// <param name="wgs84Proj">The WGS84 projection info.</param>
        /// <param name="latitude">Output latitude in degrees.</param>
        /// <param name="longitude">Output longitude in degrees.</param>
        static void PaperToLatLon(PointF paperCoord, RealWorldCoords realWorldCoords, float mapScale,
            ProjectionInfo sourceProj, ProjectionInfo wgs84Proj, out double latitude, out double longitude)
        {
            double gridScaleFactor = realWorldCoords.GridScaleFactor;
            double scaleFactor = gridScaleFactor * mapScale / 1000.0;
            double angRad = (-realWorldCoords.RealWorldAngle * Math.PI) / 180.0;

            double x = paperCoord.X * scaleFactor;
            double y = paperCoord.Y * scaleFactor;
            double realX = x * Math.Cos(angRad) - y * Math.Sin(angRad) + realWorldCoords.RealWorldOffsetX - realWorldCoords.RealWorldLocalOffsetX;
            double realY = x * Math.Sin(angRad) + y * Math.Cos(angRad) + realWorldCoords.RealWorldOffsetY - realWorldCoords.RealWorldLocalOffsetY;

            double[] xy = { realX, realY };
            double[] z = { 0 };
            Reproject.ReprojectPoints(xy, z, sourceProj, wgs84Proj, 0, 1);
            longitude = xy[0];
            latitude = xy[1];
        }

        /// <summary>
        /// Creates a KMZ file containing the map image as a single (non-tiled) ground overlay.
        /// The KMZ is a ZIP archive containing a doc.kml and the map image.
        /// Requires the map to have a known projection for coordinate conversion to WGS84.
        /// </summary>
        /// <param name="map">The loaded map with coordinate information.</param>
        /// <param name="destFile">Path to the output image file (KMZ is created alongside or as the output).</param>
        /// <param name="mapBounds">The map area that was rendered.</param>
        /// <param name="imageBytes">The encoded image data.</param>
        /// <param name="format">The image format ("png", "jpg", or "gif").</param>
        static void CreateKmzFile(Map map, string destFile, RectangleF mapBounds, byte[] imageBytes, string format)
        {
            RealWorldCoords realWorldCoords;
            float mapScale;
            using (map.Read()) {
                realWorldCoords = map.RealWorldCoords;
                mapScale = map.MapScale;
            }

            if (!realWorldCoords.RealWorldOn &&
                realWorldCoords.RealWorldAngle == 0 &&
                realWorldCoords.RealWorldOffsetX == 0 &&
                realWorldCoords.RealWorldOffsetY == 0) {
                Console.Error.WriteLine("Warning: Map has no real-world coordinates. KMZ file not created.");
                return;
            }

            if (realWorldCoords.ProjectionType != MapProjectionType.Known) {
                Console.Error.WriteLine("Warning: Map projection is unknown. Cannot create KMZ file.");
                return;
            }

            ProjectionInfo sourceProj = ProjectionInfo.FromProj4String(realWorldCoords.Proj4String);
            ProjectionInfo wgs84Proj = ProjectionInfo.FromProj4String("+proj=longlat +ellps=WGS84 +datum=WGS84 +no_defs");

            // Convert the four corners of the map bounds to lat/lon.
            double lat0, lon0, lat1, lon1, lat2, lon2, lat3, lon3;
            PointF topLeft = new PointF(mapBounds.Left, mapBounds.Top);
            PointF topRight = new PointF(mapBounds.Right, mapBounds.Top);
            PointF bottomLeft = new PointF(mapBounds.Left, mapBounds.Bottom);
            PointF bottomRight = new PointF(mapBounds.Right, mapBounds.Bottom);

            PaperToLatLon(topLeft, realWorldCoords, mapScale, sourceProj, wgs84Proj, out lat0, out lon0);
            PaperToLatLon(topRight, realWorldCoords, mapScale, sourceProj, wgs84Proj, out lat1, out lon1);
            PaperToLatLon(bottomLeft, realWorldCoords, mapScale, sourceProj, wgs84Proj, out lat2, out lon2);
            PaperToLatLon(bottomRight, realWorldCoords, mapScale, sourceProj, wgs84Proj, out lat3, out lon3);

            // Compute the bounding box in WGS84.
            double north = Math.Max(Math.Max(lat0, lat1), Math.Max(lat2, lat3));
            double south = Math.Min(Math.Min(lat0, lat1), Math.Min(lat2, lat3));
            double east = Math.Max(Math.Max(lon0, lon1), Math.Max(lon2, lon3));
            double west = Math.Min(Math.Min(lon0, lon1), Math.Min(lon2, lon3));

            // Compute the rotation angle of the map image relative to north.
            // Scale longitude difference by cos(latitude) to account for the convergence of
            // meridians, which gives a correct bearing at the map's latitude.
            double midTopLat = (lat0 + lat1) / 2.0;
            double midTopLon = (lon0 + lon1) / 2.0;
            double midBotLat = (lat2 + lat3) / 2.0;
            double midBotLon = (lon2 + lon3) / 2.0;
            double midLat = (midTopLat + midBotLat) / 2.0;
            double cosLat = Math.Cos(midLat * Math.PI / 180.0);
            double dLon = (midTopLon - midBotLon) * cosLat;
            double dLat = midTopLat - midBotLat;
            double rotation = Math.Atan2(dLon, dLat) * 180.0 / Math.PI;

            // Determine the image file name inside the KMZ.
            string imageExtension;
            switch (format) {
                case "jpg": imageExtension = ".jpg"; break;
                case "gif": imageExtension = ".gif"; break;
                default:    imageExtension = ".png"; break;
            }
            string imageFileName = "map" + imageExtension;

            // Determine the KMZ file path.
            string kmzFile;
            if (Path.GetExtension(destFile).ToLowerInvariant() == ".kmz") {
                kmzFile = destFile;
            }
            else {
                kmzFile = Path.ChangeExtension(destFile, ".kmz");
            }

            string mapName = Path.GetFileNameWithoutExtension(destFile);

            // Create the KML content.
            byte[] kmlBytes;
            using (MemoryStream ms = new MemoryStream()) {
                XmlWriterSettings xmlSettings = new XmlWriterSettings();
                xmlSettings.Indent = true;
                xmlSettings.Encoding = new UTF8Encoding(false);

                using (XmlWriter xml = XmlWriter.Create(ms, xmlSettings)) {
                    xml.WriteStartDocument();
                    xml.WriteStartElement("kml", "http://www.opengis.net/kml/2.2");
                    xml.WriteStartElement("GroundOverlay");
                    xml.WriteElementString("name", mapName);
                    xml.WriteStartElement("Icon");
                    xml.WriteElementString("href", imageFileName);
                    xml.WriteEndElement(); // Icon
                    xml.WriteStartElement("LatLonBox");
                    xml.WriteElementString("north", north.ToString("F10", CultureInfo.InvariantCulture));
                    xml.WriteElementString("south", south.ToString("F10", CultureInfo.InvariantCulture));
                    xml.WriteElementString("east", east.ToString("F10", CultureInfo.InvariantCulture));
                    xml.WriteElementString("west", west.ToString("F10", CultureInfo.InvariantCulture));
                    xml.WriteElementString("rotation", rotation.ToString("F6", CultureInfo.InvariantCulture));
                    xml.WriteEndElement(); // LatLonBox
                    xml.WriteEndElement(); // GroundOverlay
                    xml.WriteEndElement(); // kml
                    xml.WriteEndDocument();
                }

                kmlBytes = ms.ToArray();
            }

            // Create the KMZ file (a ZIP archive containing doc.kml and the image).
            using (FileStream fs = new FileStream(kmzFile, FileMode.Create, FileAccess.Write)) {
                using (ZipArchive archive = new ZipArchive(fs, ZipArchiveMode.Create)) {
                    // Add doc.kml
                    ZipArchiveEntry kmlEntry = archive.CreateEntry("doc.kml");
                    using (Stream entryStream = kmlEntry.Open()) {
                        entryStream.Write(kmlBytes, 0, kmlBytes.Length);
                    }

                    // Add the image file.
                    ZipArchiveEntry imageEntry = archive.CreateEntry(imageFileName);
                    using (Stream entryStream = imageEntry.Open()) {
                        entryStream.Write(imageBytes, 0, imageBytes.Length);
                    }
                }
            }

            Console.WriteLine("Created KMZ file '{0}'.", kmzFile);
        }
    }
}
