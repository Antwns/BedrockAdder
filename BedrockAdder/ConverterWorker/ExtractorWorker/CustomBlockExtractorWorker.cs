using BedrockAdder.FileWorker;
using BedrockAdder.Library;
using System;
using System.IO;
using YamlDotNet.RepresentationModel;

namespace BedrockAdder.ExtractorWorker.ConverterWorker
{
    internal static class CustomBlockExtractorWorker
    {
        internal static void ExtractCustomBlocksFromPaths(string itemsAdderFolder, string selectedVersion)
        {
            void LogInfo(string? blockNs, string blockId, string msg)
            {
                string ns = string.IsNullOrWhiteSpace(blockNs) ? "unknown" : blockNs;
                ConsoleWorker.Write.Line("info", ns + ":" + blockId + " " + msg);
            }

            // Resolve selected vanilla version jar (for vanilla textures on 3D blocks)
            string versionDir = string.Empty;
            string vanillaJarPath = string.Empty;

            if (!string.IsNullOrWhiteSpace(selectedVersion) &&
                !selectedVersion.Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                versionDir = Path.Combine(appData, ".minecraft", "versions", selectedVersion);

                if (Directory.Exists(versionDir))
                {
                    string jarCandidate = Path.Combine(versionDir, selectedVersion + ".jar");
                    if (File.Exists(jarCandidate))
                    {
                        vanillaJarPath = jarCandidate;
                        ConsoleWorker.Write.Line("info", "Using vanilla jar for textures: " + vanillaJarPath);
                    }
                    else
                    {
                        ConsoleWorker.Write.Line("warn",
                            "Vanilla jar not found for version " + selectedVersion + " at " + jarCandidate +
                            " — vanilla textures may not resolve.");
                    }
                }
                else
                {
                    ConsoleWorker.Write.Line("warn",
                        "Version directory not found for " + selectedVersion + " at " + versionDir +
                        " — vanilla textures may not resolve.");
                }
            }

            // Local helper: try to extract a vanilla texture from the selected version jar.
            bool TryResolveVanillaTexture(string normalizedAssetPath, out string absolutePath)
            {
                absolutePath = string.Empty;

                // must be a minecraft asset path
                if (!JsonParserWorker.IsVanillaTexturePath(normalizedAssetPath))
                    return false;

                if (string.IsNullOrWhiteSpace(vanillaJarPath) || !File.Exists(vanillaJarPath))
                    return false;

                // normalizedAssetPath is like "assets/minecraft/textures/block/stone.png"
                if (!JsonParserWorker.TryOpenZipEntryBytes(vanillaJarPath, normalizedAssetPath, out var data) || data.Length == 0)
                    return false;

                string cacheDir = Path.Combine(itemsAdderFolder, "output", "_vanilla_textures");
                Directory.CreateDirectory(cacheDir);

                // build a safe file name from the asset path
                string name = normalizedAssetPath.Replace('\\', '_').Replace('/', '_');
                foreach (char invalid in Path.GetInvalidFileNameChars())
                    name = name.Replace(invalid, '_');

                if (!name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    name += ".png";

                string cachePath = Path.Combine(cacheDir, name);

                if (!File.Exists(cachePath))
                {
                    File.WriteAllBytes(cachePath, data);
                }

                absolutePath = cachePath;
                return true;
            }

            foreach (var filePath in Lists.CustomBlockPaths)
            {
                if (!File.Exists(filePath)) continue;

                ConsoleWorker.Write.Line("info", "Scanning blocks file " + filePath);

                using var reader = new StreamReader(filePath);
                var yaml = new YamlStream();
                yaml.Load(reader);

                if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode root)
                    continue;

                if (!root.Children.TryGetValue("items", out var itemsNode) || itemsNode is not YamlMappingNode itemsMapping)
                    continue;

                foreach (var entry in itemsMapping.Children)
                {
                    if (entry.Key is not YamlScalarNode idNode || entry.Value is not YamlMappingNode props)
                        continue;

                    // ensure the entry actually has block-specific props
                    if (!BlockYamlParserWorker.TryGetBlockSpecificProps(props, out _))
                        continue;

                    string blockId = idNode.Value ?? string.Empty;
                    string? blockNs = BlockYamlParserWorker.ExtractNamespaceFromPath(filePath);
                    string ns = blockNs ?? "unknown";

                    string material = BlockYamlParserWorker.TryGetScalar(props, "material") ?? "minecraft:stick";

                    var block = new CustomBlock
                    {
                        BlockNamespace = ns,
                        BlockItemID = blockId,
                        Material = material,
                        Is3D = false
                    };

                    LogInfo(block.BlockNamespace, block.BlockItemID, "discovered block definition");
                    LogInfo(block.BlockNamespace, block.BlockItemID, "held material " + block.Material);

                    // Placed model TYPE: REAL / REAL_NOTE / REAL_WIRE / REAL_TRANSPARENT
                    if (BlockYamlParserWorker.TryGetPlacedModelType(props, out var placedType) && !string.IsNullOrWhiteSpace(placedType))
                    {
                        block.PlacedBlockType = placedType!;
                        LogInfo(block.BlockNamespace, block.BlockItemID, "placed type " + block.PlacedBlockType);
                    }

                    // Optional debug: human-friendly placed model name/id
                    if (BlockYamlParserWorker.TryGetPlacedModelName(props, out var placedModelName) && !string.IsNullOrWhiteSpace(placedModelName))
                    {
                        LogInfo(block.BlockNamespace, block.BlockItemID, "placed model " + placedModelName);
                    }

                    // CMD for the held item that represents this block
                    var cmd = BlockYamlParserWorker.GetCustomModelData(itemsAdderFolder, block.BlockNamespace, block.BlockItemID);
                    if (cmd.HasValue)
                    {
                        block.CustomModelData = cmd.Value;
                        LogInfo(block.BlockNamespace, block.BlockItemID, "CustomModelData " + cmd.Value);
                    }

                    // Prefer 3D model; else 2D/per-face textures
                    if (BlockYamlParserWorker.TryGetMapping(props, "graphics", out var graphics))
                    {
                        // 3D block model via graphics.model
                        if (BlockYamlParserWorker.TryGetScalar(graphics!, "model", out var modelName) &&
                            !string.IsNullOrWhiteSpace(modelName))
                        {
                            block.Is3D = true;
                            block.ModelTextureAssets.Clear();
                            block.ModelTexturePaths.Clear();

                            string modelAsset = "assets/" + block.BlockNamespace + "/models/" + modelName + ".json";
                            LogInfo(block.BlockNamespace, block.BlockItemID, "3D model " + modelAsset);

                            if (JsonParserWorker.TryResolveContentAssetAbsolute(itemsAdderFolder, modelAsset, out var modelAbs) &&
                                File.Exists(modelAbs))
                            {
                                block.ModelPath = modelAbs;
                                LogInfo(block.BlockNamespace, block.BlockItemID, "resolved model file " + modelAbs);
                            }
                            else
                            {
                                block.ModelPath = modelAsset;
                                ConsoleWorker.Write.Line("warn",
                                    block.BlockNamespace + ":" + block.BlockItemID +
                                    " block model missing on disk: " + modelAsset);
                            }

                            var texMap = JsonParserWorker.ResolveModelTextureMapWithParents(itemsAdderFolder, block.BlockNamespace!, modelName!);
                            foreach (var kv in texMap)
                            {
                                string slotKey = kv.Key;    // e.g. "0", "particle"
                                string assetId = kv.Value;  // e.g. "assets/workbenches/textures/...png" or "assets/minecraft/textures/...png"

                                LogInfo(block.BlockNamespace, block.BlockItemID, "model texture " + assetId + " (key " + slotKey + ")");

                                // 1) Always remember the logical asset id (works for IA + vanilla)
                                block.ModelTextureAssets[slotKey] = assetId;

                                string? texAbs = null;

                                // 2a) Try to resolve as an ItemsAdder asset
                                if (JsonParserWorker.TryResolveContentAssetAbsolute(itemsAdderFolder, assetId, out var iaAbs, block.BlockNamespace) &&
                                    File.Exists(iaAbs))
                                {
                                    texAbs = iaAbs;
                                }
                                // 2b) If that fails and it's vanilla, try extracting from the selected version jar
                                else if (TryResolveVanillaTexture(assetId, out var vanillaAbs) &&
                                         File.Exists(vanillaAbs))
                                {
                                    texAbs = vanillaAbs;
                                }

                                if (!string.IsNullOrWhiteSpace(texAbs))
                                {
                                    block.ModelTexturePaths[slotKey] = texAbs;
                                    LogInfo(block.BlockNamespace, block.BlockItemID, "resolved texture slot " + slotKey + " → " + texAbs);

                                    // Use the first successfully resolved texture as our main fallback
                                    if (string.IsNullOrWhiteSpace(block.TexturePath) || !File.Exists(block.TexturePath))
                                    {
                                        block.TexturePath = texAbs;
                                        LogInfo(block.BlockNamespace, block.BlockItemID, "auto-assigned texture " + block.TexturePath);
                                    }
                                }
                                else
                                {
                                    // At this point, neither IA nor vanilla resolution worked.
                                    ConsoleWorker.Write.Line("warn",
                                        block.BlockNamespace + ":" + block.BlockItemID +
                                        " missing texture for slot " + slotKey + ": " + assetId);
                                }
                            }

                            if (string.IsNullOrWhiteSpace(block.IconPath) &&
                                !string.IsNullOrWhiteSpace(block.TexturePath) &&
                                File.Exists(block.TexturePath))
                            {
                                block.IconPath = block.TexturePath;
                                LogInfo(block.BlockNamespace, block.BlockItemID, "auto-assigned icon " + block.IconPath);
                            }
                        }

                        // Per-face textures for non-3D blocks: graphics.textures { up/down/north/south/east/west: path }
                        if (!block.Is3D &&
                            BlockYamlParserWorker.TryGetPerFaceTexturesNormalized(block.BlockNamespace, props, out var faces) &&
                            faces.Count > 0)
                        {
                            block.PerFaceTexture = true;

                            string? firstFaceAbs = null;

                            foreach (var kv in faces)
                            {
                                string face = kv.Key;          // e.g. "up", "down", "north", ...
                                string normalized = kv.Value;   // normalized asset path: assets/ns/textures/... or assets/minecraft/...

                                string? texAbs = null;

                                // 1) ItemsAdder content
                                if (JsonParserWorker.TryResolveContentAssetAbsolute(itemsAdderFolder, normalized, out var iaAbs, block.BlockNamespace) &&
                                    File.Exists(iaAbs))
                                {
                                    texAbs = iaAbs;
                                }
                                // 2) Vanilla jar fallback (for assets/minecraft/... paths)
                                else if (TryResolveVanillaTexture(normalized, out var vanillaAbs) &&
                                         File.Exists(vanillaAbs))
                                {
                                    texAbs = vanillaAbs;
                                }

                                if (!string.IsNullOrWhiteSpace(texAbs))
                                {
                                    block.FaceTexturePaths[face] = texAbs;
                                    LogInfo(block.BlockNamespace, block.BlockItemID, "face texture " + face + " → " + texAbs);

                                    if (firstFaceAbs == null)
                                        firstFaceAbs = texAbs;
                                }
                                else
                                {
                                    ConsoleWorker.Write.Line("warn",
                                        block.BlockNamespace + ":" + block.BlockItemID +
                                        " per-face texture not found for " + face + ": " + normalized);
                                }
                            }


                            // Choose a stable fallback for main TexturePath/IconPath:
                            // Use the first successfully-resolved face as the "main" 2D representation.
                            if (firstFaceAbs != null &&
                                (string.IsNullOrWhiteSpace(block.TexturePath) || !File.Exists(block.TexturePath)))
                            {
                                block.TexturePath = firstFaceAbs;
                                LogInfo(block.BlockNamespace, block.BlockItemID,
                                    "assigned main texture from face → " + block.TexturePath);
                            }

                            if (firstFaceAbs != null && string.IsNullOrWhiteSpace(block.IconPath))
                            {
                                block.IconPath = firstFaceAbs;
                                LogInfo(block.BlockNamespace, block.BlockItemID,
                                    "assigned icon from face → " + block.IconPath);
                            }
                        }

                        // 2D texture for blocks (only if not 3D and not already per-face)
                        if (!block.Is3D &&
                            !block.PerFaceTexture &&
                            BlockYamlParserWorker.TryGetScalar(graphics!, "texture", out var tex2D) &&
                            !string.IsNullOrWhiteSpace(tex2D))
                        {
                            if (BlockYamlParserWorker.TryGet2DTexturePathNormalized(block.BlockNamespace, props, out var normalized2D))
                            {
                                if (JsonParserWorker.TryResolveContentAssetAbsolute(itemsAdderFolder, normalized2D, out var abs, block.BlockNamespace) &&
                                    File.Exists(abs))
                                {
                                    block.TexturePath = abs;
                                    LogInfo(block.BlockNamespace, block.BlockItemID, "assigned texture " + block.TexturePath);

                                    if (string.IsNullOrWhiteSpace(block.IconPath))
                                    {
                                        block.IconPath = abs;
                                        LogInfo(block.BlockNamespace, block.BlockItemID, "assigned icon " + block.IconPath);
                                    }
                                }
                                else
                                {
                                    ConsoleWorker.Write.Line("warn",
                                        block.BlockNamespace + ":" + block.BlockItemID +
                                        " 2D texture not found: " + normalized2D);
                                }
                            }
                        }
                    }
                    else if (BlockYamlParserWorker.TryGetMapping(props, "resource", out var resource))
                    {
                        // 3D block model via resource.model_path
                        if (BlockYamlParserWorker.TryGetScalar(resource!, "model_path", out var modelPath) &&
                            !string.IsNullOrWhiteSpace(modelPath))
                        {
                            block.Is3D = true;
                            block.ModelTextureAssets.Clear();
                            block.ModelTexturePaths.Clear();

                            string modelAsset = "assets/" + block.BlockNamespace + "/models/" + modelPath + ".json";
                            LogInfo(block.BlockNamespace, block.BlockItemID, "3D model " + modelAsset);

                            if (JsonParserWorker.TryResolveContentAssetAbsolute(itemsAdderFolder, modelAsset, out var modelAbs) &&
                                File.Exists(modelAbs))
                            {
                                block.ModelPath = modelAbs;
                                LogInfo(block.BlockNamespace, block.BlockItemID, "resolved model file " + modelAbs);
                            }
                            else
                            {
                                block.ModelPath = modelAsset;
                                ConsoleWorker.Write.Line("warn",
                                    block.BlockNamespace + ":" + block.BlockItemID +
                                    " block model missing on disk: " + modelAsset);
                            }

                            var texMap = JsonParserWorker.ResolveModelTextureMapWithParents(itemsAdderFolder, block.BlockNamespace!, modelPath!);
                            foreach (var kv in texMap)
                            {
                                string slotKey = kv.Key;
                                string assetId = kv.Value;

                                LogInfo(block.BlockNamespace, block.BlockItemID, "model texture " + assetId + " (key " + slotKey + ")");

                                block.ModelTextureAssets[slotKey] = assetId;

                                string? texAbs = null;

                                if (JsonParserWorker.TryResolveContentAssetAbsolute(itemsAdderFolder, assetId, out var iaAbs, block.BlockNamespace) &&
                                    File.Exists(iaAbs))
                                {
                                    texAbs = iaAbs;
                                }
                                else if (TryResolveVanillaTexture(assetId, out var vanillaAbs) &&
                                         File.Exists(vanillaAbs))
                                {
                                    texAbs = vanillaAbs;
                                }

                                if (!string.IsNullOrWhiteSpace(texAbs))
                                {
                                    block.ModelTexturePaths[slotKey] = texAbs;
                                    LogInfo(block.BlockNamespace, block.BlockItemID, "resolved texture slot " + slotKey + " → " + texAbs);

                                    if (string.IsNullOrWhiteSpace(block.TexturePath) || !File.Exists(block.TexturePath))
                                    {
                                        block.TexturePath = texAbs;
                                        LogInfo(block.BlockNamespace, block.BlockItemID, "auto-assigned texture " + block.TexturePath);
                                    }
                                }
                                else
                                {
                                    ConsoleWorker.Write.Line("warn",
                                        block.BlockNamespace + ":" + block.BlockItemID +
                                        " missing texture for slot " + slotKey + ": " + assetId);
                                }
                            }

                            if (string.IsNullOrWhiteSpace(block.IconPath) &&
                                !string.IsNullOrWhiteSpace(block.TexturePath) &&
                                File.Exists(block.TexturePath))
                            {
                                block.IconPath = block.TexturePath;
                                LogInfo(block.BlockNamespace, block.BlockItemID, "auto-assigned icon " + block.IconPath);
                            }
                        }

                        // 2D texture for blocks (only if not 3D)
                        if (!block.Is3D &&
                            BlockYamlParserWorker.TryGetScalar(resource!, "texture_path", out var texPath2D) &&
                            !string.IsNullOrWhiteSpace(texPath2D))
                        {
                            if (BlockYamlParserWorker.TryGet2DTexturePathNormalized(block.BlockNamespace, props, out var normalized2D))
                            {
                                if (JsonParserWorker.TryResolveContentAssetAbsolute(itemsAdderFolder, normalized2D, out var abs, block.BlockNamespace) &&
                                    File.Exists(abs))
                                {
                                    block.TexturePath = abs;
                                    LogInfo(block.BlockNamespace, block.BlockItemID, "assigned texture " + block.TexturePath);
                                }
                                else
                                {
                                    ConsoleWorker.Write.Line("warn",
                                        block.BlockNamespace + ":" + block.BlockItemID +
                                        " 2D texture not found: " + normalized2D);
                                }
                            }
                        }

                        // held material override
                        if (BlockYamlParserWorker.TryGetScalar(resource!, "material", out var mat) &&
                            !string.IsNullOrWhiteSpace(mat))
                        {
                            block.Material = mat!;
                            LogInfo(block.BlockNamespace, block.BlockItemID, "overrides material " + block.Material);
                        }
                    }

                    Lists.CustomBlocks.Add(block);
                    LogInfo(block.BlockNamespace, block.BlockItemID, "parsed and added to CustomBlocks");
                }
            }
        }
    }
}