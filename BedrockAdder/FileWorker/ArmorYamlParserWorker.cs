using BedrockAdder.ConsoleWorker;
using System;
using System.IO;
using YamlDotNet.RepresentationModel;

namespace BedrockAdder.FileWorker
{
    internal static class ArmorYamlParserWorker
    {
        internal static string GetFileNamespaceOrDefault(YamlMappingNode root, string defaultNamespace)
        {
            if (root.Children.TryGetValue("info", out var infoNode) && infoNode is YamlMappingNode infoMap)
            {
                if (infoMap.Children.TryGetValue("namespace", out var namespaceNode) &&
                    namespaceNode is YamlScalarNode namespaceScalar &&
                    !string.IsNullOrWhiteSpace(namespaceScalar.Value))
                {
                    return namespaceScalar.Value!;
                }
            }
            return defaultNamespace;
        }

        internal static string? TryGetScalar(YamlMappingNode node, string key)
        {
            return node.Children.TryGetValue(new YamlScalarNode(key), out var value) && value is YamlScalarNode scalar
                ? scalar.Value
                : null;
        }

        internal static bool TryGetScalar(YamlMappingNode node, string key, out string? value)
        {
            value = null;
            if (node.Children.TryGetValue(new YamlScalarNode(key), out var val) && val is YamlScalarNode scalar)
            {
                value = scalar.Value;
                return true;
            }
            return false;
        }

        internal static bool TryGetMapping(YamlMappingNode node, string key, out YamlMappingNode? map)
        {
            map = null;
            if (node.Children.TryGetValue(new YamlScalarNode(key), out var val) && val is YamlMappingNode m)
            {
                map = m;
                return true;
            }
            return false;
        }

        internal static int? TryGetCustomModelDataFromCache(string itemsAdderRootPath, string armorNamespace, string armorId)
        {
            try
            {
                string cachePath = Path.Combine(itemsAdderRootPath, "storage", "items_ids_cache.yml");
                if (!File.Exists(cachePath))
                    return null;

                using var reader = new StreamReader(cachePath);
                var yaml = new YamlStream();
                yaml.Load(reader);

                if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode root)
                    return null;

                if (root.Children.TryGetValue(armorNamespace, out var namespaceNode) &&
                    namespaceNode is YamlMappingNode namespaceMap)
                {
                    if (namespaceMap.Children.TryGetValue(armorId, out var idNode) &&
                        idNode is YamlScalarNode idScalar &&
                        int.TryParse(idScalar.Value, out int value))
                    {
                        return value;
                    }
                }
                return null;
            }
            catch (Exception ex)
            {
                Write.Line("warning", "Failed to read items_ids_cache.yml: " + ex.Message);
                return null;
            }
        }

        internal static bool TryIsArmorItem(YamlMappingNode itemProps)
        {
            if (itemProps.Children.TryGetValue("equipment", out var equipmentNode) && equipmentNode is YamlMappingNode)
                return true;

            if (itemProps.Children.TryGetValue("equipments", out var equipmentsNode) && equipmentsNode is YamlMappingNode)
                return true;

            if (itemProps.Children.TryGetValue("equipment", out var equipmentScalarNode) &&
                equipmentScalarNode is YamlScalarNode equipmentScalar &&
                (equipmentScalar.Value?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false))
                return true;

            return false;
        }

        internal static string? TryGetHelmetModelPath(YamlMappingNode itemProps)
        {
            if (itemProps.Children.TryGetValue("resource", out var resourceNode) &&
                resourceNode is YamlMappingNode resourceMap)
            {
                if (resourceMap.Children.TryGetValue("model_path", out var modelNode) &&
                    modelNode is YamlScalarNode modelScalar &&
                    !string.IsNullOrWhiteSpace(modelScalar.Value))
                    return modelScalar.Value!;
            }

            if (itemProps.Children.TryGetValue("graphics", out var graphicsNode) &&
                graphicsNode is YamlMappingNode graphicsMap)
            {
                if (graphicsMap.Children.TryGetValue("model", out var modelNode) &&
                    modelNode is YamlScalarNode modelScalar &&
                    !string.IsNullOrWhiteSpace(modelScalar.Value))
                    return modelScalar.Value!;
            }
            return null;
        }

