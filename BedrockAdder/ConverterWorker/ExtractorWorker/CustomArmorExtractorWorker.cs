// CustomArmorWorker.cs
using BedrockAdder.FileWorker;
using BedrockAdder.Library;
using System;
using System.Collections.Generic;
using System.IO;
using YamlDotNet.RepresentationModel;

namespace BedrockAdder.ExtractorWorker.ConverterWorker
{
    internal static class CustomArmorExtractorWorker
    {
        internal static void ExtractCustomArmorsFromPaths(string itemsAdderRootPath, string selectedVersion)
        {
            ConsoleWorker.Write.Line("info", "Armor: extraction started. Paths=" + (Lists.CustomArmorPaths?.Count ?? 0));

            int filesProcessed = 0;
            int armorItemsAdded = 0;

            foreach (var armorYamlPath in Lists.CustomArmorPaths)
            {
                filesProcessed++;

                try
                {
                    if (!File.Exists(armorYamlPath))
                    {
                        ConsoleWorker.Write.Line("warn", "Armor file missing: " + armorYamlPath);
                        continue;
                    }

                    using var reader = new StreamReader(armorYamlPath);
                    var yaml = new YamlStream();
                    yaml.Load(reader);

                    if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode rootNode)
                        continue;

                    // 1) Determine namespace for content-pack layer resolution
                    string armorNamespaceFromFile = ArmorYamlParserWorker.GetFileNamespaceOrDefault(rootNode, "unknown");

                    // 2) LAYERS PHASE — read all equipments.* entries first
                    var equipmentToLayers = new Dictionary<string, (string? layerChestRel, string? layerLegsRel)>(StringComparer.OrdinalIgnoreCase);

                    if (rootNode.Children.TryGetValue("equipments", out var equipmentsNode) && equipmentsNode is YamlMappingNode equipmentsMap)
                    {
                        foreach (var kv in equipmentsMap.Children)
                        {
                            if (kv.Key is not YamlScalarNode equipmentKeyNode || kv.Value is not YamlMappingNode equipmentPropsMap)
                                continue;

                            string equipmentId = equipmentKeyNode.Value ?? string.Empty;
                            if (string.IsNullOrWhiteSpace(equipmentId))
                                continue;

                            string? layerChestRel = null;
                            string? layerLegsRel = null;

                            if (MainYamlParserWorker.TryGetScalar(equipmentPropsMap, "layer_1", out var l1) && !string.IsNullOrWhiteSpace(l1))
                                layerChestRel = ArmorYamlParserWorker.NormalizeArmorLayerRelativePath(l1!);

                            if (MainYamlParserWorker.TryGetScalar(equipmentPropsMap, "layer_2", out var l2) && !string.IsNullOrWhiteSpace(l2))
                                layerLegsRel = ArmorYamlParserWorker.NormalizeArmorLayerRelativePath(l2!);

                            equipmentToLayers[equipmentId] = (layerChestRel, layerLegsRel);

                            string chestAbs = layerChestRel == null ? "(none)" :
                                ArmorYamlParserWorker.BuildItemsAdderContentTexturePath(itemsAdderRootPath, armorNamespaceFromFile, layerChestRel);
                            string legsAbs = layerLegsRel == null ? "(none)" :
                                ArmorYamlParserWorker.BuildItemsAdderContentTexturePath(itemsAdderRootPath, armorNamespaceFromFile, layerLegsRel);

                            bool chestExists = layerChestRel != null && File.Exists(chestAbs);
                            bool legsExists = layerLegsRel != null && File.Exists(legsAbs);

                            ConsoleWorker.Write.Line(
                                "info",
                                "Layer mapping " + armorNamespaceFromFile + ":" + equipmentId +
                                " layer_1 → " + chestAbs + " (exists=" + chestExists + ")"
                            );
                            ConsoleWorker.Write.Line(
                                "info",
                                "Layer mapping " + armorNamespaceFromFile + ":" + equipmentId +
                                " layer_2 → " + legsAbs + " (exists=" + legsExists + ")"
                            );

                            if (layerChestRel != null && !chestExists)
                            {
                                ConsoleWorker.Write.Line(
                                    "error",
                                    "Armor layer_1 texture missing for " + armorNamespaceFromFile + ":" + equipmentId + " → " + chestAbs
                                );
                            }

                            if (layerLegsRel != null && !legsExists)
                            {
                                ConsoleWorker.Write.Line(
                                    "error",
                                    "Armor layer_2 texture missing for " + armorNamespaceFromFile + ":" + equipmentId + " → " + legsAbs
                                );
                            }
                        }
                    }
                    else
                    {
                        ConsoleWorker.Write.Line("debug", "No 'equipments' section in " + armorYamlPath + " — items may still use legacy or guessed layers.");
                    }

                    // 3) ITEMS PHASE — iterate items and attach both layer paths (relative) to each armor piece
                    if (!rootNode.Children.TryGetValue("items", out var itemsNode) || itemsNode is not YamlMappingNode itemsMap)
                        continue;

                    foreach (var itemEntry in itemsMap.Children)
                    {
                        if (itemEntry.Key is not YamlScalarNode itemKeyNode || itemEntry.Value is not YamlMappingNode itemProps)
                            continue;

                        string fullItemKey = itemKeyNode.Value ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(fullItemKey))
                            continue;

                        // Only process armor items
                        if (!ArmorYamlParserWorker.TryIsArmorItem(itemProps))
                        {
                            ConsoleWorker.Write.Line("debug", "Skip non-armor item: " + fullItemKey);
                            continue;
                        }

                        // Resolve armor namespace and id from key (or fallback to file namespace)
                        string armorNamespace = armorNamespaceFromFile;
                        string armorId = fullItemKey;
                        var split = fullItemKey.Split(':');
                        if (split.Length == 2)
                        {
                            armorNamespace = split[0];
                            armorId = split[1];
                        }

                        string armorSlot = ArmorYamlParserWorker.GetArmorSlotFromItem(itemProps);
                        string? equipmentId = ArmorYamlParserWorker.GetEquipmentId(itemProps);

                        // Determine the layer pair to attach:
                        string? armorLayerChestRel = null;
                        string? armorLayerLegsRel = null;

                        if (!string.IsNullOrWhiteSpace(equipmentId) &&
                            equipmentToLayers.TryGetValue(equipmentId!, out var pair) &&
                            (pair.layerChestRel != null || pair.layerLegsRel != null))
                        {
                            armorLayerChestRel = pair.layerChestRel;
                            armorLayerLegsRel = pair.layerLegsRel;
                        }
                        else
                        {
                            var guess = ArmorYamlParserWorker.GuessArmorLayersFromId(armorId);
                            armorLayerChestRel = guess.chest;
                            armorLayerLegsRel = guess.legs;

                            ConsoleWorker.Write.Line(
                                "debug",
                                "Guessed layers for " + armorNamespace + ":" + armorId +
                                " layer_1=" + (armorLayerChestRel ?? "none") +
                                " layer_2=" + (armorLayerLegsRel ?? "none")
                            );

                            if (!string.IsNullOrWhiteSpace(armorLayerChestRel))
                            {
                                string chestAbsGuess = ArmorYamlParserWorker.BuildItemsAdderContentTexturePath(itemsAdderRootPath, armorNamespace, armorLayerChestRel);
                                if (!File.Exists(chestAbsGuess))
                                {
                                    ConsoleWorker.Write.Line(
                                        "error",
                                        "Armor guessed layer_1 texture missing for " + armorNamespace + ":" + armorId + " → " + chestAbsGuess
                                    );
                                }
                            }

                            if (!string.IsNullOrWhiteSpace(armorLayerLegsRel))
                            {
                                string legsAbsGuess = ArmorYamlParserWorker.BuildItemsAdderContentTexturePath(itemsAdderRootPath, armorNamespace, armorLayerLegsRel);
                                if (!File.Exists(legsAbsGuess))
                                {
                                    ConsoleWorker.Write.Line(
                                        "error",
                                        "Armor guessed layer_2 texture missing for " + armorNamespace + ":" + armorId + " → " + legsAbsGuess
                                    );
                                }
                            }
                        }

                        // Detect vanilla recolor info like items do (same semantics as CustomItem)
                        string? vanillaId;
                        bool usesVanillaTexture = ItemYamlParserWorker.TryDetectVanillaTexture(itemProps, out vanillaId);

                        string? tint = null;
                        ItemYamlParserWorker.TryGetRecolorTint(itemProps, out tint);

                        // Held / GUI icon (2D) from YAML (graphics.texture preferred, normalized to assets/... form)
                        string? heldIconTexturePath = ArmorYamlParserWorker.TryGet2DIcon(itemProps);

                        string armorMaterial = ArmorYamlParserWorker.TryGetArmorMaterial(itemProps, DefaultMaterialForSlot(armorSlot));
                        int? customModelData = ArmorYamlParserWorker.TryGetCustomModelDataFromCache(itemsAdderRootPath, armorNamespace, armorId);


                        // Only helmets can have a 3D model (if present in YAML)
                        string? helmetModelPathRaw = armorSlot == "helmet"
                            ? ArmorYamlParserWorker.TryGetHelmetModelPath(itemProps)
                            : null;

                        string? helmetModelResolved = null;
                        var helmetTextureMapAbs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                        if (!string.IsNullOrWhiteSpace(helmetModelPathRaw))
                        {
                            string normalizedModel = helmetModelPathRaw!.Replace("\\", "/").Trim();
                            string modelNameForZip = FurnitureYamlParserWorker.NormalizeModelNameForZipLookup(normalizedModel);
                            string modelAsset = "assets/" + armorNamespace + "/models/" + modelNameForZip + ".json";

                            if (JsonParserWorker.TryResolveContentAssetAbsolute(itemsAdderRootPath, modelAsset, out var modelAbs) &&
                                File.Exists(modelAbs))
                            {
                                helmetModelResolved = modelAbs;
                            }
                            else
                            {
                                helmetModelResolved = modelAsset;
                                ConsoleWorker.Write.Line(
                                    "warn",
                                    armorNamespace + ":" + armorId + " helmet model missing on disk: " + modelAsset
                                );
                            }

                            var helmetTextureMap = JsonParserWorker.ResolveModelTextureMapWithParents(itemsAdderRootPath, armorNamespace, modelNameForZip);
                            foreach (var kv in helmetTextureMap)
                            {
                                string normalizedTexture = kv.Value;
                                if (JsonParserWorker.TryResolveContentAssetAbsolute(itemsAdderRootPath, normalizedTexture, out var texAbs) &&
                                    File.Exists(texAbs))
                                {
                                    helmetTextureMapAbs[kv.Key] = texAbs;
                                }
                                else
                                {
                                    ConsoleWorker.Write.Line(
                                        "warn",
                                        armorNamespace + ":" + armorId +
                                        " missing helmet texture for slot " + kv.Key + ": " + normalizedTexture
                                    );
                                }
                            }
                        }

                        // Build the data object.
                        //
                        // Semantics (aligned with CustomItem):
                        // - UsesVanillaTexture: true if graphics/resource points to a minecraft: texture.
                        // - VanillaTextureId : the full minecraft:... id (e.g. "minecraft:item/iron_helmet.png").
                        // - TexturePath      : absolute IA icon path for non-vanilla armors (if we can resolve it).
                        var customArmor = new CustomArmor
                        {
                            ArmorNamespace = armorNamespace,
                            ArmorID = armorId,
                            Slot = armorSlot,
                            Material = armorMaterial,

                            // For non-vanilla, this will be filled below from IA; for vanilla we leave it empty
                            TexturePath = string.Empty,
                            IconPath = null,

                            ModelPath = helmetModelResolved,
                            CustomModelData = customModelData,

                            ArmorLayerChest = armorLayerChestRel ?? string.Empty,
                            ArmorLayerLegs = armorLayerLegsRel ?? string.Empty,

                            // Recolor tint (can be null if not recolored)
                            RecolorTint = tint,
                            UsesVanillaTexture = usesVanillaTexture,
                            VanillaTextureId = usesVanillaTexture ? vanillaId : null,

                            // Group all pieces that share the same equipment set
                            ArmorSetId = !string.IsNullOrWhiteSpace(equipmentId)
                                ? equipmentId              // e.g. "bronze_armor"
                                : armorId                  // fallback: per-item set
                        };

                        if (usesVanillaTexture && !string.IsNullOrWhiteSpace(vanillaId))
                        {
                            ConsoleWorker.Write.Line(
                                "info",
                                "Armor uses vanilla icon " + armorNamespace + ":" + armorId +
                                " → " + vanillaId + " tint=" + (tint ?? "none")
                            );
                        }

                        // If this armor is NOT vanilla-based, try resolving an IA-held icon
                        if (!usesVanillaTexture && !string.IsNullOrWhiteSpace(heldIconTexturePath))
                        {
                            if (JsonParserWorker.TryResolveContentAssetAbsolute(
                                    itemsAdderRootPath,
                                    heldIconTexturePath,
                                    out var heldIconAbs,
                                    armorNamespace
                                ) && File.Exists(heldIconAbs))
                            {
                                customArmor.TexturePath = heldIconAbs;
                                customArmor.IconPath = heldIconAbs;
                            }
                            else
                            {
                                ConsoleWorker.Write.Line(
                                    "debug",
                                    armorNamespace + ":" + armorId +
                                    " armor IA icon not resolved in content pack: " + heldIconTexturePath
                                );
                            }
                        }

                        foreach (var kv in helmetTextureMapAbs)
                        {
                            customArmor.ModelTexturePaths[kv.Key] = kv.Value;
                        }

                        Lists.CustomArmors.Add(customArmor);
                        armorItemsAdded++;

                        ConsoleWorker.Write.Line(
                            "info",
                            "Armor added " + armorNamespace + ":" + armorId +
                            " [" + armorSlot + "] equip=" + (equipmentId ?? "none") +
                            " layers=(layer_1=" + (armorLayerChestRel ?? "none") +
                            ", layer_2=" + (armorLayerLegsRel ?? "none") + ")"
                        );
                    }
                }
                catch (Exception ex)
                {
                    ConsoleWorker.Write.Line("warn", "Armor parse failed for " + armorYamlPath + ": " + ex.Message);
                }
            }

            ConsoleWorker.Write.Line("info", "Armor: extraction finished. Files=" + filesProcessed + " Items=" + armorItemsAdded);
        }

        private static string DefaultMaterialForSlot(string armorSlot)
        {
            return armorSlot switch
            {
                "helmet" => "leather_helmet",
                "chestplate" => "leather_chestplate",
                "leggings" => "leather_leggings",
                "boots" => "leather_boots",
                _ => "leather_helmet"
            };
        }
    }
}