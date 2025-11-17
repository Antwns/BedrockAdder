using System;
using System.Collections.Generic;
using System.IO;
using BedrockAdder.Library;
using BedrockAdder.Renderer; // CefOffscreenIconRenderer lives here

namespace BedrockAdder.ConverterWorker.ObjectWorker
{
    internal sealed class RenderIconResult
    {
        public bool Success { get; set; }
        public string? IconPngAbs { get; set; }          // where the PNG was written
        public string? SuggestedAtlasRel { get; set; }   // e.g. textures/items/ns/id.png
        public List<string> Notes { get; } = new List<string>();
    }

    /// <summary>
    /// Renders a 2D GUI-style icon for 3D Java models (items/blocks/furniture/helmets),
    /// using CefSharp OffScreen + three.js (render.html).
    /// - Uses ONLY fields from your data classes (ModelPath, TexturePath/TexturePaths, IconPath).
    /// - Does NOT modify the Bedrock pack; just returns a PNG path for builders to copy later.
    /// </summary>
    internal static class ModelImageBuilderWorker
    {
        // Factory – construct a renderer once and reuse it across calls
        public static IModelIconRenderer CreateCefRenderer(string renderHtmlAbsolutePath, int size = 256, bool transparent = true)
        {
            return new CefOffscreenIconRenderer(size, transparent, renderHtmlAbsolutePath);
        }

        // -------- Public entrypoints --------

        public static RenderIconResult RenderItemIcon(CustomItem it, string outputDirAbs, IModelIconRenderer renderer)
        {
            var res = ValidateCommon(it?.ModelPath, outputDirAbs, it?.ItemNamespace, it?.ItemID);
            if (res != null) return res;

            string ns = it.ItemNamespace;
            string id = it.ItemID;
            string modelPath = it.ModelPath!;
            var textures = BuildTextureMapForItem(it);

            // IMPORTANT CHANGE:
            // For 3D items we WANT a rendered snapshot, not a direct copy of IconPath/texture.
            // So only use the fast-path copy for NON-3D items.
            if (!it.Is3D && !string.IsNullOrWhiteSpace(it.IconPath) && File.Exists(it.IconPath))
            {
                return CopyProvidedIcon(it.IconPath!, outputDirAbs, ns, id);
            }

            return RenderViaAdapter(modelPath, textures, outputDirAbs, ns, id, renderer);
        }

        public static RenderIconResult RenderBlockIcon(CustomBlock b, string outputDirAbs, IModelIconRenderer renderer)
        {
            var res = ValidateCommon(b?.ModelPath, outputDirAbs, b?.BlockNamespace, b?.BlockItemID);
            if (res != null) return res;

            string ns = b.BlockNamespace;
            string id = b.BlockItemID;
            string modelPath = b.ModelPath!;
            var textures = BuildTextureMapForBlock(b);

            if (!string.IsNullOrWhiteSpace(b.IconPath) && File.Exists(b.IconPath))
            {
                return CopyProvidedIcon(b.IconPath!, outputDirAbs, ns, id);
            }

            return RenderViaAdapter(modelPath, textures, outputDirAbs, ns, id, renderer);
        }

        public static RenderIconResult RenderFurnitureIcon(CustomFurniture f, string outputDirAbs, IModelIconRenderer renderer)
        {
            var res = ValidateCommon(f?.ModelPath, outputDirAbs, f?.FurnitureNamespace, f?.FurnitureItemID);
            if (res != null) return res;

            string ns = f.FurnitureNamespace;
            string id = f.FurnitureItemID;
            string modelPath = f.ModelPath!;
            var textures = BuildTextureMapForFurniture(f);

            if (!string.IsNullOrWhiteSpace(f.IconPath) && File.Exists(f.IconPath))
            {
                return CopyProvidedIcon(f.IconPath!, outputDirAbs, ns, id);
            }

            return RenderViaAdapter(modelPath, textures, outputDirAbs, ns, id, renderer);
        }

        public static RenderIconResult RenderHelmetIcon(CustomArmor a, string outputDirAbs, IModelIconRenderer renderer)
        {
            if (a == null)
            {
                return Fail("Armor is null.");
            }
            if (!string.Equals(a.Slot, "helmet", StringComparison.OrdinalIgnoreCase))
            {
                return Fail("Armor slot is not 'helmet'; skipping render.");
            }

            var res = ValidateCommon(a?.ModelPath, outputDirAbs, a?.ArmorNamespace, a?.ArmorID);
            if (res != null) return res;

            string ns = a.ArmorNamespace;
            string id = a.ArmorID;
            string modelPath = a.ModelPath!;
            var textures = BuildTextureMapForArmor(a);

            if (!string.IsNullOrWhiteSpace(a.IconPath) && File.Exists(a.IconPath))
            {
                return CopyProvidedIcon(a.IconPath!, outputDirAbs, ns, id);
            }

            return RenderViaAdapter(modelPath, textures, outputDirAbs, ns, id, renderer);
        }

        // -------- Core render path --------

