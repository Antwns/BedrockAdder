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

                    if (customItem.CustomModelData.HasValue && customItem.CustomModelData.Value > 0)
                        LogInfo(itemNamespace, itemId, "has CustomModelData " + customItem.CustomModelData.Value);

                    // ---------------- graphics ----------------
                    YamlMappingNode? graphics = null;
                    if (ItemYamlParserWorker.TryGetMapping(props, "graphics", out var graphicsNode) &&
                        graphicsNode is YamlMappingNode gMap)
                    {
                        graphics = gMap;

                        // 3D model path (JSON-driven) via graphics.model
                        if (ItemYamlParserWorker.TryGetScalar(graphics, "model", out var modelName) &&
                            !string.IsNullOrWhiteSpace(modelName) &&
                            !string.IsNullOrWhiteSpace(itemNamespace))
                        {
                            customItem.Is3D = true;
                            customItem.ModelTexturePaths.Clear();

                            // IA 3D models live under contents/{ns}/resourcepack/{ns}/models/{modelName}.json
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
                                ConsoleWorker.Write.Line("warn",
                                    itemNamespace + ":" + itemId + " model json missing on disk: " + modelAsset);
                            }

                            // Pull textures from the model (and its parents)
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
                                    ConsoleWorker.Write.Line("warn",
                                        itemNamespace + ":" + itemId + " missing texture for slot " + kv.Key + ": " + normalizedAsset);
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
                                LogInfo(itemNamespace, itemId,
                                    "2D item uses vanilla texture " + customItem.VanillaTextureId + " (no IA TexturePath from graphics)");
                            }
                            else if (ItemYamlParserWorker.TryGet2DTexturePathNormalized(props, out var rel2d))
                            {
                                if (JsonParserWorker.TryResolveContentAssetAbsolute(itemsAdderFolder, rel2d, out var abs, itemNamespace) &&
                                    File.Exists(abs))
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
                    YamlMappingNode? resource = null;
                    if (ItemYamlParserWorker.TryGetMapping(props, "resource", out var resourceNode) &&
                        resourceNode is YamlMappingNode rMap)
                    {
                        resource = rMap;

                        // resource.model_path (alternate 3D model specification)
                        if (ItemYamlParserWorker.TryGetScalar(resource, "model_path", out var modelPath) &&
                            !string.IsNullOrWhiteSpace(modelPath) &&
                            !string.IsNullOrWhiteSpace(itemNamespace))
                        {
                            string norm = modelPath.Replace("\\", "/").Trim();
                            string modelName2 = Path.GetFileNameWithoutExtension(norm);

                            customItem.Is3D = true;
                            customItem.ModelTexturePaths.Clear();

                            string modelAsset = "assets/" + itemNamespace + "/models/" + modelName2 + ".json";

                            // After we resolve the base resource model, derive state models
                            // for special materials by naming convention (BOW, CROSSBOW, etc.).
                            PopulateResourceStateModels(customItem, itemsAdderFolder, itemNamespace, modelName2, itemId);

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
                                ConsoleWorker.Write.Line("warn",
                                    itemNamespace + ":" + itemId + " resource model missing on disk: " + modelAsset);
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
                                    ConsoleWorker.Write.Line("warn",
                                        itemNamespace + ":" + itemId + " missing texture for slot " + kv.Key + ": " + normalizedAsset);
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

                        // ---------------- 2D texture for purely 2D items (no graphics.model/resource.model_path) ----------------
                        if (!customItem.Is3D)
                        {
                            if (!customItem.UsesVanillaTexture)
                            {
                                // Non-vanilla: build absolute IA texture path (graphics/resource/...)
                                if (ItemYamlParserWorker.TryGet2DTexturePathNormalized(props, out var relTex) &&
                                    !string.IsNullOrWhiteSpace(relTex))
                                {
                                    if (JsonParserWorker.TryResolveContentAssetAbsolute(itemsAdderFolder, relTex, out var absTex, itemNamespace) &&
                                        File.Exists(absTex))
                                    {
                                        customItem.TexturePath = absTex;
                                        customItem.IconPath = absTex;

                                        LogInfo(itemNamespace, itemId, "2D texture " + relTex + " → " + absTex);
                                    }
                                    else
                                    {
                                        ConsoleWorker.Write.Line("warn",
                                            itemNamespace + ":" + itemId + " 2D texture not found: " + relTex);
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
                                LogInfo(itemNamespace, itemId,
                                    "2D item uses vanilla texture " + customItem.VanillaTextureId + " (no IA TexturePath)");
                            }
                        }
                    }

                    // ---- per-state variants (graphics.models / graphics.textures) ----
                    if (graphics is YamlMappingNode graphicsMap && !string.IsNullOrWhiteSpace(itemNamespace))
                    {
                        // 3D state models: pulling_0, pulling_1, blocking, cast, arrow, rocket, etc.
                        if (ItemYamlParserWorker.TryGetMapping(graphicsMap, "models", out var modelsMap) &&
                            modelsMap is YamlMappingNode stateModels)
                        {
                            foreach (var entry in stateModels.Children)
                            {
                                if (entry.Key is not YamlScalarNode stateKeyNode ||
                                    entry.Value is not YamlScalarNode modelNameNode)
                                    continue;

                                string stateName = stateKeyNode.Value ?? string.Empty;
                                string stateModelName = modelNameNode.Value ?? string.Empty;

                                if (string.IsNullOrWhiteSpace(stateName) || string.IsNullOrWhiteSpace(stateModelName))
                                    continue;

                                // Build a normalized assets/... model path for this state
                                string modelAsset = "assets/" + itemNamespace + "/models/" + stateModelName + ".json";

                                if (JsonParserWorker.TryResolveContentAssetAbsolute(itemsAdderFolder, modelAsset, out var modelAbs) &&
                                    File.Exists(modelAbs))
                                {
                                    customItem.StateModelPaths[stateName] = modelAbs;
                                    LogInfo(itemNamespace, itemId, "state model[" + stateName + "] → " + modelAbs);
                                }
                                else
                                {
                                    // Still remember the normalized asset path even if the physical file is missing
                                    customItem.StateModelPaths[stateName] = modelAsset;
                                    ConsoleWorker.Write.Line("warn",
                                        itemNamespace + ":" + itemId + " missing state model " + stateName + ": " + modelAsset);
                                }
                            }
                        }

                        // 2D state textures: pulling_0, pulling_1, cast, arrow, rocket, etc.
                        if (ItemYamlParserWorker.TryGetMapping(graphicsMap, "textures", out var texturesMap) &&
                            texturesMap is YamlMappingNode stateTextures)
                        {
                            foreach (var entry in stateTextures.Children)
                            {
                                if (entry.Key is not YamlScalarNode stateKeyNode ||
                                    entry.Value is not YamlScalarNode texNameNode)
                                    continue;

                                string stateName = stateKeyNode.Value ?? string.Empty;
                                string stateTexRaw = texNameNode.Value ?? string.Empty;

                                if (string.IsNullOrWhiteSpace(stateName) || string.IsNullOrWhiteSpace(stateTexRaw))
                                    continue;

                                string assetPath = ItemYamlParserWorker.NormalizeStateTextureAssetPath(itemNamespace, stateTexRaw);
                                if (string.IsNullOrWhiteSpace(assetPath))
                                    continue;

                                if (JsonParserWorker.TryResolveContentAssetAbsolute(itemsAdderFolder, assetPath, out var texAbs) &&
                                    File.Exists(texAbs))
                                {
                                    customItem.StateTexturePaths[stateName] = texAbs;
                                    LogInfo(itemNamespace, itemId, "state texture[" + stateName + "] → " + texAbs);
                                }
                                else
                                {
                                    customItem.StateTexturePaths[stateName] = assetPath;
                                    ConsoleWorker.Write.Line("warn",
                                        itemNamespace + ":" + itemId + " missing state texture " + stateName + ": " + assetPath);
                                }
                            }
                        }
                    }

                    // --- Promote state data into base model/texture when there is no direct base ---

                    // If the item is not yet marked 3D and has no ModelPath,
                    // but *does* have state models (graphics.models), pick one ("normal" if present)
                    // as the base 3D model so that 3D icon rendering can still work.
                    if (!customItem.Is3D &&
                        string.IsNullOrWhiteSpace(customItem.ModelPath) &&
                        customItem.StateModelPaths.Count > 0)
                    {
                        string? chosenState = null;
                        string? chosenModelPath = null;

                        if (customItem.StateModelPaths.TryGetValue("normal", out var normalModel) &&
                            !string.IsNullOrWhiteSpace(normalModel))
                        {
                            chosenState = "normal";
                            chosenModelPath = normalModel;
                        }
                        else
                        {
                            foreach (var kv in customItem.StateModelPaths)
                            {
                                if (!string.IsNullOrWhiteSpace(kv.Value))
                                {
                                    chosenState = kv.Key;
                                    chosenModelPath = kv.Value;
                                    break;
                                }
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(chosenModelPath))
                        {
                            customItem.Is3D = true;
                            customItem.ModelPath = chosenModelPath;
                            LogInfo(itemNamespace, itemId,
                                "promoted state model '" + chosenState + "' as base ModelPath → " + chosenModelPath);

                            // Also populate ModelTexturePaths and base TexturePath/IconPath
                            // so ModelBuilderWorker/ModelImageBuilderWorker have textures to work with.
                            customItem.ModelTexturePaths.Clear();

                            // Derive a modelName suitable for ResolveModelTextureMapWithParents:
                            // from either an absolute path or an assets/... path.
                            string cleanPath = chosenModelPath.Replace("\\", "/").Trim();
                            string modelNameForTex = Path.GetFileNameWithoutExtension(cleanPath);

                            var texMap = JsonParserWorker.ResolveModelTextureMapWithParents(
                                itemsAdderFolder,
                                itemNamespace!,
                                modelNameForTex
                            );

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
                                    ConsoleWorker.Write.Line("warn",
                                        itemNamespace + ":" + itemId + " missing texture for slot " + kv.Key + ": " + normalizedAsset);
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
                    }
                    // If there is no base TexturePath/IconPath but we have state textures,
                    // pick one ("normal" if present) and use that as the base 2D texture/icon.
                    if (string.IsNullOrWhiteSpace(customItem.TexturePath) &&
                        customItem.StateTexturePaths.Count > 0)
                    {
                        string? chosenState = null;
                        string? chosenTexPath = null;

                        if (customItem.StateTexturePaths.TryGetValue("normal", out var normalTex) &&
                            !string.IsNullOrWhiteSpace(normalTex))
                        {
                            chosenState = "normal";
                            chosenTexPath = normalTex;
                        }
                        else
                        {
                            foreach (var kv in customItem.StateTexturePaths)
                            {
                                if (!string.IsNullOrWhiteSpace(kv.Value))
                                {
                                    chosenState = kv.Key;
                                    chosenTexPath = kv.Value;
                                    break;
                                }
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(chosenTexPath))
                        {
                            customItem.TexturePath = chosenTexPath;
                            if (string.IsNullOrWhiteSpace(customItem.IconPath))
                                customItem.IconPath = chosenTexPath;

                            LogInfo(itemNamespace, itemId,
                                "promoted state texture '" + chosenState + "' as base TexturePath/IconPath → " + chosenTexPath);
                        }
                    }

                    Lists.CustomItems.Add(customItem);
                    totalEntries++;
                }

                processedFiles++;
            }

            ConsoleWorker.Write.Line("info", "Custom items: processed files=" + processedFiles + " entries=" + totalEntries);
        }

        /// <summary>
        /// For items that define a base 3D model via resource.model_path and use a
        /// special tool material (BOW, CROSSBOW, SHIELD, FISHING_ROD, TRIDENT),
        /// automatically derive state models based on ItemsAdder naming conventions.
        /// 
        /// Example:
        ///   base = "frost_spear"
        ///   TRIDENT → state "throwing" → "frost_spear_throwing.json"
        /// </summary>
        private static void PopulateResourceStateModels(
            CustomItem customItem,
            string itemsAdderFolder,
            string? itemNamespace,
            string baseModelName,
            string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemNamespace))
                return;

            if (string.IsNullOrWhiteSpace(customItem.Material))
                return;

            string material = customItem.Material.ToUpperInvariant();

            // Make sure we only handle the relevant materials
            bool isSpecial =
                material.Contains("BOW") ||
                material.Contains("CROSSBOW") ||
                material.Contains("SHIELD") ||
                material.Contains("FISHING_ROD") ||
                material.Contains("TRIDENT");

            if (!isSpecial)
                return;

            string baseName = baseModelName; // already without extension
            var candidates = new System.Collections.Generic.List<(string state, string modelName)>();

            if (material.Contains("BOW") && !material.Contains("CROSSBOW"))
            {
                // BOW: X_pulling_0/1/2
                candidates.Add(("pulling_0", baseName + "_pulling_0"));
                candidates.Add(("pulling_1", baseName + "_pulling_1"));
                candidates.Add(("pulling_2", baseName + "_pulling_2"));
            }
            else if (material.Contains("CROSSBOW"))
            {
                // CROSSBOW: X_pulling_0/1/2, X_arrow, X_firework
                candidates.Add(("pulling_0", baseName + "_pulling_0"));
                candidates.Add(("pulling_1", baseName + "_pulling_1"));
                candidates.Add(("pulling_2", baseName + "_pulling_2"));
                candidates.Add(("arrow", baseName + "_arrow"));
                candidates.Add(("rocket", baseName + "_firework"));
            }
            else if (material.Contains("SHIELD"))
            {
                // SHIELD: X_blocking
                candidates.Add(("blocking", baseName + "_blocking"));
            }
            else if (material.Contains("FISHING_ROD"))
            {
                // FISHING ROD: X_cast
                candidates.Add(("cast", baseName + "_cast"));
            }
            else if (material.Contains("TRIDENT"))
            {
                // TRIDENT: X_throwing
                candidates.Add(("throwing", baseName + "_throwing"));
            }

            foreach (var (state, modelName) in candidates)
            {
                // Don't override explicit graphics.models definitions if present
                if (customItem.StateModelPaths.ContainsKey(state))
                    continue;

                string modelAsset = "assets/" + itemNamespace + "/models/" + modelName + ".json";

                if (JsonParserWorker.TryResolveContentAssetAbsolute(itemsAdderFolder, modelAsset, out var modelAbs) &&
                    File.Exists(modelAbs))
                {
                    customItem.StateModelPaths[state] = modelAbs;
                    ConsoleWorker.Write.Line("info",
                        itemNamespace + ":" + itemId + " resource-derived state model[" + state + "] → " + modelAbs);
                }
                else
                {
                    ConsoleWorker.Write.Line("warn",
                        itemNamespace + ":" + itemId + " missing resource state model " + state + ": " + modelAsset);
                }
            }
        }
    }
}