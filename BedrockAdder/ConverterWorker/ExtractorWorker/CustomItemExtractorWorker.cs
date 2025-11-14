using BedrockAdder.FileWorker;
using BedrockAdder.Library;
using System;
using System.IO;
using YamlDotNet.RepresentationModel;

namespace BedrockAdder.ExtractorWorker.ConverterWorker
{
    internal static class CustomItemExtractorWorker
    {
        internal static void ExtractCustomItemsFromPaths(string itemsAdderFolder, string selectedVersion)
        {
            void LogInfo(string? itemNamespace, string itemId, string message)
            {
                string ns = string.IsNullOrWhiteSpace(itemNamespace) ? "unknown" : itemNamespace;
                ConsoleWorker.Write.Line("info", ns + ":" + itemId + " " + message);
            }

            // Resolve selected version directory (for vanilla texture logging)
            string versionDir = string.Empty;
            if (!string.IsNullOrWhiteSpace(selectedVersion) &&
                !selectedVersion.Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                versionDir = Path.Combine(appData, ".minecraft", "versions", selectedVersion);
            }

            int processedFiles = 0;
            int totalEntries = 0;

            foreach (var filePath in Lists.CustomItemPaths)
            {
                var root = ItemYamlParserWorker.LoadRootMapping(filePath);
                if (root == null)
                {
                    ConsoleWorker.Write.Line("warn", "Failed to parse items YAML " + filePath);
                    continue;
                }

                // Namespace declared in info.namespace; used as default for items without explicit ns prefix
                string fileNamespace = ItemYamlParserWorker.GetFileNameSpace(root, "unknown");

                if (!root.Children.TryGetValue(new YamlScalarNode("items"), out var itemsNode) ||
                    itemsNode is not YamlMappingNode itemsMapping)
                {
                    ConsoleWorker.Write.Line("warn", "File " + filePath + " has no 'items' root mapping.");
                    continue;
                }

                foreach (var kvp in itemsMapping.Children)
                {
                    if (kvp.Key is not YamlScalarNode itemKey || kvp.Value is not YamlMappingNode props)
                        continue;

                    string fullId = itemKey.Value ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(fullId))
                        continue;

                    string? itemNamespace = null;
                    string itemId = fullId;

                    int colonIndex = fullId.IndexOf(':');
                    if (colonIndex > 0)
                    {
                        itemNamespace = fullId.Substring(0, colonIndex);
                        itemId = fullId.Substring(colonIndex + 1);
                    }

                    // If no explicit namespace on the key, fall back to the file's namespace (info.namespace)
                    if (string.IsNullOrWhiteSpace(itemNamespace))
                        itemNamespace = fileNamespace;

                    var customItem = new CustomItem
                    {
                        ItemID = itemId,
                        ItemNamespace = itemNamespace,
                        Material = ItemYamlParserWorker.TryGetScalar(props, "material") ?? "minecraft:stick",
                        CustomModelData = ItemYamlParserWorker.GetCustomModelData(itemsAdderFolder, itemNamespace, itemId),
                        Is3D = false
                    };

                    // Recolor tint (if any)
                    if (ItemYamlParserWorker.TryGetRecolorTint(props, out var tint) && !string.IsNullOrWhiteSpace(tint))
                    {
                        customItem.RecolorTint = tint;
                        LogInfo(itemNamespace, itemId, "has recolor tint " + tint);
                    }

                    // Vanilla texture detection (minecraft:...)
                    bool isVanillaTexture = ItemYamlParserWorker.TryDetectVanillaTexture(props, out var vanillaId);
                    if (isVanillaTexture && !string.IsNullOrWhiteSpace(vanillaId))
                    {
                        customItem.UsesVanillaTexture = true;
                        customItem.VanillaTextureId = vanillaId;
                        LogInfo(itemNamespace, itemId, "uses vanilla texture " + vanillaId);
                    }

                    if (!string.IsNullOrWhiteSpace(customItem.Material))
                        LogInfo(itemNamespace, itemId, "has material " + customItem.Material);

                    if (customItem.CustomModelData > 0)
                        LogInfo(itemNamespace, itemId, "has CustomModelData " + customItem.CustomModelData);

                    // ---------------- graphics ----------------
                    if (ItemYamlParserWorker.TryGetMapping(props, "graphics", out var graphics))
                    {
                        // 3D model path (JSON-driven) via graphics.model
                        if (ItemYamlParserWorker.TryGetScalar(graphics!, "model", out var modelName) &&
                            !string.IsNullOrWhiteSpace(modelName) &&
                            !string.IsNullOrWhiteSpace(itemNamespace))
                        {
                            customItem.Is3D = true;
                            customItem.ModelTexturePaths.Clear();

                            string modelAsset = "assets/" + itemNamespace + "/models/" + modelName + ".json";
                            LogInfo(itemNamespace, itemId, "is 3D model " + modelName);
                            LogInfo(itemNamespace, itemId, "has assigned model " + modelAsset);

                            if (JsonParserWorker.TryResolveContentAssetAbsolute(itemsAdderFolder, modelAsset, out var modelAbs) &&
                                File.Exists(modelAbs))
                            {
                                customItem.ModelPath = modelAbs;
                                LogInfo(itemNamespace, itemId, "resolved model file " + modelAbs);
                            }
                            else
                            {
                                customItem.ModelPath = modelAsset;
                                ConsoleWorker.Write.Line("warn", itemNamespace + ":" + itemId + " model json missing on disk: " + modelAsset);
                            }

                            var texMap = JsonParserWorker.ResolveModelTextureMapWithParents(itemsAdderFolder, itemNamespace!, modelName);
                            foreach (var kv in texMap)
                            {
                                string normalizedAsset = kv.Value;
                                LogInfo(itemNamespace, itemId, "has model texture " + normalizedAsset + " (key " + kv.Key + ")");

                                if (JsonParserWorker.TryResolveContentAssetAbsolute(itemsAdderFolder, normalizedAsset, out var texAbs) &&
                                    File.Exists(texAbs))
                                {
                                    customItem.ModelTexturePaths[kv.Key] = texAbs;
                                    LogInfo(itemNamespace, itemId, "resolved texture slot " + kv.Key + " → " + texAbs);

                                    if (string.IsNullOrWhiteSpace(customItem.TexturePath))
                                    {
                                        customItem.TexturePath = texAbs;
                                        LogInfo(itemNamespace, itemId, "auto-assigned texture file " + customItem.TexturePath);

                                        if (JsonParserWorker.IsVanillaTexturePath(normalizedAsset))
                                        {
                                            if (!string.IsNullOrWhiteSpace(versionDir))
                                                LogInfo(itemNamespace, itemId, "vanilla texture will be taken from version " + selectedVersion + " (" + versionDir + ")");
                                            else
                                                LogInfo(itemNamespace, itemId, "vanilla texture detected but no version selected");
                                        }
                                    }
                                }
                                else
                                {
                                    ConsoleWorker.Write.Line("warn", itemNamespace + ":" + itemId + " missing texture for slot " + kv.Key + ": " + normalizedAsset);
                                }
                            }

                            if (string.IsNullOrWhiteSpace(customItem.IconPath) &&
                                !string.IsNullOrWhiteSpace(customItem.TexturePath) &&
                                File.Exists(customItem.TexturePath))
                            {
                                customItem.IconPath = customItem.TexturePath;
                                LogInfo(itemNamespace, itemId, "auto-assigned icon from texture " + customItem.IconPath);
                            }
                        }

                        // 2D item texture (YAML-driven) — only if NOT 3D
                        if (!customItem.Is3D &&
                            ItemYamlParserWorker.TryGetScalar(graphics, "texture", out var textureScalar) &&
                            !string.IsNullOrWhiteSpace(textureScalar))
                        {
                            if (customItem.UsesVanillaTexture)
                            {
                                // Don't build IA path for vanilla textures here
                                LogInfo(itemNamespace, itemId, "2D item uses vanilla texture " + customItem.VanillaTextureId + " (no IA TexturePath from graphics)");
                            }
                            else if (ItemYamlParserWorker.TryGet2DTexturePathNormalized(props, out var rel2d))
                            {
                                if (JsonParserWorker.TryResolveContentAssetAbsolute(itemsAdderFolder, rel2d, out var abs, itemNamespace) && File.Exists(abs))
                                {
                                    customItem.TexturePath = abs;
                                    if (string.IsNullOrWhiteSpace(customItem.IconPath))
                                        customItem.IconPath = abs;

                                    LogInfo(itemNamespace, itemId, "has assigned texture " + customItem.TexturePath);
                                    if (!string.IsNullOrWhiteSpace(customItem.IconPath))
                                        LogInfo(itemNamespace, itemId, "has assigned icon " + customItem.IconPath);
                                }
                                else
                                {
                                    ConsoleWorker.Write.Line("warn", itemNamespace + ":" + itemId + " 2D texture not found: " + rel2d);
                                }
                            }
                        }
                    }

                    // ---------------- resource ----------------
                    if (ItemYamlParserWorker.TryGetMapping(props, "resource", out var resource))
                    {
                        // resource.model_path (alternate 3D model specification)
                        if (ItemYamlParserWorker.TryGetScalar(resource!, "model_path", out var modelPath) &&
                            !string.IsNullOrWhiteSpace(modelPath) &&
                            !string.IsNullOrWhiteSpace(itemNamespace))
                        {
                            string norm = modelPath.Replace("\\", "/").Trim();
                            string modelName2 = Path.GetFileNameWithoutExtension(norm);

                            customItem.Is3D = true;
                            customItem.ModelTexturePaths.Clear();

                            string modelAsset = "assets/" + itemNamespace + "/models/" + modelName2 + ".json";
                            LogInfo(itemNamespace, itemId, "is 3D model (resource) " + modelAsset);

                            if (JsonParserWorker.TryResolveContentAssetAbsolute(itemsAdderFolder, modelAsset, out var modelAbs) &&
                                File.Exists(modelAbs))
                            {
                                customItem.ModelPath = modelAbs;
                                LogInfo(itemNamespace, itemId, "resolved model file " + modelAbs);
                            }
                            else
                            {
                                customItem.ModelPath = modelAsset;
                                ConsoleWorker.Write.Line("warn", itemNamespace + ":" + itemId + " resource model missing on disk: " + modelAsset);
                            }

                            var texMap2 = JsonParserWorker.ResolveModelTextureMapWithParents(itemsAdderFolder, itemNamespace!, modelName2);
                            foreach (var kv in texMap2)
                            {
                                string normalizedAsset = kv.Value;
                                LogInfo(itemNamespace, itemId, "has model texture " + normalizedAsset + " (key " + kv.Key + ")");

                                if (JsonParserWorker.TryResolveContentAssetAbsolute(itemsAdderFolder, normalizedAsset, out var texAbs) &&
                                    File.Exists(texAbs))
                                {
                                    customItem.ModelTexturePaths[kv.Key] = texAbs;
                                    LogInfo(itemNamespace, itemId, "resolved texture slot " + kv.Key + " → " + texAbs);

                                    if ((string.IsNullOrWhiteSpace(customItem.TexturePath) || !File.Exists(customItem.TexturePath)))
                                    {
                                        customItem.TexturePath = texAbs;
                                        LogInfo(itemNamespace, itemId, "auto-assigned texture file " + customItem.TexturePath);

                                        if (JsonParserWorker.IsVanillaTexturePath(normalizedAsset))
                                        {
                                            if (!string.IsNullOrWhiteSpace(versionDir))
                                                LogInfo(itemNamespace, itemId, "vanilla texture will be taken from version " + selectedVersion + " (" + versionDir + ")");
                                            else
                                                LogInfo(itemNamespace, itemId, "vanilla texture detected but no version selected");
                                        }
                                    }
                                }
                                else
                                {
                                    ConsoleWorker.Write.Line("warn", itemNamespace + ":" + itemId + " missing texture for slot " + kv.Key + ": " + normalizedAsset);
                                }
                            }

                            if (string.IsNullOrWhiteSpace(customItem.IconPath) &&
                                !string.IsNullOrWhiteSpace(customItem.TexturePath) &&
                                File.Exists(customItem.TexturePath))
                            {
                                customItem.IconPath = customItem.TexturePath;
                                LogInfo(itemNamespace, itemId, "auto-assigned icon from texture " + customItem.IconPath);
                            }
                        }

                        // ---------------- 2D texture for purely 2D items (no graphics.model) ----------------
                        if (!customItem.Is3D)
                        {
                            if (!customItem.UsesVanillaTexture)
                            {
                                // Non-vanilla: build absolute IA texture path
                                if (ItemYamlParserWorker.TryGet2DTexturePathNormalized(props, out var relTex) &&
                                    !string.IsNullOrWhiteSpace(relTex))
                                {
                                    if (JsonParserWorker.TryResolveContentAssetAbsolute(itemsAdderFolder, relTex, out var abs, itemNamespace) && File.Exists(abs))
                                    {
                                        customItem.TexturePath = abs;
                                        customItem.IconPath = abs;

                                        LogInfo(itemNamespace, itemId, "2D texture " + relTex + " → " + abs);
                                    }
                                    else
                                    {
                                        ConsoleWorker.Write.Line("warn", itemNamespace + ":" + itemId + " 2D texture not found: " + relTex);
                                    }
                                }
                                else
                                {
                                    LogInfo(itemNamespace, itemId, "no 2D texture found for non-vanilla item");
                                }
                            }
                            else
                            {
                                // Vanilla texture: don't try to map to IA content folder, we will handle via jar + recolorer later
                                LogInfo(itemNamespace, itemId, "2D item uses vanilla texture " + customItem.VanillaTextureId + " (no IA TexturePath)");
                            }
                        }
                    }

                    // (second recolor tint block removed – handled once at the top)

                    Lists.CustomItems.Add(customItem);
                    totalEntries++;
                }

                processedFiles++;
            }

            ConsoleWorker.Write.Line("info", "Custom items: processed files=" + processedFiles + " entries=" + totalEntries);
        }
    }
}