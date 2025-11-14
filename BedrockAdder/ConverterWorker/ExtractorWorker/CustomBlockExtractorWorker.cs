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

            // We keep selectedVersion for parity with items; not used for blocks (no vanilla extraction for blocks).
            string versionDir = string.Empty;
            if (!string.IsNullOrWhiteSpace(selectedVersion) && !selectedVersion.Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                versionDir = Path.Combine(appData, ".minecraft", "versions", selectedVersion);
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

                    // Defensive: ensure the entry actually has block-specific props
                    if (!BlockYamlParserWorker.TryGetBlockSpecificProps(props, out _))
                        continue;

                    string blockId = idNode.Value ?? string.Empty;
                    string? blockNs = BlockYamlParserWorker.ExtractNamespaceFromPath(filePath);

                    var block = new CustomBlock
                    {
                        BlockNamespace = blockNs ?? "unknown",
                        BlockItemID = blockId,
                        Material = MainYamlParserWorker.TryGetScalar(props, "material") ?? "minecraft:stick",
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

                    // Prefer 3D model; else 2D texture
                    if (MainYamlParserWorker.TryGetMapping(props, "graphics", out var graphics))
                    {
                        // 3D block model via graphics.model
                        if (MainYamlParserWorker.TryGetScalar(graphics!, "model", out var modelName) && !string.IsNullOrWhiteSpace(modelName))
                        {
                            block.Is3D = true;
                            block.ModelTexturePaths.Clear();

                            string modelAsset = "assets/" + block.BlockNamespace + "/models/" + modelName + ".json";
                            LogInfo(block.BlockNamespace, block.BlockItemID, "3D model " + modelAsset);

                            if (JsonParserWorker.TryResolveContentAssetAbsolute(itemsAdderFolder, modelAsset, out var modelAbs) && File.Exists(modelAbs))
                            {
                                block.ModelPath = modelAbs;
                                LogInfo(block.BlockNamespace, block.BlockItemID, "resolved model file " + modelAbs);
                            }
                            else
                            {
                                block.ModelPath = modelAsset;
                                ConsoleWorker.Write.Line("warn", block.BlockNamespace + ":" + block.BlockItemID + " block model missing on disk: " + modelAsset);
                            }

                            var texMap = JsonParserWorker.ResolveModelTextureMapWithParents(itemsAdderFolder, block.BlockNamespace!, modelName!);
                            foreach (var kv in texMap)
                            {
                                string resolved = kv.Value;
                                LogInfo(block.BlockNamespace, block.BlockItemID, "model texture " + resolved + " (key " + kv.Key + ")");

                                if (JsonParserWorker.TryResolveContentAssetAbsolute(itemsAdderFolder, resolved, out var texAbs) && File.Exists(texAbs))
                                {
                                    block.ModelTexturePaths[kv.Key] = texAbs;
                                    LogInfo(block.BlockNamespace, block.BlockItemID, "resolved texture slot " + kv.Key + " → " + texAbs);

                                    if (string.IsNullOrWhiteSpace(block.TexturePath) || !File.Exists(block.TexturePath))
                                    {
                                        block.TexturePath = texAbs;
                                        LogInfo(block.BlockNamespace, block.BlockItemID, "auto-assigned texture " + block.TexturePath);
                                    }
                                }
                                else
                                {
                                    ConsoleWorker.Write.Line("warn", block.BlockNamespace + ":" + block.BlockItemID + " missing texture for slot " + kv.Key + ": " + resolved);
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
                            MainYamlParserWorker.TryGetScalar(graphics!, "texture", out var tex2D) &&
                            !string.IsNullOrWhiteSpace(tex2D))
                        {
                            if (BlockYamlParserWorker.TryGet2DTexturePathNormalized(props, out var normalized2D))
                            {
                                if (JsonParserWorker.TryResolveContentAssetAbsolute(itemsAdderFolder, normalized2D, out var abs, block.BlockNamespace) && File.Exists(abs))
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
                                    ConsoleWorker.Write.Line("warn", block.BlockNamespace + ":" + block.BlockItemID + " 2D texture not found: " + normalized2D);
                                }
                            }
                        }
                    }
                    else if (MainYamlParserWorker.TryGetMapping(props, "resource", out var resource))
                    {
                        // 3D block model via resource.model_path
                        if (MainYamlParserWorker.TryGetScalar(resource!, "model_path", out var modelPath) && !string.IsNullOrWhiteSpace(modelPath))
                        {
                            block.Is3D = true;
                            block.ModelTexturePaths.Clear();

                            string modelAsset = "assets/" + block.BlockNamespace + "/models/" + modelPath + ".json";
                            LogInfo(block.BlockNamespace, block.BlockItemID, "3D model " + modelAsset);

                            if (JsonParserWorker.TryResolveContentAssetAbsolute(itemsAdderFolder, modelAsset, out var modelAbs) && File.Exists(modelAbs))
                            {
                                block.ModelPath = modelAbs;
                                LogInfo(block.BlockNamespace, block.BlockItemID, "resolved model file " + modelAbs);
                            }
                            else
                            {
                                block.ModelPath = modelAsset;
                                ConsoleWorker.Write.Line("warn", block.BlockNamespace + ":" + block.BlockItemID + " block model missing on disk: " + modelAsset);
                            }

                            var texMap = JsonParserWorker.ResolveModelTextureMapWithParents(itemsAdderFolder, block.BlockNamespace!, modelPath!);
                            foreach (var kv in texMap)
                            {
                                string resolved = kv.Value;
                                LogInfo(block.BlockNamespace, block.BlockItemID, "model texture " + resolved + " (key " + kv.Key + ")");

                                if (JsonParserWorker.TryResolveContentAssetAbsolute(itemsAdderFolder, resolved, out var texAbs) && File.Exists(texAbs))
                                {
                                    block.ModelTexturePaths[kv.Key] = texAbs;
                                    LogInfo(block.BlockNamespace, block.BlockItemID, "resolved texture slot " + kv.Key + " → " + texAbs);

                                    if (string.IsNullOrWhiteSpace(block.TexturePath) || !File.Exists(block.TexturePath))
                                    {
                                        block.TexturePath = texAbs;
                                        LogInfo(block.BlockNamespace, block.BlockItemID, "auto-assigned texture " + block.TexturePath);
                                    }
                                }
                                else
                                {
                                    ConsoleWorker.Write.Line("warn", block.BlockNamespace + ":" + block.BlockItemID + " missing texture for slot " + kv.Key + ": " + resolved);
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
                            MainYamlParserWorker.TryGetScalar(resource!, "texture_path", out var texPath2D) &&
                            !string.IsNullOrWhiteSpace(texPath2D))
                        {
                            if (BlockYamlParserWorker.TryGet2DTexturePathNormalized(props, out var normalized2D))
                            {
                                if (JsonParserWorker.TryResolveContentAssetAbsolute(itemsAdderFolder, normalized2D, out var abs, block.BlockNamespace) && File.Exists(abs))
                                {
                                    block.TexturePath = abs;
                                    LogInfo(block.BlockNamespace, block.BlockItemID, "assigned texture " + block.TexturePath);
                                }
                                else
                                {
                                    ConsoleWorker.Write.Line("warn", block.BlockNamespace + ":" + block.BlockItemID + " 2D texture not found: " + normalized2D);
                                }
                            }
                        }

                        // held material override
                        if (MainYamlParserWorker.TryGetScalar(resource!, "material", out var mat) && !string.IsNullOrWhiteSpace(mat))
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
