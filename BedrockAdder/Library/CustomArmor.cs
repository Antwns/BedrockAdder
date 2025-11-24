using System.Collections.Generic;

namespace BedrockAdder.Library
{
    internal class CustomArmor
    {
        public string ArmorNamespace { get; set; }
        public string ArmorID { get; set; }
        public string Slot { get; set; } // "helmet", "chestplate", etc.
        public string Material { get; set; } // Java item base (e.g. "leather_helmet")

        // For non-vanilla/custom armor, this is the main 2D texture used as the source
        // for the Bedrock icon (and sometimes as a generic texture). For worn layers,
        // use ArmorLayerChest / ArmorLayerLegs.
        public string TexturePath { get; set; }

        // 3D helmet model (Java/ItemsAdder JSON). Only meaningful when Slot == "helmet".
        // We will treat this the same way as CustomItem.ModelPath.
        public string? ModelPath { get; set; }

        // True if this armor piece (only really relevant for Slot == "helmet")
        // uses a custom 3D model. This is our equivalent of CustomItem.Is3D.
        public bool Is3DHelmet { get; set; }

        public int? CustomModelData { get; set; } // Optional CMD if relevant

        // Optional 2D inventory icon; if null, we may fall back to TexturePath
        // or render from the 3D model.
        public string? IconPath { get; set; }

        // Worn body textures (Bedrock armor layers)
        public string ArmorLayerChest { get; set; } // layer_1
        public string ArmorLayerLegs { get; set; }  // layer_2

        // Usually the equipments.* id, e.g. "bronze_armor"
        public string ArmorSetId { get; set; } = string.Empty;

        // Hex color tint for recoloring vanilla-based icons, e.g. "FFE3E3"
        public string? RecolorTint { get; set; }

        // Vanilla texture info (for recolor path)
        public bool UsesVanillaTexture { get; set; }   // true if graphics/resource uses minecraft: texture
        public string? VanillaTextureId { get; set; }  // e.g. "minecraft:item/iron_helmet.png"

        // Per-slot texture map for the 3D helmet model (absolute file paths), same idea
        // as CustomItem.ModelTexturePaths. Only used when Is3DHelmet == true.
        public Dictionary<string, string> ModelTexturePaths { get; }
            = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
    }
}