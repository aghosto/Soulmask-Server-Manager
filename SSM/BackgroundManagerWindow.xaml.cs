using Microsoft.Win32;
using ModernWpf.Controls;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;

namespace SoulmaskServerManager
{
    public partial class BackgroundManagerWindow : Window
    {
        public static readonly string BackgroundsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Backgrounds");
        private readonly ObservableCollection<ImageItem> _images = new();
        private bool _initialized;

        public BackgroundManagerWindow()
        {
            InitializeComponent();
            ImageListBox.ItemsSource = _images;
            LoadImages();
            Loaded += BackgroundManagerWindow_Loaded;
        }

        private void BackgroundManagerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var mainWindow = Application.Current.MainWindow as MainWindow;
            if (mainWindow == null) return;
            OpacitySlider.Value = mainWindow.SsmSettings.AppSettings.WallpaperOpacity;
            OpacityValueText.Text = $"{(mainWindow.SsmSettings.AppSettings.WallpaperOpacity * 100):F0}%";
            _initialized = true;
        }

        private void LoadImages()
        {
            _images.Clear();
            if (!Directory.Exists(BackgroundsDir))
            {
                Directory.CreateDirectory(BackgroundsDir);
                return;
            }

            var supportedExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp" };
            foreach (var file in Directory.EnumerateFiles(BackgroundsDir)
                .Where(f => supportedExtensions.Contains(Path.GetExtension(f).ToLower()))
                .OrderBy(f => f))
            {
                _images.Add(new ImageItem
                {
                    Path = file,
                    FileName = Path.GetFileName(file)
                });
            }
        }

        private void AddImage_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp",
                Title = "选择要添加的背景图片",
                Multiselect = true
            };

            if (dialog.ShowDialog() != true) return;

            if (!Directory.Exists(BackgroundsDir))
                Directory.CreateDirectory(BackgroundsDir);

            foreach (var file in dialog.FileNames)
            {
                string fileName = Path.GetFileName(file);
                string dest = Path.Combine(BackgroundsDir, fileName);
                if (File.Exists(dest)) continue;
                File.Copy(file, dest);
            }

            LoadImages();
        }

        private void DeleteImage_Click(object sender, RoutedEventArgs e)
        {
            if (ImageListBox.SelectedItem is not ImageItem item) return;

            var result = MessageBox.Show($"确定要删除背景图\"{item.FileName}\"吗？", "确认删除",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            try
            {
                if (File.Exists(item.Path))
                    File.Delete(item.Path);
                _images.Remove(item);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"删除失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            LoadImages();
        }

        private void ImageListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            bool hasSelection = ImageListBox.SelectedItem != null;
            DeleteImageButton.IsEnabled = hasSelection;
            ApplyButton.IsEnabled = hasSelection;
        }

        private void ImageListBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ApplySelected();
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            ApplySelected();
        }

        private void ApplySelected()
        {
            if (ImageListBox.SelectedItem is not ImageItem item) return;

            var mainWindow = Application.Current.MainWindow as MainWindow;
            if (mainWindow == null) return;

            mainWindow.SsmSettings.AppSettings.WallpaperEnabled = true;
            mainWindow.SsmSettings.AppSettings.WallpaperPath = item.Path;
            mainWindow.UpdateWallpaper();
            MainSettings.Save(mainWindow.SsmSettings);
            Close();
        }

        private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_initialized) return;

            var mainWindow = Application.Current.MainWindow as MainWindow;
            if (mainWindow?.SsmSettings == null) return;

            double val = Math.Round(OpacitySlider.Value, 2);
            OpacityValueText.Text = $"{(val * 100):F0}%";
            mainWindow.SsmSettings.AppSettings.WallpaperOpacity = val;
            mainWindow.UpdateWallpaper();
            MainSettings.Save(mainWindow.SsmSettings);
        }

        private async void ResetWallpaper_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "确认重置",
                Content = "确定要重置背景图吗？\n这将清除当前设置的背景图并恢复默认界面。",
                PrimaryButtonText = "是",
                SecondaryButtonText = "否"
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

            var mainWindow = Application.Current.MainWindow as MainWindow;
            if (mainWindow == null) return;

            mainWindow.SsmSettings.AppSettings.WallpaperEnabled = false;
            mainWindow.SsmSettings.AppSettings.WallpaperPath = "";
            mainWindow.UpdateWallpaper();
            MainSettings.Save(mainWindow.SsmSettings);
            Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }

    public class ImageItem
    {
        public string Path { get; set; }
        public string FileName { get; set; }
        public BitmapImage ImageSource
        {
            get
            {
                if (string.IsNullOrEmpty(Path) || !File.Exists(Path))
                    return null;
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(Path);
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
        }
    }
}
