using System;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace CostumeManager.Views
{

    public sealed partial class SettingsPage : Page
    {
        static string Dir => AppContext.BaseDirectory;
        static string GameCfg => Path.Combine(Dir, "gamedir.txt");
        static string ServerCfg => Path.Combine(Dir, "serverdir.txt");

        bool _loading;

        public SettingsPage()
        {
            InitializeComponent();
            Load();
        }

        void Load()
        {
            _loading = true;
            try
            {

                if (File.Exists(ServerCfg)) TxtServerDir.Text = File.ReadAllText(ServerCfg).Trim();
                if (File.Exists(GameCfg)) TxtGameDir.Text = File.ReadAllText(GameCfg).Trim();
            }
            catch {  }
            finally { _loading = false; }

            Validate();
        }

        void GameDir_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            Save(GameCfg, TxtGameDir.Text);
            Validate();
        }

        void ServerDir_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            Save(ServerCfg, TxtServerDir.Text);
        }

        static void Save(string path, string value)
        {
            try { File.WriteAllText(path, (value ?? "").Trim()); } catch { }
        }

        void Validate()
        {
            string dir = (TxtGameDir.Text ?? "").Trim();
            if (dir.Length == 0)
            {
                BarGame.IsOpen = false;
                TxtResolved.Text = "";
                return;
            }

            if (CostumeManager.Core.GamePaths.LooksLikeGameFolder(dir))
            {
                BarGame.Severity = InfoBarSeverity.Success;
                BarGame.Message = "Looks like a Marvel Heroes install.";
                BarGame.IsOpen = true;

                var (cooked, manifest, bin) = CostumeManager.Core.GamePaths.Resolve(dir);
                TxtResolved.Text =
                    $"Resolved paths:\n  UPKs      {cooked}\n  manifest  {manifest}\n  config    {bin}";
            }
            else
            {
                BarGame.Severity = InfoBarSeverity.Warning;
                BarGame.Message = "That does not look like a Marvel Heroes install folder.";
                BarGame.IsOpen = true;
                TxtResolved.Text = "";
            }
        }

        async void BrowseGame_Click(object sender, RoutedEventArgs e)
        {
            string picked = await PickFolderAsync("Pick the Marvel Heroes install folder");
            if (picked != null) TxtGameDir.Text = picked;
        }

        async void BrowseServer_Click(object sender, RoutedEventArgs e)
        {
            string picked = await PickFolderAsync("Pick the server folder to write ServerCostumes.json into");
            if (picked != null) TxtServerDir.Text = picked;
        }

        static async System.Threading.Tasks.Task<string> PickFolderAsync(string title)
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
            picker.CommitButtonText = title;

            Window w = App.MainWindowRef;
            if (w == null) return null;
            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(w);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            StorageFolder folder = await picker.PickSingleFolderAsync();
            return folder?.Path;
        }
    }
}
