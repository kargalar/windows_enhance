# Modern Klasör Seçici

Gezgin → **Modern Klasör Seçici** (`modern-folder-dialog`) klasik
`SHBrowseForFolder` pencerelerini Windows'un `IFileOpenDialog` klasör seçicisiyle
değiştirir. Adres çubuğu, arama ve yeniden boyutlandırma Windows tarafından sağlanır.
Modül otomatik keşfedilir; ayrı bir uygulama kurulmaz.

## Davranış ve kapsam

- WinForge çalışırken etkinleştirilen 64 bit uygulamalardaki standart
  ağaç görünümlü `SHBrowseForFolder` pencerelerini tespit eder. En eski pencere
  türünde seçim aktarımı desteklenmezse klasik pencereye geri döner.
- Modern seçicideki klasör, uygulamanın asıl penceresine PIDL ve `BFFM_SETSELECTIONW`
  ile aktarılır. Sonucu ve PIDL belleğini asıl Windows penceresi yönetir.
- Kök klasör dışında kalan seçimlerde ve başlangıçta Tamam düğmesi devre dışıysa
  klasik pencereye dönülür. Özel uygulama callback'lerinin tüm davranışları ve
  dosya sistemi dışındaki nesneler için tam uyumluluk garanti edilmez.
- İptal, asıl klasör seçimini iptal eder. Modül kapatılırsa veya WinForge'un
  yaşam sinyali kesilirse açık modern seçici kapanır, klasik pencere geri gelir.
- 32 bit uygulamalar ve özel tasarlanmış seçiciler kapsam dışıdır. Yükseltilmiş
  uygulamalar için WinForge da aynı yetki düzeyinde çalışmalıdır.
- Mevcut başlangıç klasörü modern seçiciye aktarılmaz; Windows'un son kullanılan
  klasör davranışı geçerlidir. İlk klasik pencere algılama sırasında kısa süre görünebilir.

## Doğrulama

```powershell
./scripts/build-native.ps1
& ./native/WinForge.Shell/bin/x64/Release/WinForge.FolderDialogFixture.exe `
  "$PWD/native/WinForge.Shell/bin/x64/Release/WinForge.FolderDialogTap.dll"
dotnet run --project tests/WinForge.NavigationTests -c Release -p:Platform=x64 -- --folder-dialog
```

Fixture gerçek Windows pencerelerinde seçim sonucunu, iptali, kapatmada geri
dönüşü, kök klasör kısıtlamasını, başlangıçtaki doğrulama engelini ve en eski
pencere türünde güvenli geri dönüşü kontrol eder. Test çalışırken modül kapalı olmalıdır.
Bu kontroller `scripts/test.ps1 -NativeSmoke` akışına da dahildir.
Yönetilen modül testi ayrıca ayrı bir işlemdeki gerçek pencereye kanca kurulmasını,
seçim sonucunu ve tekrarlanan açma/kapatma çağrılarını doğrular.

API referansı: [Microsoft SHBrowseForFolder](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shbrowseforfolderw).
