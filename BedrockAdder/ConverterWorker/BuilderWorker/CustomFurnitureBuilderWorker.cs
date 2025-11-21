using BedrockAdder.ConverterWorker.ObjectWorker;   // ModelBuilderWorker, IModelIconRenderer, ModelImageBuilderWorker
using BedrockAdder.Library;
using BedrockAdder.Managers;
using BedrockAdder.Renderer;                       // CefOffscreenIconRenderer
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;

namespace BedrockAdder.ConverterWorker.BuilderWorker
{
    internal static class CustomFurnitureBuilderWorker
    {
        /// <summary>
        /// Build all custom furniture into the Bedrock resource pack.
        /// </summary>
        public static void BuildCustomFurniture(PackSession session, int iconSize, string itemsAdderFolder)
        {
            if (session == null)
            {
                ConsoleWorker.Write.Line("error", "CustomFurnitureBuilderWorker: session is null");
                return;
            }

            if (Lists.CustomFurniture == null || Lists.CustomFurniture.Count == 0)
            {
                ConsoleWorker.Write.Line("info", "CustomFurnitureBuilderWorker: no custom furniture to build.");
                return;
            }

            string renderHtmlAbs = Path.Combine(AppContext.BaseDirectory, "Renderer", "cef", "render.html");
            IModelIconRenderer renderer = new CefOffscreenIconRenderer(iconSize, true, renderHtmlAbs);

            foreach (CustomFurniture furniture in Lists.CustomFurniture)
            {
                try
                {
                    BuildOneFurniture(session, furniture, renderer);
                }
                catch (Exception ex)
                {
                    ConsoleWorker.Write.Line("error", "BuildOneFurniture (furniture) failed: " + ex.Message);
                }
            }
        }

