# WinForge

Windows 11 için modüler kişiselleştirme kutusu. PowerToys ve Windhawk'ın
arkasında duran fikri tek uygulamada toplar: her tweak ayrı bir modüldür,
uygulama içinden tek tek açılır/kapatılır, ayarları kendi panelinden düzenlenir.

## Şu an çalışan modüller

| Modül | Ne yapar |
|---|---|
| **Taskbar Araçları** | Orta tuşla taskbar düğmesinden pencere kapatma, tekerlekle ses, saatte saniye ve özel saat biçimi, arama/görev görünümü/widget/Copilot düğmeleri, pencere birleştirme, görev çubuğu hizası, "Görevi sonlandır", simge boyutu, saydamlık, vurgu rengi ve tüm tepsi simgelerini gösterme |
| **Windhawk Görev Çubuğu** | Windhawk 2.0 CLI ile başlat gizleme, tray ızgarası, görev çubuğu boyutu, tema, pencere gruplama, gelişmiş saat, ses denetimi, bildirim konumu ve tarayıcı tam ekran balonu; ExplorerPatcher algılanır ve çakışma durumu bildirilir |
| **İnternet Hızı** | Anlık indirme/yükleme hızı görev çubuğunun üstünde (isteğe bağlı olarak içinde), bildirim alanına hizalı; otomatik/MB/s/Mbit/s birim, ayarlanabilir yenileme aralığı, taskbar gizliyken otomatik gizlenme |
| **Sistem Tepsisi Simgeleri** | Bildirim alanında hangi uygulama simgelerinin görüneceğini tek tek seçer; değişiklik anında uygulanır, kapatınca önceki durum geri gelir |
| **Her Zaman Üstte** | `Alt+F` ile aktif pencereyi üste sabitler, aynı kısayol bırakır; sabitlenen pencerenin çevresine renkli çerçeve çizer |
| **Ekrandan Metin Al** | `Alt+Q` ile bölge seçer, OCR ile metni panoya kopyalar |
| **Otomatik Döşeme** | Yeni pencere açıldıkça pencereleri Linux tiling WM gibi dizer; "Ana + yığın", "Izgara" veya "Sütunlar" yerleşimi, ana pencere oranı, pencereler arası boşluk, çoklu monitör desteği |
| **AltSnap Pencere Kontrolü** | Sol Alt ile pencerenin her yerinden taşıma/boyutlandırma; ayarlanabilir fare eylemleri, mıknatıslanma, çoklu monitör, bölgeler ve AltSnap'in tüm gelişmiş ayarlarına erişim; resmi 1.68 motoru uygulamayla birlikte gelir |
| **Uyanık Kal** | Modül açıkken sistemin uykuya geçmesini engeller; isteğe bağlı olarak ekranı da açık tutar |
| **Açık / Koyu Tema** | `Alt+Shift+L` ile uygulama ve sistem temasını tek tuşla değiştirir |
| **Renk Seçici** | `Win+Alt+C` ile seçim modunu açar; tıkladığınız noktanın rengini HEX, RGB veya HSL olarak panoya kopyalar |
| **Düz Metin Yapıştır** | `Win+Ctrl+Alt+V` ile panodaki metnin biçimlendirmesini temizleyip yapıştırır |
| **Ekran Cetveli** | `Ctrl+Alt+M` ile alan seçip piksel, santimetre veya inç ölçüsünü panoya kopyalar |
| **Fare Ayarları** | Hızlandırmayı kapatma, tekerlek kaydırma satırı ve çift tık hızı |
| **Fare İşaretçisi** | Çift Ctrl ile fareyi vurgular; tıklamalarda renkli halka gösterir |
| **Arayüz İnce Ayarları** | Pencere animasyonları, menü gecikmesi, görev çubuğu animasyonları ve Aero Shake |
| **Pencere Köşeleri** | Windows 11'in yuvarlak köşelerini kapatır veya küçük yuvarlak köşelere çevirir |
| **Klasik Sağ Tık Menüsü** | Windows 11'in kısa menüsü yerine Windows 10'un tam sağ tık menüsünü kullanır |
| **Sağ Tık Menüsü** | Gezgin menüsüne özel öğeler ekler ve kayıtlı kabuk uzantılarını gizler; yerleşik komutlar ve diğer uygulamaların kendi menüleri kapsam dışı |
| **Gezgin İnce Ayarları** | Uzantılar, gizli dosyalar, son/sık kullanılanlar, kompakt mod, tam yol, onay kutuları, ayrı süreç ve masaüstü simgeleri |
| **Başlat Menüsü Temizliği** | Önerileri, yeni uygulama bildirimlerini ve son kullanılan öğeleri gizler |
| **Arama Temizliği** | Web (Bing) sonuçlarını, arama vurgularını ve kutu önerilerini kapatır |
| **Pano Geçmişi** | Pano geçmişini açar; isteğe bağlı cihazlar arası eşitleme, `Win+V` ile son kopyalananlar |
| **Başlangıçta Aç** | Windows oturum açtığında WinForge'u arka planda (tepside) başlatır |

