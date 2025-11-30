using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using BedrockAdder.ConsoleWorker;
using BedrockAdder.Library;

namespace BedrockAdder.ConverterWorker.BuilderWorker
{
    internal static class AnimationControllerBuilder
    {
        // All special items (bows, crossbows, shields, fishing rods, tridents)
        private static readonly List<CustomItem> _specialItems = new List<CustomItem>();

        /// <summary>
        /// Register a special item while we build the resource pack.
        /// Called once per item in CustomItemBuilderWorker.BuildOneItem.
        /// </summary>
        internal static void RegisterSpecialItem(PackSession session, CustomItem item)
        {
            if (item == null)
                return;

            _specialItems.Add(item);

            string ns = item.ItemNamespace ?? "unknown";
            string id = item.ItemID ?? "unknown";

            Write.Line(
                "info",
                ns + ":" + id + " registered as special item (material=" + item.Material
                + "), state models=" + item.StateModelPaths.Count
                + ", state textures=" + item.StateTexturePaths.Count
            );
        }

        /// <summary>
        /// Build (or overwrite) the player_custom.animation_controllers.json file
        /// in the Bedrock pack, based on which special materials exist.
        ///
        /// For now this writes a very simple controller that just sets up
        /// separate states per tool type keyed off tags we will later map in Geyser:
        ///   cube:is_bow, cube:is_crossbow, cube:is_shield, cube:is_fishing_rod, cube:is_trident
        /// </summary>
        internal static void BuildControllers(PackSession session)
        {
            if (session == null)
            {
                Write.Line("error", "AnimationControllerBuilder.BuildControllers: session is null");
                return;
            }

            if (_specialItems.Count == 0)
            {
                // Nothing special detected; no need to write a custom controller file.
                Write.Line("info", "AnimationControllerBuilder: no special items registered, skipping controller generation.");
                return;
            }

            // Detect which tool categories we actually have
            bool hasBow = false;
            bool hasCrossbow = false;
            bool hasShield = false;
            bool hasFishingRod = false;
            bool hasTrident = false;

            foreach (var it in _specialItems)
            {
                if (it == null || string.IsNullOrWhiteSpace(it.Material))
                    continue;

                var mat = it.Material.Trim();

                if (mat.Equals("BOW", StringComparison.OrdinalIgnoreCase))
                    hasBow = true;
                else if (mat.Equals("CROSSBOW", StringComparison.OrdinalIgnoreCase))
                    hasCrossbow = true;
                else if (mat.Equals("SHIELD", StringComparison.OrdinalIgnoreCase))
                    hasShield = true;
                else if (mat.Equals("FISHING_ROD", StringComparison.OrdinalIgnoreCase))
                    hasFishingRod = true;
                else if (mat.Equals("TRIDENT", StringComparison.OrdinalIgnoreCase))
                    hasTrident = true;
            }

            // If somehow we had no valid materials, bail
            if (!hasBow && !hasCrossbow && !hasShield && !hasFishingRod && !hasTrident)
            {
                Write.Line("info", "AnimationControllerBuilder: special items list populated, but no recognized tool materials found.");
                return;
            }

            // ---------------- build the JSON ----------------

            // Root
            var root = new JObject
            {
                ["format_version"] = "1.10.0"
            };

            var controllers = new JObject();
            root["animation_controllers"] = controllers;

            // Single controller to handle all our custom tools via Geyser tags
            var controller = new JObject();
            controllers["controller.animation.player.cube_custom_items"] = controller;

            controller["initial_state"] = "default";

            var states = new JObject();
            controller["states"] = states;

            // default state: choose specialized states based on tags
            var defaultState = new JObject();
            states["default"] = defaultState;

            var defaultTransitions = new JArray();
            defaultState["transitions"] = defaultTransitions;

            // We only add transitions for categories that actually exist in this pack
            if (hasBow)
            {
                defaultTransitions.Add(new JObject
                {
                    ["bow"] = "query.get_equipped_item_any_tag('cube:is_bow')"
                });
            }

            if (hasCrossbow)
            {
                defaultTransitions.Add(new JObject
                {
                    ["crossbow"] = "query.get_equipped_item_any_tag('cube:is_crossbow')"
                });
            }

            if (hasShield)
            {
                defaultTransitions.Add(new JObject
                {
                    ["shield"] = "query.get_equipped_item_any_tag('cube:is_shield')"
                });
            }

            if (hasFishingRod)
            {
                defaultTransitions.Add(new JObject
                {
                    ["fishing_rod"] = "query.get_equipped_item_any_tag('cube:is_fishing_rod')"
                });
            }

            if (hasTrident)
            {
                defaultTransitions.Add(new JObject
                {
                    ["trident"] = "query.get_equipped_item_any_tag('cube:is_trident')"
                });
            }

            // Each specialized state just checks when to fall back to default.
            // We keep this minimal for now; later we can chain into vanilla animations
            // or more detailed pull/charge/cast logic.
            if (hasBow)
            {
                var bowState = new JObject();
                states["bow"] = bowState;

                var bowTransitions = new JArray
                {
                    new JObject
                    {
                        ["default"] = "!query.get_equipped_item_any_tag('cube:is_bow')"
                    }
                };
                bowState["transitions"] = bowTransitions;
            }

            if (hasCrossbow)
            {
                var crossState = new JObject();
                states["crossbow"] = crossState;

                var crossTransitions = new JArray
                {
                    new JObject
                    {
                        ["default"] = "!query.get_equipped_item_any_tag('cube:is_crossbow')"
                    }
                };
                crossState["transitions"] = crossTransitions;
            }

            if (hasShield)
            {
                var shieldState = new JObject();
                states["shield"] = shieldState;

                var shieldTransitions = new JArray
                {
                    new JObject
                    {
                        ["default"] = "!query.get_equipped_item_any_tag('cube:is_shield')"
                    }
                };
                shieldState["transitions"] = shieldTransitions;

                // Later: we can add animations here or rely on a custom shield render_controller
                // that uses q.blocking together with vanilla blocking logic.
            }

            if (hasFishingRod)
            {
                var rodState = new JObject();
                states["fishing_rod"] = rodState;

                var rodTransitions = new JArray
                {
                    new JObject
                    {
                        ["default"] = "!query.get_equipped_item_any_tag('cube:is_fishing_rod')"
                    }
                };
                rodState["transitions"] = rodTransitions;
            }

            if (hasTrident)
            {
                var tridentState = new JObject();
                states["trident"] = tridentState;

                var tridentTransitions = new JArray
                {
                    new JObject
                    {
                        ["default"] = "!query.get_equipped_item_any_tag('cube:is_trident')"
                    }
                };
                tridentState["transitions"] = tridentTransitions;
            }

            // ---------------- write file ----------------

            string acDir = Path.Combine(session.PackRoot, "animation_controllers");
            Directory.CreateDirectory(acDir);

            string acAbs = Path.Combine(acDir, "player_custom.animation_controllers.json");

            try
            {
                // Compact formatting; Bedrock doesn't care about pretty vs minified.
                string json = JsonConvert.SerializeObject(root, Formatting.None);
                File.WriteAllText(acAbs, json);
                string displayPath = acAbs;
                try
                {
                    if (!string.IsNullOrWhiteSpace(session.PackRoot) && acAbs.StartsWith(session.PackRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        displayPath = acAbs.Substring(session.PackRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    }
                }
                catch
                {
                    // fall back to absolute
                    displayPath = acAbs;
                }

                Write.Line("info", "AnimationControllerBuilder → " + displayPath);
            }
            catch (Exception ex)
            {
                Write.Line("error", "AnimationControllerBuilder failed to write animation controllers: " + ex.Message);
            }
        }

        /// <summary>
        /// Clear internal state (optional, if you ever rebuild in the same process).
        /// </summary>
        internal static void Reset()
        {
            _specialItems.Clear();
        }
    }
}