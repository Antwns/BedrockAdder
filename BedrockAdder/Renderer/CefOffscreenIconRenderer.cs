using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Drawing;
using System.Drawing.Imaging;
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

        public bool TryRenderIcon(string javaModelPath,
                                  IReadOnlyDictionary<string, string> textureSlotsAbs,
                                  string iconPngAbs)
        {
            try
            {
                if (!File.Exists(_renderHtmlAbs))
                {
                    ConsoleWorker.Write.Line("error", "render.html not found: " + _renderHtmlAbs);
                    return false;
                }
                if (string.IsNullOrWhiteSpace(javaModelPath) || !File.Exists(javaModelPath))
                {
                    ConsoleWorker.Write.Line("warn", "Model path missing: " + javaModelPath);
                    return false;
                }

                // Build texture map with file:/// URLs
                var texMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in textureSlotsAbs)
                {
                    if (!string.IsNullOrWhiteSpace(kv.Value) && File.Exists(kv.Value))
                    {
                        texMap[kv.Key] = new Uri(Path.GetFullPath(kv.Value)).AbsoluteUri;
                    }
                    else
                    {
                        ConsoleWorker.Write.Line("warn", "Missing texture for slot '" + kv.Key + "': " + kv.Value);
                    }
                }

                string texMapJson = Newtonsoft.Json.JsonConvert.SerializeObject(texMap);
                string texMapB64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(texMapJson));

                string modelFileUrl = new Uri(Path.GetFullPath(javaModelPath)).AbsoluteUri;
                string htmlFileUrl = new Uri(Path.GetFullPath(_renderHtmlAbs)).AbsoluteUri;

                string url = htmlFileUrl
                    + "?size=" + _size
                    + "&transparent=" + (_transparent ? "1" : "0")
                    + "&modelPath=" + Uri.EscapeDataString(modelFileUrl)
                    + "&texMap=" + Uri.EscapeDataString(texMapB64);

                using (var browser = new ChromiumWebBrowser())
                {
                    browser.Size = new System.Drawing.Size(_size, _size);
                    browser.BrowserInitialized += (s, e) => browser.Load(url);

                    // wait page signal
                    var okTask = WaitForRenderDone(browser, TimeSpan.FromSeconds(12));
                    okTask.Wait();
                    if (!okTask.Result)
                    {
                        ConsoleWorker.Write.Line("warn", "Render timeout or page init failed.");
                        return false;
                    }

                    // capture PNG (no ScreenshotFormat enum here)
                    var bmpTask = browser.ScreenshotAsync(ignoreExistingScreenshot: true);
                    bmpTask.Wait();
                    using (Bitmap bmp = bmpTask.Result)
                    {
                        if (bmp == null)
                        {
                            ConsoleWorker.Write.Line("warn", "No bitmap returned from ScreenshotAsync.");
                            return false;
                        }

                        Directory.CreateDirectory(Path.GetDirectoryName(iconPngAbs) ?? AppContext.BaseDirectory);
                        bmp.Save(iconPngAbs, ImageFormat.Png);
                    }

                    ConsoleWorker.Write.Line("info", "Cef render → " + iconPngAbs);
                    return File.Exists(iconPngAbs);
                }
            }
            catch (Exception ex)
            {
                ConsoleWorker.Write.Line("error", "CefOffscreenIconRenderer error: " + ex.Message);
                return false;
            }
        }

        private static void EnsureCefInitialized()
        {
            if (_cefInit) return;

            var settings = new CefSettings
            {
                WindowlessRenderingEnabled = true,
                PersistSessionCookies = false,
                // PersistUserPreferences — remove; not available on this version
                LogSeverity = LogSeverity.Disable
            };

            // allow local file textures
            settings.CefCommandLineArgs.Add("allow-file-access-from-files", "1");
            settings.CefCommandLineArgs.Add("disable-gpu", "1");
            settings.CefCommandLineArgs.Add("disable-gpu-compositing", "1");

            Cef.Initialize(settings, performDependencyCheck: true, browserProcessHandler: null);
            _cefInit = true;
        }

        private static async Task<bool> WaitForRenderDone(ChromiumWebBrowser browser, TimeSpan timeout)
        {
            var start = DateTime.UtcNow;
            while (DateTime.UtcNow - start < timeout)
            {
                if (!browser.IsBrowserInitialized)
                {
                    await Task.Delay(50);
                    continue;
                }

                var resp = await browser.EvaluateScriptAsync("window.renderDone === true");
                if (resp?.Success == true && resp.Result is bool b && b) return true;

                await Task.Delay(50);
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