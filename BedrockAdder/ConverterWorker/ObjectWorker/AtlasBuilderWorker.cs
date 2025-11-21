using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Drawing;
using BedrockAdder.Library;

namespace BedrockAdder.ConverterWorker.ObjectWorker
{
    internal sealed class AtlasRegion
    {
        public string Slot { get; set; } = "";
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }

        public override string ToString()
        {
            return Slot + " @ (" + X + "," + Y + ") " + Width + "x" + Height;
        }
    }

    internal sealed class AtlasBuildResult
    {
        public bool Success { get; set; }
        public string? AtlasPngAbs { get; set; }
        public int AtlasWidth { get; set; }
        public int AtlasHeight { get; set; }
        public Dictionary<string, AtlasRegion> Regions { get; } =
            new Dictionary<string, AtlasRegion>(StringComparer.OrdinalIgnoreCase);
        public List<string> Notes { get; } = new List<string>();
    }

    /// <summary>
    /// Bakes multiple PNG textures into a single atlas PNG and reports
    /// where each original texture ended up inside the atlas.
    ///
    /// Typical usage for furniture:
    ///  1) Call BuildAtlasFromTextures(furniture.TexturePaths, atlasPath)
    ///  2) Use result.Regions[slot] to recompute UVs into atlas space
    ///  3) Use result.AtlasPngAbs as the only texture for the Bedrock model & HTML renderer
    /// </summary>
    internal static class AtlasBuilderWorker
    {
        /// <summary>
        /// High-level helper for furniture: directly bakes an atlas from a CustomFurniture instance.
        /// </summary>
        public static AtlasBuildResult BuildAtlasFromFurniture(CustomFurniture furniture, string atlasPngAbs)
        {
            if (furniture == null)
            {
                return new AtlasBuildResult
                {
                    Success = false,
                    Notes = { "Furniture is null in BuildAtlasFromFurniture." }
                };
            }

            return BuildAtlasFromTextures(furniture.TexturePaths, atlasPngAbs,
                "furniture " + furniture.FurnitureNamespace + ":" + furniture.FurnitureItemID);
        }

        /// <summary>
        /// Core API: bakes the given texture slots into a single atlas PNG.
        /// keys = slot names (e.g. "2", "3", "4", "particle")
        /// values = absolute PNG paths.
        /// </summary>
        public static AtlasBuildResult BuildAtlasFromTextures(
            IReadOnlyDictionary<string, string> textureSlotsAbs,
            string atlasPngAbs,
            string? debugName = null)
        {
            var result = new AtlasBuildResult();

            if (textureSlotsAbs == null || textureSlotsAbs.Count == 0)
            {
                result.Success = false;
                result.Notes.Add("No textures supplied to BuildAtlasFromTextures.");
                return result;
            }

            debugName ??= "atlas";

            // --- Step 1: Load all existing PNGs into memory ---

            var entries = new List<(string Slot, string Path, Bitmap Bitmap)>();

            foreach (var kv in textureSlotsAbs)
            {
                string slot = kv.Key;
                string path = kv.Value;

                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    result.Notes.Add("Missing PNG for slot \"" + slot + "\" at path \"" + path + "\" - skipping.");
                    continue;
                }

                try
                {
                    // Clone into our own Bitmap so callers can safely dispose their copies
                    var bmp = new Bitmap(path);
                    entries.Add((slot, path, bmp));
                }
                catch (Exception ex)
                {
                    result.Notes.Add("Failed loading PNG for slot \"" + slot + "\" at \"" + path + "\": " + ex.Message);
                }
            }

            if (entries.Count == 0)
            {
                result.Success = false;
                result.Notes.Add("No valid PNGs loaded; aborting atlas build.");
                return result;
            }

            // --- Step 2: Simple row-by-row packing ---

            // Sort bigger textures first (height desc) to make packing slightly better
            entries.Sort((a, b) => b.Bitmap.Height.CompareTo(a.Bitmap.Height));

            int padding = 2; // small padding to avoid bleeding
            int curX = 0;
            int curY = 0;
            int rowHeight = 0;
            int maxWidth = 0;

            var placements = new List<(string Slot, Bitmap Bitmap, int X, int Y)>();

            foreach (var e in entries)
            {
                int w = e.Bitmap.Width;
                int h = e.Bitmap.Height;

                if (rowHeight == 0)
                    rowHeight = h;

                // Start a new row if this one would become too tall-misaligned
                if (curX > 0 && (curY + h > curY + rowHeight))
                {
                    // new row
                    curX = 0;
                    curY += rowHeight + padding;
                    rowHeight = h;
                }

                placements.Add((e.Slot, e.Bitmap, curX, curY));

                curX += w + padding;
                if (curX > maxWidth)
                    maxWidth = curX;

                if (h > rowHeight)
                    rowHeight = h;
            }

            int atlasWidth = maxWidth > 0 ? maxWidth : entries.Max(e => e.Bitmap.Width);
            int atlasHeight = curY + rowHeight;

            if (atlasWidth <= 0 || atlasHeight <= 0)
            {
                result.Success = false;
                result.Notes.Add("Computed invalid atlas size " + atlasWidth + "x" + atlasHeight + ".");
                DisposeAll(entries);
                return result;
            }

            result.AtlasWidth = atlasWidth;
            result.AtlasHeight = atlasHeight;

            // --- Step 3: Render the atlas bitmap ---

            try
            {
                string? atlasDir = Path.GetDirectoryName(atlasPngAbs);
                if (!string.IsNullOrWhiteSpace(atlasDir))
                    Directory.CreateDirectory(atlasDir);

                using (var atlas = new Bitmap(atlasWidth, atlasHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                using (var g = Graphics.FromImage(atlas))
                {
                    g.Clear(Color.Transparent);

                    foreach (var p in placements)
                    {
                        g.DrawImage(p.Bitmap, p.X, p.Y, p.Bitmap.Width, p.Bitmap.Height);

                        var region = new AtlasRegion
                        {
                            Slot = p.Slot,
                            X = p.X,
                            Y = p.Y,
                            Width = p.Bitmap.Width,
                            Height = p.Bitmap.Height
                        };

                        result.Regions[p.Slot] = region;
                    }

                    atlas.Save(atlasPngAbs, System.Drawing.Imaging.ImageFormat.Png);
                }

                result.AtlasPngAbs = atlasPngAbs;
                result.Success = true;
                result.Notes.Add("Built atlas \"" + debugName + "\" " +
                                 atlasWidth + "x" + atlasHeight +
                                 " with " + placements.Count + " textures → " + atlasPngAbs);

                ConsoleWorker.Write.Line("info",
                    "AtlasBuilderWorker: built " + debugName + " atlas " +
                    atlasWidth + "x" + atlasHeight + " (" + placements.Count + " textures)");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Notes.Add("Exception while writing atlas PNG: " + ex.Message);
                ConsoleWorker.Write.Line("error",
                    "AtlasBuilderWorker: failed building " + debugName + " atlas: " + ex.Message);
            }
            finally
            {
                DisposeAll(entries);
            }

            return result;
        }

        private static void DisposeAll(IEnumerable<(string Slot, string Path, Bitmap Bitmap)> entries)
        {
            foreach (var e in entries)
            {
                try { e.Bitmap.Dispose(); }
                catch { /* ignore */ }
            }
        }
    }
}