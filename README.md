# WinForge

Windows 11 için modüler kişiselleştirme kutusu. PowerToys ve Windhawk'ın
arkasında duran fikri tek uygulamada toplar: her tweak ayrı bir modüldür,
uygulama içinden tek tek açılır/kapatılır, ayarları kendi panelinden düzenlenir.

## Şu an çalışan modüller

| Modül | Ne yapar |
|---|---|
| **Taskbar Araçları** | Orta tuşla taskbar düğmesinden pencere kapatma, taskbar üzerinde fare tekerleğiyle ses kontrolü, saatte saniye ve özel saat biçimi, arama kutusu/görev görünümü/widget/Copilot düğmeleri ve pencere birleştirme modu |
| **İnternet Hızı** | Taskbar'a demirlenmiş anlık indirme/yükleme hızı (MB/s); taskbar gizliyken ekran kenarında kalır |
| **Her Zaman Üstte** | `Alt+F` ile aktif pencereyi üste sabitler, aynı kısayol bırakır; sabitlenen pencerenin çevresine renkli çerçeve çizer |
| **Ekrandan Metin Al** | `Alt+Q` ile bölge seçer, OCR ile metni panoya kopyalar |

Kısayollar: `Alt+F` her zaman üstte · `Alt+Q` ekrandan metin.

## Görünüm

Sol alttaki **Görünüm** düğmesinden uygulamanın görünümü kişiselleştirilir:

| Ayar | Seçenekler |
|---|---|
| Ton | Varsayılan, Gece, Grafit |
| Vurgu rengi | Mavi, Mor, Yeşil, Turuncu, Pembe, Kırmızı |
| Arka plan parlaması | Açık / kapalı |

Seçimler anında uygulanır ve `%APPDATA%\WinForge\appearance.json` dosyasına yazılır.
Sürüm numarası sol altta gösterilir; `WinForge.App.csproj` içindeki `<Version>` ile artırılır.

## Çalıştırma

Gereksinim: Windows 10/11 (x64) ve .NET 10 SDK.

```powershell
.\scripts\build.ps1
.\scripts\run.ps1
```

Çıktı: `src\WinForge.App\bin\Release\net10.0-windows10.0.19041.0\WinForge.exe`
(yanındaki `modules\` klasörü modülleri içerir).

Ayarlar ve log: `%APPDATA%\WinForge\settings.json`, `%APPDATA%\WinForge\winforge.log`.
Uygulama kapatınca kapanmaz, sistem tepsisinde çalışmaya devam eder; kısayollar
bu yüzden açık kalır. Tepsi menüsünden **Çıkış** ile tamamen kapanır.

## Mimari

```
WinForge.Core     ITweakModule sözleşmesi, modül yükleme, ayar deposu,
                  global kısayol, fare kancası, ses (Core Audio COM), OSD
WinForge.App      WPF arayüzü, modül listesi, otomatik ayar paneli, tepsi
modules\*.dll     Her modül bağımsız bir sınıf kütüphanesi
```

- Modüller `modules` klasöründen yansıma ile yüklenir; yeni modül eklemek için
  uygulamayı değiştirmek gerekmez.
- Ayar paneli `SettingDescriptor` listesinden otomatik üretilir (aç/kapa, sayı,
  metin, seçenek). Modül yazarı arayüz kodu yazmaz.
- Modül kapatıldığında `Deactivate()` çalışır ve yaptığı değişiklikleri geri alır.
- Ayar değişince `Apply()` çağrılır; açık olmayan modülde hiçbir şey yapılmaz.
- Ayarlar json'a anında yazılır; çökme sonrası son durum korunur.

### Yeni modül ekleme

1. `src\WinForge.Modules.X` altında sınıf kütüphanesi aç, `WinForge.Core`a referans ver.
2. `ITweakModule` uygula:

```csharp
public sealed class MyModule : ITweakModule
{
    public string Id => "my-module";
    public string Name => "Modülüm";
    public string Category => "Sistem";
    public string Description => "Ne yaptığını bir cümleyle anlat.";
    public string? Warning => null;
    public bool IsActive { get; private set; }

    public IReadOnlyList<SettingDescriptor> Settings { get; } = new[]
    {
        new SettingDescriptor { Key = "enabled-x", Label = "X olsun", Default = true }
    };

    public void Initialize(IModuleContext context) => _context = context;
    public void Activate() { /* kaynakları kur, IsActive = true */ }
    public void Deactivate() { /* geri al */ }
    public void Apply() { /* ayarları yeniden oku */ }
}
```

3. `WinForge.App.csproj` içine `ReferenceOutputAssembly="false"` proje referansı ekle;
   `CopyModules` hedefi DLL'i `modules\` klasörüne kopyalar.

## Henüz yapılmayanlar

Explorer'ın XAML katmanına enjeksiyon gerektiren modlar bu sürümde yok:
Windows 11 taskbar styler, başlat düğmesini gizleme, taskbar yüksekliği/ikon
boyutu, tray ızgarası, bildirim konumu ve uygulamaya özel (ör. Chrome tam ekran
balonu) düzeltmeler. Ayrıca bazı Windows yapılarında `TaskbarDa` (widget düğmesi)
kayıt değeri korumalıdır; bu durumda ayar sessizce uygulanmaz, loga hata yazılır.

Bunlar için ayrı bir aşama gerekiyor: hedef sürece (explorer.exe, chrome.exe)
DLL enjeksiyonu, XAML görsel ağacına erişim ve sürüme bağlı imza/hook yönetimi.
Mimari buna hazır: modüller süreç dışı çalışabildiği gibi, ileride native
enjeksiyon motoru aynı `ITweakModule` sözleşmesinin arkasına yerleştirilebilir.
