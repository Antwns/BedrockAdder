using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using BedrockAdder.Library;

namespace BedrockAdder.ConverterWorker.ObjectWorker
{
    // Plug your MinecraftRenderer-based implementation here
    internal interface IModelIconRenderer
    {
        // Should render a PNG icon for the given model+texture(s) to iconPngAbs and return true if created.
        bool TryRenderIcon(string javaModelPath, IReadOnlyDictionary<string, string> textureSlotsAbs, string iconPngAbs);
    }

    internal static class ModelBuilderWorker
    {
        // Public overloads – use ONLY fields from your data classes
        public static Built3DObject Build(CustomItem item, string itemsAdderFolder, IModelIconRenderer? iconRenderer = null)
        {
            var texMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in item.ModelTexturePaths)
                texMap[kv.Key] = kv.Value;
            if (texMap.Count == 0 && !string.IsNullOrWhiteSpace(item.TexturePath))
                texMap["default"] = item.TexturePath;

            return BuildCore(
                kind: Built3DKind.Item,
                ns: item.ItemNamespace,
                id: item.ItemID,
                javaModelPath: item.ModelPath,
                providedIconAbs: item.IconPath,
                textureSlotsAbs: texMap,
                iconRenderer: iconRenderer
            );
        }

        public static Built3DObject Build(CustomBlock block, IModelIconRenderer? iconRenderer = null)
        {
            var texMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in block.ModelTexturePaths)
                texMap[kv.Key] = kv.Value;
            if (texMap.Count == 0 && !string.IsNullOrWhiteSpace(block.TexturePath))
                texMap["default"] = block.TexturePath;

