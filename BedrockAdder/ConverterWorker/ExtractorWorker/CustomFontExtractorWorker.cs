using BedrockAdder.FileWorker;
using BedrockAdder.Library;
using System;
using System.IO;
using YamlDotNet.RepresentationModel;

namespace BedrockAdder.ExtractorWorker.ConverterWorker
{
    internal static class CustomFontExtractorWorker
    {
        internal static void ExtractCustomFontsFromPaths(string itemsAdderRoot, string selectedVersion)
        {
            ConsoleWorker.Write.Line("info", "Fonts: extraction started. Paths=" + (Lists.CustomFontPaths?.Count ?? 0));

            int filesProcessed = 0;
            int glyphsAdded = 0;

            foreach (var filePath in Lists.CustomFontPaths)
            {
                filesProcessed++;
                try
                {
                    if (!File.Exists(filePath))
                    {
                        ConsoleWorker.Write.Line("warn", "Font file missing: " + filePath);
                        continue;
                    }

                    // NEW: detect if this entire YAML file is a GUI definitions file
                    bool isGuiFile = FontYamlParserWorker.DetectIsGuiFile(filePath);

                    using var reader = new StreamReader(filePath);
                    var yaml = new YamlStream();
                    yaml.Load(reader);

                    if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode root)
                        continue;

                    string fontNamespace = FontYamlParserWorker.GetFileNamespaceOrDefault(root, "unknown");
                    if (!FontYamlParserWorker.TryGetFontImagesRoot(root, out var fontImagesRoot) || fontImagesRoot is null)
                        continue;

                    foreach (var kv in fontImagesRoot.Children)
                    {
                        if (kv.Key is not YamlScalarNode setKey || kv.Value is not YamlMappingNode setMap)
                            continue;

                        string fontSetId = setKey.Value ?? "default";

                        // scale_ratio at set level (default 1)
                        int scaleRatio = 1;
                        if (MainYamlParserWorker.TryGetScalar(setMap, "scale_ratio", out var sr) && int.TryParse(sr, out var srInt))
                            scaleRatio = srInt;

                        // Detect 'images' as a sequence
                        setMap.Children.TryGetValue(new YamlScalarNode("images"), out var imagesRaw);

                        if (imagesRaw is not YamlSequenceNode imagesSeq)
                        {
                            // Shorthand set: single 'path' + optional 'y_position'
                            if (MainYamlParserWorker.TryGetScalar(setMap, "path", out var setPath) && !string.IsNullOrWhiteSpace(setPath))
                            {
                                string textureRel = FontYamlParserWorker.NormalizeFontTextureRel(setPath);
                                int yPos = 0;
                                if (MainYamlParserWorker.TryGetScalar(setMap, "y_position", out var yStr) && int.TryParse(yStr, out var yVal))
                                    yPos = yVal;

                                string symbol = FontYamlParserWorker.TryGetUnicodeFromCache(itemsAdderRoot, fontNamespace, fontSetId, textureRel) ?? string.Empty;

                                var cfSet = new CustomFont
                                {
                                    FontImagePath = textureRel,
                                    FontID = fontSetId,
                                    FontNamespace = fontNamespace,
                                    ScaleRatio = scaleRatio,
                                    YPosition = yPos,
                                    FontSymbol = symbol,
                                    IsGui = isGuiFile // NEW
                                };

                                Lists.CustomFonts.Add(cfSet);
                                glyphsAdded++;

                                string abs = FontYamlParserWorker.BuildIaContentFontTextureAbs(itemsAdderRoot, fontNamespace, textureRel);
                                bool exists = File.Exists(abs);
                                ConsoleWorker.Write.Line(
                                    exists ? "info" : "warn",
                                    "Font set " + fontNamespace + ":" + fontSetId +
                                    " path=" + textureRel + " (exists=" + exists + ") scale=" + scaleRatio + " y=" + yPos +
                                    (string.IsNullOrEmpty(symbol) ? "" : " char='" + symbol + "'") +
                                    (isGuiFile ? " [GUI]" : "")
                                );
                            }

                            continue; // next set
                        }

                        // images[] form: per-glyph entries
                        foreach (var imgNode in imagesSeq.Children)
                        {
                            if (imgNode is not YamlMappingNode glyphMap) continue;

                            string? rawTex = FontYamlParserWorker.FirstNonEmpty(
                                MainYamlParserWorker.TryGetScalar(glyphMap, "file"),
                                MainYamlParserWorker.TryGetScalar(glyphMap, "image"),
                                MainYamlParserWorker.TryGetScalar(glyphMap, "texture"),
                                MainYamlParserWorker.TryGetScalar(glyphMap, "path")
                            );
                            if (string.IsNullOrWhiteSpace(rawTex)) continue;

                            string textureRel = FontYamlParserWorker.NormalizeFontTextureRel(rawTex!);
                            if (string.IsNullOrWhiteSpace(textureRel)) continue;

                            string charRaw = FontYamlParserWorker.FirstNonEmpty(
                                MainYamlParserWorker.TryGetScalar(glyphMap, "char"),
                                MainYamlParserWorker.TryGetScalar(glyphMap, "character")
                            ) ?? string.Empty;
                            string charDecoded = FontYamlParserWorker.DecodeYamlUnicodeChar(charRaw);

                            // Override with cache if present
                            string? cachedUnicode = FontYamlParserWorker.TryGetUnicodeFromCache(itemsAdderRoot, fontNamespace, fontSetId, textureRel);
                            if (!string.IsNullOrWhiteSpace(cachedUnicode))
                                charDecoded = cachedUnicode!;

                            int yPos = 0;
                            if (MainYamlParserWorker.TryGetScalar(glyphMap, "ascent", out var ascentStr) && int.TryParse(ascentStr, out var ascent))
                                yPos = ascent;

                            var cf = new CustomFont
                            {
                                FontImagePath = textureRel,
                                FontID = fontSetId,
                                FontNamespace = fontNamespace,
                                ScaleRatio = scaleRatio,
                                YPosition = yPos,
                                FontSymbol = charDecoded,
                                IsGui = isGuiFile // NEW
                            };

                            Lists.CustomFonts.Add(cf);
                            glyphsAdded++;

                            string absGlyph = FontYamlParserWorker.BuildIaContentFontTextureAbs(itemsAdderRoot, fontNamespace, textureRel);
                            bool existsGlyph = File.Exists(absGlyph);
                            ConsoleWorker.Write.Line(
                                existsGlyph ? "info" : "warn",
                                "Font glyph " + fontNamespace + ":" + fontSetId +
                                " char='" + charDecoded + "' tex=" + textureRel + " (exists=" + existsGlyph + ") scale=" + scaleRatio + " y=" + yPos +
                                (isGuiFile ? " [GUI]" : "")
                            );
                        }
                    }
                }
                catch (Exception ex)
                {
                    ConsoleWorker.Write.Line("warn", "Font parse failed for " + filePath + ": " + ex.Message);
                }
            }

            ConsoleWorker.Write.Line("info", "Fonts: extraction finished. Files=" + filesProcessed + " Glyphs=" + glyphsAdded);
        }
    }
}