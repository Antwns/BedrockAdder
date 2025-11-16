using BedrockAdder.ConverterWorker.ObjectWorker;   // ModelBuilderWorker, IModelIconRenderer
using BedrockAdder.Library;
using BedrockAdder.Managers;
using BedrockAdder.Renderer;                       // CefOffscreenIconRenderer
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using YamlDotNet.Core.Tokens;

namespace BedrockAdder.ConverterWorker.BuilderWorker
{
    internal static class CustomBlockBuilderWorker
    {
        // Convenience: build ALL blocks with a renderer created from render.html
        public static void BuildCustomBlocks(PackSession session, int iconSize, string itemsAdderFolder)
        {
            string renderHtmlAbs = Path.Combine(AppContext.BaseDirectory, "Renderer", "cef", "render.html");

            if (session == null)
            {
                ConsoleWorker.Write.Line("error", "CustomBlockBuilderWorker: session is null");
                return;
            }

            if (Lists.CustomBlocks == null || Lists.CustomBlocks.Count == 0)
            {
                ConsoleWorker.Write.Line("info", "CustomBlockBuilderWorker: no custom blocks to build.");
                return;
            }

            IModelIconRenderer renderer = new CefOffscreenIconRenderer(iconSize, true, renderHtmlAbs);

            foreach (CustomBlock block in Lists.CustomBlocks)
            {
                try
                {
                    BuildOneBlock(session, block, renderer);
                }
                catch (Exception ex)
                {
                    ConsoleWorker.Write.Line("error", "BuildOneBlock (block) failed: " + ex.Message);
                }
            }
        }

        public static void BuildOneBlock(PackSession session, CustomBlock block, IModelIconRenderer renderer)
        {
            if (block == null)
            {
                ConsoleWorker.Write.Line("warn", "BuildOneBlock: block is null");
                return;
            }

            string ns = string.IsNullOrWhiteSpace(block.BlockNamespace) ? "unknown" : block.BlockNamespace;
            string id = string.IsNullOrWhiteSpace(block.BlockItemID) ? "unknown" : block.BlockItemID;

            string bedrockId = BedrockManager.MakeBedrockItemId(ns, id);
            string atlasKey = "ia_block_" + Sanitize(ns) + "_" + Sanitize(id);
            string iconAtlasRel = "textures/items/" + Sanitize(ns) + "/" + Sanitize(id) + ".png";
            string iconAbs = Path.Combine(session.PackRoot, iconAtlasRel.Replace('/', Path.DirectorySeparatorChar));

            BedrockManager.EnsureDir(Path.Combine(session.PackRoot, "items"));
            BedrockManager.EnsureDir(Path.Combine(session.PackRoot, "attachables"));
            BedrockManager.EnsureDir(Path.Combine(session.PackRoot, "models", "entity"));
            BedrockManager.EnsureDir(Path.Combine(session.PackRoot, "textures", "items"));
            BedrockManager.EnsureDir(Path.Combine(session.PackRoot, "textures", "models", ns));

            string? iconSourceAbs = null;

            // ---------- 3D GEOMETRY / MODEL TEXTURES (for 3D blocks) ----------

            if (block.Is3D && !string.IsNullOrWhiteSpace(block.ModelPath) && File.Exists(block.ModelPath))
            {
                var built = ModelBuilderWorker.Build(block, iconRenderer: null);

                // 1) Write geometry (.geo.json) if present
                if (!string.IsNullOrWhiteSpace(built.GeometryJson))
                {
                    string geoAbs = Path.Combine(session.PackRoot, built.GeometryOutRel.Replace('/', Path.DirectorySeparatorChar));
                    string? geoDir = Path.GetDirectoryName(geoAbs);
                    if (!string.IsNullOrWhiteSpace(geoDir))
                        Directory.CreateDirectory(geoDir);

                    File.WriteAllText(geoAbs, built.GeometryJson);
                    ConsoleWorker.Write.Line("info", ns + ":" + id + " wrote geometry " + built.GeometryOutRel);
                }

                // 2) Copy textures used by the 3D model
                foreach (var (srcAbs, dstRel) in built.TextureCopies)
                {
                    var fromAbs = srcAbs;
                    var toRel = dstRel;
                    // Build absolute output path from relative
                    var toAbs = Path.Combine(session.PackRoot, toRel.Replace('/', Path.DirectorySeparatorChar));
                    string? toDir = Path.GetDirectoryName(toAbs);
                    if (!string.IsNullOrWhiteSpace(toDir))
                        Directory.CreateDirectory(toDir);
                    try
                    {
                        File.Copy(fromAbs, toAbs, true);
                        ConsoleWorker.Write.Line("info", ns + ":" + id + " copied texture " + fromAbs + " → " + toRel);
                    }
                    catch (Exception ex)
                    {
                        ConsoleWorker.Write.Line("warn", ns + ":" + id + " failed copying texture " + fromAbs + " → " + toRel + " ex=" + ex.Message);
                    }
                }

                // ---------- 3D ICON RENDERING (for 3D blocks) ----------

                bool hasModel = !string.IsNullOrWhiteSpace(block.ModelPath) && File.Exists(block.ModelPath);
                if (hasModel)
                {
                    var texMap = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var kv in block.ModelTexturePaths)
                    {
                        if (!string.IsNullOrWhiteSpace(kv.Value))
                            texMap[kv.Key] = kv.Value;
                    }

                    if (texMap.Count == 0 && !string.IsNullOrWhiteSpace(block.TexturePath))
                    {
                        texMap["default"] = block.TexturePath;
                    }

                    if (texMap.Count > 0)
                    {
                        try
                        {
                            bool rendered = renderer.TryRenderIcon(block.ModelPath, texMap, iconAbs);
                            if (rendered && File.Exists(iconAbs))
                            {
                                iconSourceAbs = iconAbs;
                                ConsoleWorker.Write.Line(
                                    "info",
                                    ns + ":" + id + " 3D block icon rendered → " + iconAtlasRel);
                            }
                            else
                            {
                                ConsoleWorker.Write.Line(
                                    "warn",
                                    ns + ":" + id + " 3D block icon render failed, will fall back to flat texture.");
                            }
                        }
                        catch (Exception ex)
                        {
                            ConsoleWorker.Write.Line(
                                "warn",
                                ns + ":" + id + " exception while rendering block icon: " + ex.Message);
                        }
                    }
                }
            }

