using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace BedrockAdder.FileWorker
{
    public static class JsonParserWorker
    {
        public static string GetGeneratedZipPath(string itemsAdderFolder)
        {
            return Path.Combine(itemsAdderFolder, "output", "generated.zip");
        }

        // NEW: quick exists check
        public static bool ZipEntryExists(string zipPath, string entryPath)
        {
            try
            {
                if (!File.Exists(zipPath)) return false;
                using (var zip = ZipFile.OpenRead(zipPath))
                    return zip.GetEntry(entryPath.Replace('\\', '/')) != null;
            }
            catch { return false; }
        }

        // NEW: read bytes helper (useful for future image probes)
        public static bool TryOpenZipEntryBytes(string zipPath, string entryPath, out byte[] data)
        {
            data = Array.Empty<byte>();
            try
            {
                if (!File.Exists(zipPath)) return false;
                using var zip = ZipFile.OpenRead(zipPath);
                var entry = zip.GetEntry(entryPath.Replace('\\', '/'));
                if (entry == null) return false;
                using var s = entry.Open();
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                data = ms.ToArray();
                return true;
            }
            catch { return false; }
        }

        public static bool TryOpenZipEntryText(string zipPath, string entryPath, out string text)
        {
            text = string.Empty;
            try
            {
                if (!File.Exists(zipPath)) return false;
                using var zip = ZipFile.OpenRead(zipPath);
                var entry = zip.GetEntry(entryPath.Replace('\\', '/'));
                if (entry == null) return false;
                using var s = entry.Open();
                using var sr = new StreamReader(s);
                text = sr.ReadToEnd();
                return true;
            }
            catch { return false; }
        }

        public static bool TryParseJson(string json, out JObject root)
        {
            root = new JObject();
            try { root = JObject.Parse(json); return true; }
            catch { return false; }
        }

        // NEW: tries common model locations if the flat path doesn't exist
        public static bool TryLoadAnyModelJsonFromGenerated(string itemsAdderFolder, string itemNamespace, string modelName, out JObject root)
        {
            root = new JObject();
            if (string.IsNullOrWhiteSpace(itemsAdderFolder) || string.IsNullOrWhiteSpace(itemNamespace) || string.IsNullOrWhiteSpace(modelName))
                return false;

            string zipPath = GetGeneratedZipPath(itemsAdderFolder);
            // Search order: flat, item/, block/
            var candidates = new[]
            {
                "assets/" + itemNamespace + "/models/" + modelName + ".json",
                "assets/" + itemNamespace + "/models/item/" + modelName + ".json",
                "assets/" + itemNamespace + "/models/block/" + modelName + ".json"
            };

            foreach (var entryPath in candidates)
            {
                if (TryOpenZipEntryText(zipPath, entryPath, out string json) && TryParseJson(json, out root))
                    return true;
            }
            return false;
        }

        // Keeps the original name for backward-compat
        public static bool TryLoadModelJsonFromGenerated(string itemsAdderFolder, string itemNamespace, string modelName, out JObject root)
            => TryLoadAnyModelJsonFromGenerated(itemsAdderFolder, itemNamespace, modelName, out root);

        public static List<string> ResolveModelTexturesFromZip(string itemsAdderFolder, string itemNamespace, string modelName)
        {
            if (!TryLoadModelJsonFromGenerated(itemsAdderFolder, itemNamespace, modelName, out var root))
                return new List<string>();
            return ResolveModelTexturesFromJObject(root);
        }

        // NEW: parent-aware resolver (optionally gather keys -> paths)
        public static Dictionary<string, string> ResolveModelTextureMapWithParents(string itemsAdderFolder, string itemNamespace, string modelName, int maxDepth = 8)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (!TryLoadAnyModelJsonFromGenerated(itemsAdderFolder, itemNamespace, modelName, out var current))
                return result;

            int depth = 0;
            while (current != null && depth++ < maxDepth)
            {
                // read this model's textures first so child overrides win
                var textures = ExtractTextureMapFromModel(current);
                foreach (var kv in textures)
                    if (!result.ContainsKey(kv.Key)) result[kv.Key] = kv.Value;

                // follow parent
                var parent = current["parent"]?.ToString();
                if (string.IsNullOrWhiteSpace(parent)) break;

                // parent can be namespaced or vanilla (e.g., "item/generated")
                string parentNs = itemNamespace;
                string parentName = parent;

                int colon = parent.IndexOf(':');
                if (colon > 0)
                {
                    parentNs = parent.Substring(0, colon);
                    parentName = parent.Substring(colon + 1);
                }

                // vanilla parents live under minecraft
                if (!parent.Contains(":") && (parent.StartsWith("item/", StringComparison.OrdinalIgnoreCase) ||
                                              parent.StartsWith("block/", StringComparison.OrdinalIgnoreCase)))
                {
                    parentNs = "minecraft";
                }

                if (!TryLoadAnyModelJsonFromGenerated(itemsAdderFolder, parentNs, parentName, out var parentObj))
                    break;

                current = parentObj;
            }

            // normalize to resource-pack paths and remove particle
            var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in result)
            {
                if (kv.Key.Equals("particle", StringComparison.OrdinalIgnoreCase)) continue;
                var path = NormalizeTexturePathFromModelValue(kv.Value);
                if (!string.IsNullOrWhiteSpace(path))
                    normalized[kv.Key] = path;
            }
            return normalized;
        }

        // The original list-returning API (no parent chain)
        public static List<string> ResolveModelTexturesFromJObject(JObject root)
        {
            var results = new List<string>();
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var texturesObj = root["textures"] as JObject;
            if (texturesObj == null) return results;

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in texturesObj.Properties())
                map[prop.Name] = prop.Value?.ToString() ?? string.Empty;

            string ResolveRef(string val)
            {
                const int maxHops = 16;
                int hops = 0;
                string current = val;
                while (current.StartsWith("#", StringComparison.Ordinal) && hops < maxHops)
                {
                    string key = current.Substring(1);
                    if (!map.TryGetValue(key, out string next) || string.IsNullOrEmpty(next)) break;
                    current = next;
                    hops++;
                }
                return current;
            }

            foreach (var kv in map)
            {
                if (kv.Key.Equals("particle", StringComparison.OrdinalIgnoreCase)) continue;
                string resolved = ResolveRef(kv.Value);
                string pngPath = NormalizeTexturePathFromModelValue(resolved);
                if (!string.IsNullOrWhiteSpace(pngPath) && set.Add(pngPath))
                    results.Add(pngPath);
            }
            return results;
        }

        // NEW: extract raw key->value texture map from a model (no normalization)
        public static Dictionary<string, string> ExtractTextureMapFromModel(JObject root)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var obj = root["textures"] as JObject;
            if (obj == null) return map;

            // first pass: raw
            foreach (var prop in obj.Properties())
                map[prop.Name] = prop.Value?.ToString() ?? string.Empty;

            // second pass: collapse indirections
            string ResolveRefLocal(string val)
            {
                const int maxHops = 16;
                int hops = 0;
                string current = val;
                while (current.StartsWith("#", StringComparison.Ordinal) && hops < maxHops)
                {
                    string key = current.Substring(1);
                    if (!map.TryGetValue(key, out string next) || string.IsNullOrEmpty(next)) break;
                    current = next;
                    hops++;
                }
                return current;
            }

            var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in map)
                resolved[kv.Key] = ResolveRefLocal(kv.Value);

            return resolved;
        }

        public static string NormalizeTexturePathFromModelValue(string modelValue)
        {
            if (string.IsNullOrWhiteSpace(modelValue)) return string.Empty;

            int colon = modelValue.IndexOf(':');
            if (colon > 0)
            {
                string ns = modelValue.Substring(0, colon);
                string path = modelValue.Substring(colon + 1);

                if (!path.StartsWith("textures/", StringComparison.OrdinalIgnoreCase))
                    path = "textures/" + path;
                if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    path += ".png";

                return "assets/" + ns + "/" + path;
            }

            string vanilla = modelValue;
            if (!vanilla.StartsWith("textures/", StringComparison.OrdinalIgnoreCase))
                vanilla = "textures/" + vanilla;
            if (!vanilla.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                vanilla += ".png";

            return "assets/minecraft/" + vanilla;
        }

        public static bool TryResolveContentAssetAbsolute(
            string itemsAdderFolder,
            string normalizedAssetPath,
            out string absolutePath,
            string? defaultNamespace = null)
        {
            absolutePath = string.Empty;

            if (string.IsNullOrWhiteSpace(itemsAdderFolder) || string.IsNullOrWhiteSpace(normalizedAssetPath))
                return false;

            string clean = normalizedAssetPath.Replace('\\', '/').Trim();
            if (string.IsNullOrWhiteSpace(clean))
                return false;

            clean = clean.TrimStart('/');

            if (!clean.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(defaultNamespace))
                {
                    if (!clean.StartsWith("textures/", StringComparison.OrdinalIgnoreCase))
                        clean = "textures/" + clean;
                    clean = $"assets/{defaultNamespace}/{clean}";
                }
                else
                {
                    clean = "assets/" + clean;
                }
            }

            string withoutAssets = clean.Substring("assets/".Length);
            int firstSlash = withoutAssets.IndexOf('/');
            if (firstSlash <= 0)
                return false;

            string ns = withoutAssets.Substring(0, firstSlash);
            string rel = withoutAssets.Substring(firstSlash + 1); // e.g. "models/foo.json" or "textures/item/bar.png"
            string relPath = rel.Replace('/', Path.DirectorySeparatorChar);

            var candidates = new List<string>
            {
                Path.Combine(itemsAdderFolder, "contents", ns, "resourcepack", "assets", ns, relPath),
                Path.Combine(itemsAdderFolder, "contents", ns, "resourcepack", ns, relPath),
                Path.Combine(itemsAdderFolder, "contents", ns, "resourcepack", relPath),
                Path.Combine(itemsAdderFolder, "output", "resourcepack", "assets", ns, relPath),
                Path.Combine(itemsAdderFolder, "output", "resourcepack", relPath)
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    absolutePath = candidate;
                    return true;
                }
            }

            string zipPath = GetGeneratedZipPath(itemsAdderFolder);
            if (TryOpenZipEntryBytes(zipPath, clean, out var data) && data.Length > 0)
            {
                string cacheDir = Path.Combine(itemsAdderFolder, "output", "_cache_assets");
                Directory.CreateDirectory(cacheDir);

                string safeName = MakeSafeCacheFileName(clean);
                string cachePath = Path.Combine(cacheDir, safeName);

                if (!File.Exists(cachePath))
                {
                    File.WriteAllBytes(cachePath, data);
                }

                absolutePath = cachePath;
                return true;
            }

            if (candidates.Count > 0)
                absolutePath = candidates[0];

            return false;
        }

        public static bool IsVanillaTexturePath(string normalizedPath)
        {
            return !string.IsNullOrWhiteSpace(normalizedPath)
                && normalizedPath.Replace('\\', '/')
                   .StartsWith("assets/minecraft/textures/", StringComparison.OrdinalIgnoreCase);
        }

        private static string MakeSafeCacheFileName(string normalizedAssetPath)
        {
            string name = normalizedAssetPath.Replace('\\', '_').Replace('/', '_');
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(invalid, '_');
            }
            if (!name.EndsWith(Path.GetExtension(normalizedAssetPath), StringComparison.OrdinalIgnoreCase))
            {
                string ext = Path.GetExtension(normalizedAssetPath);
                if (string.IsNullOrWhiteSpace(ext))
                    ext = ".bin";
                name += ext;
            }
            return name;
        }
    }
}