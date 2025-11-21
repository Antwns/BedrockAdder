using BedrockAdder.Managers;
using System;
using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace BedrockAdder.ConsoleWorker
{
    internal class Write
    {
        internal static void Line(string messageType, string text)
        {
            string prefix;
            SolidColorBrush prefixColor;

            switch (messageType.ToLower())
            {
                case "warn":
                case "warning":
                    prefix = "[WARNING] ";
                    prefixColor = Brushes.Yellow;
                    break;
                case "error":
                    prefix = "[ERROR] ";
                    prefixColor = Brushes.Red;
                    break;
                default:
                    prefix = "[INFO] ";
                    prefixColor = Brushes.DeepSkyBlue;
                    break;
            }
            App.Current.Dispatcher.Invoke(() =>
            {
                var paragraph = new Paragraph { Margin = new Thickness(0) };

                paragraph.Inlines.Add(new Run(prefix) { Foreground = prefixColor });
                paragraph.Inlines.Add(new Run(text));

                WindowManager.Main.ConversionLogTextBox.Document.Blocks.Add(paragraph);
                WindowManager.Main.ConversionLogTextBox.ScrollToEnd();
            });

            using (StreamWriter logWriter = File.AppendText(Environment.CurrentDirectory + "\\log.txt"))
            {
                logWriter.WriteLine(prefix + text);
            }
            if (messageType == "error")
            {
                using (StreamWriter errorWriter = File.AppendText(Environment.CurrentDirectory + "\\error_log.txt"))
                {
                    errorWriter.WriteLine(prefix + text);
                }
            }
            if (messageType == "warn" || messageType == "warning")
            {
                using (StreamWriter warningWriter = File.AppendText(Environment.CurrentDirectory + "\\warning_log.txt"))
                {
                    warningWriter.WriteLine(prefix + text);
                }
            }
        }
    }
}