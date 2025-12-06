using BedrockAdder.ConverterWorker.ObjectWorker;           // ModelBuilderWorker, ModelImageBuilderWorker, VanillaRecolorerWorker, CustomRecolorerWorker
using BedrockAdder.FileWorker;                               // CefOffscreenIconRenderer (IModelIconRenderer)
using BedrockAdder.Library;
using BedrockAdder.Managers;
using BedrockAdder.Renderer;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;

namespace BedrockAdder.ConverterWorker.BuilderWorker
{
    internal static class CustomItemBuilderWorker
    {
        // Convenience: build ALL items with a renderer created from render.html
        public static void BuildCustomItems(PackSession session, int iconSize, string itemsAdderFolder)
        {
            string renderHtmlAbs = Path.Combine(AppContext.BaseDirectory, "Renderer", "cef", "render.html"); // <-- filename is render.html
            if (session == null)
            {
                ConsoleWorker.Write.Line("error", "CustomItemBuilderWorker: session is null");
                return;
            }

            if (Lists.CustomItems == null)
            {
                ConsoleWorker.Write.Line("error", "CustomItemBuilderWorker: items is null (nothing to build).");
                return;
            }

            // One renderer instance for all items
            IModelIconRenderer renderer = new CefOffscreenIconRenderer(iconSize, true, renderHtmlAbs);

            foreach (CustomItem customItem in Lists.CustomItems)
            {
                try
                {
                    BuildOneItem(session, customItem, renderer, itemsAdderFolder);
                }
                catch (Exception ex)
                {
                    ConsoleWorker.Write.Line("error", "Failed building item " + customItem.ItemNamespace + ":" + customItem.ItemID + " ex=" + ex.Message);
                }
            }
        }

        // ---------- core for a single item ----------

