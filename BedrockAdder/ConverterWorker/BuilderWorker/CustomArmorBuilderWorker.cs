using BedrockAdder.ConverterWorker.ObjectWorker;    // VanillaRecolorerWorker, ModelImageBuilderWorker, ModelBuilderWorker
using BedrockAdder.FileWorker;                     // ConsoleWorker
using BedrockAdder.Library;                        // Lists, CustomArmor, CustomItem, PackSession
using BedrockAdder.Managers;                       // BedrockManager, WindowManager
using BedrockAdder.Renderer;                       // CefOffscreenIconRenderer, IModelIconRenderer
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Collections.Generic;


namespace BedrockAdder.ConverterWorker.BuilderWorker
{
    internal static class CustomArmorBuilderWorker
    {
        /// <summary>
        /// Build ALL custom armor assets (worn layers + icons) for the current pack session.
        ///
        /// Usage:
        ///     CustomArmorBuilderWorker.BuildCustomArmors(currentSession, ItemsAdderDir, selectedVersion);
        ///
        /// Worn layers:
        ///   textures/models/armor/{namespace}_{armorSetId}_layer_1.png
        ///   textures/models/armor/{namespace}_{armorSetId}_layer_2.png
        ///
        /// Icons (per item piece):
        ///   textures/items/armors/{namespace}_{armorId}.png
        ///
        /// Recolor logic:
        ///   If UsesVanillaTexture + RecolorTint are set, recolor the vanilla icon
        ///   from the selected Minecraft version JAR. Otherwise we use:
        ///     - 3D helmet render (Is3DHelmet) if available,
        ///     - or a direct texture/icon copy as fallback.
        /// </summary>
        public static void BuildCustomArmors(PackSession session, string itemsAdderRootPath, string selectedVersion)
        {
            if (session == null)
            {
                ConsoleWorker.Write.Line("error", "CustomArmorBuilderWorker: session is null");
                return;
            }

            if (Lists.CustomArmors == null || Lists.CustomArmors.Count == 0)
            {
                ConsoleWorker.Write.Line("info", "CustomArmorBuilderWorker: no custom armors to build.");
                return;
            }

            string armorTexturesRoot = Path.Combine(session.PackRoot, "textures", "models", "armor");
            string armorIconsRoot = Path.Combine(session.PackRoot, "textures", "items", "armors");

            BedrockManager.EnsureDir(armorTexturesRoot);
            BedrockManager.EnsureDir(armorIconsRoot);

            // Needed for 3D helmets (same dirs as 3D items use)
            BedrockManager.EnsureDir(Path.Combine(session.PackRoot, "models", "entity"));
            BedrockManager.EnsureDir(Path.Combine(session.PackRoot, "attachables"));


            // 3D icon renderer (shared for all 3D helmets)
            string renderHtmlAbs = Path.Combine(AppContext.BaseDirectory, "Renderer", "cef", "render.html");
            int iconSize = 512; // same idea as items; tweak if you want
            IModelIconRenderer renderer = new CefOffscreenIconRenderer(iconSize, true, renderHtmlAbs);

            int texturesCopied = 0;
            int iconsCopied = 0;
            int armorsProcessed = 0;

            // Keep track of which armor sets have had their LAYERS written.
            // Key: "{namespace}:{armorSetId}"
            var processedLayerSets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var armor in Lists.CustomArmors)
            {
                if (armor == null)
                    continue;

                try
                {
                    BuildOneArmor(
                        session,
                        armor,
                        itemsAdderRootPath,
                        armorTexturesRoot,
                        armorIconsRoot,
                        selectedVersion,
                        renderer,
                        processedLayerSets,
                        ref texturesCopied,
                        ref iconsCopied
                    );
                    armorsProcessed++;
                }
                catch (Exception ex)
                {
                    ConsoleWorker.Write.Line(
                        "warn",
                        "CustomArmorBuilderWorker: build failed for " +
                        armor.ArmorNamespace + ":" + armor.ArmorID + " – " + ex.Message
                    );
                }
            }

            ConsoleWorker.Write.Line(
                "info",
                "CustomArmorBuilderWorker: build finished. Armors=" + armorsProcessed +
                " TexturesCopied=" + texturesCopied +
                " IconsCopied=" + iconsCopied
            );
        }