            // ---------- CUBE ICON RENDER (for non-3D cuboid blocks) ----------

            if (iconSourceAbs == null && !block.Is3D)
            {
                string cubeModelPath = Path.Combine(AppContext.BaseDirectory, "Library", "example_cuboid.json");
                if (File.Exists(cubeModelPath))
                {
                    var texMap = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    if (block.PerFaceTexture && block.FaceTexturePaths != null && block.FaceTexturePaths.Count > 0)
                    {
                        foreach (var kv in block.FaceTexturePaths)
                        {
                            if (!string.IsNullOrWhiteSpace(kv.Value) && File.Exists(kv.Value))
                            {
                                texMap[kv.Key] = kv.Value;
                            }
                        }
                    }

                    if (texMap.Count == 0 &&
                        !string.IsNullOrWhiteSpace(block.TexturePath) &&
                        File.Exists(block.TexturePath))
                    {
                        texMap["default"] = block.TexturePath;
                    }

                    if (texMap.Count > 0)
                    {
                        try
                        {
                            bool rendered = renderer.TryRenderIcon(cubeModelPath, texMap, iconAbs);
                            if (rendered && File.Exists(iconAbs))
                            {
                                iconSourceAbs = iconAbs;
                                ConsoleWorker.Write.Line(
                                    "info",
                                    ns + ":" + id + " cube block icon rendered → " + iconAtlasRel);
                            }
                            else
                            {
                                ConsoleWorker.Write.Line(
                                    "warn",
                                    ns + ":" + id + " cube block icon render failed, will fall back to flat texture.");
                            }
                        }
                        catch (Exception ex)
                        {
                            ConsoleWorker.Write.Line(
                                "warn",
                                ns + ":" + id + " exception while rendering cube block icon: " + ex.Message);
                        }
                    }
                }
                else
                {
                    ConsoleWorker.Write.Line(
                        "warn",
                        "Cube model not found at " + cubeModelPath + " – skipping cube icon render for " + ns + ":" + id);
                }
            }

