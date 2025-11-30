using System;
using System.Collections.Generic;
using System.IO;
using BedrockAdder.Library;      // Lists, CustomSound, PackSession
using BedrockAdder.Managers;    // BedrockManager
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BedrockAdder.ConverterWorker.BuilderWorker
{
    internal static class CustomSoundBuilderWorker
    {
        /// <summary>
        /// Copy all extracted custom sounds into the Bedrock pack and
        /// generate sounds/sound_definitions.json in Furnace style.
        /// </summary>
        public static void BuildCustomSounds(PackSession session, string itemsAdderRoot)
        {
            if (session == null)
            {
                ConsoleWorker.Write.Line("error", "CustomSoundBuilderWorker: session is null");
                return;
            }

            if (Lists.CustomSounds == null || Lists.CustomSounds.Count == 0)
            {
                ConsoleWorker.Write.Line("info", "CustomSoundBuilderWorker: no custom sounds to build.");
                return;
            }

            ConsoleWorker.Write.Line("info", "CustomSoundBuilderWorker: build started. Sounds=" + Lists.CustomSounds.Count);

            // Ensure sounds/ folder exists in Bedrock pack
            string soundsRoot = Path.Combine(session.PackRoot, "sounds");
            BedrockManager.EnsureDir(soundsRoot);

            // sound_definitions: "<ns:id>": { "sounds": [ {...}, ... ] }
            var soundDefinitions = new Dictionary<string, List<JObject>>(StringComparer.OrdinalIgnoreCase);

            int copiedFiles = 0;
            int registeredSounds = 0;

            foreach (CustomSound customSound in Lists.CustomSounds)
            {
                if (customSound == null)
                    continue;

                try
                {
                    if (string.IsNullOrWhiteSpace(customSound.SoundPath) || !File.Exists(customSound.SoundPath))
                    {
                        ConsoleWorker.Write.Line(
                            "warn",
                            "CustomSoundBuilderWorker: source sound missing for " +
                            (customSound.SoundNamespace ?? "unknown") + ":" + (customSound.SoundID ?? "unknown") +
                            " path=" + (customSound.SoundPath ?? "<null>")
                        );
                        continue;
                    }

                    string ns = string.IsNullOrWhiteSpace(customSound.SoundNamespace)
                        ? "unknown"
                        : customSound.SoundNamespace.Trim();

                    // Compute the relative path inside IA's contents/<ns>/sounds folder.
                    // This mirrors how Furnace produced:
                    //  sounds/spawn_1.ogg
                    //  sounds/demon_sounds/demon_laugh1.ogg
                    string relPath = GetRelPathFromIaSoundsRoot(itemsAdderRoot, ns, customSound.SoundPath);

                    // Destination in Bedrock pack: sounds/<relPath>
                    string destRel = Path.Combine("sounds", relPath).Replace('\\', '/');
                    string destAbs = Path.Combine(session.PackRoot, destRel.Replace('/', Path.DirectorySeparatorChar));

                    string destDir = Path.GetDirectoryName(destAbs) ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(destDir))
                        BedrockManager.EnsureDir(destDir);

                    try
                    {
                        File.Copy(customSound.SoundPath, destAbs, overwrite: true);
                        copiedFiles++;
                    }
                    catch (Exception exCopy)
                    {
                        ConsoleWorker.Write.Line(
                            "warn",
                            "CustomSoundBuilderWorker: failed copying sound " +
                            customSound.SoundPath + " → " + destAbs + " ex=" + exCopy.Message
                        );
                        // still continue, so the JSON entry exists
                    }

                    // Bedrock "name": path without extension (forward slashes), starting from "sounds/"
                    string nameNoExt = RemoveExtension(destRel);

                    // Furnace-style sound key: "<namespace>:<localId>"
                    string soundKey = BuildSoundKey(ns, customSound.SoundID);

                    if (!soundDefinitions.TryGetValue(soundKey, out var list))
                    {
                        list = new List<JObject>();
                        soundDefinitions[soundKey] = list;
                    }

                    var soundObj = new JObject
                    {
                        ["name"] = nameNoExt,
                        ["volume"] = customSound.Volume,
                        ["pitch"] = customSound.Pitch,
                        ["stream"] = customSound.Stream
                    };

                    // NEW: only write these if present
                    if (customSound.AttenuationDistance.HasValue)
                    {
                        soundObj["attenuation_distance"] = customSound.AttenuationDistance.Value;
                    }

                    if (customSound.Weight.HasValue)
                    {
                        soundObj["weight"] = customSound.Weight.Value;
                    }

                    list.Add(soundObj);
                    registeredSounds++;
                }
                catch (Exception ex)
                {
                    ConsoleWorker.Write.Line(
                        "warn",
                        "CustomSoundBuilderWorker: failed processing sound " +
                        (customSound.SoundNamespace ?? "unknown") + ":" + (customSound.SoundID ?? "unknown") +
                        " ex=" + ex.Message
                    );
                }
            }

            if (soundDefinitions.Count == 0)
            {
                ConsoleWorker.Write.Line("info", "CustomSoundBuilderWorker: no valid sounds to write to sound_definitions.json.");
                return;
            }

            // Build sounds/sound_definitions.json
            string soundDefsAbs = Path.Combine(session.PackRoot, "sounds", "sound_definitions.json");
            BedrockManager.EnsureDir(Path.GetDirectoryName(soundDefsAbs) ?? string.Empty);

            var root = new JObject
            {
                ["format_version"] = "1.14.0"
            };

            var defsObj = new JObject();
            root["sound_definitions"] = defsObj;

            foreach (var kv in soundDefinitions)
            {
                string soundKey = kv.Key;
                List<JObject> clipList = kv.Value;

                var arr = new JArray();
                foreach (var clip in clipList)
                    arr.Add(clip);

                var defObj = new JObject
                {
                    ["sounds"] = arr
                };

                defsObj[soundKey] = defObj;
            }

            try
            {
                File.WriteAllText(soundDefsAbs, root.ToString(Formatting.Indented), System.Text.Encoding.UTF8);
                ConsoleWorker.Write.Line(
                    "info",
                    "CustomSoundBuilderWorker: sound_definitions.json written → " +
                    soundDefsAbs.Replace(Path.DirectorySeparatorChar, '/')
                );
            }
            catch (Exception ex)
            {
                ConsoleWorker.Write.Line(
                    "error",
                    "CustomSoundBuilderWorker: failed writing sound_definitions.json: " + ex.Message
                );
            }

            ConsoleWorker.Write.Line(
                "info",
                "CustomSoundBuilderWorker: build finished. Definitions=" + soundDefinitions.Count +
                " Clips=" + registeredSounds + " FilesCopied=" + copiedFiles
            );
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------

        /// <summary>
        /// Compute the IA-relative path inside contents/&lt;ns&gt;/sounds.
        /// If we can't, fall back to just the file name.
        /// Examples:
        ///   IA base: E:/.../ItemsAdder/contents/music/sounds
        ///   abs:     E:/.../ItemsAdder/contents/music/sounds/spawn_1.ogg
        ///   -> "spawn_1.ogg"
        ///
        ///   abs:     E:/.../ItemsAdder/contents/music/sounds/demon_sounds/demon_laugh1.ogg
        ///   -> "demon_sounds/demon_laugh1.ogg"
        /// </summary>
        private static string GetRelPathFromIaSoundsRoot(string itemsAdderRoot, string soundNamespace, string absPath)
        {
            string root = Path.Combine(itemsAdderRoot, "contents", soundNamespace, "sounds");
            string normRoot = root.Replace('\\', '/').TrimEnd('/');
            string normAbs = absPath.Replace('\\', '/');

            if (normAbs.StartsWith(normRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                return normAbs.Substring(normRoot.Length + 1);
            }

            // Fallback: just use the file name
            return Path.GetFileName(absPath);
        }

        /// <summary>
        /// Build the Bedrock sound key "namespace:localId".
        /// If SoundID already contains a colon, we drop the left part and keep the right.
        /// </summary>
        private static string BuildSoundKey(string soundNamespace, string soundId)
        {
            string ns = string.IsNullOrWhiteSpace(soundNamespace)
                ? "unknown"
                : soundNamespace.Trim();

            string local = string.IsNullOrWhiteSpace(soundId)
                ? "sound"
                : soundId.Trim();

            int colon = local.IndexOf(':');
            if (colon >= 0 && colon + 1 < local.Length)
                local = local.Substring(colon + 1);

            return ns + ":" + local;
        }

        private static string RemoveExtension(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            string p = path.Replace('\\', '/');
            int dot = p.LastIndexOf('.');
            if (dot <= 0)
                return p;

            return p.Substring(0, dot);
        }
    }
}