Kısayollar: `Alt+F` her zaman üstte · `Alt+Q` ekrandan metin · `Alt+Shift+L` tema ·
`Win+Alt+C` renk seçici · `Win+Ctrl+Alt+V` düz metin yapıştır · `Ctrl+Alt+M` ekran cetveli ·
`Ctrl` çift basış fareyi vurgular.

## Görünüm

Üst çubuktaki **Görünüm** düğmesinden uygulamanın görünümü kişiselleştirilir.
Sol gezinme paneli yerine üstte kategori filtreleri bulunur. Arama (`Ctrl+F`)
modül adı, açıklaması ve kategorisinde çalışır; `Esc` aramayı temizler.
**Yalnızca etkin** filtresi açık modülleri gösterir. Sonuç bulunamazsa
**Filtreleri temizle** düğmesi tüm modüllere geri döndürür.

| Ayar | Seçenekler |
|---|---|
| Ton | Varsayılan, Gece, Grafit, Açık |
| Vurgu rengi | Mavi, Mor, Yeşil, Turuncu, Pembe, Kırmızı |
| Arka plan parlaması | Açık / kapalı; yeni kurulumlarda kapalı |

Seçimler anında uygulanır ve `%APPDATA%\WinForge\appearance.json` dosyasına yazılır.
Sürüm numarası üst çubukta gösterilir; `WinForge.App.csproj` içindeki `<Version>` ile artırılır.

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

## Doğrulama

```powershell
.\scripts\test.ps1
```

Ek test paketi gerektirmeyen Windows/WPF regresyon kontrolleri; ayarların
kaydı ve bozuk veriden toparlanmayı, modül etkinleştirme/geri alma hatalarını,
24 modülün yüklenmesini ve ayar tanımlarını, arama/kategori/etkin filtrelerini,
açma-kapama düğmelerini, tema kaynaklarını ve en küçük pencere yerleşimini
doğrular. Gerçek modüller yüklenip ayar ekranları oluşturulur; Windows ayarlarını
değiştiren modüllerin etkinleştirme işlemleri bu testte çalıştırılmaz. Yaşam
döngüsü ve açma-kapama testleri yan etkisiz bir test modülüyle yapılır.
Ekran görüntüleri test çıktısının `artifacts` klasörüne kaydedilir.

AltSnap paket bütünlüğü, çevrimdışı açılımı, Unicode INI dosyaları, özel bölge ve
parametreli eylemlerin korunması, kapalı modüldeki bekleyen ayarlar ve panel
kontrolleri de doğrulanır. Gerçek AltSnap motorunun başlatılması, canlı ayar
değişiklikleri, yapılandırma penceresi ve temiz kapanması için:

```powershell
.\scripts\test.ps1 -AltSnapSmoke
```

Bu test kısa süreli gerçek fare/klavye kancaları kurar. Ayrı bir AltSnap zaten
çalışıyorsa çakışma kontrolünde durur; mevcut AltSnap'i kendiliğinden kapatmaz.

## AltSnap Pencere Kontrolü

