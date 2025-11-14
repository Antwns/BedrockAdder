using BedrockAdder.FileWorker;
using BedrockAdder.Library;
using System;
using System.Collections.Generic;
using System.IO;
using YamlDotNet.RepresentationModel;

namespace BedrockAdder.ExtractorWorker.ConverterWorker
{
    internal static class CustomFurnitureExtractorWorker
    {
        internal static void ExtractCustomFurnitureFromPaths(string itemsAdderRootPath, string selectedVersion)
        {
            ConsoleWorker.Write.Line("info", "Furniture: extraction started. Paths=" + (Lists.CustomFurniturePaths?.Count ?? 0));

            int filesProcessed = 0;
            int furnitureItemsAdded = 0;

            foreach (var furnitureYamlPath in Lists.CustomFurniturePaths)
            {
                filesProcessed++;

                try
                {
                    if (!File.Exists(furnitureYamlPath))
                    {
                        ConsoleWorker.Write.Line("warn", "Furniture file missing: " + furnitureYamlPath);
                        continue;
                    }

                    using var reader = new StreamReader(furnitureYamlPath);
                    var yaml = new YamlStream();
                    yaml.Load(reader);

                    if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode rootNode)
                        continue;

                    string contentNamespace = FurnitureYamlParserWorker.GetFileNamespaceOrDefault(rootNode, "unknown");

                    if (!rootNode.Children.TryGetValue("items", out var itemsNode) || itemsNode is not YamlMappingNode itemsMap)
                        continue;

                    foreach (var itemEntry in itemsMap.Children)
                    {
                        if (itemEntry.Key is not YamlScalarNode itemKeyNode || itemEntry.Value is not YamlMappingNode itemProps)
                            continue;

                        string fullItemKey = itemKeyNode.Value ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(fullItemKey))
                            continue;

                        // Furniture detection
                        if (!MainYamlParserWorker.TryIsFurnitureItem(itemProps))
                        {
                            ConsoleWorker.Write.Line("debug", "Skip non-furniture item: " + fullItemKey);
                            continue;
                        }

                        // namespace:id support; fallback to file namespace
                        string furnitureNamespace = contentNamespace;
                        string furnitureItemId = fullItemKey;
                        var parts = fullItemKey.Split(':');
                        if (parts.Length == 2)
                        {
                            furnitureNamespace = parts[0];
                            furnitureItemId = parts[1];
                        }

                        // Model path (YAML) → model name for zip lookup
                        string? modelPathRaw = FurnitureYamlParserWorker.TryGetFurnitureModelPathFromItem(itemProps);
                        if (string.IsNullOrWhiteSpace(modelPathRaw))
                        {
                            ConsoleWorker.Write.Line("warn", "Furniture item without model path: " + fullItemKey);
                            continue;
                        }
                        string modelNameForZip = FurnitureYamlParserWorker.NormalizeModelNameForZipLookup(modelPathRaw!);

                        // Resolve full texture map (key → rp-relative "assets/<ns>/textures/...png") via JsonParserWorker (parent-aware)
                        Dictionary<string, string> textureMap =
                            JsonParserWorker.ResolveModelTextureMapWithParents(itemsAdderRootPath, furnitureNamespace, modelNameForZip);

                        var textureMapAbs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var kv in textureMap)
                        {
                            string normalizedAsset = kv.Value;
                            if (JsonParserWorker.TryResolveContentAssetAbsolute(itemsAdderRootPath, normalizedAsset, out var texAbs) && File.Exists(texAbs))
                            {
                                textureMapAbs[kv.Key] = texAbs;
                                ConsoleWorker.Write.Line("debug", furnitureNamespace + ":" + furnitureItemId + " texture slot " + kv.Key + " → " + texAbs);
                            }
                            else
                            {
                                ConsoleWorker.Write.Line("warn", furnitureNamespace + ":" + furnitureItemId + " missing furniture texture for slot " + kv.Key + ": " + normalizedAsset);
                            }
                        }

                        // Icon (2D) if present (rp-relative)
                        string iconPath = string.Empty;
                        if (FurnitureYamlParserWorker.TryGet2DTexturePathNormalized(itemProps, out var iconNormalized) && !string.IsNullOrWhiteSpace(iconNormalized))
                        {
                            if (JsonParserWorker.TryResolveContentAssetAbsolute(itemsAdderRootPath, iconNormalized, out var iconAbs) && File.Exists(iconAbs))
                            {
                                iconPath = iconAbs;
                            }
                            else
                            {
                                ConsoleWorker.Write.Line("warn", furnitureNamespace + ":" + furnitureItemId + " icon texture not found: " + iconNormalized);
                            }
                        }

                        // Material and CustomModelData (use existing helpers)
                        string material = FurnitureYamlParserWorker.TryGetArmorMaterial(itemProps, "minecraft:stick");
                        int? customModelData = FurnitureYamlParserWorker.TryGetCustomModelDataFromCache(itemsAdderRootPath, furnitureNamespace, furnitureItemId);

                        // Build rp-relative model path (keep ".json" suffix, and keep subfolder prefix, e.g., "models/<modelName>.json")
                        string modelPathRpRelative = "assets/" + furnitureNamespace + "/models/" + modelNameForZip + ".json";

                        string modelPathAbsolute = modelPathRpRelative;
                        if (JsonParserWorker.TryResolveContentAssetAbsolute(itemsAdderRootPath, modelPathRpRelative, out var modelAbs) && File.Exists(modelAbs))
                        {
                            modelPathAbsolute = modelAbs;
                        }
                        else
                        {
                            ConsoleWorker.Write.Line("warn", furnitureNamespace + ":" + furnitureItemId + " furniture model missing on disk: " + modelPathRpRelative);
                        }

                        var customFurniture = new CustomFurniture
                        {
                            FurnitureNamespace = furnitureNamespace,
                            FurnitureItemID = furnitureItemId,
                            ModelPath = modelPathAbsolute,
                            IconPath = string.IsNullOrWhiteSpace(iconPath) ? null : iconPath,
                            Material = material,
                            CustomModelData = customModelData
                        };

                        foreach (var kv in textureMapAbs)
                        {
                            customFurniture.TexturePaths[kv.Key] = kv.Value;
                        }

                        Lists.CustomFurniture.Add(customFurniture);
                        furnitureItemsAdded++;

                        ConsoleWorker.Write.Line(
                            "info",
                            "Furniture item added " + furnitureNamespace + ":" + furnitureItemId +
                            " model=" + modelPathRpRelative +
                            (customFurniture.IconPath != null ? " icon=" + customFurniture.IconPath : "")
                        );

                        foreach (var kv in customFurniture.TexturePaths)
                        {
                            ConsoleWorker.Write.Line("debug", "  texture [" + kv.Key + "] → " + kv.Value);
                        }
                    }
                }
                catch (Exception ex)
                {
                    ConsoleWorker.Write.Line("warn", "Furniture parse failed for " + furnitureYamlPath + ": " + ex.Message);
                }
            }

            ConsoleWorker.Write.Line("info", "Furniture: extraction finished. Files=" + filesProcessed + " Items=" + furnitureItemsAdded);
        }
    }
}