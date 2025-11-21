using System;
using System.Collections.Generic;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading.Tasks;
using CefSharp;
using CefSharp.OffScreen;
using BedrockAdder.ConverterWorker.ObjectWorker;

namespace BedrockAdder.Renderer
{
    internal sealed class CefOffscreenIconRenderer : IModelIconRenderer, IDisposable
    {
        private static bool _cefInit;
        private bool _disposed;

        private readonly int _size;
        private readonly bool _transparent;
        private readonly string _renderHtmlAbs;

        public CefOffscreenIconRenderer(int size, bool transparent, string renderHtmlAbsolutePath)
        {
            _size = size;
            _transparent = transparent;
            _renderHtmlAbs = renderHtmlAbsolutePath;
            EnsureCefInitialized();
        }

        // Public sync entry – internally runs async logic on a worker thread
        public bool TryRenderIcon(string javaModelPath, IReadOnlyDictionary<string, string> textureSlotsAbs, string iconPngAbs)
        {
            try
            {
                return Task.Run(() => TryRenderIconInternalAsync(javaModelPath, textureSlotsAbs, iconPngAbs)).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                ConsoleWorker.Write.Line("error", "CefOffscreenIconRenderer fatal error: " + ex);
                return false;
            }
        }

        private async Task<bool> TryRenderIconInternalAsync(
            string javaModelPath,
            IReadOnlyDictionary<string, string> textureSlotsAbs,
            string iconPngAbs)
        {
            if (!File.Exists(_renderHtmlAbs))
            {
                ConsoleWorker.Write.Line("error", "render.html not found: " + _renderHtmlAbs);
                return false;
            }

            if (string.IsNullOrWhiteSpace(javaModelPath))
            {
                ConsoleWorker.Write.Line("error", "Model path is null or empty.");
                return false;
            }

            string modelFullPath = Path.GetFullPath(javaModelPath);
            ConsoleWorker.Write.Line("info", "CEF model path resolved to: " + modelFullPath);

            if (!File.Exists(modelFullPath))
            {
                ConsoleWorker.Write.Line("error", "Model JSON file does not exist: " + modelFullPath);
                return false;
            }

            if (!modelFullPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                ConsoleWorker.Write.Line("warn", "Model path is not a .json file: " + modelFullPath);
            }

            // --- Read the model JSON and send it inline as base64 ---
            string modelJson;
            try
            {
                modelJson = File.ReadAllText(modelFullPath);
            }
            catch (Exception ex)
            {
                ConsoleWorker.Write.Line("error", "Failed to read model JSON: " + ex.Message);
                return false;
            }

            string modelJsonB64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(modelJson));

            // -------------------------------------------------------
            // Decide if this looks like a cuboid-style block model
            // (i.e., has the classic 6 face slots: north/south/east/west/up/down).
            // We will only rotate textures for these, so items remain untouched.
            // -------------------------------------------------------
            var normalizedSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in textureSlotsAbs)
            {
                string key = kv.Key ?? string.Empty;
                if (key.StartsWith("#"))
                {
                    key = key.Substring(1);
                }

                if (!string.IsNullOrWhiteSpace(key))
                {
                    normalizedSlots.Add(key);
                }
            }

            bool looksLikeCuboid =
                normalizedSlots.Contains("north") &&
                normalizedSlots.Contains("south") &&
                normalizedSlots.Contains("east") &&
                normalizedSlots.Contains("west");
            // (up/down not strictly required, but usually present)

            if (looksLikeCuboid)
            {
                ConsoleWorker.Write.Line("info", "Model detected as cuboid-like (has north/south/east/west slots). North texture will be rotated 180° for icon render.");
            }

            // Build texture map for JS (slot -> file:// URL)
            var texMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            bool anyTextureExists = false;