        private static RenderIconResult RenderViaAdapter(
            string javaModelPath,
            IReadOnlyDictionary<string, string> textureSlotsAbs,
            string outputDirAbs,
            string ns,
            string id,
            IModelIconRenderer renderer)
        {
            try
            {
                Directory.CreateDirectory(outputDirAbs);
                string fileName = ns + "_" + id + "_icon.png";
                string outAbs = Path.Combine(outputDirAbs, fileName);

                // sanitize: ensure at least one texture exists; renderer handles warnings too
                var clean = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in textureSlotsAbs)
                {
                    if (!string.IsNullOrWhiteSpace(kv.Value) && File.Exists(kv.Value))
                    {
                        clean[kv.Key] = kv.Value;
                    }
                }

                if (clean.Count == 0)
                {
                    var r = Fail("No valid textures available to render icon.");
                    r.SuggestedAtlasRel = Built3DObjectNaming.MakeIconRel(ns, id);
                    return r;
                }

                bool ok = renderer.TryRenderIcon(javaModelPath, clean, outAbs);
                if (!ok || !File.Exists(outAbs))
                {
                    var r = Fail("Renderer failed to produce icon.");
                    r.SuggestedAtlasRel = Built3DObjectNaming.MakeIconRel(ns, id);
                    return r;
                }

                return new RenderIconResult
                {
                    Success = true,
                    IconPngAbs = outAbs,
                    SuggestedAtlasRel = Built3DObjectNaming.MakeIconRel(ns, id)
                };
            }
            catch (Exception ex)
            {
                var r = Fail("RenderViaAdapter exception: " + ex.Message);
                r.SuggestedAtlasRel = Built3DObjectNaming.MakeIconRel(ns, id);
                return r;
            }
        }

        // -------- Helpers --------

        private static RenderIconResult? ValidateCommon(string? modelPath, string outputDirAbs, string? ns, string? id)
        {
            if (string.IsNullOrWhiteSpace(ns) || string.IsNullOrWhiteSpace(id))
            {
                return Fail("Namespace or Id is empty.");
            }
            if (string.IsNullOrWhiteSpace(outputDirAbs))
            {
                return Fail("Output directory is empty.");
            }
            if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
            {
                return Fail("ModelPath is missing or file not found: " + modelPath);
            }
            return null;
        }

        private static RenderIconResult CopyProvidedIcon(string iconAbs, string outputDirAbs, string ns, string id)
        {
            try
            {
                Directory.CreateDirectory(outputDirAbs);
                string dst = Path.Combine(outputDirAbs, ns + "_" + id + "_icon.png");
                File.Copy(iconAbs, dst, true);
                return new RenderIconResult
                {
                    Success = true,
                    IconPngAbs = dst,
                    SuggestedAtlasRel = Built3DObjectNaming.MakeIconRel(ns, id)
                };
            }
            catch (Exception ex)
            {
                return Fail("Failed to copy provided IconPath: " + ex.Message);
            }
        }

        private static RenderIconResult Fail(string msg)
        {
            var r = new RenderIconResult { Success = false };
            r.Notes.Add(msg);
            ConsoleWorker.Write.Line("warn", msg);
            return r;
        }

        private static IReadOnlyDictionary<string, string> BuildTextureMapForItem(CustomItem it)
        {
            if (it.ModelTexturePaths.Count > 0) return it.ModelTexturePaths;

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(it.TexturePath)) map["default"] = it.TexturePath;
            return map;
        }

        private static IReadOnlyDictionary<string, string> BuildTextureMapForBlock(CustomBlock b)
        {
            // 1) Prefer explicit per-face textures if we have them.
            //    These come from the YAML:
            //    graphics:
            //      textures:
            //        up:    ns:path_top
            //        down:  ns:path_bottom
            //        north: ns:path_side
            //        ...
            //
            // CustomBlockExtractorWorker has already resolved those to absolute PNG paths
            // in b.FaceTexturePaths and set b.PerFaceTexture = true.
            if (b.PerFaceTexture && b.FaceTexturePaths != null && b.FaceTexturePaths.Count > 0)
            {
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var kv in b.FaceTexturePaths)
                {
                    var faceName = kv.Key;
                    var absPath = kv.Value;

                    if (string.IsNullOrWhiteSpace(faceName)) continue;
                    if (string.IsNullOrWhiteSpace(absPath)) continue;

                    // Normalise face key: "UP" / "Up" / " up " → "up"
                    faceName = faceName.Trim().ToLowerInvariant(); // up, down, north, south, east, west

                    // Our example cuboid model (and similar) use:
                    //   textures: { up: ..., down: ..., north: ... }
                    //   faces: { up: { "texture": "#up" }, north: { "texture": "#north" }, ... }
                    // The renderer strips the '#' and then looks up textures[faceName].
                    if (!map.ContainsKey(faceName))
                    {
                        map[faceName] = absPath;
                    }
                }

                // If we successfully built a per-face map, use it.
                if (map.Count > 0)
                    return map;
            }

            // 2) Otherwise, fall back to textures resolved from the model JSON.
            //    This covers true 3D models where ModelTexturePaths contains slots like "0", "1", etc.
            if (b.ModelTexturePaths != null && b.ModelTexturePaths.Count > 0)
                return b.ModelTexturePaths;

            // 3) Final fallback: single shared texture.
            var fallback = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(b.TexturePath))
                fallback["default"] = b.TexturePath;

            return fallback;
        }

        private static IReadOnlyDictionary<string, string> BuildTextureMapForFurniture(CustomFurniture f)
        {
            if (f.TexturePaths != null && f.TexturePaths.Count > 0) return f.TexturePaths;
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private static IReadOnlyDictionary<string, string> BuildTextureMapForArmor(CustomArmor a)
        {
            if (a.ModelTexturePaths.Count > 0) return a.ModelTexturePaths;

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(a.TexturePath)) map["default"] = a.TexturePath;
            return map;
        }
    }
}