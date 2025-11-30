using System;
using System.IO;
using BedrockAdder.FileWorker;
using BedrockAdder.Library;
using YamlDotNet.RepresentationModel;

namespace BedrockAdder.ExtractorWorker.ConverterWorker
{
    internal static class CustomSoundExtractorWorker
    {
        // Parse all provided sounds.yml files and append to Lists.CustomSounds.
        internal static void ExtractCustomSoundsFromPaths(string itemsAdderRoot)
        {
            int filesProcessed = 0;

            foreach (var filePath in Lists.CustomSoundPaths)
            {
                filesProcessed++;

                try
                {
                    if (!File.Exists(filePath))
                    {
                        ConsoleWorker.Write.Line("warn", "Sounds file missing: " + filePath);
                        continue;
                    }

                    using var reader = new StreamReader(filePath);
                    var yaml = new YamlStream();
                    yaml.Load(reader);

                    if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode root)
                        continue;

                    // info:namespace or fallback
                    string ns = SoundYamlParserWorker.GetFileNamespaceOrDefault(root, "unknown");

                    // sounds: { ... }
                    if (!SoundYamlParserWorker.TryGetSoundsRoot(root, out var soundsMap) || soundsMap is null)
                        continue;

                    foreach (var kv in soundsMap.Children)
                    {
                        if (kv.Key is not YamlScalarNode sKey || kv.Value is not YamlMappingNode sMap)
                            continue;

                        string localId = sKey.Value ?? "unnamed";
                        string soundId = ns + ":" + localId;

                        // Base path
                        string basePathRel = SoundYamlParserWorker.TryGetScalar(sMap, "path", out var p0)
                            ? SoundYamlParserWorker.NormalizeSoundPathRel(p0)
                            : string.Empty;

                        if (string.IsNullOrWhiteSpace(basePathRel))
                        {
                            ConsoleWorker.Write.Line("warn", soundId + " has no 'path' entry.");
                            continue;
                        }

                        string absPath = SoundYamlParserWorker.BuildIaContentSoundAbs(
                            itemsAdderRoot,
                            ns,
                            basePathRel
                        );

                        // Base settings (settings: { volume, pitch, stream })
                        var (vol, pitch, stream, attenuation, weight) = SoundYamlParserWorker.ReadSettings(sMap);

                        Lists.CustomSounds.Add(new CustomSound
                        {
                            SoundNamespace = ns,
                            SoundID = soundId,
                            SoundPath = absPath,
                            Volume = vol,
                            Pitch = pitch,
                            Stream = stream,
                            AttenuationDistance = attenuation,
                            Weight = weight
                        });
                        ConsoleWorker.Write.Line(
                            "info",
                            "Processed: " + soundId + " at " + absPath +
                            " with properties:[Volume:" + vol + ",Pitch:" + pitch + ",Stream:" + stream + "]"
                        );

                        // Variants: keys starting with "variant"
                        foreach (var vpair in sMap.Children)
                        {
                            if (vpair.Key is not YamlScalarNode vKey || vpair.Value is not YamlMappingNode vMap)
                                continue;

                            string k = vKey.Value ?? string.Empty;
                            if (!k.StartsWith("variant", StringComparison.OrdinalIgnoreCase))
                                continue;

                            // variant path, fallback to base if missing
                            string vRel = SoundYamlParserWorker.TryGetScalar(vMap, "path", out var vp)
                                ? SoundYamlParserWorker.NormalizeSoundPathRel(vp)
                                : basePathRel;

                            string vAbs = SoundYamlParserWorker.BuildIaContentSoundAbs(
                                itemsAdderRoot,
                                ns,
                                vRel
                            );

                            var (vVol, vPitch, vStream, vAtt, vWeight) = SoundYamlParserWorker.ReadSettingsOverride(
                                vMap,
                                vol,
                                pitch,
                                stream,
                                attenuation,
                                weight
                            );

                            Lists.CustomSounds.Add(new CustomSound
                            {
                                SoundNamespace = ns,
                                SoundID = soundId,     // same logical sound, more files
                                SoundPath = vAbs,
                                Volume = vVol,
                                Pitch = vPitch,
                                Stream = vStream,
                                AttenuationDistance = vAtt,
                                Weight = vWeight
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    ConsoleWorker.Write.Line(
                        "error",
                        "Sounds parse failed: " + filePath + " ex=" + ex.Message
                    );
                }
            }
            ConsoleWorker.Write.Line(
                "info",
                "Sounds: processed files=" + filesProcessed +
                " entries=" + Lists.CustomSounds.Count
            );
        }
    }
}