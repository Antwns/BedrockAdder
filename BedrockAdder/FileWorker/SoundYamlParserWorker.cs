using CefSharp.DevTools.Storage;
using System;
using System.IO;
using YamlDotNet.RepresentationModel;

namespace BedrockAdder.FileWorker
{
    internal static class SoundYamlParserWorker
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

        internal static string NormalizeSoundPathRel(string raw)
        {
            string s = (raw ?? string.Empty).Replace("\\", "/").Trim();

            if (s.StartsWith("sounds/", StringComparison.OrdinalIgnoreCase))
            {
                s = s.Substring("sounds/".Length);
            }

            s = s.TrimStart('/');

            if (!s.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)
                && !s.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
                && !s.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
            {
                s += ".ogg";
            }

            return s;
        }

        internal static string BuildIaContentSoundAbs(string itemsAdderRoot, string soundNamespace, string rel)
        {
            string r = (rel ?? string.Empty).Replace("\\", "/").TrimStart('/');
            if (r.StartsWith("sounds/", StringComparison.OrdinalIgnoreCase))
            {
                r = r.Substring("sounds/".Length);
            }

            return Path.Combine(itemsAdderRoot, "contents", soundNamespace, "sounds", r);
        }

        internal static bool TryGetSoundsRoot(YamlMappingNode root, out YamlMappingNode? sounds)
        {
            sounds = null;
            if (root.Children.TryGetValue(new YamlScalarNode("sounds"), out var node)
                && node is YamlMappingNode map)
            {
                sounds = map;
                return true;
            }
            return false;
        }

        internal static bool TryGetScalar(YamlMappingNode map, string key, out string value)
        {
            value = string.Empty;

            if (map.Children.TryGetValue(new YamlScalarNode(key), out var n)
                && n is YamlScalarNode s
                && !string.IsNullOrWhiteSpace(s.Value))
            {
                value = s.Value!;
                return true;
            }

            return false;
        }

        internal static (float vol, float pitch, bool stream, int? attenuation, int? weight)ReadSettings(YamlMappingNode sMap)
        {
            float vol = 1.0f;
            float pitch = 1.0f;
            bool stream = false;
            int? attenuation = null;
            int? weight = null;

            if (sMap.Children.TryGetValue(new YamlScalarNode("settings"), out var node)
                && node is YamlMappingNode set)
            {
                if (TryGetScalar(set, "volume", out var v) && float.TryParse(v, out var vf))
                    vol = vf;

                if (TryGetScalar(set, "pitch", out var p) && float.TryParse(p, out var pf))
                    pitch = pf;

                if (TryGetScalar(set, "stream", out var st) && bool.TryParse(st, out var sb))
                    stream = sb;

                if (TryGetScalar(set, "attenuation_distance", out var attStr) &&
                    int.TryParse(attStr, out var attI))
                {
                    attenuation = attI;
                }

                if (TryGetScalar(set, "weight", out var wStr) &&
                    int.TryParse(wStr, out var wI))
                {
                    weight = wI;
                }
            }

            return (vol, pitch, stream, attenuation, weight);
        }


        internal static (float vol, float pitch, bool stream, int? attenuation, int? weight) ReadSettingsOverride(YamlMappingNode vMap, float baseVol, float basePitch, bool baseStream, int? baseAttenuation, int? baseWeight)
        {
            float vol = baseVol;
            float pitch = basePitch;
            bool stream = baseStream;
            int? attenuation = baseAttenuation;
            int? weight = baseWeight;

            if (TryGetScalar(vMap, "volume", out var v) && float.TryParse(v, out var vf))
                vol = vf;
            if (TryGetScalar(vMap, "pitch", out var p) && float.TryParse(p, out var pf))
                pitch = pf;
            if (TryGetScalar(vMap, "stream", out var st) && bool.TryParse(st, out var sb))
                stream = sb;

            if (TryGetScalar(vMap, "attenuation_distance", out var attStr) &&
                int.TryParse(attStr, out var attI))
            {
                attenuation = attI;
            }

            if (TryGetScalar(vMap, "weight", out var wStr) &&
                int.TryParse(wStr, out var wI))
            {
                weight = wI;
            }

            return (vol, pitch, stream, attenuation, weight);
        }
    }
}