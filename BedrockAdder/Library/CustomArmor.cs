using System.Collections.Generic;

namespace BedrockAdder.Library
{
    internal class CustomArmor
    {
        public string ArmorNamespace { get; set; }
        public string ArmorID { get; set; }
        public string Slot { get; set; }// "helmet", "chestplate", etc.
        public string Material { get; set; } // Java item base (e.g. "leather_helmet")
        public string TexturePath { get; set; }// Armor texture for Bedrock layers
        public string? ModelPath { get; set; } // Optional: custom .json model, only supported by helmet
        public int? CustomModelData { get; set; } // Optional CMD if relevant
        public string? IconPath { get; set; } // Optional 2D inventory icon
        public string ArmorLayerChest { get; set; } // Needed to get the image for the chest layer(Worn)
        public string ArmorLayerLegs { get; set; } // Needed to get the image for the legs layer(Worn)`
        public string ArmorSetId { get; set; } = string.Empty; // usually the equipments.* id, e.g. "bronze_armor"
        public string? RecolorTint { get; set; } // Hex color tint for recoloring, e.g. "FFE3E3"
        public bool UsesVanillaTexture { get; set; }   // true if graphics/resource uses minecraft: texture
        public string? VanillaTextureId { get; set; }  // e.g. "minecraft:item/gold_nugget.png"
        public Dictionary<string, string> ModelTexturePaths { get; } = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
    }
}