            foreach (var kv in textureSlotsAbs)
            {
                string rawSlotName = kv.Key ?? "(null-slot)";
                string slotName = rawSlotName;

                if (slotName.StartsWith("#"))
                {
                    slotName = slotName.Substring(1);
                }

                string texPath = kv.Value;

                if (!string.IsNullOrWhiteSpace(texPath))
                {
                    string fullTexPath = Path.GetFullPath(texPath);
                    if (File.Exists(fullTexPath))
                    {
                        string pathForRenderer = fullTexPath;

                        // For cuboid-like models, rotate the NORTH face texture 180° for icon rendering.
                        if (looksLikeCuboid && slotName.Equals("north", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                pathForRenderer = CreateRotatedTextureCopy(fullTexPath, 180, "north180");
                                ConsoleWorker.Write.Line("info", "Rotated north texture 180° for icon: " + pathForRenderer);
                            }
                            catch (Exception ex)
                            {
                                ConsoleWorker.Write.Line("warn", "Failed to rotate north texture '" + fullTexPath + "': " + ex.Message + " – falling back to original.");
                                pathForRenderer = fullTexPath;
                            }
                        }

                        texMap[slotName] = new Uri(pathForRenderer).AbsoluteUri;
                        anyTextureExists = true;
                        ConsoleWorker.Write.Line("info", "Texture for slot '" + slotName + "': " + pathForRenderer);
                    }
                    else
                    {
                        ConsoleWorker.Write.Line("error", "Texture for slot '" + slotName + "' does not exist: " + fullTexPath);
                    }
                }
                else
                {
                    ConsoleWorker.Write.Line("error", "Texture path for slot '" + slotName + "' is null/empty.");
                }
            }

            if (!anyTextureExists)
            {
                ConsoleWorker.Write.Line("error", "No existing textures found for model: " + modelFullPath + " – cannot render icon.");
                return false;
            }