        internal static (string? chest, string? legs) GuessArmorLayersFromId(string armorId)
        {
            string basePath = "textures/models/armor/" + armorId.ToLowerInvariant();
            return (basePath + "_layer_1.png", basePath + "_layer_2.png");
        }

        internal static string GetArmorSlotFromItem(YamlMappingNode itemProps)
        {
            if (itemProps.Children.TryGetValue("equipment", out var equipmentNode) &&
                equipmentNode is YamlMappingNode equipmentMap)
            {
                if (equipmentMap.Children.TryGetValue("slot", out var slotNode) &&
                    slotNode is YamlScalarNode slotScalar &&
                    !string.IsNullOrWhiteSpace(slotScalar.Value))
                {
                    string slotValue = slotScalar.Value!.Trim().ToUpperInvariant();
                    return slotValue switch
                    {
                        "HEAD" => "helmet",
                        "CHEST" => "chestplate",
                        "LEGS" => "leggings",
                        "FEET" => "boots",
                        _ => "helmet"
                    };
                }
            }
            return "helmet";
        }

        internal static string? GetEquipmentId(YamlMappingNode itemProps)
        {
            if (itemProps.Children.TryGetValue("equipment", out var equipmentNode) &&
                equipmentNode is YamlMappingNode equipmentMap)
            {
                if (equipmentMap.Children.TryGetValue("id", out var idNode) &&
                    idNode is YamlScalarNode idScalar &&
                    !string.IsNullOrWhiteSpace(idScalar.Value))
                    return idScalar.Value!;
            }
            return null;
        }

        internal static (string? chest, string? legs) GetLayersFromEquipments(YamlMappingNode root, string equipmentId)
        {
            if (root.Children.TryGetValue("equipments", out var equipmentsNode) &&
                equipmentsNode is YamlMappingNode equipmentsMap)
            {
                if (equipmentsMap.Children.TryGetValue(equipmentId, out var setNode) &&
                    setNode is YamlMappingNode setMap)
                {
                    string? layer1 = setMap.Children.TryGetValue("layer_1", out var n1) && n1 is YamlScalarNode s1 ? s1.Value : null;
                    string? layer2 = setMap.Children.TryGetValue("layer_2", out var n2) && n2 is YamlScalarNode s2 ? s2.Value : null;
                    return (NormalizeArmorLayerRelativePath(layer1), NormalizeArmorLayerRelativePath(layer2));
                }
            }
            return (null, null);
        }

