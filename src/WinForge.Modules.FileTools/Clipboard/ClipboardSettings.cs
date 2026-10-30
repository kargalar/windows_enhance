using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

using WinForge.FileTools;
namespace WinForge.ClipboardLine;

internal sealed partial class ClipboardShelfWindow
{
    private void Settings()
    {
        var dialog = new Window { Owner = this, Title = "ClipBoardLine ayarları", Width = 570, Height = 710, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(24) }; dialog.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var preferences = _library.Preferences;
        var toggles = new Dictionary<string, CheckBox>();
        void Toggle(string key, string label, bool value)
        {
            var toggle = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 6, 0, 6) }; toggles.Add(key, toggle); panel.Children.Add(toggle);
        }
        Toggle("text", "Kopyalanan metinleri topla", preferences.CaptureText);
        Toggle("images", "Kopyalanan görselleri topla", preferences.CaptureImages);
        Toggle("links", "Tek başına kopyalanan bağlantıları topla", preferences.CaptureLinks);
        Toggle("link-metadata", "Genel HTTPS bağlantılarının başlık ve küçük resimlerini al", preferences.FetchLinkMetadata);
        Toggle("ocr", "Görselleri yerel OCR ile indeksle", preferences.IndexImages);
        Toggle("show-images", "Görsel rafını göster", preferences.ShowImages);
        Toggle("show-texts", "Metin rafını göster", preferences.ShowTexts);
        Toggle("show-links", "Bağlantı rafını göster", preferences.ShowLinks);
        Toggle("list", "Kütüphanede liste görünümü", preferences.ListView);
        Toggle("sounds", "Kopyalama sesi", preferences.Sounds);
        var retention = new List<ComboBox>();
        foreach (var (name, value) in new[] { ("Görsel saklama süresi", preferences.ImageRetentionDays), ("Metin saklama süresi", preferences.TextRetentionDays), ("Bağlantı saklama süresi", preferences.LinkRetentionDays) })
        {
            panel.Children.Add(new TextBlock { Text = name + " (gün)", Margin = new Thickness(0, 8, 0, 3) });
            var picker = new ComboBox { ItemsSource = new[] { 1, 3, 7, 15, 30, value }.Distinct().Order().ToArray(), SelectedItem = value }; panel.Children.Add(picker); retention.Add(picker);
        }
        panel.Children.Add(new TextBlock { Text = "Kart boyutu", Margin = new Thickness(0, 8, 0, 3) });
        var size = new ComboBox { ItemsSource = new[] { "small", "medium", "large" }, SelectedItem = preferences.CardSize }; panel.Children.Add(size);
        panel.Children.Add(new TextBlock { Text = "İzlenen klasörler (en fazla 3)", Margin = new Thickness(0, 12, 0, 5) });
        var folders = new ListBox { Height = 90 }; var watched = preferences.WatchFolders.ToList(); folders.ItemsSource = watched.ToArray(); panel.Children.Add(folders);
        panel.Children.Add(ActionButton("Klasör ekle", () => { if (watched.Count >= 3) return; var picker = new OpenFolderDialog(); if (picker.ShowDialog(dialog) == true && !watched.Contains(picker.FolderName, StringComparer.OrdinalIgnoreCase)) { watched.Add(picker.FolderName); folders.ItemsSource = watched.ToArray(); } }));
        panel.Children.Add(ActionButton("Seçili klasörü kaldır", () => { if (folders.SelectedItem is string folder) { watched.Remove(folder); folders.ItemsSource = watched.ToArray(); } }));
        panel.Children.Add(new TextBlock { Text = "Raf ve kütüphane kısayolları · boş değer eylemi kapatır", Margin = new Thickness(0, 12, 0, 5), TextWrapping = TextWrapping.Wrap });
        var shortcutEditor = new TextBox
        {
            AcceptsReturn = true, Height = 210, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Text = string.Join(Environment.NewLine, ClipboardKeymap.Defaults.Select(entry => entry.Key + "=" + preferences.LocalShortcuts.GetValueOrDefault(entry.Key, entry.Value)))
        };
        panel.Children.Add(shortcutEditor);
        panel.Children.Add(new TextBlock { Text = "Sabitlenen, dosyalanan ve etiketlenen kayıtlar süre dolunca korunur. Kütüphane ve Türkçe/İngilizce OCR bu cihazda çalışır.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) });
        panel.Children.Add(ActionButton("Kaydet", () =>
        {
            try
            {
                bool On(string key) => toggles[key].IsChecked == true;
                var shortcuts = ClipboardKeymap.ParseOverrides(shortcutEditor.Text);
                _library.SetPreferences(preferences with
                {
                    CaptureText = On("text"), CaptureImages = On("images"), CaptureLinks = On("links"), FetchLinkMetadata = On("link-metadata"), IndexImages = On("ocr"), ShowImages = On("show-images"), ShowTexts = On("show-texts"), ShowLinks = On("show-links"), ListView = On("list"), Sounds = On("sounds"),
                    ImageRetentionDays = (int)retention[0].SelectedItem, TextRetentionDays = (int)retention[1].SelectedItem, LinkRetentionDays = (int)retention[2].SelectedItem, CardSize = (string)size.SelectedItem, WatchFolders = watched.ToArray(), LocalShortcuts = new Dictionary<string, string>(shortcuts, StringComparer.Ordinal)
                });
                _capture.RefreshWatchers(); _library.Expire(); dialog.Close(); ResetAndRender();
            }
            catch (Exception error) { MessageBox.Show(dialog, error.Message, "Ayarlar kaydedilemedi"); }
        }));
        dialog.ShowDialog();
    }
}
