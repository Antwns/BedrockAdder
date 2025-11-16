using System.Collections.Generic;

namespace BedrockAdder.Library
{
    internal enum Built3DKind
    {
        Item,
        Block,
        Furniture,
        Helmet
    }

    internal sealed class Built3DObject
    {
        public Built3DKind Kind { get; set; }

        public string Namespace { get; set; } = "";
        public string Id { get; set; } = ""; // logical id (itemsAdder id)

        public string BedrockIdentifier { get; set; } = "";  // e.g. ia:ns_id
        public string GeometryIdentifier { get; set; } = ""; // e.g. geometry.item_ns_id

        // Geometry
        public string GeometryJson { get; set; } = "";        // content of .geo.json
        public string GeometryOutRel { get; set; } = "";      // e.g. models/entity/ns_id.geo.json

        // Attachable (for in-hand/worn rendering)
        public string AttachableJson { get; set; } = "";      // content of attachable json
        public string AttachableOutRel { get; set; } = "";    // e.g. attachables/ns_id.json

        // Textures: copy plan (src abs → dest relative in RP)
        public List<(string SrcAbs, string DstRel)> TexturesToCopy { get; } = new();

        // Backwards-compat alias for older code (e.g. CustomBlockParserWorker)
        // so existing calls to obj.TextureCopies[...] or .Add(...) still work.
        public List<(string SrcAbs, string DstRel)> TextureCopies => TexturesToCopy;

        // 2D Icon (optional)
        public string? IconPngAbs { get; set; }               // absolute path to PNG (generated or provided)
        public string? IconAtlasRel { get; set; }             // suggested atlas path e.g. textures/items/ns/id.png

        // Notes for logs / diagnostics
        public List<string> Notes { get; } = new();
    }

    internal static class Built3DObjectNaming
    {
        public static string MakeIaId(string ns, string id)
        {
            return "ia:" + Sanitize(ns) + "_" + Sanitize(id);
        }

        public static string MakeGeoId(Built3DKind kind, string ns, string id)
        {
            // We keep a single naming convention for handheld visuals
            return "geometry.item_" + Sanitize(ns) + "_" + Sanitize(id);
        }

        public static string MakeGeoRel(string ns, string id)
        {
            return "models/entity/" + Sanitize(ns) + "_" + Sanitize(id) + ".geo.json";
        }

        public static string MakeAttachableRel(string ns, string id)
        {
            return "attachables/" + Sanitize(ns) + "_" + Sanitize(id) + ".json";
        }

        public static string MakeModelTextureRel(string ns, string fileName)
        {
            return "textures/models/" + Sanitize(ns) + "/" + fileName;
        }

        public static string MakeIconRel(string ns, string id)
        {
            return "textures/items/" + Sanitize(ns) + "/" + Sanitize(id) + ".png";
        }

        private static string Sanitize(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "unknown";
            var sb = new System.Text.StringBuilder();
            foreach (char ch in s.Trim())
            {
                if (char.IsLetterOrDigit(ch) || ch == '_') sb.Append(char.ToLowerInvariant(ch));
                else sb.Append('_');
            }
            return sb.ToString();
        }
    }
}