        public static void BuildOneFurniture(PackSession session, CustomFurniture furniture, IModelIconRenderer renderer)
        {
            if (furniture == null)
            {
                ConsoleWorker.Write.Line("warn", "BuildOneFurniture: furniture is null");
                return;
            }

            string ns = string.IsNullOrWhiteSpace(furniture.FurnitureNamespace)
                ? "unknown"
                : furniture.FurnitureNamespace;

            string id = string.IsNullOrWhiteSpace(furniture.FurnitureItemID)
                ? "unknown"
                : furniture.FurnitureItemID;

            string nsSafe = Sanitize(ns);
            string idSafe = Sanitize(id);

            // Bedrock identifier + atlas key + icon path
            string bedrockId = BedrockManager.MakeBedrockItemId(ns, id); // ia:ns_id
            string atlasKey = "ia_furniture_" + nsSafe + "_" + idSafe;   // key inside item_texture.json

            // Use the same icon path convention as Built3DObjectNaming
            string iconAtlasRel = Built3DObjectNaming.MakeIconRel(ns, id); // textures/items/ns/id.png
            string iconAbs = Path.Combine(
                session.PackRoot,
                iconAtlasRel.Replace('/', Path.DirectorySeparatorChar)
            );

            // Ensure base dirs (mirrors block builder, but furniture has no terrain textures)
            BedrockManager.EnsureDir(Path.Combine(session.PackRoot, "items"));
            BedrockManager.EnsureDir(Path.Combine(session.PackRoot, "attachables"));
            BedrockManager.EnsureDir(Path.Combine(session.PackRoot, "models", "entity"));
            BedrockManager.EnsureDir(Path.Combine(session.PackRoot, "textures", "items"));
            BedrockManager.EnsureDir(Path.Combine(session.PackRoot, "textures", "models", nsSafe));

            // ---------- 3D GEOMETRY / MODEL TEXTURES ----------
            if (!string.IsNullOrWhiteSpace(furniture.ModelPath) && File.Exists(furniture.ModelPath))
            {
                var built = ModelBuilderWorker.Build(furniture, iconRenderer: null);

                // 1) Write geometry (.geo.json) if present
                if (!string.IsNullOrWhiteSpace(built.GeometryJson))
                {
                    string geoAbs = Path.Combine(
                        session.PackRoot,
                        built.GeometryOutRel.Replace('/', Path.DirectorySeparatorChar)
                    );
                    string? geoDir = Path.GetDirectoryName(geoAbs);
                    if (!string.IsNullOrWhiteSpace(geoDir))
                        Directory.CreateDirectory(geoDir);

                    try
                    {
                        File.WriteAllText(geoAbs, built.GeometryJson, System.Text.Encoding.UTF8);
                        ConsoleWorker.Write.Line("info", ns + ":" + id + " wrote furniture geometry " + built.GeometryOutRel);
                    }
                    catch (Exception ex)
                    {
                        ConsoleWorker.Write.Line(
                            "warn",
                            ns + ":" + id + " failed writing furniture geometry: " + geoAbs + " ex=" + ex.Message
                        );
                    }
                }

                // 2) Write attachable (.json) if present
                if (!string.IsNullOrWhiteSpace(built.AttachableJson))
                {
                    string attAbs = Path.Combine(
                        session.PackRoot,
                        built.AttachableOutRel.Replace('/', Path.DirectorySeparatorChar)
                    );
                    string? attDir = Path.GetDirectoryName(attAbs);
                    if (!string.IsNullOrWhiteSpace(attDir))
                        Directory.CreateDirectory(attDir);

                    try
                    {
                        File.WriteAllText(attAbs, built.AttachableJson, System.Text.Encoding.UTF8);
                        ConsoleWorker.Write.Line("info", ns + ":" + id + " wrote furniture attachable " + built.AttachableOutRel);
                    }
                    catch (Exception ex)
                    {
                        ConsoleWorker.Write.Line(
                            "warn",
                            ns + ":" + id + " failed writing furniture attachable: " + attAbs + " ex=" + ex.Message
                        );
                    }
                }
                else
                {
                    ConsoleWorker.Write.Line("warn", ns + ":" + id + " furniture has no AttachableJson produced by ModelBuilderWorker.");
                }

                // 3) Copy textures used by the 3D model
                if (built.TexturesToCopy != null)
                {
                    foreach (var (srcAbs, dstRel) in built.TexturesToCopy)
                    {
                        if (string.IsNullOrWhiteSpace(srcAbs) || string.IsNullOrWhiteSpace(dstRel))
                            continue;

                        string toAbs = Path.Combine(
                            session.PackRoot,
                            dstRel.Replace('/', Path.DirectorySeparatorChar)
                        );
                        string? toDir = Path.GetDirectoryName(toAbs);
                        if (!string.IsNullOrWhiteSpace(toDir))
                            Directory.CreateDirectory(toDir);

                        try
                        {
                            File.Copy(srcAbs, toAbs, true);
                            ConsoleWorker.Write.Line("info", ns + ":" + id + " copied furniture texture " + srcAbs + " → " + dstRel);
                        }
                        catch (Exception ex)
                        {
                            ConsoleWorker.Write.Line(
                                "warn",
                                ns + ":" + id + " failed copying furniture texture " + srcAbs + " → " + dstRel + " ex=" + ex.Message
                            );
                        }
                    }
                }
            }
            else
            {
                ConsoleWorker.Write.Line("warn", ns + ":" + id + " furniture has no valid ModelPath, skipping geometry.");
            }

            // ---------- ICON RENDERING ----------

            string? iconSourceAbs = null;

            try
            {
                string iconTempDir = Path.Combine(session.PackRoot, "_furniture_icons");
                var renderResult = ModelImageBuilderWorker.RenderFurnitureIcon(furniture, iconTempDir, renderer);

                // Use whatever the renderer suggests as the atlas-relative path if available
                if (!string.IsNullOrWhiteSpace(renderResult.SuggestedAtlasRel))
                {
                    iconAtlasRel = renderResult.SuggestedAtlasRel;
                    iconAbs = Path.Combine(
                        session.PackRoot,
                        iconAtlasRel.Replace('/', Path.DirectorySeparatorChar)
                    );
                }

                if (renderResult.Success &&
                    !string.IsNullOrWhiteSpace(renderResult.IconPngAbs) &&
                    File.Exists(renderResult.IconPngAbs))
                {
                    iconSourceAbs = renderResult.IconPngAbs;
                    ConsoleWorker.Write.Line(
                        "info",
                        ns + ":" + id + " furniture 3D icon rendered → " + renderResult.IconPngAbs
                    );
                }
                else
                {
                    ConsoleWorker.Write.Line(
                        "warn",
                        ns + ":" + id + " furniture icon render failed; will try flat fallbacks."
                    );
                }
            }
            catch (Exception ex)
            {
                ConsoleWorker.Write.Line(
                    "warn",
                    ns + ":" + id + " exception while rendering furniture icon: " + ex.Message
                );
            }

            // Fallbacks if 3D render failed
            if (iconSourceAbs == null)
            {
                // 1) Explicit IconPath from extractor
                if (!string.IsNullOrWhiteSpace(furniture.IconPath) && File.Exists(furniture.IconPath))
                {
                    iconSourceAbs = furniture.IconPath;
                    ConsoleWorker.Write.Line(
                        "info",
                        ns + ":" + id + " furniture icon override → " + furniture.IconPath
                    );
                }
                else
                {
                    // 2) First available texture in TexturePaths
                    foreach (var kv in furniture.TexturePaths)
                    {
                        if (!string.IsNullOrWhiteSpace(kv.Value) && File.Exists(kv.Value))
                        {
                            iconSourceAbs = kv.Value;
                            ConsoleWorker.Write.Line(
                                "info",
                                ns + ":" + id + " furniture icon from texture slot " + kv.Key + " → " + kv.Value
                            );
                            break;
                        }
                    }
                }

                if (iconSourceAbs == null)
                {
                    ConsoleWorker.Write.Line(
                        "error",
                        ns + ":" + id + " furniture has no usable icon or textures; skipping icon creation."
                    );
                }
            }

            // Copy icon into pack at textures/items/<ns>/<id>.png
            if (!string.IsNullOrWhiteSpace(iconSourceAbs) && File.Exists(iconSourceAbs))
            {
                string? iconDir = Path.GetDirectoryName(iconAbs);
                if (!string.IsNullOrWhiteSpace(iconDir))
                    Directory.CreateDirectory(iconDir);

                if (string.Equals(iconSourceAbs, iconAbs, StringComparison.OrdinalIgnoreCase))
                {
                    ConsoleWorker.Write.Line(
                        "info",
                        ns + ":" + id + " furniture icon already at target path " + iconAbs + ", skipping copy."
                    );
                }
                else
                {
                    try
                    {
                        File.Copy(iconSourceAbs, iconAbs, true);
                        ConsoleWorker.Write.Line(
                            "info",
                            ns + ":" + id + " copied furniture icon " + iconSourceAbs + " → " + iconAbs
                        );
                    }
                    catch (Exception ex)
                    {
                        ConsoleWorker.Write.Line(
                            "warn",
                            ns + ":" + id + " failed copying furniture icon " + iconSourceAbs + " → " + iconAbs + " ex=" + ex.Message
                        );
                    }
                }
            }

            // ---------- UPDATE item_texture.json ----------

            try
            {
                string atlasPath = Path.Combine(session.PackRoot, "textures", "item_texture.json");
                JObject atlasRoot;

                if (File.Exists(atlasPath))
                {
                    atlasRoot = JObject.Parse(File.ReadAllText(atlasPath));
                }
                else
                {
                    atlasRoot = new JObject
                    {
                        ["resource_pack_name"] = session.PackName ?? "BedrockAdder",
                        ["texture_name"] = "atlas.items",
                        ["texture_data"] = new JObject()
                    };
                }

                if (atlasRoot["texture_data"] is not JObject texData)
                {
                    texData = new JObject();
                    atlasRoot["texture_data"] = texData;
                }

                texData[atlasKey] = new JObject
                {
                    ["textures"] = BedrockManager.NormalizeAtlasPath(iconAtlasRel)
                };

                File.WriteAllText(atlasPath, atlasRoot.ToString(Formatting.Indented));
                ConsoleWorker.Write.Line(
                    "info",
                    ns + ":" + id + " furniture item_texture.json → " + atlasKey + " = " + iconAtlasRel
                );
            }
            catch (Exception ex)
            {
                ConsoleWorker.Write.Line(
                    "warn",
                    ns + ":" + id + " failed updating item_texture.json for furniture: " + ex.Message
                );
            }

            // ---------- MINIMAL ITEM DEFINITION ----------

            WriteItemDefinition(session, bedrockId, atlasKey);
        }

        // ---------- helpers ----------

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
                        // Use the atlas key we registered in item_texture.json
                        ["minecraft:icon"] = new JObject { ["texture"] = atlasKey }
                    }
                }
            };

            File.WriteAllText(itemJsonAbs, item.ToString(Formatting.Indented));
            ConsoleWorker.Write.Line("info", "Wrote furniture item json → " + itemJsonRel + " (id=" + bedrockId + ")");
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