**Verimlilik → AltSnap Pencere Kontrolü** modülünü açın. Harici kurulum veya
çalışma anında indirme gerekmez. Bu özellik resmi AltSnap 1.68 x64 motorunu
ayrı bir yardımcı süreç olarak çalıştırır; motor sıfırdan yeniden yazılmamıştır.

Varsayılan kullanım:

| Hareket | Sonuç |
|---|---|
| Sol Alt + sol tuşla sürükle | Başlık çubuğunu hedeflemeden taşı |
| Sol Alt + sağ tuşla sürükle | İmlecin pencere içindeki konumuna göre kenar/köşeden boyutlandır |
| Sol Alt + orta tuş | Pencere eylemleri menüsü |
| Sol Alt + sol çift tık | Büyüt / geri yükle; Shift ile başlık çubuğuna sar |
| Sol Alt + sağ çift tık | Tıklanan bölgeye göre ekran kenarına veya köşesine oturt |
| Taşırken Shift | Pencere kenarlarına mıknatıslanma davranışını değiştir |
| Taşırken Ctrl | Çoklu monitörde geçerli monitör sınırında tut |
| Taşırken boşluk | Otomatik mıknatıslanmayı geçici olarak kapat |
| Sol Alt + Sol Windows + tekerlek | Alternatif eylem olarak ses düzeyi |

Türkçe klavyede AltGr yazımını korumak için yalnızca **Sol Alt** varsayılandır.
Sağ Alt, her iki Alt, Windows veya Ctrl seçenekleri panelden seçilebilir.

Panelde beş fare tuşunun normal ve alternatif eylemleri, dikey/yatay tekerlek,
alternatif tuş, tuşsuz fare etkinleştirme, öne getirme, Aero/akıllı oturtma,
kenar mıknatıslanması, bitişik pencere boyutlandırma, merkez bölgesi, bölme
oranları, eşikler, boşluk, taşırken saydamlık, MDI, tam ekran, sabit boyutlu
pencereler ve uygulama/pencere istisnaları bulunur. Üstte tutma, ortalama,
çerçevesiz yapma, arkaya gönderme, sarma, küçültme/büyütme, diğer pencereleri
küçültme ve süreç eylemleri fare tuşlarına atanabilir. Sonlandırma ve
duraklatma gibi eylemler yalnızca kullanıcı bunları seçerse çalışır.

**Tüm AltSnap ayarlarını aç**, motorun Genel, Fare, Klavye, İstisnalar ve
Gelişmiş sekmelerini açar. Buradan başlık çubuğu eylemleri, tıklamasız
eylemler, özel kısayollar, çift tuşlu kombinasyonlar, pencere tanımlama ve
diğer AltSnap seçenekleri kullanılabilir. **Bölge menüsü için AltSnap simgesini
göster** ile açılan tepsi simgesine sağ tıklayarak bölge yerleşimleri oluşturup
düzenleyebilir ve kaydedebilirsiniz. Simge başlangıçta gizlidir; WinForge
panelinden yeniden gizlenebilir.

**Gelişmiş INI dosyasını düzenle** tüm motor seçeneklerine, bölge
koordinatlarına, eylem parametrelerine ve gelişmiş performans ayarlarına erişir.
Dosyayı veya AltSnap iletişim kutusunu kaydettikten sonra **Gelişmiş ayarları
yeniden oku ve uygula** düğmesine basın. Panelde yapılan değişiklikler anında
uygulanır; diğer gelişmiş değerler, yorumlar, özel eylemler ve bölgeler korunur.
Tam açıklamalar modülün **AltSnap kullanım kılavuzunu aç** düğmesindedir.

Motor ve kendine ait ayarları `%LOCALAPPDATA%\WinForge\AltSnap\1.68` içindedir.
Ayrı AltSnap kurulumunun dosyaları değiştirilmez. Başka bir AltSnap açıksa
modül açık hata verir. Otomatik Döşeme modülü aynı pencereleri tekrar
yerleştirebilir; manuel AltSnap düzeninde bu modülü kapatın. Yönetici olarak
çalışan pencereleri yönetmek için WinForge'u da yönetici olarak çalıştırın.
AltSnap'in kendi otomatik başlangıç/yükseltme seçenekleri yerine WinForge'un
**Başlangıçta Aç** modülünü ve yetki düzeyini kullanın; motorun `AlwaysElevate`
değeri başlatılırken kapatılır.

