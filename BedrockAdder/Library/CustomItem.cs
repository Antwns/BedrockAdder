using System.Collections.Generic;

internal class CustomItem
{
    public bool Is3D { get; set; }              // True if 3D model
    public string ModelPath { get; set; }       // Absolute path to Java model file (may be empty for 2D)
    public string TexturePath { get; set; }     // Absolute IA texture path for non-vanilla
    public string ItemID { get; set; }          // ItemsAdder ID
    public string ItemNamespace { get; set; }   // Namespace ("materials", "tools", etc.)
    public string? IconPath { get; set; }       // 2D icon path (usually same as TexturePath)
    public string Material { get; set; }        // Java material like "minecraft:stick"
    public int? CustomModelData { get; set; }   // Optional

    // Recolor info (2D only)
    public string? RecolorTint { get; set; }    // e.g. "010002"

    // Vanilla texture info
    public bool UsesVanillaTexture { get; set; }   // true if graphics/resource uses minecraft: texture
    public string? VanillaTextureId { get; set; }  // e.g. "minecraft:item/gold_nugget.png"

    // Per-slot texture map for 3D rendering/building (absolute file paths)
    public Dictionary<string, string> ModelTexturePaths { get; } = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

    // 🔹 NEW: per-state variants (bow pull, shield blocking, rod cast, etc.)
    // Keys are logical IA state names: "pulling_0", "pulling_1", "pulling_2", "blocking", "cast", "arrow", "rocket", ...
    // Values are absolute file paths, same style as ModelPath/TexturePath.
    public Dictionary<string, string> StateModelPaths { get; } = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> StateTexturePaths { get; } = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
}