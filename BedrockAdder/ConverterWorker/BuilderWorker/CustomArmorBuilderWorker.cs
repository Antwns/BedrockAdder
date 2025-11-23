using BedrockAdder.ConverterWorker.ObjectWorker;    // VanillaRecolorerWorker
using BedrockAdder.FileWorker;                     // ConsoleWorker
using BedrockAdder.Library;                        // Lists, CustomArmor, PackSession
using BedrockAdder.Managers;                       // BedrockManager, WindowManager
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
        ///   If TexturePath starts with "minecraft:" and RecolorTint is set,
        ///   recolor the vanilla icon from the selected Minecraft version JAR.
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

            // 1) Recolored vanilla icon: UsesVanillaTexture + VanillaTextureId + RecolorTint
            bool iconHandled = false;
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
            // 2) Fallback: explicit icon or content-pack texture (non-vanilla)
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