Modülü kapatınca motor ve kancaları kapanır; yapılan pencere konumu/boyutu
değişiklikleri kalır. WinForge tepsiye küçüldüğünde özellik çalışmaya devam
eder, **Çıkış** ile durur. Windows Job Object bağı sayesinde WinForge aniden
sonlandırıldığında da yardımcı süreç kapanır.

Resmi ikili ve karşılık gelen kaynak arşivleri DLL'e gömülüdür; açılmadan önce
SHA-256 ile doğrulanır. GPLv3 lisans metni ve kaynak arşivi motorun yanında
bulunur. Sürüm, kaynak adresleri ve hash değerleri:
[third_party/AltSnap/NOTICE.md](third_party/AltSnap/NOTICE.md).

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

Taskbar styler'ın kayıt defteriyle yapılabilen kısmı yerleşik: simge boyutu,
saydamlık, vurgu rengi ve **Sistem Tepsisi Simgeleri** modülüyle tek tek tepsi
simgesi görünürlüğü. Başlat düğmesini gizleme, gerçek tray ızgarası, görev
çubuğu yüksekliği ve XAML temaları **Windhawk Görev Çubuğu** modülüyle,
Windhawk 2.0 CLI üzerinden yönetilir.
Modül CLI'yi yaygın kurulum yollarında ve PATH'te arar; bulamazsa uyarı verir.
CLI işlemleri yönetici hakları gerektirebilir. Windows'un kendi kayıt defterinde
bildirim konumu veya Chrome tam ekran balonu için ayar yoktur; bunlar da aynı
Windhawk modları üzerinden yönetilir. Windhawk 1.x kuruluysa CLI bulunmaz ve bu
gelişmiş toggllar çalışmaz; Windhawk 2.0 gerekir. ExplorerPatcher da algılanır ve
varsa XAML modlarıyla çakışabileceği bildirilir, ancak bu özellikler için
Windhawk gerekir. Ayrıca bazı Windows yapılarında
`TaskbarDa` (widget düğmesi) kayıt değeri korumalıdır; bu durumda ayar sessizce
uygulanmaz, loga hata yazılır.

Windhawk olmadan yerel kayıt defteriyle güvenilir biçimde yapılamayanlar:
Windows 11'de pencere gruplamayı kapatma, gerçek tray ızgarası, Başlat düğmesini
gizleme, görev çubuğu yüksekliğini sabitleme, XAML tema stilleri, bildirim konumu
ve Chrome/Edge tam ekran balonu. Bunlar `explorer.exe` veya tarayıcı sürecine kod
enjeksiyonu gerektirir. WinForge'un native karşılıkları: saat biçimi ve saniye,
tekerlekle ses, orta tuşla kapatma, `TaskbarSi` ile simge boyutu, saydamlık/vurgu
rengi ve **Sistem Tepsisi Simgeleri** ile tek tek tepsi görünürlüğü.

Otomatik döşeme yalnızca normal üst düzey pencerelere uygulanır: büyütülmüş
(azami) pencereler, tam ekran uygulamalar, araç pencereleri, WinForge'ın kendi
pencereleri ve minimum boyut kısıtı olan uygulamalar atlanır. Boşluk ayarı
yalnızca pencereler arasında uygulanır; DWM görünmez çerçevesi telafi edilerek
pencereler ekran kenarlarına ve birbirine sıfıra sıfır oturur. Kapatınca pencereler son konumunda kalır.

Windhawk dışında kendi native enjeksiyon motorunu kurmak ayrı bir aşama
gerektirir: hedef sürece (explorer.exe, chrome.exe) DLL enjeksiyonu, XAML görsel
ağacına erişim ve sürüme bağlı imza/hook yönetimi. Mimari buna hazır: modüller
süreç dışı çalışabildiği gibi, ileride native enjeksiyon motoru aynı
`ITweakModule` sözleşmesinin arkasına yerleştirilebilir.
