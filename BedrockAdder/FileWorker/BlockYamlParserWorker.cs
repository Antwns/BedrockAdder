using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YamlDotNet.RepresentationModel;

namespace BedrockAdder.FileWorker
{
    internal static class BlockYamlParserWorker
    {
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

        internal static int? AsInt(string? s)
        {
            return int.TryParse(s, out var result) ? result : (int?)null;
        }

        internal static string? ExtractNamespaceFromPath(string filePath)
        {
            var segments = filePath.Split(Path.DirectorySeparatorChar);
            int index = Array.IndexOf(segments, "contents");
            return (index != -1 && index + 1 < segments.Length) ? segments[index + 1] : null;
        }

        internal static int? GetCustomModelData(string itemsAdderFolder, string? itemNamespace, string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemNamespace))
                return null;

            string path = Path.Combine(itemsAdderFolder, "storage", "items_ids_cache.yml");
            if (!File.Exists(path))
                return null;

            using var reader = new StreamReader(path);
            var yaml = new YamlStream();
            yaml.Load(reader);

            if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode root)
                return null;

            string key = itemNamespace + ":" + itemId;
            if (root.Children.TryGetValue(new YamlScalarNode(key), out var valueNode) && valueNode is YamlScalarNode scalar)
            {
                return AsInt(scalar.Value);
            }

            return null;
        }

        // --- Block-specific helpers ---

        internal static bool TryGetBlockSpecificProps(YamlMappingNode itemProps, out YamlMappingNode? blockMap)
        {
            blockMap = null;
            if (itemProps == null) return false;

            if (!TryGetMapping(itemProps, "specific_properties", out var spec) || spec == null)
                return false;

            if (!TryGetMapping(spec, "block", out var blk) || blk == null)
                return false;

            blockMap = blk;
            return true;
        }

        internal static bool TryGetPlacedModelType(YamlMappingNode itemProps, out string? placedType)
        {
            placedType = null;
            if (!TryGetBlockSpecificProps(itemProps, out var blockMap) || blockMap == null)
                return false;

            if (TryGetMapping(blockMap, "placed_model", out var pm) && pm != null &&
                TryGetScalar(pm, "type", out var t) && !string.IsNullOrWhiteSpace(t))
            {
                placedType = t.Trim().ToUpperInvariant();
                return true;
            }

            return false;
        }

        internal static bool TryGetPlacedModelName(YamlMappingNode itemProps, out string? placedModel)
        {
            placedModel = null;
            if (!TryGetBlockSpecificProps(itemProps, out var blockMap) || blockMap == null)
                return false;

            if (TryGetScalar(blockMap, "placed_model", out var scalarVal) && !string.IsNullOrWhiteSpace(scalarVal))
            {
                placedModel = scalarVal.Trim();
                return true;
            }

            if (TryGetMapping(blockMap, "placed_model", out var pm) && pm != null)
            {
                if (TryGetScalar(pm, "name", out var name) && !string.IsNullOrWhiteSpace(name))
                {
                    placedModel = name.Trim();
                    return true;
                }
                if (TryGetScalar(pm, "id", out var id) && !string.IsNullOrWhiteSpace(id))
                {
                    placedModel = id.Trim();
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Normalize a block texture reference (from YAML) into a normalized asset path:
        ///  - If it has a namespace (ns:path), delegate to JsonParserWorker.NormalizeTexturePathFromModelValue.
        ///  - If it has no namespace:
        ///     - If it looks like "block/foo" or "item/foo", treat as vanilla (minecraft).
        ///     - Otherwise treat as ItemsAdder content under this blockNamespace.
        /// </summary>
        internal static string NormalizeBlockTextureModelValue(string modelValue, string blockNamespace)
        {
            if (string.IsNullOrWhiteSpace(modelValue))
                return string.Empty;

            string raw = modelValue.Replace("\\", "/").Trim();

            // explicit namespace → let JsonParserWorker decide (handles minecraft: and custom ns)
            // (use IndexOf instead of Contains with StringComparison to support older frameworks)
            if (raw.IndexOf(':') >= 0)
            {
                return JsonParserWorker.NormalizeTexturePathFromModelValue(raw);
            }

            // If it looks like block/... or item/... with no namespace, assume vanilla minecraft
            if (raw.StartsWith("block/", StringComparison.OrdinalIgnoreCase) ||
                raw.StartsWith("item/", StringComparison.OrdinalIgnoreCase) ||
                raw.StartsWith("textures/", StringComparison.OrdinalIgnoreCase))
            {
                return JsonParserWorker.NormalizeTexturePathFromModelValue(raw);
            }

            // ItemsAdder-style relative path (e.g. "advanced_generator/advanced_generator_top")
            string rel = raw.TrimStart('/');
            if (!rel.StartsWith("textures/", StringComparison.OrdinalIgnoreCase))
                rel = "textures/" + rel;
            if (!rel.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                rel += ".png";

            return "assets/" + blockNamespace + "/" + rel;
        }

        internal static bool TryGet2DTexturePathNormalized(string blockNamespace, YamlMappingNode itemProps, out string normalizedPath)
        {
            normalizedPath = string.Empty;

            if (TryGetMapping(itemProps, "graphics", out var graphics) &&
                TryGetScalar(graphics!, "texture", out var gfxTex) &&
                !string.IsNullOrWhiteSpace(gfxTex))
            {
                normalizedPath = NormalizeBlockTextureModelValue(gfxTex!, blockNamespace);
                return !string.IsNullOrWhiteSpace(normalizedPath);
            }

            if (TryGetMapping(itemProps, "resource", out var resource) &&
                TryGetScalar(resource!, "texture_path", out var resTex) &&
                !string.IsNullOrWhiteSpace(resTex))
            {
                normalizedPath = NormalizeBlockTextureModelValue(resTex!, blockNamespace);
                return !string.IsNullOrWhiteSpace(normalizedPath);
            }

            return false;
        }

        /// <summary>
        /// Reads graphics.textures mapping (up/down/north/south/east/west -> texture path),
        /// normalizes each value into an assets/... path using blockNamespace.
        /// Returns true if at least one face entry was found.
        /// </summary>
        internal static bool TryGetPerFaceTexturesNormalized(string blockNamespace, YamlMappingNode itemProps, out Dictionary<string, string> faceMap)
        {
            faceMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (!TryGetMapping(itemProps, "graphics", out var graphics) || graphics == null)
                return false;

            if (!graphics.Children.TryGetValue(new YamlScalarNode("textures"), out var texturesNode) ||
                texturesNode is not YamlMappingNode texturesMap)
            {
                return false;
            }

            bool any = false;

            foreach (var kv in texturesMap.Children)
            {
                if (kv.Key is not YamlScalarNode faceKeyNode || kv.Value is not YamlScalarNode texValNode)
                    continue;

                string? faceName = faceKeyNode.Value;
                string? rawPath = texValNode.Value;

                if (string.IsNullOrWhiteSpace(faceName) || string.IsNullOrWhiteSpace(rawPath))
                    continue;

                string normalized = NormalizeBlockTextureModelValue(rawPath, blockNamespace);
                if (string.IsNullOrWhiteSpace(normalized))
                    continue;

                faceMap[faceName.Trim()] = normalized;
                any = true;
            }

            return any;
        }
    }
}