            return BuildCore(
                kind: Built3DKind.Block,
                ns: block.BlockNamespace,
                id: block.BlockItemID,
                javaModelPath: block.ModelPath,
                providedIconAbs: block.IconPath,
                textureSlotsAbs: texMap,
                iconRenderer: iconRenderer
            );
        }

        public static Built3DObject Build(CustomFurniture furniture, IModelIconRenderer? iconRenderer = null)
        {
            // Use the texture map already parsed by your workers
            var texMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in furniture.TexturePaths)
                texMap[kv.Key] = kv.Value;

            return BuildCore(
                kind: Built3DKind.Furniture,
                ns: furniture.FurnitureNamespace,
                id: furniture.FurnitureItemID,
                javaModelPath: furniture.ModelPath,
                providedIconAbs: furniture.IconPath,
                textureSlotsAbs: texMap,
                iconRenderer: iconRenderer
            );
        }

        public static Built3DObject Build(CustomArmor armor, IModelIconRenderer? iconRenderer = null)
        {
            // Only a custom helmet model is supported here
            if (!string.Equals(armor.Slot, "helmet", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(armor.ModelPath))
            {
                var skip = new Built3DObject
                {
                    Kind = Built3DKind.Helmet,
                    Namespace = armor.ArmorNamespace,
                    Id = armor.ArmorID,
                    BedrockIdentifier = Built3DObjectNaming.MakeIaId(armor.ArmorNamespace, armor.ArmorID),
                    GeometryIdentifier = Built3DObjectNaming.MakeGeoId(Built3DKind.Helmet, armor.ArmorNamespace, armor.ArmorID),
                    GeometryJson = "",
                    GeometryOutRel = Built3DObjectNaming.MakeGeoRel(armor.ArmorNamespace, armor.ArmorID),
                    AttachableJson = "",
                    AttachableOutRel = Built3DObjectNaming.MakeAttachableRel(armor.ArmorNamespace, armor.ArmorID)
                };
                skip.Notes.Add("Armor slot not helmet or missing ModelPath – no 3D geometry built.");
                return skip;
            }

            var texMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in armor.ModelTexturePaths)
                texMap[kv.Key] = kv.Value;
            if (texMap.Count == 0 && !string.IsNullOrWhiteSpace(armor.TexturePath))
                texMap["default"] = armor.TexturePath;

            return BuildCore(
                kind: Built3DKind.Helmet,
                ns: armor.ArmorNamespace,
                id: armor.ArmorID,
                javaModelPath: armor.ModelPath!,
                providedIconAbs: armor.IconPath,
                textureSlotsAbs: texMap,
                iconRenderer: iconRenderer
            );
        }

        // ---------------- core ----------------

        private static Built3DObject BuildCore(
            Built3DKind kind,
            string ns,
            string id,
            string javaModelPath,
            string? providedIconAbs,
            IReadOnlyDictionary<string, string> textureSlotsAbs,
            IModelIconRenderer? iconRenderer)
        {
            var result = new Built3DObject
            {
                Kind = kind,
                Namespace = ns,
                Id = id,
                BedrockIdentifier = Built3DObjectNaming.MakeIaId(ns, id),
                GeometryIdentifier = Built3DObjectNaming.MakeGeoId(kind, ns, id),
                GeometryOutRel = Built3DObjectNaming.MakeGeoRel(ns, id),
                AttachableOutRel = Built3DObjectNaming.MakeAttachableRel(ns, id),
                IconAtlasRel = Built3DObjectNaming.MakeIconRel(ns, id)
            };

            if (string.IsNullOrWhiteSpace(javaModelPath) || !File.Exists(javaModelPath))
            {
                result.Notes.Add("Java model missing: " + javaModelPath);
                return result;
            }

            // 1) Parse Java model (elements, faces, rotations)
            JObject javaRoot;
            try
            {
                javaRoot = JObject.Parse(File.ReadAllText(javaModelPath));
            }
            catch (Exception ex)
            {
                result.Notes.Add("Failed to parse Java model: " + ex.Message);
                return result;
            }

            // 2) Convert elements -> Bedrock cubes
            var cubes = ConvertElementsToCubes(javaRoot, result);

            if (cubes.Count == 0)
            {
                result.Notes.Add("No elements found in Java model.");
                return result;
            }

            // 3) Decide texture_width/height (best-effort: use the first provided texture if exists)
            int texW = 64, texH = 64;
            foreach (var kv in textureSlotsAbs)
            {
                if (File.Exists(kv.Value))
                {
                    TryGetPngSize(kv.Value, out texW, out texH); // best effort; stays 64x64 if not PNG or unknown
                    break;
                }
            }

            // 4) Build Bedrock geometry JSON (single-bone)
            result.GeometryJson = BuildBedrockGeometryJson(result.GeometryIdentifier, cubes, texW, texH);

            // 5) Plan texture copies (use ONLY provided map)
            foreach (var kv in textureSlotsAbs)
            {
                string src = kv.Value;
                if (string.IsNullOrWhiteSpace(src) || !File.Exists(src)) { result.Notes.Add("Texture missing: " + kv.Key + " -> " + src); continue; }
                string dstRel = Built3DObjectNaming.MakeModelTextureRel(ns, Path.GetFileName(src));
                result.TexturesToCopy.Add((src, dstRel));
            }

            // 6) Build attachable JSON (named texture map if multiple)
            result.AttachableJson = BuildAttachableJson(
                itemIdentifier: result.BedrockIdentifier,
                geometryIdentifier: result.GeometryIdentifier,
                textureSlotsRel: MakeRelativeTextureMap(ns, textureSlotsAbs)
            );

            // 7) Icon: prefer provided; else try renderer
            if (!string.IsNullOrWhiteSpace(providedIconAbs) && File.Exists(providedIconAbs))
            {
                result.IconPngAbs = providedIconAbs;
            }
            else if (iconRenderer != null && textureSlotsAbs.Count > 0)
            {
                // Generate a temp icon alongside model (same directory)
                string iconAbs = Path.Combine(Path.GetDirectoryName(javaModelPath) ?? "", id + "_icon.png");
                bool ok = iconRenderer.TryRenderIcon(javaModelPath, textureSlotsAbs, iconAbs);
                if (ok && File.Exists(iconAbs)) result.IconPngAbs = iconAbs;
                else result.Notes.Add("Icon renderer failed or returned no file.");
            }
            else
            {
                result.Notes.Add("No icon provided and no renderer available.");
            }

            return result;
        }

        // ---- helpers ----

        private static List<JObject> ConvertElementsToCubes(JObject javaRoot, Built3DObject logTarget)
        {
            var cubes = new List<JObject>();
            var elements = javaRoot["elements"] as JArray;
            if (elements == null) return cubes;

            foreach (var e in elements)
            {
                if (e is not JObject el) continue;

                var from = el["from"] as JArray;
                var to = el["to"] as JArray;
                if (from == null || to == null || from.Count != 3 || to.Count != 3) continue;

                float fx = (float)from[0]; float fy = (float)from[1]; float fz = (float)from[2];
                float tx = (float)to[0]; float ty = (float)to[1]; float tz = (float)to[2];

                var size = new JArray(tx - fx, ty - fy, tz - fz);
                var origin = new JArray(fx - 8f, 24f - ty, fz - 8f);

                var cube = new JObject
                {
                    ["origin"] = origin,
                    ["size"] = size
                };

                // rotation/pivot
                var rot = el["rotation"] as JObject;
                if (rot != null)
                {
                    float angle = rot["angle"] != null ? (float)rot["angle"]! : 0f;
                    string axis = rot["axis"]?.ToString() ?? "y";
                    var ro = rot["origin"] as JArray;
                    if (ro != null && ro.Count == 3)
                    {
                        float rOx = (float)ro[0];
                        float rOy = (float)ro[1];
                        float rOz = (float)ro[2];
                        var pivot = new JArray(rOx - 8f, 24f - rOy, rOz - 8f);
                        cube["pivot"] = pivot;
                    }
                    float rx = 0, ry = 0, rz = 0;
                    switch (axis)
                    {
                        case "x": rx = angle; break;
                        case "y": ry = angle; break;
                        case "z": rz = angle; break;
                    }
                    cube["rotation"] = new JArray(rx, ry, rz);
                }

                // per-face UV
                var faces = el["faces"] as JObject;
                if (faces != null)
                {
                    var uv = new JObject();
                    foreach (var faceName in new[] { "north", "south", "east", "west", "up", "down" })
                    {
                        if (faces[faceName] is JObject f)
                        {
                            var uvArr = f["uv"] as JArray;
                            if (uvArr != null && uvArr.Count == 4)
                            {
                                float u1 = (float)uvArr[0], v1 = (float)uvArr[1], u2 = (float)uvArr[2], v2 = (float)uvArr[3];
                                float u = Math.Min(u1, u2), v = Math.Min(v1, v2), w = Math.Abs(u2 - u1), h = Math.Abs(v2 - v1);
                                uv[faceName] = new JObject { ["uv"] = new JArray(u, v), ["uv_size"] = new JArray(w, h) };
                            }
                            // We ignore per-face "rotation" for now; add later if samples require it.
                        }
                    }
                    if (uv.HasValues) cube["uv"] = uv;
                }

                cubes.Add(cube);
            }

            if (cubes.Count == 0) logTarget.Notes.Add("Converter produced 0 cubes.");
            return cubes;
        }

        private static string BuildBedrockGeometryJson(string identifier, List<JObject> cubes, int textureWidth, int textureHeight)
        {
            var bone = new JObject
            {
                ["name"] = "root",
                ["pivot"] = new JArray(0, 24, 0),
                ["cubes"] = new JArray(cubes)
            };

            var geo = new JObject
            {
                ["format_version"] = "1.12.0",
                ["minecraft:geometry"] = new JArray
                {
                    new JObject
                    {
                        ["description"] = new JObject
                        {
                            ["identifier"] = identifier,
                            ["texture_width"] = textureWidth,
                            ["texture_height"] = textureHeight
                        },
                        ["bones"] = new JArray(bone)
                    }
                }
            };

            return geo.ToString(Newtonsoft.Json.Formatting.Indented);
        }

        private static string BuildAttachableJson(string itemIdentifier, string geometryIdentifier, Dictionary<string, string> textureSlotsRel)
        {
            // If no textures available, reference a neutral path (Bedrock expects a texture)
            var texturesObj = new JObject();
            if (textureSlotsRel.Count == 0)
            {
                texturesObj["default"] = "textures/items/unknown";
            }
            else
            {
                foreach (var kv in textureSlotsRel)
                {
                    texturesObj[kv.Key] = kv.Value.Replace("\\", "/");
                }
            }

            var desc = new JObject
            {
                ["identifier"] = itemIdentifier,
                ["materials"] = new JObject { ["default"] = "entity_alphatest" },
                ["textures"] = texturesObj,
                ["geometry"] = new JObject { ["default"] = geometryIdentifier },
                ["render_controllers"] = new JArray("controller.render.item_default")
            };

            var root = new JObject
            {
                ["format_version"] = "1.10.0",
                ["minecraft:attachable"] = new JObject { ["description"] = desc }
            };

            return root.ToString(Newtonsoft.Json.Formatting.Indented);
        }

        private static Dictionary<string, string> MakeRelativeTextureMap(string ns, IReadOnlyDictionary<string, string> slotsAbs)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in slotsAbs)
            {
                string src = kv.Value;
                if (string.IsNullOrWhiteSpace(src)) continue;
                string rel = Built3DObjectNaming.MakeModelTextureRel(ns, Path.GetFileName(src));
                map[kv.Key] = rel;
            }
            return map;
        }

        // read PNG IHDR (best-effort); fallback stays 64x64
        private static void TryGetPngSize(string path, out int width, out int height)
        {
            width = 64; height = 64;
            try
            {
                using var fs = File.OpenRead(path);
                using var br = new BinaryReader(fs);
                // Signature
                ulong sig = br.ReadUInt64();
                if (sig != 0x89504E470D0A1A0AUL) return;

                int len = ReadBigEndianInt32(br);              // IHDR length (should be 13)
                var chunk = new string(br.ReadChars(4));       // "IHDR"
                if (chunk != "IHDR") return;

                width = ReadBigEndianInt32(br);
                height = ReadBigEndianInt32(br);
                // skip rest of IHDR (13 - 8 bytes already read) + CRC
                fs.Seek(len - 8 + 4, SeekOrigin.Current);
            }
            catch { }

            static int ReadBigEndianInt32(BinaryReader r)
            {
                var bytes = r.ReadBytes(4);
                if (bytes.Length < 4) return 0;
                Array.Reverse(bytes);
                return BitConverter.ToInt32(bytes, 0);
            }
        }
    }
}