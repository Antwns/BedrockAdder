using System;
using System.IO;
using BedrockAdder.Library;

namespace BedrockAdder.ConverterWorker.ObjectWorker
{
    internal static class CustomRecolorerWorker
    {
        /// <summary>
        /// Recolors a non-vanilla ItemsAdder texture using the tint from CustomItem.RecolorTint.
        /// Uses CustomItem.TexturePath as the base PNG and writes the tinted result to outputPngAbs.
        /// </summary>
        internal static bool TryBuildRecoloredCustomTexture(CustomItem item, string outputPngAbs, out string? error)
        {
            error = null;

            if (item == null)
            {
                error = "CustomRecolorerWorker: item is null";
                return false;
            }

            if (string.IsNullOrWhiteSpace(item.RecolorTint))
            {
                error = "CustomRecolorerWorker: item has no RecolorTint";
                return false;
            }

            if (string.IsNullOrWhiteSpace(item.TexturePath) || !File.Exists(item.TexturePath))
            {
                error = "CustomRecolorerWorker: base TexturePath is missing or file not found: " + item.TexturePath;
                return false;
            }

            if (!VanillaRecolorerWorker.TryParseTint(item.RecolorTint, out var tint))
            {
                error = "CustomRecolorerWorker: failed to parse tint " + item.RecolorTint;
                return false;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outputPngAbs) ?? ".");
                VanillaRecolorerWorker.ApplyMultiplyTintExternal(item.TexturePath, outputPngAbs, tint);

                ConsoleWorker.Write.Line(
                    "info",
                    item.ItemNamespace + ":" + item.ItemID +
                    " recolored custom texture → " + outputPngAbs
                );

                return true;
            }
            catch (Exception ex)
            {
                error = "CustomRecolorerWorker: exception while recoloring: " + ex.Message;
                ConsoleWorker.Write.Line(
                    "warn",
                    item.ItemNamespace + ":" + item.ItemID +
                    " failed to recolor custom texture: " + ex.Message
                );
                return false;
            }
        }
    }
}