            // ---------- FLAT ICON FALLBACKS (for non-3D or failed 3D / cube render) ----------

            if (iconSourceAbs == null)
            {
                // User-provided icon override (inventory-only)
                if (!string.IsNullOrWhiteSpace(block.IconPath) && File.Exists(block.IconPath))
                {
                    iconSourceAbs = block.IconPath;
                    ConsoleWorker.Write.Line(
                        "info",
                        ns + ":" + id + " icon override → " + block.IconPath);
                }
                else if (!string.IsNullOrWhiteSpace(block.TexturePath) && File.Exists(block.TexturePath))
                {
                    // Use the main block texture as the inventory icon
                    iconSourceAbs = block.TexturePath;
                    ConsoleWorker.Write.Line(
                        "info",
                        ns + ":" + id + " icon from TexturePath → " + block.TexturePath);
                }
                else
                {
                    ConsoleWorker.Write.Line(
                        "error",
                        ns + ":" + id + " has no usable icon or texture; skipping icon creation.");
                }
            }

            // ---------- COPY ICON INTO PACK ----------

            // ---------- COPY ICON INTO PACK ----------

            if (!string.IsNullOrWhiteSpace(iconSourceAbs) && File.Exists(iconSourceAbs))
            {
                string? iconDir = Path.GetDirectoryName(iconAbs);
                if (!string.IsNullOrWhiteSpace(iconDir))
                    Directory.CreateDirectory(iconDir);

                // If the renderer already wrote directly to iconAbs, don't copy again.
                if (string.Equals(iconSourceAbs, iconAbs, StringComparison.OrdinalIgnoreCase))
                {
                    ConsoleWorker.Write.Line(
                        "info",
                        ns + ":" + id + " icon already at target path " + iconAbs + ", skipping copy.");
                }
                else
                {
                    try
                    {
                        File.Copy(iconSourceAbs, iconAbs, true);
                    }
                    catch (Exception ex)
                    {
                        ConsoleWorker.Write.Line(
                            "warn",
                            ns + ":" + id + " failed copying block icon " + iconSourceAbs + " → " + iconAbs +
                            " ex=" + ex.Message);
                    }
                }
            }

            // ---------- UPDATE item_texture.json ----------

            try
            {
                string atlasPath = Path.Combine(session.PackRoot, "textures", "item_texture.json");
                JObject atlasRoot;
                if (File.Exists(atlasPath))
                {
                    atlasRoot = JObject.Parse(File.ReadAllText(atlasPath));
                }
                else
                {
                    atlasRoot = new JObject
                    {
                        ["resource_pack_name"] = "BedrockAdder",
                        ["texture_name"] = "atlas.items",
                        ["texture_data"] = new JObject()
                    };
                }

                if (atlasRoot["texture_data"] is not JObject texData)
                {
                    texData = new JObject();
                    atlasRoot["texture_data"] = texData;
                }

                var entry = new JObject
                {
                    ["textures"] = new JArray(iconAtlasRel)
                };
                texData[atlasKey] = entry;

                File.WriteAllText(atlasPath, atlasRoot.ToString(Formatting.Indented));
                ConsoleWorker.Write.Line(
                    "info",
                    ns + ":" + id + " item_texture.json → " + atlasKey + " = " + iconAtlasRel);
            }
            catch (Exception ex)
            {
                ConsoleWorker.Write.Line(
                    "warn",
                    ns + ":" + id + " failed updating item_texture.json: " + ex.Message);
            }
        }

        private static string Sanitize(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "unknown";
            var sb = new System.Text.StringBuilder();
            foreach (char ch in s.Trim())
            {
                if (char.IsLetterOrDigit(ch) || ch == '_')
                    sb.Append(char.ToLowerInvariant(ch));
                else
                    sb.Append('_');
            }
            return sb.ToString();
        }
    }
}