            string texMapJson = Newtonsoft.Json.JsonConvert.SerializeObject(texMap);
            string texMapB64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(texMapJson));

            string modelFileUrl = new Uri(modelFullPath).AbsoluteUri;
            string htmlFileUrl = new Uri(Path.GetFullPath(_renderHtmlAbs)).AbsoluteUri;

            // NOTE:
            // - modelPath is URL-encoded (it may contain spaces etc).
            // - modelJsonB64 and texMapB64 are NOT passed through Uri.EscapeDataString,
            //   because they are already safe for a query string and encoding them can
            //   exceed Uri.EscapeDataString's internal length limits and throw.
            string url = htmlFileUrl
                + "?size=" + _size
                + "&transparent=" + (_transparent ? "1" : "0")
                + "&modelPath=" + Uri.EscapeDataString(modelFileUrl)
                + "&modelJson=" + modelJsonB64
                + "&texMap=" + texMapB64;

            // Avoid logging the entire massive URL; just log some summary info.
            ConsoleWorker.Write.Line(
                "info",
                "CEF loading render.html with params: size=" + _size +
                " transparent=" + (_transparent ? "1" : "0") +
                " modelJsonLen=" + modelJsonB64.Length +
                " texMapLen=" + texMapB64.Length
            );

            // Construct browser WITH the URL so it starts loading immediately
            using (var browser = new ChromiumWebBrowser(url))
            {
                browser.Size = new Size(_size, _size);

                // Wait for first load of render.html (and its query params)
                var initialResponse = await browser.WaitForInitialLoadAsync().ConfigureAwait(false);
                ConsoleWorker.Write.Line(
                    "info",
                    "CEF initial load: success=" + initialResponse.Success +
                    " httpStatus=" + initialResponse.HttpStatusCode +
                    " errorCode=" + initialResponse.ErrorCode
                );

                bool ready = await WaitForRenderDoneAsync(browser, TimeSpan.FromSeconds(12)).ConfigureAwait(false);
                if (!ready)
                {
                    ConsoleWorker.Write.Line("warn", "Render timeout or page init flag not detected, capturing anyway.");
                }

                ConsoleWorker.Write.Line(
                    "info",
                    "Before screenshot: IsBrowserInitialized=" + browser.IsBrowserInitialized +
                    " CanExecuteJavascriptInMainFrame=" + browser.CanExecuteJavascriptInMainFrame
                );

                // Capture screenshot as PNG bytes
                var screenshot = await browser.CaptureScreenshotAsync().ConfigureAwait(false);
                if (screenshot == null || screenshot.Length == 0)
                {
                    ConsoleWorker.Write.Line("error", "CaptureScreenshotAsync returned no data (null or empty buffer).");
                    return false;
                }

                // The HTML/WebGL renderer now outputs correctly oriented images,
                // so we no longer flip the PNG vertically here.
                byte[] pngBytes = screenshot;

                try
                {
                    string? iconDir = Path.GetDirectoryName(iconPngAbs);
                    if (string.IsNullOrWhiteSpace(iconDir))
                    {
                        iconDir = AppContext.BaseDirectory;
                    }

                    Directory.CreateDirectory(iconDir);
                    File.WriteAllBytes(iconPngAbs, pngBytes);

                    bool exists = File.Exists(iconPngAbs);
                    ConsoleWorker.Write.Line("info", "Cef icon render → " + iconPngAbs + " (exists=" + exists + ")");
                    return exists;
                }
                catch (Exception ex)
                {
                    ConsoleWorker.Write.Line("error", "Failed to write icon PNG '" + iconPngAbs + "': " + ex.Message);
                    return false;
                }
            }
        }

        /// <summary>
        /// Creates a rotated copy (in %TEMP%/BedrockAdderIconTex) of the source texture.
        /// Returns the absolute path of the rotated PNG.
        /// </summary>
        private static string CreateRotatedTextureCopy(string srcPath, int degrees, string suffix)
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "BedrockAdderIconTex");
            Directory.CreateDirectory(tempRoot);

            string baseName = Path.GetFileNameWithoutExtension(srcPath);
            string fileName = baseName + "_" + suffix + ".png";
            string destPath = Path.Combine(tempRoot, fileName);

            // If the rotated copy already exists and is newer than source, reuse it.
            if (File.Exists(destPath))
            {
                DateTime srcTime = File.GetLastWriteTimeUtc(srcPath);
                DateTime dstTime = File.GetLastWriteTimeUtc(destPath);
                if (dstTime >= srcTime)
                {
                    return destPath;
                }
            }

            using (var bmp = new Bitmap(srcPath))
            {
                RotateFlipType rotateFlipType = RotateFlipType.RotateNoneFlipNone;
                int normalized = ((degrees % 360) + 360) % 360;
                switch (normalized)
                {
                    case 90:
                        rotateFlipType = RotateFlipType.Rotate90FlipNone;
                        break;
                    case 180:
                        rotateFlipType = RotateFlipType.Rotate180FlipNone;
                        break;
                    case 270:
                        rotateFlipType = RotateFlipType.Rotate270FlipNone;
                        break;
                    default:
                        rotateFlipType = RotateFlipType.RotateNoneFlipNone;
                        break;
                }

                if (rotateFlipType != RotateFlipType.RotateNoneFlipNone)
                {
                    bmp.RotateFlip(rotateFlipType);
                }

                bmp.Save(destPath, ImageFormat.Png);
            }

            return destPath;
        }

        private static void EnsureCefInitialized()
        {
            if (_cefInit) return;

            var settings = new CefSettings
            {
                WindowlessRenderingEnabled = true,
                PersistSessionCookies = false,
                LogSeverity = LogSeverity.Disable
            };

            // Allow local file textures
            settings.CefCommandLineArgs.Add("allow-file-access-from-files", "1");
            settings.CefCommandLineArgs.Add("disable-gpu", "1");
            settings.CefCommandLineArgs.Add("disable-gpu-compositing", "1");

            Cef.Initialize(settings, performDependencyCheck: true, browserProcessHandler: null);
            _cefInit = true;
        }

        private static async Task<bool> WaitForRenderDoneAsync(ChromiumWebBrowser browser, TimeSpan timeout)
        {
            var start = DateTime.UtcNow;

            while (DateTime.UtcNow - start < timeout)
            {
                if (!browser.IsBrowserInitialized || browser.GetBrowser() == null)
                {
                    await Task.Delay(50).ConfigureAwait(false);
                    continue;
                }

                if (!browser.CanExecuteJavascriptInMainFrame)
                {
                    await Task.Delay(50).ConfigureAwait(false);
                    continue;
                }

                const string script = @"
                    (function() {
                        if (window.renderDone === true) return true;
                        if (document && document.readyState === 'complete') return true;
                        return false;
                    })()
                ";

                var resp = await browser.EvaluateScriptAsync(script).ConfigureAwait(false);
                if (resp?.Success == true && resp.Result is bool b && b)
                {
                    ConsoleWorker.Write.Line("info", "CEF page reported renderDone/readyState complete.");
                    return true;
                }

                await Task.Delay(50).ConfigureAwait(false);
            }

            return false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}