        public static void BuildOneItem(PackSession session, CustomItem it, IModelIconRenderer renderer, string itemsAdderFolder)
        {

            if (it == null)
            {
                ConsoleWorker.Write.Line("warn", "BuildOneItem: item is null");
                return;
            }

            string ns = it.ItemNamespace ?? "unknown";
            string id = it.ItemID ?? "unknown";
            string bedrockId = BedrockManager.MakeBedrockItemId(ns, id);                 // ia:ns_id
            string atlasKey = "ia_" + Sanitize(ns) + "_" + Sanitize(id);                 // key inside item_texture.json
            string iconAtlasRel = Built3DObjectNaming.MakeIconRel(ns, id);               // textures/items/ns/id.png

            // Resolve the final icon path inside the Bedrock pack once
            string iconAbs = Path.Combine(session.PackRoot, iconAtlasRel.Replace('/', Path.DirectorySeparatorChar));

            // Selected Minecraft version (for vanilla recolors)
            string selectedVersion = string.Empty;
            App.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    selectedVersion = WindowManager.Main?.VersionSelector?.SelectedItem?.ToString() ?? string.Empty;
                }
                catch
                {
                    //May decide to handle this in the future
                }
            });

            // Ensure base dirs
            BedrockManager.EnsureDir(Path.Combine(session.PackRoot, "items"));
            BedrockManager.EnsureDir(Path.Combine(session.PackRoot, "attachables"));
            BedrockManager.EnsureDir(Path.Combine(session.PackRoot, "models", "entity"));
            BedrockManager.EnsureDir(Path.Combine(session.PackRoot, "textures", "items"));
            BedrockManager.EnsureDir(Path.Combine(session.PackRoot, "textures", "models", ns));

            string? iconSourceAbs = null;

            if (it.Is3D)
            {
                // 1) Build the 3D model / geometry & collect model textures.
                // Icons are handled by ModelImageBuilderWorker; we pass no renderer here.
                var built = ModelBuilderWorker.Build(it, itemsAdderFolder, iconRenderer: null);

                // 1a) Write Bedrock geometry (.geo.json)
                if (!string.IsNullOrWhiteSpace(built.GeometryJson))
                {
                    string geoAbs = Path.Combine(session.PackRoot, built.GeometryOutRel.Replace('/', Path.DirectorySeparatorChar));
                    string? geoDir = Path.GetDirectoryName(geoAbs);
                    if (!string.IsNullOrWhiteSpace(geoDir))
                        Directory.CreateDirectory(geoDir);

                    try
                    {
                        File.WriteAllText(geoAbs, built.GeometryJson, System.Text.Encoding.UTF8);
                        ConsoleWorker.Write.Line("info", ns + ":" + id + " geometry → " + built.GeometryOutRel);
                    }
                    catch (Exception ex)
                    {
                        ConsoleWorker.Write.Line("warn", ns + ":" + id + " failed writing geometry: " + geoAbs + " ex=" + ex.Message);
                    }
                }
                else
                {
                    if (built.Notes.Count > 0)
                    {
                        foreach (var note in built.Notes)
                            ConsoleWorker.Write.Line("warn", ns + ":" + id + " geometry note: " + note);
                    }
                    else
                    {
                        ConsoleWorker.Write.Line("warn", ns + ":" + id + " has no GeometryJson produced by ModelBuilderWorker.");
                    }
                }

                // 1b) Write Bedrock attachable (.json)
                if (!string.IsNullOrWhiteSpace(built.AttachableJson))
                {
                    string attAbs = Path.Combine(session.PackRoot, built.AttachableOutRel.Replace('/', Path.DirectorySeparatorChar));
                    string? attDir = Path.GetDirectoryName(attAbs);
                    if (!string.IsNullOrWhiteSpace(attDir))
                        Directory.CreateDirectory(attDir);

                    try
                    {
                        File.WriteAllText(attAbs, built.AttachableJson, System.Text.Encoding.UTF8);
                        ConsoleWorker.Write.Line("info", ns + ":" + id + " attachable → " + built.AttachableOutRel);
                    }
                    catch (Exception ex)
                    {
                        ConsoleWorker.Write.Line("warn", ns + ":" + id + " failed writing attachable: " + attAbs + " ex=" + ex.Message);
                    }
                }
                else
                {
                    ConsoleWorker.Write.Line("warn", ns + ":" + id + " has no AttachableJson produced by ModelBuilderWorker.");
                }

                // 1c) Copy model textures into the pack
                if (built.TexturesToCopy != null)
                {
                    foreach (var (src, dstRel) in built.TexturesToCopy)
                    {
                        if (string.IsNullOrWhiteSpace(src) || string.IsNullOrWhiteSpace(dstRel))
                            continue;

                        string dstAbs = Path.Combine(session.PackRoot, dstRel.Replace('/', Path.DirectorySeparatorChar));
                        string? dstDir = Path.GetDirectoryName(dstAbs);
                        if (!string.IsNullOrWhiteSpace(dstDir))
                            Directory.CreateDirectory(dstDir);

                        try
                        {
                            File.Copy(src, dstAbs, true);
                        }
                        catch (Exception ex)
                        {
                            ConsoleWorker.Write.Line("warn",
                                "Failed copying model texture " + src + " -> " + dstAbs + " ex=" + ex.Message);
                        }
                    }
                }

                // --- 3D ICON RENDERING ---

                // 1d) Copy textures used by any 3D state models (graphics.models.*, resource-derived states)
                // so that pulling/throwing/cast/blocking variants have all their textures available.
                CopyStateModelTextures(session, it, itemsAdderFolder);

                bool hasModel = !string.IsNullOrWhiteSpace(it.ModelPath) && File.Exists(it.ModelPath);

                // 2) Primary: render a snapshot into the *pack session folder*.
                if (hasModel)
                {
                    string iconWorkRoot = Path.Combine(session.PackRoot, "_icons");
                    Directory.CreateDirectory(iconWorkRoot);

                    ConsoleWorker.Write.Line("info", ns + ":" + id + " attempting 3D icon render via ModelImageBuilderWorker.");
                    var icon = ModelImageBuilderWorker.RenderItemIcon(it, iconWorkRoot, renderer);
                    if (icon.Success &&
                        !string.IsNullOrWhiteSpace(icon.IconPngAbs) &&
                        File.Exists(icon.IconPngAbs))
                    {
                        iconSourceAbs = icon.IconPngAbs;
                        ConsoleWorker.Write.Line("info", ns + ":" + id + " 3D snapshot icon → " + icon.IconPngAbs);
                    }
                    else
                    {
                        // diagnostic notes if any
                        if (icon.Notes.Count > 0)
                        {
                            foreach (var note in icon.Notes)
                            {
                                ConsoleWorker.Write.Line("warn", ns + ":" + id + " icon note: " + note);
                            }
                        }
                    }
                }

                try
                {
                    // Additionally, render 3D icons for any state models (pulling_0/1/2, arrow, rocket, etc.)
                    // into textures/items/{ns}/{id}_{state}.png so GUI can show the correct frames.
                    RenderStateIconsFromModels(session, it, renderer, itemsAdderFolder);
                }
                catch
                {
                    ConsoleWorker.Write.Line("Error", ns + ":" + id + " failed rendering 3D state icons for " + it.ItemNamespace + ":" + it.ItemID + ", does it contain custom states?");
                }

                // 3) Fallback: if ModelBuilderWorker already produced an icon, use that.
                if (string.IsNullOrWhiteSpace(iconSourceAbs) &&
                    built.IconPngAbs != null &&
                    File.Exists(built.IconPngAbs))
                {
                    iconSourceAbs = built.IconPngAbs;
                    ConsoleWorker.Write.Line("info", ns + ":" + id + " fallback icon from ModelBuilderWorker → " + built.IconPngAbs);
                }

                // 4) Final fallback: use the base texture as a last resort.
                if (string.IsNullOrWhiteSpace(iconSourceAbs) &&
                    !string.IsNullOrWhiteSpace(it.TexturePath) &&
                    File.Exists(it.TexturePath))
                {
                    iconSourceAbs = it.TexturePath;
                    ConsoleWorker.Write.Line("info", ns + ":" + id + " final fallback icon → " + it.TexturePath);
                }
            }
            else
            {
                // 2D item: use the resolved 2D texture directly.
                iconSourceAbs = it.TexturePath;
            }

            // Handle ItemsAdder recolor tint for 2D items (vanilla/custom)
            if (!it.Is3D && !string.IsNullOrWhiteSpace(it.RecolorTint))
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(iconAbs)!);
                    string? recolorError = null;
                    bool recolorSuccess = false;

                    if (it.UsesVanillaTexture)
                    {
                        recolorSuccess = VanillaRecolorerWorker.TryBuildRecoloredItemVanillaTexture(it, selectedVersion, iconAbs, out recolorError);
                    }
                    else
                    {
                        recolorSuccess = CustomRecolorerWorker.TryBuildRecoloredCustomTexture(it, iconAbs, out recolorError);
                    }

                    if (recolorSuccess && File.Exists(iconAbs))
                    {
                        iconSourceAbs = iconAbs;
                        ConsoleWorker.Write.Line("info", ns + ":" + id + " recolored icon → " + iconAtlasRel);
                    }
                    else if (!string.IsNullOrWhiteSpace(recolorError))
                    {
                        ConsoleWorker.Write.Line("warn", ns + ":" + id + " recolor failed: " + recolorError);
                    }
                }
                catch (Exception ex)
                {
                    ConsoleWorker.Write.Line("warn", ns + ":" + id + " recolor exception: " + ex.Message);
                }
            }

            // Decide whether we conceptually "have an icon" based on having some path,
            // even if the file does not exist yet (that becomes a separate warning).
            bool hasIconPath = !string.IsNullOrWhiteSpace(iconSourceAbs);

            // Copy icon into the pack when the source file is available.
            if (hasIconPath && File.Exists(iconSourceAbs!))
            {
                // iconAbs already computed above
                Directory.CreateDirectory(Path.GetDirectoryName(iconAbs)!);

                // If the recolorer already wrote to iconAbs, don't try to copy file onto itself.
                if (!string.Equals(iconSourceAbs, iconAbs, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(iconSourceAbs!, iconAbs, true);
                }

                ConsoleWorker.Write.Line("info", ns + ":" + id + " icon → " + iconAtlasRel);
            }
            else if (hasIconPath)
            {
                // We have a declared icon path (TexturePath/IconPath), but the source file is missing.
                ConsoleWorker.Write.Line("warn", ns + ":" + id + " IconPath/TexturePath points to a missing file: " + iconSourceAbs);
            }
            else
            {
                // Truly no icon path at all; this means the data class is incomplete for this item.
                ConsoleWorker.Write.Line("warn", ns + ":" + id + " has no IconPath and no TexturePath; atlas entry will be skipped.");
            }

            // If we have at least some icon path, still create/refresh the atlas entry
            // so fixes to paths/files later don't require regenerating the whole pack.
            if (hasIconPath)
            {
                UpdateItemAtlas(session, atlasKey, iconAtlasRel);
            }

            // Write minimal item definition json (binds to atlas key)
            WriteItemDefinition(session, bedrockId, atlasKey);

            // Copy any per-state 2D textures (graphics.textures.*) into the pack
            // so state frames are available for animations/render controllers.
            CopyStateTextures(session, it);

            // 🔹 NEW: register special items for animation controller building
            if (IsSpecialToolMaterial(it.Material))
            {
                try
                {
                    AnimationControllerBuilderWorker.RegisterSpecialItem(session, it);
                }
                catch (Exception ex)
                {
                    ConsoleWorker.Write.Line(
                        "warn",
                        ns + ":" + id + " animation controller registration failed: " + ex.Message
                    );
                }
            }
        }

    // ---------- helpers ----------

    private static void UpdateItemAtlas(PackSession session, string key, string iconRel)
        {
            string atlasPath = Path.Combine(session.PackRoot, "textures", "item_texture.json");
            JObject root;

            if (File.Exists(atlasPath))
            {
                try { root = JObject.Parse(File.ReadAllText(atlasPath)); }
                catch { root = new JObject(); }
            }
            else
            {
                root = new JObject
                {
                    ["resource_pack_name"] = session.PackName ?? "BedrockAdder",
                    ["texture_name"] = "atlas.items",
                    ["texture_data"] = new JObject()
                };
            }

            if (root["texture_data"] is not JObject data)
            {
                data = new JObject();
                root["texture_data"] = data;
            }

            // Normalize slashes
            string texPath = BedrockManager.NormalizeAtlasPath(iconRel);

            data[key] = new JObject
            {
                ["textures"] = texPath
            };

            File.WriteAllText(atlasPath, root.ToString(Formatting.Indented), System.Text.Encoding.UTF8);
            ConsoleWorker.Write.Line("info", "Updated item_texture.json → " + key + " = " + texPath);
        }

        private static void WriteItemDefinition(PackSession session, string bedrockId, string atlasKey)
        {
            string safeName = bedrockId.Replace(':', '_'); // ia_ns_id
            string itemJsonRel = Path.Combine("items", safeName + ".json");
            string itemJsonAbs = Path.Combine(session.PackRoot, itemJsonRel);

            Directory.CreateDirectory(Path.GetDirectoryName(itemJsonAbs)!);

            var item = new JObject
            {
                ["format_version"] = "1.20.0",
                ["minecraft:item"] = new JObject
                {
                    ["description"] = new JObject
                    {
                        ["identifier"] = bedrockId
                    },
                    ["components"] = new JObject
                    {
                        // atlas key goes here
                        ["minecraft:icon"] = new JObject { ["texture"] = atlasKey }
                        // you can add more later: category, use_animation, max_stack_size, etc.
                    }
                }
            };

            File.WriteAllText(itemJsonAbs, item.ToString(Formatting.Indented), System.Text.Encoding.UTF8);
            ConsoleWorker.Write.Line("info", "Wrote item json → " + itemJsonRel + " (id=" + bedrockId + ")");
        }

        private static string Sanitize(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "unknown";
            s = s.Trim();
            var sb = new System.Text.StringBuilder();
            foreach (char ch in s)
            {
                if (char.IsLetterOrDigit(ch) || ch == '_') sb.Append(char.ToLowerInvariant(ch));
                else sb.Append('_');
            }
            return sb.ToString();
        }

        private static bool IsSpecialToolMaterial(string material)
        {
            if (string.IsNullOrWhiteSpace(material))
                return false;

            return material.Equals("BOW", StringComparison.OrdinalIgnoreCase)
                || material.Equals("CROSSBOW", StringComparison.OrdinalIgnoreCase)
                || material.Equals("FISHING_ROD", StringComparison.OrdinalIgnoreCase)
                || material.Equals("SHIELD", StringComparison.OrdinalIgnoreCase) || material.Equals("TRIDENT", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Copies textures used by 3D state models (graphics.models.*, resource-derived states)
        /// into the Bedrock pack, so that every state geometry has its textures available.
        ///
        /// We reuse the same "textures/models/{ns}/{file}.png" convention
        /// that ModelBuilderWorker uses for base 3D model textures.
        /// </summary>
        private static void CopyStateModelTextures(PackSession session, CustomItem it, string itemsAdderFolder)
        {
            if (it.StateModelPaths == null || it.StateModelPaths.Count == 0)
                return;

            string ns = it.ItemNamespace ?? "unknown";
            string id = it.ItemID ?? "unknown";

            // Simple de-dupe so we don't spam copy/logs for shared textures.
            var copied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var kv in it.StateModelPaths)
            {
                string stateName = kv.Key;
                string modelPath = kv.Value;

                if (string.IsNullOrWhiteSpace(stateName) || string.IsNullOrWhiteSpace(modelPath))
                    continue;

                // Derive a modelName suitable for ResolveModelTextureMapWithParents:
                //   - if it's an assets/... path, strip "assets/ns/models/" prefix and ".json"
                //   - if it's an absolute path, look for "/models/" or "\models\" and strip extension
                string modelName = DeriveModelNameFromPath(modelPath, ns);
                if (string.IsNullOrWhiteSpace(modelName))
                    continue;

                var texMap = JsonParserWorker.ResolveModelTextureMapWithParents(itemsAdderFolder, ns, modelName);
                if (texMap == null || texMap.Count == 0)
                    continue;

                foreach (var texKv in texMap)
                {
                    string normalizedAsset = texKv.Value;
                    if (string.IsNullOrWhiteSpace(normalizedAsset))
                        continue;

                    if (!JsonParserWorker.TryResolveContentAssetAbsolute(itemsAdderFolder, normalizedAsset, out var texAbs, ns) ||
                        string.IsNullOrWhiteSpace(texAbs) ||
                        !File.Exists(texAbs))
                    {
                        continue;
                    }

                    string fileName = Path.GetFileName(texAbs);
                    if (string.IsNullOrWhiteSpace(fileName))
                        continue;

                    string dstRel = Path.Combine("textures", "models", ns, fileName).Replace('\\', '/');
                    string dstAbs = Path.Combine(session.PackRoot, dstRel.Replace('/', Path.DirectorySeparatorChar));

                    if (!copied.Add(dstAbs))
                        continue; // already copied this texture somewhere

                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(dstAbs)!);
                        File.Copy(texAbs, dstAbs, true);
                        ConsoleWorker.Write.Line("info",
                            ns + ":" + id + " state-model texture [" + stateName + ":" + texKv.Key + "] "
                            + texAbs + " → " + dstRel);
                    }
                    catch (Exception ex)
                    {
                        ConsoleWorker.Write.Line("warn",
                            ns + ":" + id + " failed copying state-model texture [" + stateName + ":" + texKv.Key + "]: " + ex.Message);
                    }
                }
            }
        }

        /// <summary>
        /// Derives a modelName (as used in assets/ns/models/{modelName}.json) from either
        /// an assets/... path or an absolute path on disk.
        /// Examples:
        ///   "assets/ranged/models/black_bow_pulling_0.json" → "black_bow_pulling_0"
        ///   "C:\...\contents\ranged\resourcepack\ranged\models\bows\black_bow_pulling_0.json" → "bows/black_bow_pulling_0"
        /// </summary>
        private static string DeriveModelNameFromPath(string modelPath, string itemNamespace)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
                return string.Empty;

            string clean = modelPath.Replace('\\', '/').Trim();

            // Case 1: already "assets/ns/models/..." style
            if (clean.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
            {
                // Expect: assets/{ns}/models/{rel}.json
                string withoutAssets = clean.Substring("assets/".Length);
                int firstSlash = withoutAssets.IndexOf('/');
                if (firstSlash <= 0)
                    return string.Empty;

                // ns = before first slash; remainder = "models/...json"
                string ns = withoutAssets.Substring(0, firstSlash);
                string rel = withoutAssets.Substring(firstSlash + 1); // models/...json

                // Ensure we're under models/
                const string modelsPrefix = "models/";
                if (rel.StartsWith(modelsPrefix, StringComparison.OrdinalIgnoreCase))
                    rel = rel.Substring(modelsPrefix.Length);

                if (rel.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    rel = rel.Substring(0, rel.Length - 5);

                return rel; // e.g. "bows/black_bow_pulling_0"
            }

            // Case 2: absolute path – look for "/models/"
            int idx = clean.IndexOf("/models/", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                string rel = clean.Substring(idx + "/models/".Length); // e.g. "bows/black_bow_pulling_0.json"
                if (rel.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    rel = rel.Substring(0, rel.Length - 5);
                return rel;
            }

            // Fallback: just strip directory and extension; treat as flat name
            string fileName = Path.GetFileName(clean);
            if (fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                fileName = fileName.Substring(0, fileName.Length - 5);

            return fileName;
        }

        /// <summary>
        /// Renders 2D icons for 3D state models (graphics.models.*, resource-derived states)
        /// into the Bedrock pack, one PNG per state:
        ///
        ///   textures/items/{namespace}/{id}_{state}.png
        ///
        /// This is primarily useful for bows/crossbows where Bedrock shows different
        /// icons in the hotbar based on state.
        /// </summary>
        private static void RenderStateIconsFromModels(
            PackSession session,
            CustomItem it,
            IModelIconRenderer renderer,
            string itemsAdderFolder)
        {
            if (it.StateModelPaths == null || it.StateModelPaths.Count == 0)
                return;

            string ns = it.ItemNamespace ?? "unknown";
            string baseId = it.ItemID ?? "unknown";

            // We'll render into the same _icons work folder used for the base icon.
            string iconWorkRoot = Path.Combine(session.PackRoot, "_icons");
            Directory.CreateDirectory(iconWorkRoot);

            foreach (var kv in it.StateModelPaths)
            {
                string stateName = kv.Key;
                string stateModelPath = kv.Value;

                if (string.IsNullOrWhiteSpace(stateName) ||
                    string.IsNullOrWhiteSpace(stateModelPath))
                    continue;

                // Skip "normal", we already rendered the base icon for that.
                if (stateName.Equals("normal", StringComparison.OrdinalIgnoreCase))
                    continue;

                // We only care about our special tools here (bows, crossbows, etc.)
                if (!IsSpecialToolMaterial(it.Material))
                    continue;

                // Build a lightweight temporary CustomItem just for rendering this state's icon.
                var temp = new CustomItem
                {
                    ItemNamespace = it.ItemNamespace,
                    ItemID = baseId + "_" + stateName,
                    Material = it.Material,
                    RecolorTint = it.RecolorTint,
                    UsesVanillaTexture = it.UsesVanillaTexture,
                    VanillaTextureId = it.VanillaTextureId,
                    CustomModelData = it.CustomModelData,
                    Is3D = true,
                    ModelPath = stateModelPath
                };

                temp.ModelTexturePaths.Clear();

                // Derive the modelName for ResolveModelTextureMapWithParents
                string modelName = DeriveModelNameFromPath(stateModelPath, ns);
                var texMap = JsonParserWorker.ResolveModelTextureMapWithParents(itemsAdderFolder, ns, modelName);

                foreach (var texKv in texMap)
                {
                    string normalizedAsset = texKv.Value;
                    if (string.IsNullOrWhiteSpace(normalizedAsset))
                        continue;

                    if (JsonParserWorker.TryResolveContentAssetAbsolute(itemsAdderFolder, normalizedAsset, out var texAbs, ns) &&
                        !string.IsNullOrWhiteSpace(texAbs) &&
                        File.Exists(texAbs))
                    {
                        temp.ModelTexturePaths[texKv.Key] = texAbs;

                        if (string.IsNullOrWhiteSpace(temp.TexturePath))
                            temp.TexturePath = texAbs;
                    }
                }

                // If we still have no textures, don't even try to render.
                if (temp.ModelTexturePaths.Count == 0 && string.IsNullOrWhiteSpace(temp.TexturePath))
                {
                    ConsoleWorker.Write.Line(
                        "warn",
                        ns + ":" + baseId + " no textures available to render state icon [" + stateName + "]");
                    continue;
                }

                ConsoleWorker.Write.Line(
                    "info",
                    ns + ":" + baseId + " attempting 3D state icon render for [" + stateName + "] via ModelImageBuilderWorker.");

                var icon = ModelImageBuilderWorker.RenderItemIcon(temp, iconWorkRoot, renderer);
                if (icon.Success &&
                    !string.IsNullOrWhiteSpace(icon.IconPngAbs) &&
                    File.Exists(icon.IconPngAbs))
                {
                    // Copy the generated PNG into the pack as textures/items/ns/id_state.png
                    string fileName = Sanitize(baseId) + "_" + Sanitize(stateName) + ".png";
                    string rel = Path.Combine("textures", "items", ns, fileName).Replace('\\', '/');
                    string abs = Path.Combine(session.PackRoot, rel.Replace('/', Path.DirectorySeparatorChar));

                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
                        File.Copy(icon.IconPngAbs, abs, true);
                        ConsoleWorker.Write.Line(
                            "info",
                            ns + ":" + baseId + " 3D state icon [" + stateName + "] → " + rel);
                    }
                    catch (Exception ex)
                    {
                        ConsoleWorker.Write.Line(
                            "warn",
                            ns + ":" + baseId + " failed copying 3D state icon [" + stateName + "]: " + ex.Message);
                    }
                }
                else
                {
                    if (icon.Notes != null && icon.Notes.Count > 0)
                    {
                        foreach (var note in icon.Notes)
                        {
                            ConsoleWorker.Write.Line(
                                "warn",
                                ns + ":" + baseId + " state icon[" + stateName + "] note: " + note);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Copies per-state 2D textures into the Bedrock pack so that multi-state
        /// items (bows, rods, etc.) have all their frames available.
        /// 
        /// We do NOT wire these into the item atlas yet; they are assets that
        /// animation controllers / attachables can reference later.
        /// 
        /// Output pattern:
        ///   textures/items/{namespace}/{id}_{state}.png
        /// e.g.  textures/items/ranged/black_bow_bow_0.png
        /// </summary>
        private static void CopyStateTextures(PackSession session, CustomItem it)
        {
            if (it.StateTexturePaths == null || it.StateTexturePaths.Count == 0)
                return;

            string ns = it.ItemNamespace ?? "unknown";
            string id = it.ItemID ?? "unknown";

            foreach (var kv in it.StateTexturePaths)
            {
                string stateName = kv.Key;
                string src = kv.Value;

                if (string.IsNullOrWhiteSpace(stateName) ||
                    string.IsNullOrWhiteSpace(src) ||
                    !File.Exists(src))
                {
                    continue;
                }

                string safeState = Sanitize(stateName);
                string ext = Path.GetExtension(src);
                if (string.IsNullOrEmpty(ext))
                    ext = ".png";

                string fileName = Sanitize(id) + "_" + safeState + ext;
                string rel = Path.Combine("textures", "items", ns, fileName).Replace('\\', '/');
                string abs = Path.Combine(session.PackRoot, rel.Replace('/', Path.DirectorySeparatorChar));

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
                    File.Copy(src, abs, true);
                    ConsoleWorker.Write.Line("info", ns + ":" + id + " state texture [" + stateName + "] → " + rel);
                }
                catch (Exception ex)
                {
                    ConsoleWorker.Write.Line("warn", ns + ":" + id + " failed copying state texture [" + stateName + "]: " + ex.Message);
                }
            }
        }
    }
}