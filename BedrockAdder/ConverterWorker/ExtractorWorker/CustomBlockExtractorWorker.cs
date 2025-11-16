using BedrockAdder.FileWorker;
using BedrockAdder.Library;
using System;
using System.IO;
using System.Linq;
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

            if (Lists.CustomBlockPaths == null || Lists.CustomBlockPaths.Count == 0)
            {
                ConsoleWorker.Write.Line("info", "No custom block YAML paths to process.");
                return;
            }

            foreach (var filePath in Lists.CustomBlockPaths)
            {
                if (!File.Exists(filePath))
                {
                    ConsoleWorker.Write.Line("warn", "CustomBlockExtractor: YAML file missing: " + filePath);
                    continue;
                }

                string? nsFromPath = BlockYamlParserWorker.ExtractNamespaceFromPath(filePath);
                if (string.IsNullOrWhiteSpace(nsFromPath))
                {
                    ConsoleWorker.Write.Line("warn", "CustomBlockExtractor: could not infer namespace from path: " + filePath);
                }

                using var reader = new StreamReader(filePath);
                var yaml = new YamlStream();
                yaml.Load(reader);

                if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode root)
                    continue;

                if (!root.Children.TryGetValue(new YamlScalarNode("items"), out var itemsNode) ||
                    itemsNode is not YamlMappingNode itemsMapping)
                    continue;

                foreach (var entry in itemsMapping.Children)
                {
                    if (entry.Key is not YamlScalarNode idNode || entry.Value is not YamlMappingNode props)
                        continue;

                    string itemId = idNode.Value ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(itemId))
                        continue;

                    var block = new CustomBlock
                    {
                        BlockNamespace = nsFromPath ?? "unknown",
                        BlockItemID = itemId,
                        Material = "STONE",
                        PlacedBlockType = "REAL_NOTE"
                    };

                    // Material
                    if (BlockYamlParserWorker.TryGetScalar(props, "material", out var mat) &&
                        !string.IsNullOrWhiteSpace(mat))
                    {
                        block.Material = mat!;
                        LogInfo(block.BlockNamespace, block.BlockItemID, "material " + block.Material);
                    }

                    // Placed model type
                    if (BlockYamlParserWorker.TryGetPlacedModelType(props, out var placedType) &&
                        !string.IsNullOrWhiteSpace(placedType))
                    {
                        block.PlacedBlockType = placedType!;
                        LogInfo(block.BlockNamespace, block.BlockItemID, "placed type " + block.PlacedBlockType);
                    }

                    // Optional debug placed model name
                    if (BlockYamlParserWorker.TryGetPlacedModelName(props, out var placedModelName) &&
                        !string.IsNullOrWhiteSpace(placedModelName))
                    {
                        LogInfo(block.BlockNamespace, block.BlockItemID, "placed model " + placedModelName);
                    }

                    // CMD for held item
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
                        if (MainYamlParserWorker.TryGetScalar(graphics!, "model", out var modelName) &&
                            !string.IsNullOrWhiteSpace(modelName))
                        {
                            block.Is3D = true;
                            block.ModelTexturePaths.Clear();

                            string modelAsset = "assets/" + block.BlockNamespace + "/models/" + modelName + ".json";
                            LogInfo(block.BlockNamespace, block.BlockItemID, "3D model " + modelAsset);

                            if (JsonParserWorker.TryResolveContentAssetAbsolute(itemsAdderFolder, modelAsset, out var modelAbs, block.BlockNamespace) &&
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
                                    " model file not found in contents, keeping logical asset " + modelAsset);
                            }

                            // Load texture map for model (with parent chain)
                            var texMap = JsonParserWorker.ResolveModelTextureMapWithParents(itemsAdderFolder, block.BlockNamespace, modelName!);
                            foreach (var kv in texMap)
                            {
                                block.ModelTextureAssets[kv.Key] = kv.Value;

                                if (JsonParserWorker.TryResolveContentAssetAbsolute(itemsAdderFolder, kv.Value, out var absTex, block.BlockNamespace) &&
                                    File.Exists(absTex))
                                {
                                    block.ModelTexturePaths[kv.Key] = absTex;
                                }
                            }

                            // Try to give TexturePath/IconPath a default from model textures
                            foreach (var kv in block.ModelTexturePaths)
                            {
                                if (!string.IsNullOrWhiteSpace(kv.Value) && File.Exists(kv.Value))
                                {
                                    if (string.IsNullOrWhiteSpace(block.TexturePath))
                                    {
                                        block.TexturePath = kv.Value;
                                        LogInfo(block.BlockNamespace, block.BlockItemID, "default TexturePath from model texture " + kv.Value);
                                    }
                                    if (string.IsNullOrWhiteSpace(block.IconPath))
                                    {
                                        block.IconPath = kv.Value;
                                        LogInfo(block.BlockNamespace, block.BlockItemID, "default IconPath from model texture " + kv.Value);
                                    }
                                    break;
                                }
                            }
                        }

                        // 2D texture via graphics.texture
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
                                ConsoleWorker.Write.Line(
                                    "warn",
                                    block.BlockNamespace + ":" + block.BlockItemID + " 2D texture not found: " + normalized2D);
                            }
                        }


                        // Per-face textures for cuboid blocks (graphics.textures)
                        if (!block.Is3D &&
                            BlockYamlParserWorker.TryGetMapping(props, "graphics", out var gfxMap) &&
                            gfxMap is YamlMappingNode gfxNode)
                        {
                            if (BlockYamlParserWorker.TryGetMapping(gfxNode, "textures", out var texFaces) &&
                                texFaces is YamlMappingNode facesNode)
                            {
                                foreach (var faceEntry in facesNode.Children)
                                {
                                    if (faceEntry.Key is YamlScalarNode faceKey &&
                                        faceEntry.Value is YamlScalarNode faceVal)
                                    {
                                        string faceName = faceKey.Value ?? string.Empty;
                                        string texIdRaw = faceVal.Value ?? string.Empty;
                                        if (string.IsNullOrWhiteSpace(faceName) ||
                                            string.IsNullOrWhiteSpace(texIdRaw))
                                            continue;

                                        string modelValue = texIdRaw;
                                        if (modelValue.IndexOf(":", StringComparison.Ordinal) < 0 &&
                                            !string.IsNullOrWhiteSpace(block.BlockNamespace))
                                        {
                                            modelValue = block.BlockNamespace + ":" + modelValue;
                                        }

                                        string normalizedFaceTex = JsonParserWorker.NormalizeTexturePathFromModelValue(modelValue);

                                        if (JsonParserWorker.TryResolveContentAssetAbsolute(itemsAdderFolder, normalizedFaceTex, out var absFace, block.BlockNamespace) &&
                                            File.Exists(absFace))
                                        {
                                            block.PerFaceTexture = true;
                                            block.FaceTexturePaths[faceName] = absFace;
                                            LogInfo(block.BlockNamespace, block.BlockItemID, "face texture " + faceName + " → " + absFace);

                                            if (string.IsNullOrWhiteSpace(block.TexturePath))
                                            {
                                                block.TexturePath = absFace;
                                                LogInfo(block.BlockNamespace, block.BlockItemID, "assigned fallback TexturePath from face " + faceName);
                                            }

                                            if (string.IsNullOrWhiteSpace(block.IconPath))
                                            {
                                                block.IconPath = absFace;
                                                LogInfo(block.BlockNamespace, block.BlockItemID, "assigned fallback IconPath from face " + faceName);
                                            }
                                        }
                                        else
                                        {
                                            ConsoleWorker.Write.Line(
                                                "warn",
                                                block.BlockNamespace + ":" + block.BlockItemID +
                                                " per-face texture not found for " + faceName + ": " + normalizedFaceTex);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else if (MainYamlParserWorker.TryGetMapping(props, "resource", out var resource))
                    {
                        // 3D block model via resource.model_path
                        if (MainYamlParserWorker.TryGetScalar(resource!, "model_path", out var resModel) &&
                            !string.IsNullOrWhiteSpace(resModel))
                        {
                            block.Is3D = true;
                            block.ModelTexturePaths.Clear();

                            string modelAsset = JsonParserWorker.NormalizeModelPathFromYamlValue(block.BlockNamespace, resModel!);
                            LogInfo(block.BlockNamespace, block.BlockItemID, "3D model " + modelAsset);

                            if (JsonParserWorker.TryResolveContentAssetAbsolute(itemsAdderFolder, modelAsset, out var modelAbs, block.BlockNamespace) &&
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
                                    " model file not found in contents, keeping logical asset " + modelAsset);
                            }

                            string modelNameNoExt = Path.GetFileNameWithoutExtension(modelAsset);
                            var texMap = JsonParserWorker.ResolveModelTextureMapWithParents(itemsAdderFolder, block.BlockNamespace, modelNameNoExt);
                            foreach (var kv in texMap)
                            {
                                block.ModelTextureAssets[kv.Key] = kv.Value;

                                if (JsonParserWorker.TryResolveContentAssetAbsolute(itemsAdderFolder, kv.Value, out var absTex, block.BlockNamespace) &&
                                    File.Exists(absTex))
                                {
                                    block.ModelTexturePaths[kv.Key] = absTex;
                                }
                            }

                            foreach (var kv in block.ModelTexturePaths)
                            {
                                if (!string.IsNullOrWhiteSpace(kv.Value) && File.Exists(kv.Value))
                                {
                                    if (string.IsNullOrWhiteSpace(block.TexturePath))
                                        block.TexturePath = kv.Value;
                                    if (string.IsNullOrWhiteSpace(block.IconPath))
                                        block.IconPath = kv.Value;
                                    break;
                                }
                            }
                        }

                        // 2D texture via resource.texture_path
                        if (BlockYamlParserWorker.TryGet2DTexturePathNormalized(block.BlockNamespace, props, out var normalized2DRes))
                        {
                            if (JsonParserWorker.TryResolveContentAssetAbsolute(itemsAdderFolder, normalized2DRes, out var abs, block.BlockNamespace) &&
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
                                ConsoleWorker.Write.Line(
                                    "warn",
                                    block.BlockNamespace + ":" + block.BlockItemID + " 2D texture not found: " + normalized2DRes);
                            }
                        }

                    }

                    // Finally add to list
                    Lists.CustomBlocks.Add(block);
                    LogInfo(block.BlockNamespace, block.BlockItemID, "parsed and added to CustomBlocks");
                }
            }
        }
    }
}