using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using WinForge.Core;
using WinForge.FileTools;

namespace WinForge.ClipboardLine;

internal sealed partial class ClipboardShelfWindow
{
    private void Settings()
    {
        var dialog = new ClipboardSettingsWindow(this, _library.Preferences,
            (_context as IFeatureUiContext)?.FeatureUi);
        dialog.ShowDialog();
    }

    private sealed class ClipboardSettingsWindow : Window
    {
        private readonly ClipboardShelfWindow shelf;
        private readonly IFeatureUiSurface uiSurface;
        private ClipPreferences draft;
        private string shortcutsText;
        private string status = "Değişiklikler otomatik kaydedilir.";
        private readonly AutomaticSettingsSave autosave;

        public ClipboardSettingsWindow(ClipboardShelfWindow shelf, ClipPreferences preferences,
            IFeatureUiService? featureUi)
        {
            this.shelf = shelf;
            autosave = new(() => { try { Save(); } catch (Exception error) { status = error.Message; PushState(); } });
            draft = preferences;
            shortcutsText = string.Join(Environment.NewLine,
                ClipboardKeymap.Defaults.Select(entry =>
                    entry.Key + "=" + preferences.LocalShortcuts.GetValueOrDefault(entry.Key, entry.Value)));
            Owner = shelf;
            Title = "ClipBoardLine ayarları";
            Width = 720;
            Height = 780;
            MinWidth = 600;
            MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            uiSurface = featureUi?.CreateSurface("clipboard-settings")
                ?? throw new InvalidOperationException("Pano ayarları için React arayüzü gerekli.");
            uiSurface.MessageReceived += OnUiMessage;
            Content = uiSurface.Element;
            Closed += (_, _) => { autosave.Dispose(); uiSurface.Dispose(); };
            PushState();
        }

        private void OnUiMessage(string command, JsonElement payload)
        {
            try
            {
                switch (command)
                {
                    case "clipboard-settings.close": DialogResult = false; break;
                    case "clipboard-settings.toggle": UpdateToggle(payload); break;
                    case "clipboard-settings.select": UpdateSelection(payload); break;
                    case "clipboard-settings.shortcuts":
                        shortcutsText = payload.GetProperty("value").GetString() ?? "";
                        PushState();
                        break;
                    case "clipboard-settings.folder-add": AddFolder(); break;
                    case "clipboard-settings.folder-remove": RemoveFolder(payload); break;
                    case "clipboard-settings.save": Save(); break;
                }
                if (command is "clipboard-settings.toggle" or "clipboard-settings.select" or "clipboard-settings.shortcuts" or "clipboard-settings.folder-add" or "clipboard-settings.folder-remove") autosave.Schedule();
            }
            catch (Exception error)
            {
                status = error.Message;
                PushState();
            }
        }

        private void UpdateToggle(JsonElement payload)
        {
            var key = payload.GetProperty("key").GetString();
            var value = payload.GetProperty("value").GetBoolean();
            draft = key switch
            {
                "captureText" => draft with { CaptureText = value },
                "captureImages" => draft with { CaptureImages = value },
                "captureLinks" => draft with { CaptureLinks = value },
                "fetchLinkMetadata" => draft with { FetchLinkMetadata = value },
                "indexImages" => draft with { IndexImages = value },
                "showImages" => draft with { ShowImages = value },
                "showTexts" => draft with { ShowTexts = value },
                "showLinks" => draft with { ShowLinks = value },
                "listView" => draft with { ListView = value },
                "sounds" => draft with { Sounds = value },
                _ => draft
            };
            PushState();
        }

        private void UpdateSelection(JsonElement payload)
        {
            var key = payload.GetProperty("key").GetString();
            var value = payload.GetProperty("value");
            draft = key switch
            {
                "imageRetentionDays" => draft with { ImageRetentionDays = value.GetInt32() },
                "textRetentionDays" => draft with { TextRetentionDays = value.GetInt32() },
                "linkRetentionDays" => draft with { LinkRetentionDays = value.GetInt32() },
                "cardSize" => draft with { CardSize = value.GetString() ?? "medium" },
                _ => draft
            };
            PushState();
        }

        private void AddFolder()
        {
            if (draft.WatchFolders.Length >= 3) return;
            var picker = new OpenFolderDialog { Title = "Pano için izlenecek klasörü seçin" };
            if (picker.ShowDialog(this) != true || draft.WatchFolders.Contains(picker.FolderName,
                    StringComparer.OrdinalIgnoreCase)) return;
            draft = draft with { WatchFolders = [.. draft.WatchFolders, picker.FolderName] };
            PushState();
        }

        private void RemoveFolder(JsonElement payload)
        {
            var index = payload.GetProperty("index").GetInt32();
            if (index < 0 || index >= draft.WatchFolders.Length) return;
            draft = draft with { WatchFolders = draft.WatchFolders.Where((_, current) => current != index).ToArray() };
            PushState();
        }

        private void Save()
        {
            draft = draft with
            {
                LocalShortcuts = new Dictionary<string, string>(
                    ClipboardKeymap.ParseOverrides(shortcutsText), StringComparer.Ordinal)
            };
            shelf._library.SetPreferences(draft);
            shelf._capture.RefreshWatchers();
            shelf._library.Expire();
            shelf.ResetAndRender();
            status = "Değişiklikler otomatik kaydedildi.";
            PushState();
        }

        private void PushState() => uiSurface.SetState(new
        {
            captureText = draft.CaptureText,
            captureImages = draft.CaptureImages,
            captureLinks = draft.CaptureLinks,
            fetchLinkMetadata = draft.FetchLinkMetadata,
            indexImages = draft.IndexImages,
            showImages = draft.ShowImages,
            showTexts = draft.ShowTexts,
            showLinks = draft.ShowLinks,
            listView = draft.ListView,
            sounds = draft.Sounds,
            imageRetentionDays = draft.ImageRetentionDays,
            textRetentionDays = draft.TextRetentionDays,
            linkRetentionDays = draft.LinkRetentionDays,
            cardSize = draft.CardSize,
            watchFolders = draft.WatchFolders,
            shortcuts = shortcutsText,
            status
        });
    }
}
