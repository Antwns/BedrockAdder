using BedrockAdder.ConverterWorker.BuilderWorker;
using BedrockAdder.ExtractorWorker.ConverterWorker;
using BedrockAdder.FileWorker;
using BedrockAdder.Managers;
using System;
using System.Threading.Tasks;
using System.Windows;

namespace BedrockAdder.Library
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            WindowManager.Main = this;
        }

        private void ConvertButton_Click(object sender, RoutedEventArgs e)
        {
            ConvertButton.IsEnabled = false;
            string Version = VersionSelector.SelectedItem.ToString() ?? "None";
            string ItemsAdderDir = ItemsAdderPluginFolderTextBox.Text;
            string GeyserMappingsDir = GeyserMappingsFolderTextBox.Text;
            string GeyserPackDir = GeyserPackFolderTextBox.Text;
            Task.Run(() =>
            {
                PathValidator.CheckPaths(ItemsAdderDir, "IAFolder");
                PathValidator.CheckPaths(GeyserPackDir, "GeyserPackFolder");
                PathValidator.CheckPaths(GeyserMappingsDir, "GeyserMappingsFolder");
                if (Bools.ValidGeyserPackFolder == true && Bools.ValidGeyserMappingsFolder == true && Bools.ValidIAFolder == true)
                {
                    ConsoleWorker.Write.Line("info", "Deleting previous logs...");
                    try
                    {
                        System.IO.File.Delete(AppContext.BaseDirectory + "\\log.txt");
                        ConsoleWorker.Write.Line("info", "Previous log file deleted successfully.");
                    }
                    catch (Exception ex)
                    {
                        ConsoleWorker.Write.Line("warn", "Could not delete previous log file: " + ex.Message);
                    }
                    Task.Delay(4000).Wait();
                    ConsoleWorker.Write.Line("info", "All paths are valid ✅. Starting conversion... 🔨");
                    Task.Delay(4000).Wait();
                    ConsoleWorker.Write.Line("info", "Scanning YAML files...");
                    Task.Delay(4000).Wait();
                    LoadFiles.ScanYamlFiles(ItemsAdderDir);
                    Task.Delay(4000).Wait();
                    ConsoleWorker.Write.Line("info", "YAML file scan complete.");
                    Task.Delay(4000).Wait();
                    ConsoleWorker.Write.Line("info", "--------------------------------");
                    ConsoleWorker.Write.Line("info", "Extracting custom items...");
                    ConsoleWorker.Write.Line("info", "--------------------------------");
                    Task.Delay(4000).Wait();
                    CustomItemExtractorWorker.ExtractCustomItemsFromPaths(ItemsAdderDir, Version);
                    ConsoleWorker.Write.Line("info", "--------------------------------");
                    ConsoleWorker.Write.Line("info", "Finished extracting custom items!");
                    ConsoleWorker.Write.Line("info", "--------------------------------");
                    Task.Delay(4000).Wait();
                    ConsoleWorker.Write.Line("info", "--------------------------------");
                    ConsoleWorker.Write.Line("info", "Extracting custom blocks...");
                    ConsoleWorker.Write.Line("info", "--------------------------------");
                    Task.Delay(4000).Wait();
                    CustomBlockExtractorWorker.ExtractCustomBlocksFromPaths(ItemsAdderDir, Version);
                    ConsoleWorker.Write.Line("info", "--------------------------------");
                    ConsoleWorker.Write.Line("info", "Finished extracting custom blocks!");
                    ConsoleWorker.Write.Line("info", "--------------------------------");
                    Task.Delay(4000).Wait();
                    ConsoleWorker.Write.Line("info", "--------------------------------");
                    ConsoleWorker.Write.Line("info", "Extracting custom armors...");
                    ConsoleWorker.Write.Line("info", "--------------------------------");
                    Task.Delay(4000).Wait();
                    CustomArmorExtractorWorker.ExtractCustomArmorsFromPaths(ItemsAdderDir, Version);
                    ConsoleWorker.Write.Line("info", "--------------------------------");
                    ConsoleWorker.Write.Line("info", "Finished extracting custom armors!");
                    ConsoleWorker.Write.Line("info", "--------------------------------");
                    Task.Delay(4000).Wait();
                    ConsoleWorker.Write.Line("info", "--------------------------------");
                    ConsoleWorker.Write.Line("info", "Extracting custom furniture...");
                    ConsoleWorker.Write.Line("info", "--------------------------------");
                    Task.Delay(4000).Wait();
                    CustomFurnitureExtractorWorker.ExtractCustomFurnitureFromPaths(ItemsAdderDir, Version);
                    ConsoleWorker.Write.Line("info", "--------------------------------");
                    ConsoleWorker.Write.Line("info", "Finished extracting custom furniture!");
                    ConsoleWorker.Write.Line("info", "--------------------------------");
                    Task.Delay(4000).Wait();
                    ConsoleWorker.Write.Line("info", "--------------------------------");
                    ConsoleWorker.Write.Line("info", "Extracting custom fonts...");
                    ConsoleWorker.Write.Line("info", "--------------------------------");
                    Task.Delay(4000).Wait();
                    CustomFontExtractorWorker.ExtractCustomFontsFromPaths(ItemsAdderDir, Version);
                    ConsoleWorker.Write.Line("info", "--------------------------------");
                    ConsoleWorker.Write.Line("info", "Finished extracting custom fonts!");
                    ConsoleWorker.Write.Line("info", "--------------------------------");
                    Task.Delay(4000).Wait();
                    ConsoleWorker.Write.Line("info", "--------------------------------");
                    ConsoleWorker.Write.Line("info", "Extracting custom sounds...");
                    ConsoleWorker.Write.Line("info", "--------------------------------");
                    Task.Delay(4000).Wait();
                    CustomSoundExtractorWorker.ExtractCustomSoundsFromPaths(ItemsAdderDir);
                    ConsoleWorker.Write.Line("info", "--------------------------------");
                    ConsoleWorker.Write.Line("info", "Finished extracting custom sounds!");
                    ConsoleWorker.Write.Line("info", "--------------------------------");
                    Task.Delay(4000).Wait();
                    ConsoleWorker.Write.Line("info", "--------------------------------");
                    ConsoleWorker.Write.Line("info", "Generating base bedrock pack...");
                    ConsoleWorker.Write.Line("info", "--------------------------------");
                    PackSession currentSession = BedrockManager.BeginNewPackOrAbort(GeyserPackDir, "BedrockAdder", "Generated by BedrockAdder", "1.0.0");
                    if (currentSession == null)
                    {
                        ConsoleWorker.Write.Line("error", "Could not create Bedrock pack session. Aborting build.");
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            ConvertButton.IsEnabled = true;
                        });
                        return;
                    }
                    ConsoleWorker.Write.Line("info", "--------------------------------");
                    ConsoleWorker.Write.Line("info", "Finished generating base bedrock pack!");
                    ConsoleWorker.Write.Line("info", "--------------------------------");
                    Task.Delay(4000).Wait();
                    ConsoleWorker.Write.Line("info", "--------------------------------");
                    ConsoleWorker.Write.Line("info", "Generating bedrock items...");
                    ConsoleWorker.Write.Line("info", "--------------------------------");
                    CustomItemBuilderWorker.BuildCustomItems(currentSession, 128, ItemsAdderDir);
                    ConsoleWorker.Write.Line("info", "--------------------------------");
                    ConsoleWorker.Write.Line("info", "Finished generating bedrock items!");
                    ConsoleWorker.Write.Line("info", "--------------------------------");
                    Task.Delay(4000).Wait();
                    ConsoleWorker.Write.Line("info", "--------------------------------");
                    ConsoleWorker.Write.Line("info", "Generating bedrock blocks...");
                    ConsoleWorker.Write.Line("info", "--------------------------------");
                    Task.Delay(4000).Wait();
                    CustomBlockBuilderWorker.BuildCustomBlocks(currentSession, 128, ItemsAdderDir);
                    ConsoleWorker.Write.Line("info", "--------------------------------");
                    ConsoleWorker.Write.Line("info", "Finished generating bedrock blocks!");
                    ConsoleWorker.Write.Line("info", "--------------------------------");
                    Task.Delay(4000).Wait();
                }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    ConvertButton.IsEnabled = true;
                });
            });
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            VersionManager.GetMinecraftVersions(VersionSelector);
        }
    }
}