        private static void BuildOneArmor(
            PackSession session,
            CustomArmor armor,
            string itemsAdderRootPath,
            string armorTexturesRoot,
            string armorIconsRoot,
            string selectedVersion,
            IModelIconRenderer renderer,
            HashSet<string> processedLayerSets,
            ref int texturesCopied,
            ref int iconsCopied
        )
        {
            string ns = string.IsNullOrWhiteSpace(armor.ArmorNamespace) ? "unknown" : armor.ArmorNamespace;
            string id = string.IsNullOrWhiteSpace(armor.ArmorID) ? "unknown" : armor.ArmorID;

            string nsSafe = Sanitize(ns);
            string idSafe = Sanitize(id);

            // Per-piece name (for icons)
            string baseName = nsSafe + "_" + idSafe;
            string armorKey = ns + ":" + id;

            // Per-set name (for worn layers)
            string setId = string.IsNullOrWhiteSpace(armor.ArmorSetId) ? id : armor.ArmorSetId;
            string setSafe = Sanitize(setId);
            string setBase = nsSafe + "_" + setSafe;
            string setKey = ns + ":" + setId;

            // -------- worn textures: layer_1 & layer_2 (per SET, only once) --------
            if (processedLayerSets.Add(setKey))
            {
                // layer_1: chest / helmet / boots
                if (!string.IsNullOrWhiteSpace(armor.ArmorLayerChest))
                {
                    string sourceChestAbs =
                        ArmorYamlParserWorker.BuildItemsAdderContentTexturePath(
                            itemsAdderRootPath,
                            ns,
                            armor.ArmorLayerChest
                        );

                    string destChestAbs = Path.Combine(armorTexturesRoot, setBase + "_layer_1.png");

                    CopyArmorTextureIfExists(
                        setKey,
                        "layer_1",
                        sourceChestAbs,
                        destChestAbs,
                        ref texturesCopied
                    );
                }

                // layer_2: leggings
                if (!string.IsNullOrWhiteSpace(armor.ArmorLayerLegs))
                {
                    string sourceLegsAbs =
                        ArmorYamlParserWorker.BuildItemsAdderContentTexturePath(
                            itemsAdderRootPath,
                            ns,
                            armor.ArmorLayerLegs
                        );

                    string destLegsAbs = Path.Combine(armorTexturesRoot, setBase + "_layer_2.png");

                    CopyArmorTextureIfExists(
                        setKey,
                        "layer_2",
                        sourceLegsAbs,
                        destLegsAbs,
                        ref texturesCopied
                    );
                }
            }

            // ---------- ICON LOGIC ----------

            bool iconHandled = false;

            // 1) Recolored vanilla icon: UsesVanillaTexture + VanillaTextureId + RecolorTint
            if (armor.UsesVanillaTexture &&
                !string.IsNullOrWhiteSpace(armor.VanillaTextureId) &&
                !string.IsNullOrWhiteSpace(armor.RecolorTint) &&
                !string.IsNullOrWhiteSpace(selectedVersion) &&
                !selectedVersion.Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                string iconDestAbs = Path.Combine(armorIconsRoot, baseName + ".png");
                Directory.CreateDirectory(Path.GetDirectoryName(iconDestAbs) ?? ".");

                if (VanillaRecolorerWorker.TryBuildRecoloredArmorVanillaTexture(armor, selectedVersion, iconDestAbs, out var recolorError))
                {
                    iconsCopied++;
                    iconHandled = true;

                    ConsoleWorker.Write.Line(
                        "info",
                        "Armor icon (recolored vanilla) built for " + armorKey +
                        " from " + armor.VanillaTextureId +
                        " tint=" + armor.RecolorTint +
                        " → textures/items/armors/" + baseName + ".png"
                    );
                    ConsoleWorker.Write.Line(
                        "info",
                        "[ARMOR ICON RECOLOR OK] FINAL PNG WRITTEN: " +
                        iconDestAbs.Replace(Path.DirectorySeparatorChar, '/')
                    );
                }
                else
                {
                    ConsoleWorker.Write.Line(
                        "warn",
                        "Armor icon recolor failed for " + armorKey +
                        " (" + armor.VanillaTextureId + "), reason: " + (recolorError ?? "unknown")
                    );
                }
            }

            // 2) 3D helmet model + icon (only for helmets, when we have a model and no icon yet)
            if (!iconHandled &&
                armor.Slot.Equals("helmet", StringComparison.OrdinalIgnoreCase) &&
                armor.Is3DHelmet &&
                !string.IsNullOrWhiteSpace(armor.ModelPath) &&
                File.Exists(armor.ModelPath))
            {
                try
                {
                    // ---- Build a lightweight CustomItem to reuse the 3D model pipeline ----
                    var tempItem = new CustomItem
                    {
                        ItemNamespace = armor.ArmorNamespace,
                        ItemID = armor.ArmorID + "_helmet3d",
                        Is3D = true,
                        ModelPath = armor.ModelPath
                    };

                    foreach (var kv in armor.ModelTexturePaths)
                    {
                        tempItem.ModelTexturePaths[kv.Key] = kv.Value;
                    }

                    ConsoleWorker.Write.Line(
                        "info",
                        armorKey + " building 3D helmet model / geometry / attachable via ModelBuilderWorker."
                    );

                    // ---- 2a) Build Bedrock geometry + attachable + collect textures ----
                    var built = ModelBuilderWorker.Build(tempItem, itemsAdderRootPath, iconRenderer: null);

                    // geometry (.geo.json)
                    if (!string.IsNullOrWhiteSpace(built.GeometryJson))
                    {
                        string geoAbs = Path.Combine(
                            session.PackRoot,
                            built.GeometryOutRel.Replace('/', Path.DirectorySeparatorChar)
                        );
                        string? geoDir = Path.GetDirectoryName(geoAbs);
                        if (!string.IsNullOrWhiteSpace(geoDir))
                        {
                            Directory.CreateDirectory(geoDir);
                        }

                        try
                        {
                            File.WriteAllText(geoAbs, built.GeometryJson, System.Text.Encoding.UTF8);
                            ConsoleWorker.Write.Line(
                                "info",
                                armorKey + " 3D helmet geometry → " + built.GeometryOutRel
                            );
                        }
                        catch (Exception ex)
                        {
                            ConsoleWorker.Write.Line(
                                "warn",
                                armorKey + " failed writing 3D helmet geometry: " +
                                geoAbs + " ex=" + ex.Message
                            );
                        }
                    }
                    else
                    {
                        if (built.Notes.Count > 0)
                        {
                            foreach (var note in built.Notes)
                            {
                                ConsoleWorker.Write.Line(
                                    "warn",
                                    armorKey + " 3D helmet geometry note: " + note
                                );
                            }
                        }
                        else
                        {
                            ConsoleWorker.Write.Line(
                                "warn",
                                armorKey + " has no GeometryJson produced by ModelBuilderWorker for 3D helmet."
                            );
                        }
                    }

                    // attachable (.json)
                    if (!string.IsNullOrWhiteSpace(built.AttachableJson))
                    {
                        string attAbs = Path.Combine(
                            session.PackRoot,
                            built.AttachableOutRel.Replace('/', Path.DirectorySeparatorChar)
                        );
                        string? attDir = Path.GetDirectoryName(attAbs);
                        if (!string.IsNullOrWhiteSpace(attDir))
                        {
                            Directory.CreateDirectory(attDir);
                        }

                        try
                        {
                            File.WriteAllText(attAbs, built.AttachableJson, System.Text.Encoding.UTF8);
                            ConsoleWorker.Write.Line(
                                "info",
                                armorKey + " 3D helmet attachable → " + built.AttachableOutRel
                            );
                        }
                        catch (Exception ex)
                        {
                            ConsoleWorker.Write.Line(
                                "warn",
                                armorKey + " failed writing 3D helmet attachable: " +
                                attAbs + " ex=" + ex.Message
                            );
                        }
                    }
                    else
                    {
                        ConsoleWorker.Write.Line(
                            "warn",
                            armorKey + " has no AttachableJson produced by ModelBuilderWorker for 3D helmet."
                        );
                    }

                    // copy model textures
                    if (built.TexturesToCopy != null)
                    {
                        foreach (var (src, dstRel) in built.TexturesToCopy)
                        {
                            if (string.IsNullOrWhiteSpace(src) || string.IsNullOrWhiteSpace(dstRel))
                            {
                                continue;
                            }

                            string dstAbs = Path.Combine(
                                session.PackRoot,
                                dstRel.Replace('/', Path.DirectorySeparatorChar)
                            );
                            string? dstDir = Path.GetDirectoryName(dstAbs);
                            if (!string.IsNullOrWhiteSpace(dstDir))
                            {
                                Directory.CreateDirectory(dstDir);
                            }

                            try
                            {
                                File.Copy(src, dstAbs, overwrite: true);
                            }
                            catch (Exception ex)
                            {
                                ConsoleWorker.Write.Line(
                                    "warn",
                                    armorKey +
                                    " failed copying 3D helmet texture " + src +
                                    " -> " + dstAbs + " ex=" + ex.Message
                                );
                            }
                        }
                    }

                    // ---- 2b) 3D helmet icon render via ModelImageBuilderWorker ----
                    string iconWorkRoot = Path.Combine(session.PackRoot, "_icons");
                    Directory.CreateDirectory(iconWorkRoot);

                    ConsoleWorker.Write.Line(
                        "info",
                        armorKey + " attempting 3D helmet icon render via ModelImageBuilderWorker."
                    );

                    var iconResult = ModelImageBuilderWorker.RenderItemIcon(tempItem, iconWorkRoot, renderer);

                    if (iconResult.Success &&
                        !string.IsNullOrWhiteSpace(iconResult.IconPngAbs) &&
                        File.Exists(iconResult.IconPngAbs))
                    {
                        string iconDestAbs = Path.Combine(armorIconsRoot, baseName + ".png");
                        string? destDir = Path.GetDirectoryName(iconDestAbs);
                        if (!string.IsNullOrWhiteSpace(destDir))
                        {
                            Directory.CreateDirectory(destDir);
                        }

                        File.Copy(iconResult.IconPngAbs, iconDestAbs, overwrite: true);
                        iconsCopied++;
                        iconHandled = true;

                        ConsoleWorker.Write.Line(
                            "info",
                            armorKey +
                            " 3D helmet icon rendered → textures/items/armors/" +
                            baseName + ".png"
                        );
                    }
                    else
                    {
                        if (iconResult.Notes.Count > 0)
                        {
                            foreach (var note in iconResult.Notes)
                            {
                                ConsoleWorker.Write.Line(
                                    "warn",
                                    armorKey + " 3D helmet icon note: " + note
                                );
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    ConsoleWorker.Write.Line(
                        "warn",
                        armorKey + " 3D helmet build/icon render exception: " + ex.Message
                    );
                }
            }

            // 3) Fallback: explicit icon or content-pack texture (non-vanilla / non-3D or 3D render failed)
            if (!iconHandled)
            {
                string? iconSource = armor.IconPath;

                if (string.IsNullOrWhiteSpace(iconSource) &&
                    !string.IsNullOrWhiteSpace(armor.TexturePath) &&
                    !armor.TexturePath.StartsWith("minecraft:", StringComparison.OrdinalIgnoreCase))
                {
                    iconSource = armor.TexturePath;
                }

                if (!string.IsNullOrWhiteSpace(iconSource))
                {
                    string iconSourceAbs = iconSource;

                    if (!File.Exists(iconSourceAbs))
                    {
                        iconSourceAbs = ArmorYamlParserWorker.BuildItemsAdderContentTexturePath(
                            itemsAdderRootPath,
                            ns,
                            iconSource
                        );
                    }

                    if (File.Exists(iconSourceAbs))
                    {
                        string iconDestAbs = Path.Combine(armorIconsRoot, baseName + ".png");
                        string? destDir = Path.GetDirectoryName(iconDestAbs);
                        if (!string.IsNullOrWhiteSpace(destDir))
                            Directory.CreateDirectory(destDir);

                        try
                        {
                            File.Copy(iconSourceAbs, iconDestAbs, overwrite: true);
                            iconsCopied++;

                            ConsoleWorker.Write.Line(
                                "info",
                                "Armor icon copied for " + armorKey +
                                " (" + iconSourceAbs + " → textures/items/armors/" + baseName + ".png)"
                            );
                            ConsoleWorker.Write.Line(
                                "warn",
                                "[ARMOR ICON FALLBACK] Used fallback icon source for " +
                                armorKey + " → " + iconDestAbs.Replace(Path.DirectorySeparatorChar, '/')
                            );
                        }
                        catch (Exception ex)
                        {
                            ConsoleWorker.Write.Line(
                                "warn",
                                "Armor icon copy failed for " + armorKey +
                                " (" + iconSourceAbs + " → " + iconDestAbs + "): " + ex.Message
                            );
                        }
                    }
                    else
                    {
                        ConsoleWorker.Write.Line(
                            "debug",
                            "Armor icon source missing for " + armorKey + ": " + iconSourceAbs
                        );
                    }
                }
            }

            // ---------- BEDROCK ITEM + ATLAS ENTRY (only if icon exists) ----------

            string iconOnDiskAbs = Path.Combine(armorIconsRoot, baseName + ".png");
            if (File.Exists(iconOnDiskAbs))
            {
                string atlasKey = "ia_" + nsSafe + "_" + idSafe;

                string iconRel = Path.Combine("textures", "items", "armors", baseName + ".png")
                    .Replace(Path.DirectorySeparatorChar, '/');

                UpdateItemAtlas(session, atlasKey, iconRel);

                string bedrockId = ns + ":" + id;
                string fileNameSafe = nsSafe + "_" + idSafe;

                WriteItemDefinition(session, bedrockId, fileNameSafe, atlasKey);
            }
            else
            {
                ConsoleWorker.Write.Line(
                    "debug",
                    "Armor " + armorKey +
                    " has no icon on disk (" +
                    iconOnDiskAbs.Replace(Path.DirectorySeparatorChar, '/') +
                    "); skipping item definition."
                );
            }
        }

        private static void CopyArmorTextureIfExists(
            string armorKey,
            string layerLabel,
            string sourceAbs,
            string destAbs,
            ref int texturesCopied
        )
        {
            if (string.IsNullOrWhiteSpace(sourceAbs))
                return;

            if (!File.Exists(sourceAbs))
            {
                ConsoleWorker.Write.Line(
                    "error",
                    "Armor " + layerLabel + " texture missing for " + armorKey + " at " + sourceAbs
                );
                return;
            }

            string? destDir = Path.GetDirectoryName(destAbs);
            if (!string.IsNullOrWhiteSpace(destDir))
                Directory.CreateDirectory(destDir);

            try
            {
                File.Copy(sourceAbs, destAbs, overwrite: true);
                texturesCopied++;

                ConsoleWorker.Write.Line(
                    "info",
                    "Armor " + layerLabel + " texture copied for " + armorKey +
                    " → " + destAbs.Replace(Path.DirectorySeparatorChar, '/')
                );
            }
            catch (Exception ex)
            {
                ConsoleWorker.Write.Line(
                    "warn",
                    "Armor " + layerLabel + " copy failed for " + armorKey +
                    " (" + sourceAbs + " → " + destAbs + "): " + ex.Message
                );
            }
        }

        /// <summary>
        /// Update textures/item_texture.json with an entry for this armor icon.
        /// </summary>
        private static void UpdateItemAtlas(PackSession session, string atlasKey, string iconRel)
        {
            string atlasAbs = Path.Combine(session.PackRoot, "textures", "item_texture.json");

            JObject root;
            if (File.Exists(atlasAbs))
            {
                root = JObject.Parse(File.ReadAllText(atlasAbs));
            }
            else
            {
                root = new JObject();
            }

            if (root["texture_data"] is not JObject textureData)
            {
                textureData = new JObject();
                root["texture_data"] = textureData;
            }

            if (textureData[atlasKey] == null)
            {
                string normalized = iconRel.Replace("\\", "/");

                textureData[atlasKey] = new JObject
                {
                    ["textures"] = new JArray(normalized)
                };
            }

            Directory.CreateDirectory(Path.GetDirectoryName(atlasAbs)!);
            File.WriteAllText(atlasAbs, root.ToString(Formatting.Indented));
        }

        /// <summary>
        /// Write items/&lt;fileNameSafe&gt;.json for this armor piece, pointing at the atlasKey.
        /// </summary>
        private static void WriteItemDefinition(
            PackSession session,
            string bedrockId,
            string fileNameSafe,
            string atlasKey
        )
        {
            string itemsDir = Path.Combine(session.PackRoot, "items");
            Directory.CreateDirectory(itemsDir);

            string itemJsonAbs = Path.Combine(itemsDir, fileNameSafe + ".json");

            var root = new JObject
            {
                ["format_version"] = "1.21.0",
                ["minecraft:item"] = new JObject
                {
                    ["description"] = new JObject
                    {
                        ["identifier"] = bedrockId
                    },
                    ["components"] = new JObject
                    {
                        ["minecraft:icon"] = new JObject
                        {
                            ["texture"] = atlasKey
                        },
                        ["minecraft:display_name"] = new JObject
                        {
                            ["value"] = bedrockId
                        },
                        ["minecraft:creative_category"] = new JObject
                        {
                            ["parent"] = "itemGroup.name.armor"
                        }
                    }
                }
            };

            File.WriteAllText(itemJsonAbs, root.ToString(Formatting.Indented));

            ConsoleWorker.Write.Line(
                "debug",
                "Armor item definition written → items/" + fileNameSafe + ".json"
            );
        }

        private static string Sanitize(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "unknown";
            var sb = new System.Text.StringBuilder();
            foreach (char ch in s.Trim())
            {
                if (char.IsLetterOrDigit(ch) || ch == '_')
                    sb.Append(char.ToLowerInvariant(ch));
                else
                    sb.Append('_');
            }
            return sb.ToString();
        }
    }
}