using System.Collections.Generic;

namespace BedrockAdder.Library
{
    internal class CustomBlock
    {
        public bool Is3D { get; set; } = false; // Is the custom block a 3D model?
        public string BlockNamespace { get; set; }
        public string BlockItemID { get; set; } // The held item ID of the block
        public string ModelPath { get; set; } // Absolute path of the Java model
        public string TexturePath { get; set; } // Shared block/item texture (absolute)
        public string? IconPath { get; set; } // Optional icon override for inventory
        public string Material { get; set; } // Material for the held item
        public string PlacedBlockType { get; set; } // The block type when placed in the world such as mushroom block, noteblock, chorus fruit or string
        public int? CustomModelData { get; set; } // Optional CMD

        // Does the block use per-face textures?
        public bool PerFaceTexture { get; set; } = false;

        // Per-face texture paths (absolute) <side, texture path>
        public Dictionary<string, string> FaceTexturePaths { get; } = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

        // Logical asset IDs for model textures (works for IA + vanilla).
        // Example values:
        //   "0" -> "assets/workbenches/textures/amethonium_anvil/amethonium_anvil.png"
        //   "0" -> "assets/minecraft/textures/block/smithing_table_top.png"
        public Dictionary<string, string> ModelTextureAssets { get; } = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

        // Absolute PNG paths for model textures we can resolve *now* (typically IA contents).
        public Dictionary<string, string> ModelTexturePaths { get; } = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
    }
}