        internal static string? NormalizeArmorLayerRelativePath(string? layerPath)
        {
            if (string.IsNullOrWhiteSpace(layerPath))
                return null;

            string normalized = layerPath.Replace("\\", "/").TrimStart('/');
            if (!normalized.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                normalized += ".png";
            return normalized;
        }

        internal static string BuildItemsAdderContentTexturePath(string itemsAdderRootPath, string armorNamespace, string relativeTexturePath)
        {
            string rel = relativeTexturePath.Replace("\\", "/").TrimStart('/');
            return Path.Combine(itemsAdderRootPath, "contents", armorNamespace, "resourcepack", armorNamespace, "textures", rel);
        }

        internal static string TryGetArmorMaterial(YamlMappingNode itemProps, string defaultMaterial)
        {
            if (itemProps.Children.TryGetValue("resource", out var resourceNode) &&
                resourceNode is YamlMappingNode resourceMap)
            {
                if (resourceMap.Children.TryGetValue("material", out var materialNode) &&
                    materialNode is YamlScalarNode materialScalar &&
                    !string.IsNullOrWhiteSpace(materialScalar.Value))
                    return materialScalar.Value!;
            }

            if (itemProps.Children.TryGetValue("material", out var materialNode2) &&
                materialNode2 is YamlScalarNode materialScalar2 &&
                !string.IsNullOrWhiteSpace(materialScalar2.Value))
                return materialScalar2.Value!;

            return defaultMaterial;
        }

        /// <summary>
        /// For armor items we want something like "boots/rubber_boots.png" or
        /// "assets/ns/textures/boots/rubber_boots.png", same behavior as
        /// ItemYamlParserWorker.TryGet2DTexturePathNormalized.
        /// This is later resolved to an absolute path by the extractor.
        /// </summary>
        internal static string? TryGet2DIcon(YamlMappingNode itemProps)
        {
            string raw = string.Empty;

            // 1) graphics.texture
            if (TryGetMapping(itemProps, "graphics", out var graphicsNode) &&
                graphicsNode is YamlMappingNode graphicsMap &&
                TryGetScalar(graphicsMap, "texture", out var gTex) &&
                !string.IsNullOrWhiteSpace(gTex))
            {
                raw = gTex!;
            }
            else if (TryGetMapping(itemProps, "resource", out var resourceNode) &&
                     resourceNode is YamlMappingNode resourceMap)
            {
                // 2) resource.texture_path
                if (TryGetScalar(resourceMap, "texture_path", out var rTexPath) &&
                    !string.IsNullOrWhiteSpace(rTexPath))
                {
                    raw = rTexPath!;
                }
                // 3) resource.texture
                else if (TryGetScalar(resourceMap, "texture", out var rTex) &&
                         !string.IsNullOrWhiteSpace(rTex))
                {
                    raw = rTex!;
                }
                // 4) resource.textures: [ "foo/bar", ... ]
                else if (resourceMap.Children.TryGetValue(new YamlScalarNode("textures"), out var texturesNode) &&
                         texturesNode is YamlSequenceNode seq &&
                         seq.Children.Count > 0 &&
                         seq.Children[0] is YamlScalarNode firstTexNode &&
                         !string.IsNullOrWhiteSpace(firstTexNode.Value))
                {
                    raw = firstTexNode.Value!;
                }
            }

            if (string.IsNullOrWhiteSpace(raw))
                return null;

            // Basic YAML-style normalization (NOT the model JSON normalizer)
            var tex = raw.Trim().Replace("\\", "/");

            // If someone put a full vanilla id here, bail; that is handled via TryDetectVanillaTexture on items side.
            if (tex.StartsWith("minecraft:", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // Namespaced textures (e.g. namespace:items/sword)
            int colonIndex = tex.IndexOf(':');
            if (colonIndex > 0)
            {
                string ns = tex.Substring(0, colonIndex).Trim();
                string rel = tex.Substring(colonIndex + 1).TrimStart('/');

                if (string.IsNullOrWhiteSpace(ns) || string.IsNullOrWhiteSpace(rel))
                {
                    return null;
                }

                if (!rel.StartsWith("textures/", StringComparison.OrdinalIgnoreCase))
                    rel = "textures/" + rel;

                if (string.IsNullOrEmpty(Path.GetExtension(rel)))
                    rel += ".png";

                // "assets/ns/textures/foo/bar.png"
                return $"assets/{ns}/{rel}";
            }

            // Strip any full asset prefix that might be present
            const string assetsPrefix = "assets/minecraft/textures/";
            const string texturesPrefix = "textures/";

            if (tex.StartsWith(assetsPrefix, StringComparison.OrdinalIgnoreCase))
                tex = tex.Substring(assetsPrefix.Length);
            else if (tex.StartsWith(texturesPrefix, StringComparison.OrdinalIgnoreCase))
                tex = tex.Substring(texturesPrefix.Length);

            // If there is no extension, assume .png (ItemsAdder convention)
            if (string.IsNullOrEmpty(Path.GetExtension(tex)))
                tex += ".png";

            return tex;
        }

        /// <summary>
        /// Detects vanilla recolor info for armor in the "graphics" section
        /// where ItemsAdder might specify:
        ///   graphics:
        ///     texture: minecraft:item/iron_helmet.png
        ///     color:  FFCC66
        /// </summary>
        internal static bool TryGetVanillaRecolorInfo(
            YamlMappingNode itemProps,
            out string? vanillaTextureId,
            out string? tintHex
        )
        {
            vanillaTextureId = null;
            tintHex = null;

            if (TryGetMapping(itemProps, "graphics", out var graphicsMap) && graphicsMap is not null)
            {
                TryGetScalar(graphicsMap, "texture", out var texRaw);
                TryGetScalar(graphicsMap, "color", out var colorRaw);

                if (!string.IsNullOrWhiteSpace(texRaw) && !string.IsNullOrWhiteSpace(colorRaw))
                {
                    vanillaTextureId = texRaw.Trim();   // e.g. "minecraft:item/iron_helmet.png"
                    tintHex = colorRaw.Trim();          // e.g. "FFCC66"
                    return true;
                }
            }

            return false;
        }
    }
}