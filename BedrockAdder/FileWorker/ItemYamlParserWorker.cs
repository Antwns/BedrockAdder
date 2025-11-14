using BedrockAdder.ConsoleWorker;
using System;
using System.IO;
using System.Linq;
using YamlDotNet.RepresentationModel;

namespace BedrockAdder.FileWorker
{
    internal static class ItemYamlParserWorker
    {
        internal static YamlMappingNode? LoadRootMapping(string filePath)
        {
            if (!File.Exists(filePath))
            {
                Write.Line("warn", "YAML file not found: " + filePath);
                return null;
            }

            using var reader = new StreamReader(filePath);
            var yaml = new YamlStream();
            yaml.Load(reader);

            if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode root)
                return null;

            return root;
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

        internal static int? AsInt(string? s)
        {
            return int.TryParse(s, out var result) ? result : (int?)null;
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

        // For IA items we want something like "hammers/osmium_hammer.png",
        // which then gets turned into:
        // {itemsAdderRoot}\contents\{ns}\resourcepack\{ns}\textures\{that-relative-path}
        internal static bool TryGet2DTexturePathNormalized(YamlMappingNode itemProps, out string normalizedPath)
        {
            normalizedPath = string.Empty;
            string raw = string.Empty;

            // 1) graphics.texture (for items defined under graphics:)
            if (TryGetMapping(itemProps, "graphics", out var graphicsNode) &&
                graphicsNode is YamlMappingNode graphicsMap &&
                TryGetScalar(graphicsMap, "texture", out var gTex) &&
                !string.IsNullOrWhiteSpace(gTex))
            {
                raw = gTex;
            }
            else if (TryGetMapping(itemProps, "resource", out var resourceNode) &&
                     resourceNode is YamlMappingNode resourceMap)
            {
                // 2) resource.texture_path
                if (TryGetScalar(resourceMap, "texture_path", out var rTexPath) &&
                    !string.IsNullOrWhiteSpace(rTexPath))
                {
                    raw = rTexPath;
                }
                // 3) resource.texture (your weapons:bronze_sword case)
                else if (TryGetScalar(resourceMap, "texture", out var rTex) &&
                         !string.IsNullOrWhiteSpace(rTex))
                {
                    raw = rTex;
                }
                // 4) resource.textures: [ "foo/bar", ... ]
                else if (resourceMap.Children.TryGetValue(new YamlScalarNode("textures"), out var texturesNode) &&
                         texturesNode is YamlSequenceNode seq &&
                         seq.Children.Count > 0 &&
                         seq.Children[0] is YamlScalarNode firstTexNode &&
                         !string.IsNullOrWhiteSpace(firstTexNode.Value))
                {
                    raw = firstTexNode.Value;
                }
            }

            if (string.IsNullOrWhiteSpace(raw))
                return false;

            // Basic YAML-style normalization (NOT the model JSON normalizer)
            var tex = raw.Trim().Replace("\\", "/");

            // If someone put a full vanilla id here, bail; that is handled via TryDetectVanillaTexture.
            if (tex.StartsWith("minecraft:", StringComparison.OrdinalIgnoreCase))
            {
                normalizedPath = string.Empty;
                return false;
            }

            // Namespaced textures (e.g. namespace:items/sword)
            int colonIndex = tex.IndexOf(':');
            if (colonIndex > 0)
            {
                string ns = tex.Substring(0, colonIndex).Trim();
                string rel = tex.Substring(colonIndex + 1).TrimStart('/');

                if (string.IsNullOrWhiteSpace(ns) || string.IsNullOrWhiteSpace(rel))
                {
                    normalizedPath = string.Empty;
                    return false;
                }

                if (!rel.StartsWith("textures/", StringComparison.OrdinalIgnoreCase))
                    rel = "textures/" + rel;

                if (string.IsNullOrEmpty(Path.GetExtension(rel)))
                    rel += ".png";

                normalizedPath = $"assets/{ns}/{rel}";
                return true;
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

            normalizedPath = tex;
            return true;
        }

        /// <summary>
        /// Detects ItemsAdder recolor tint if present.
        /// Preferred: graphics.color; then resource.color; fallback: top-level color.
        /// </summary>
        internal static bool TryGetRecolorTint(YamlMappingNode itemProps, out string? tint)
        {
            tint = null;

            // Preferred: graphics.color
            if (TryGetMapping(itemProps, "graphics", out var graphicsNode) &&
                graphicsNode is YamlMappingNode graphicsMap &&
                TryGetScalar(graphicsMap, "color", out var gColor) &&
                !string.IsNullOrWhiteSpace(gColor))
            {
                tint = gColor.Trim();
                return true;
            }

            // Fallback: resource.color
            if (TryGetMapping(itemProps, "resource", out var resourceNode) &&
                resourceNode is YamlMappingNode resourceMap &&
                TryGetScalar(resourceMap, "color", out var rColor) &&
                !string.IsNullOrWhiteSpace(rColor))
            {
                tint = rColor.Trim();
                return true;
            }

            // Last resort: top-level color (just in case some configs use it)
            if (TryGetScalar(itemProps, "color", out var topColor) &&
                !string.IsNullOrWhiteSpace(topColor))
            {
                tint = topColor.Trim();
                return true;
            }

            return false;
        }

        internal static string GetFileNameSpace(YamlMappingNode root, string fallback)
        {
            if (root == null)
                return fallback;

            // YAML:
            // info:
            //   namespace: tools
            if (root.Children.TryGetValue(new YamlScalarNode("info"), out var infoNode) &&
                infoNode is YamlMappingNode infoMap &&
                infoMap.Children.TryGetValue(new YamlScalarNode("namespace"), out var nsNode) &&
                nsNode is YamlScalarNode nsScalar &&
                !string.IsNullOrWhiteSpace(nsScalar.Value))
            {
                return nsScalar.Value;
            }

            return fallback;
        }


        /// <summary>
        /// Builds the absolute file path for a content-pack texture using ItemsAdder content layout:
        /// {itemsAdderRootPath}\contents\{ns}\resourcepack\{ns}\textures\{relativeTexturePath}
        /// </summary>
        internal static string BuildItemsAdderContentTexturePath(string itemsAdderRootPath, string ns, string relativeTexturePath)
        {
            string rel = relativeTexturePath.Replace("\\", "/").TrimStart('/');
            return Path.Combine(itemsAdderRootPath, "contents", ns, "resourcepack", ns, "textures", rel);
        }

        internal static bool TryDetectVanillaTexture(YamlMappingNode itemProps, out string? vanillaId)
        {
            vanillaId = null;

            if (itemProps == null)
                return false;

            // 1) graphics.texture
            if (TryGetMapping(itemProps, "graphics", out var graphicsNode) &&
                graphicsNode is YamlMappingNode graphicsMap &&
                TryGetScalar(graphicsMap, "texture", out var gTex) &&
                !string.IsNullOrWhiteSpace(gTex))
            {
                var val = gTex.Trim();
                if (val.StartsWith("minecraft:", StringComparison.OrdinalIgnoreCase))
                {
                    vanillaId = val;
                    return true;
                }
            }

            // 2) resource.texture_path
            if (TryGetMapping(itemProps, "resource", out var resourceNode) &&
                resourceNode is YamlMappingNode resourceMap)
            {
                if (TryGetScalar(resourceMap, "texture_path", out var tPath) &&
                    !string.IsNullOrWhiteSpace(tPath))
                {
                    var val = tPath.Trim();
                    if (val.StartsWith("minecraft:", StringComparison.OrdinalIgnoreCase))
                    {
                        vanillaId = val;
                        return true;
                    }
                }

                // 3) resource.texture
                if (TryGetScalar(resourceMap, "texture", out var rTex) &&
                    !string.IsNullOrWhiteSpace(rTex))
                {
                    var val = rTex.Trim();
                    if (val.StartsWith("minecraft:", StringComparison.OrdinalIgnoreCase))
                    {
                        vanillaId = val;
                        return true;
                    }
                }
            }

            return false;
        }
    }
}