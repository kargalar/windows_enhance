using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using WinForge.ClipboardLine;

internal static class ClipboardLibraryChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        check(ClipboardShelfWindow.ShelfPageCapacity(400, "medium") == 2 && ClipboardShelfWindow.ShelfPageCapacity(400, "large") == 1, "Shelf pages fit the configured card size in available width.");
        check(ClipboardShelfWindow.ShelfPageCapacity(10000, "small") == 9 && ClipboardShelfWindow.ShelfPageCapacity(10, "small") == 1, "Shelf pages stay between one and nine cards.");

        var root = Path.Combine(Path.GetTempPath(), "WinForge-ClipboardChecks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using (var contract = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "text-identity.json"))))
            {
                var identityLibrary = new ClipboardLibrary(Path.Combine(root, "identity"));
                var seen = new HashSet<Guid>();
                foreach (var entry in contract.RootElement.GetProperty("cases").EnumerateArray())
                {
                    var copies = entry.GetProperty("copies").EnumerateArray().Select(value => value.GetString()!).ToArray();
                    var first = identityLibrary.AddText(copies[0]);
                    check(seen.Add(first.Id), "Distinct text identity: " + entry.GetProperty("id").GetString());
                    foreach (var copy in copies.Skip(1))
                    {
                        var again = identityLibrary.AddText(copy);
                        check(again.Id == first.Id && again.Content == copy, "Equivalent text keeps its identity and newest literal content.");
                    }
                }
            }
            var watchedFile = Path.Combine(root, "watched.txt");
            await using (var writer = new FileStream(watchedFile, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite))
            {
                await writer.WriteAsync(new byte[] { 1, 2, 3 }); await writer.FlushAsync();
                var waiting = ClipboardCapture.WaitForStableFileAsync(watchedFile, TimeSpan.FromMilliseconds(30), 50, CancellationToken.None);
                await Task.Delay(150);
                check(!waiting.IsCompleted, "Watched files are not imported while a writer still has them open.");
                await writer.DisposeAsync();
                check(await waiting, "A completed watched file becomes ready after its size and write time settle.");
            }
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                try { await ClipboardCapture.WaitForStableFileAsync(watchedFile, TimeSpan.FromMilliseconds(30), 50, cancelled.Token); check(false, "Watch wait must be cancellable."); }
                catch (OperationCanceledException) { check(true, "Watch wait stops on module cancellation."); }
            }
            var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            var library = new ClipboardLibrary(root, () => now);
            check(!library.Preferences.CaptureText && !library.Preferences.CaptureImages && library.Preferences.CaptureLinks, "Capture defaults preserve text and image opt-in.");
            Throws<ArgumentException>(() => library.SetPreferences(library.Preferences with { SidebarWidth = double.NaN }), check, "Non-finite sidebar widths are rejected.");
            Throws<ArgumentException>(() => library.SetPreferences(library.Preferences with { InspectorWidth = 1000 }), check, "Oversized inspector widths are rejected.");
            Throws<ArgumentException>(() => library.SetPreferences(library.Preferences with { ListColumnWidths = [20, 20] }), check, "Malformed list column widths are rejected.");
            check(ClipboardKeymap.Match(Key.S, ModifierKeys.None, library.Preferences.LocalShortcuts) == "raftan-kaldir" && ClipboardKeymap.Match(Key.S, ModifierKeys.Shift, library.Preferences.LocalShortcuts) == "ara" && ClipboardKeymap.Match(Key.S, ModifierKeys.Control, library.Preferences.LocalShortcuts) is null, "Plain, Shift and Control shortcuts are distinguished.");
            check(ClipboardKeymap.Match(Key.Enter, ModifierKeys.Control, library.Preferences.LocalShortcuts) == "yapistir" && ClipboardKeymap.Match(Key.E, ModifierKeys.Control | ModifierKeys.Shift, library.Preferences.LocalShortcuts) == "disa-aktar", "Paste and export shortcuts map to their corresponding actions.");
            check(ClipboardKeymap.Match(Key.I, ModifierKeys.Shift, library.Preferences.LocalShortcuts) == "raf-gorsel" && ClipboardKeymap.Match(Key.L, ModifierKeys.Shift, library.Preferences.LocalShortcuts) == "raf-baglanti", "Shelf visibility shortcuts are available without conflicting with library access.");
            check(ClipboardKeymap.Match(Key.D0, ModifierKeys.Control, library.Preferences.LocalShortcuts) == "tur-tumu" && ClipboardKeymap.Match(Key.D3, ModifierKeys.Control, library.Preferences.LocalShortcuts) == "tur-baglanti", "Library type filters expose the expected modified number shortcuts.");
            check(ClipboardKeymap.Match(Key.P, ModifierKeys.Control, library.Preferences.LocalShortcuts) == "sabitle" && ClipboardKeymap.Match(Key.F, ModifierKeys.None, library.Preferences.LocalShortcuts) == "sabit-filtre", "Pin and pinned-filter default aliases are recognized.");
            var remappedPin = ClipboardKeymap.ParseOverrides("sabitle=Q\nsabit-filtre=G");
            check(ClipboardKeymap.Match(Key.P, ModifierKeys.Control, remappedPin) is null && ClipboardKeymap.Match(Key.F, ModifierKeys.None, remappedPin) is null, "Remapping disables the corresponding default aliases.");
            var keymap = ClipboardKeymap.ParseOverrides("raftan-kaldir=\nonizle=Ctrl+Alt+Q");
            check(ClipboardKeymap.Match(Key.S, ModifierKeys.None, keymap) is null && ClipboardKeymap.Match(Key.Q, ModifierKeys.Control | ModifierKeys.Alt, keymap) == "onizle", "A remapped shortcut replaces the default and an empty value disables its action.");
            Throws<ArgumentException>(() => ClipboardKeymap.ParseOverrides("onizle=P"), check, "Conflicting shortcut assignments are rejected.");
            Throws<ArgumentException>(() => ClipboardKeymap.ParseOverrides("unknown=Q"), check, "Unknown shortcut actions are rejected.");
            var stamp = DateTimeOffset.UtcNow;
            var recentItem = new ClipItem(Guid.NewGuid(), ClipKind.Text, "recent", stamp, stamp);
            var savedItem = recentItem with { Id = Guid.NewGuid(), Saved = true };
            var organizedItem = recentItem with { Id = Guid.NewGuid(), Tags = ["tag"] };
            var scopeItems = new[] { recentItem, savedItem, organizedItem };
            check(ClipboardShelfWindow.ApplyLibraryScope(scopeItems, ClipboardLibraryScope.Recent).Select(item => item.Id).SequenceEqual([recentItem.Id]), "Recent scope excludes saved and organized items.");
            check(ClipboardShelfWindow.ApplyLibraryScope(scopeItems, ClipboardLibraryScope.Saved).Count == 2 && ClipboardShelfWindow.ApplyLibraryScope(scopeItems, ClipboardLibraryScope.Unorganized).Single().Id == savedItem.Id, "Saved and unorganized scopes include the correct items.");
            var tagsA = recentItem with { Id = Guid.NewGuid(), Title = "A", Tags = ["one", "two"] };
            var tagsB = recentItem with { Id = Guid.NewGuid(), Title = "B", Tags = ["one"] };
            check(ClipboardShelfWindow.FilterTags([tagsA, tagsB], "ONE, two").Single().Id == tagsA.Id, "Multiple tag filters use case-insensitive AND semantics.");
            check(ClipboardShelfWindow.SortLibraryResults([tagsA, tagsB], 3).Select(item => item.Title).SequenceEqual(["B", "A"]), "Library title descending sort is applied to actual results.");
            var copiedEarlier = tagsA with { LastCopiedAt = stamp.AddMinutes(-2) };
            var copiedLater = tagsB with { LastCopiedAt = stamp.AddMinutes(-1) };
            check(ClipboardShelfWindow.SortLibraryResults([copiedEarlier, copiedLater], 1).Select(item => item.Id).SequenceEqual([copiedLater.Id, copiedEarlier.Id]), "Last-copied sorting follows actual copy timestamps rather than edit times.");
            var text = library.AddText("İlk satır\r\nİkinci satır");
            var link = library.AddText("https://example.com/path");
            var copiedText = library.Items.Single(item => item.Kind == ClipKind.Text);
            library.MarkCopied([copiedText.Id]);
            check(library.Items.Single(item => item.Id == copiedText.Id).LastCopiedAt == now, "Successful library copies record their real last-copied timestamp.");
            check(link.Kind == ClipKind.Link && library.AddText("example https://example.com").Kind == ClipKind.Text, "Only standalone HTTP URLs are links.");
            var details = LinkMetadata.Parse("<title>Fallback</title><meta property='og:title' content='A &amp; B'><meta name='author' content='Example Author'><meta property='og:image' content='/image.png'>", new Uri(link.Content));
            check(details.Title == "A & B" && details.Author == "Example Author" && details.Thumbnail?.AbsoluteUri == "https://example.com/image.png", "Link metadata reads inert title, author and relative thumbnail.");
            library.SetLinkMetadata(link.Id, link.Content, details, null);
            check(library.Search("example author").Single().Id == link.Id && library.Items.Single(item => item.Id == link.Id).UpdatedAt == link.UpdatedAt, "Link metadata is searchable without extending retention.");
            check(library.MarkLinkMetadataPending(link.Id) && !library.Items.Single(item => item.Id == link.Id).LinkEnriched && library.Items.Single(item => item.Id == link.Id).LinkTitle == "A & B", "Refreshing link metadata marks enrichment pending while retaining the last known details.");
            library.SetLinkMetadata(link.Id, link.Content, details, null);
            foreach (var address in new[] { "127.0.0.1", "10.0.0.2", "169.254.169.254", "192.168.1.1", "::1", "::ffff:127.0.0.1", "2001:db8::1" })
                check(!LinkMetadata.IsPublicAddress(System.Net.IPAddress.Parse(address)), "Private or reserved metadata address rejected: " + address);
            check(LinkMetadata.IsPublicAddress(System.Net.IPAddress.Parse("1.1.1.1")), "A public IPv4 metadata address is accepted.");
            check(!LinkMetadata.IsSafeUrl(new Uri("http://example.com/")) && !LinkMetadata.IsSafeUrl(new Uri("https://example.com/?access_token=secret")) && !LinkMetadata.IsSafeUrl(new Uri("https://127.0.0.1/")), "Automatic metadata rejects HTTP, credentials and private destinations.");
            try { await LinkMetadata.ReadAsync(new Uri("https://127.0.0.1/"), CancellationToken.None); check(false, "Loopback metadata request rejected."); }
            catch (InvalidDataException) { check(true, "Loopback metadata request is rejected before a connection."); }
            library.TogglePin([text.Id]);
            var folder = library.CreateFolder("İş");
            var child = library.CreateFolder("Notlar", folder.Id);
            var grandchild = library.CreateFolder("Projects", child.Id);
            check(ClipboardShelfWindow.OrderedFolders(library.Folders).Select(entry => entry.Depth).SequenceEqual([0, 1, 2]), "Nested folders are displayed under their actual ancestors at every depth.");
            library.AssignFolder([text.Id], grandchild.Id);
            check(library.Search("", folder: grandchild.Id).Single().Id == text.Id, "A deeply nested folder accepts and filters items.");
            var drag = new ClipboardDragSession(); var dragToken = drag.Begin([text.Id, link.Id, text.Id]);
            check(drag.Resolve(dragToken).SequenceEqual([text.Id, link.Id]) && drag.Resolve("forged").Count == 0, "Internal folder drag preserves ordered identities and rejects unowned tokens.");
            drag.End(); check(drag.Resolve(dragToken).Count == 0, "Completed drag identities cannot be replayed.");
            library.AssignFolder([text.Id], child.Id);
            library.AddTags([text.Id], ["first", "FIRST", "second"]);
            check(library.Items.Single(item => item.Id == text.Id).Tags.SequenceEqual(["first", "second"]), "Batch tags are case-insensitively deduplicated.");
            library.SaveDetails(text.Id, "Başlık", text.Content, "Toplantı notu", ["İş", "etiket"]);
            now = now.AddMinutes(1);
            var repeated = library.AddText("İlk satır\nİkinci satır");
            check(repeated.Id == text.Id && library.Items.Count(item => item.Id == text.Id) == 1, "Newline-normalized copies keep one identity.");
            check(repeated.Pinned && repeated.Saved && repeated.FolderIds.Contains(child.Id) && repeated.Tags.Length == 2, "Recopy preserves organization.");
            check(library.Search("toplanti ETIKET", folder: child.Id).Single().Id == text.Id, "Search combines accent-insensitive note, tag, and folder filters.");
            check(library.Search("", domain: "example.com").Single().Id == link.Id, "Domain filter matches links.");

            library.TakeDown([text.Id, link.Id]);
            check(!library.Items.Single(item => item.Id == text.Id).OnShelf && library.Items.Single(item => item.Id == text.Id).TrashedAt is null, "Saved take-down retains the library record.");
            check(library.Items.Single(item => item.Id == link.Id).TrashedAt is not null, "Temporary take-down goes to trash.");
            var newer = library.AddText("new item after take-down");
            library.Undo();
            check(library.Items.Single(item => item.Id == text.Id).OnShelf && library.Items.Single(item => item.Id == link.Id).TrashedAt is null && library.Items.Any(item => item.Id == newer.Id), "Undo restores a batch without losing a later capture.");
            library.Redo();
            check(!library.Items.Single(item => item.Id == text.Id).OnShelf && library.Items.Single(item => item.Id == link.Id).TrashedAt is not null && library.Items.Any(item => item.Id == newer.Id), "Redo reapplies only the original batch.");

            var reloaded = new ClipboardLibrary(root, () => now);
            check(reloaded.Items.Count == library.Items.Count && reloaded.Folders.Count == 3, "Items and nested folders survive restart.");
            check(reloaded.Items.Single(item => item.Id == text.Id).Notes == "Toplantı notu", "Inspector fields survive restart.");
            reloaded.RemoveFolder(child.Id);
            check(reloaded.Folders.Count == 1 && reloaded.Items.Single(item => item.Id == text.Id).FolderIds.Length == 0, "Removing a folder removes its descendants and clears their memberships.");
            reloaded.Undo();
            check(reloaded.Folders.Count == 3 && reloaded.Items.Single(item => item.Id == text.Id).FolderIds.Contains(grandchild.Id), "Undo restores descendant folders and item memberships.");

            now = now.AddDays(31);
            reloaded.Expire();
            check(reloaded.Items.Single(item => item.Id == newer.Id).TrashedAt is not null && reloaded.Items.Single(item => item.Id == text.Id).TrashedAt is null, "Expiration preserves saved items.");
            var protectedFile = Path.Combine(root, "invalid.png"); File.WriteAllText(protectedFile, "not an image");
            Throws<InvalidDataException>(() => reloaded.AddMedia(protectedFile), check, "Invalid PNG rejected before entering the library.");
            var redPath = Path.Combine(root, "red.png"); var bluePath = Path.Combine(root, "blue.png");
            CreateShape(redPath, red: true); CreateShape(bluePath, red: false);
            var red = reloaded.AddMedia(redPath); var blue = reloaded.AddMedia(bluePath);
            check(File.Exists(reloaded.MediaPath(red)) && File.Exists(reloaded.MediaPath(blue)), "Managed image files are copied locally.");
            var exportDirectory = Path.Combine(root, "export"); Directory.CreateDirectory(exportDirectory);
            var existing = Path.Combine(exportDirectory, "report.txt"); File.WriteAllText(existing, "untouched");
            var exportText = new ClipItem(Guid.NewGuid(), ClipKind.Text, "exported text", now, now) { Title = "report" };
            var reserved = exportText with { Id = Guid.NewGuid(), Title = "CON" };
            var exported = ClipboardFileExport.Export(reloaded, [exportText, reserved, blue], exportDirectory);
            check(File.ReadAllText(existing) == "untouched" && Path.GetFileName(exported[0]) == "report (1).txt" && File.ReadAllText(exported[0]) == "exported text", "Export never overwrites an existing file.");
            check(Path.GetFileName(exported[1]).StartsWith("Pano-", StringComparison.Ordinal) && File.ReadAllBytes(exported[2]).SequenceEqual(File.ReadAllBytes(reloaded.MediaPath(blue))), "Reserved names are replaced and image bytes are preserved.");
            var dragRoot = Path.Combine(reloaded.DirectoryPath, "drag");
            var expired = Path.Combine(dragRoot, Guid.NewGuid().ToString("N")); var recent = Path.Combine(dragRoot, Guid.NewGuid().ToString("N")); var unrelated = Path.Combine(dragRoot, "unrelated");
            Directory.CreateDirectory(expired); Directory.CreateDirectory(recent); Directory.CreateDirectory(unrelated);
            File.WriteAllText(Path.Combine(expired, "owned.txt"), "temporary"); File.WriteAllText(Path.Combine(unrelated, "user.txt"), "keep");
            Directory.SetLastWriteTimeUtc(expired, DateTime.UtcNow.AddDays(-2)); Directory.SetLastWriteTimeUtc(unrelated, DateTime.UtcNow.AddDays(-2));
            ClipboardFileExport.RemoveExpiredDragFiles(reloaded.DirectoryPath, DateTimeOffset.UtcNow);
            check(!Directory.Exists(expired) && Directory.Exists(recent) && File.Exists(Path.Combine(unrelated, "user.txt")), "Expired owned drag files are cleaned without touching recent or unrelated directories.");
            var original = reloaded.MediaPath(red, original: true);
            reloaded.SaveImageEdit(red.Id, bluePath);
            check(reloaded.Items.Single(item => item.Id == red.Id).Content != red.Content && File.Exists(original), "Image edits preserve the original.");
            reloaded.Undo();
            check(reloaded.Items.Single(item => item.Id == red.Id).Content == red.Content, "Image edit undo restores previous media identity.");
            reloaded.Redo();
            check(reloaded.Items.Single(item => item.Id == red.Id).Content == blue.Content, "Image edit redo restores rendered media identity.");
            reloaded.RestoreOriginal(red.Id);
            check(reloaded.Items.Single(item => item.Id == red.Id).Content == red.Content, "Original restoration preserves the immutable original.");
            Throws<InvalidOperationException>(() => reloaded.DeletePermanently([red.Id]), check, "Permanent deletion requires trash first.");

            var models = Path.Combine(AppContext.BaseDirectory, "models");
            check(File.Exists(Path.Combine(models, "mobileclip-s2-image.onnx")), "The actual offline model is included in the test output.");
            using var semantic = new SemanticImageSearch(root, models);
            var images = new[] { (red.Id, reloaded.MediaPath(red)), (blue.Id, reloaded.MediaPath(blue)) };
            var redMatches = await semantic.SearchAsync("a red circle", images, CancellationToken.None);
            var blueMatches = await semantic.SearchAsync("a blue square", images, CancellationToken.None);
            check(redMatches.Count > 0 && redMatches[0].Id == red.Id, "MobileCLIP ranks the actual red circle first.");
            check(blueMatches.Count > 0 && blueMatches[0].Id == blue.Id, "MobileCLIP ranks the actual blue square first.");

            reloaded.Trash([red.Id]); reloaded.DeletePermanently([red.Id]);
            check(!reloaded.Items.Any(item => item.Id == red.Id) && !File.Exists(original), "Permanent delete removes an unreferenced original.");
            check(File.Exists(reloaded.MediaPath(blue)), "Permanent delete preserves media referenced by another item.");
            var restored = new ClipboardLibrary(root, () => now);
            check(!restored.Items.Any(item => item.Id == red.Id) && restored.Items.Any(item => item.Id == blue.Id), "Permanent deletion is durable across restart.");

            var recoveryRoot = Path.Combine(root, "recovery");
            var recovery = new ClipboardLibrary(recoveryRoot, () => now);
            recovery.AddText("recover me"); recovery.AddText("newest");
            var primary = Path.Combine(recoveryRoot, "library.json");
            var backup = primary + ".backup";
            var backupBytes = File.ReadAllBytes(backup);
            File.WriteAllText(primary, "{invalid");
            recovery = new ClipboardLibrary(recoveryRoot, () => now);
            check(recovery.RecoveryNotice is not null && recovery.Items.Any(item => item.Content == "recover me"), "A corrupt primary is recovered from the valid backup.");
            check(File.ReadAllBytes(backup).SequenceEqual(backupBytes), "Recovery preserves the known-good backup.");
            recovery.AddText("after recovery"); File.WriteAllText(primary, "{invalid again");
            check(new ClipboardLibrary(recoveryRoot, () => now).Items.Any(item => item.Content == "recover me"), "A later corrupt primary still has a usable backup.");

            var orphan = Path.Combine(root, "media", new string('a', 64) + ".png"); File.Copy(bluePath, orphan);
            var libraryFile = Path.Combine(root, "library.json");
            var json = File.ReadAllText(libraryFile).Replace("\"CleanupPending\": false", "\"CleanupPending\": true", StringComparison.Ordinal);
            File.WriteAllText(libraryFile, json);
            restored = new ClipboardLibrary(root, () => now);
            check(!File.Exists(orphan) && File.Exists(restored.MediaPath(blue)), "Interrupted permanent-delete cleanup retries after restart without deleting referenced media.");
        }
        finally
        {
            var parent = Path.GetFullPath(Path.GetTempPath());
            var resolved = Path.GetFullPath(root);
            if (Path.GetDirectoryName(resolved) == parent.TrimEnd(Path.DirectorySeparatorChar) && Path.GetFileName(resolved).StartsWith("WinForge-ClipboardChecks-", StringComparison.Ordinal)) Directory.Delete(resolved, true);
        }
    }

    private static void Throws<T>(Action action, Action<bool, string> check, string message) where T : Exception
    {
        try { action(); check(false, message); } catch (T) { check(true, message); }
    }

    internal static void CreateShape(string path, bool red)
    {
        const int side = 256;
        var pixels = new byte[side * side * 4];
        for (var y = 0; y < side; y++) for (var x = 0; x < side; x++)
        {
            var index = (y * side + x) * 4;
            var foreground = red ? (x - 128) * (x - 128) + (y - 128) * (y - 128) < 80 * 80 : x is > 48 and < 208 && y is > 48 and < 208;
            pixels[index] = foreground && red ? (byte)0 : (byte)255;
            pixels[index + 1] = foreground ? (byte)0 : (byte)255;
            pixels[index + 2] = foreground && !red ? (byte)0 : (byte)255;
            pixels[index + 3] = 255;
        }
        var image = BitmapSource.Create(side, side, 96, 96, PixelFormats.Bgra32, null, pixels, side * 4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
