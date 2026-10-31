# WinForge

Windows 11 için kendi motorlarını kullanan modüler kişiselleştirme kutusu. Her özellik ayrı bir modüldür,
uygulama içinden tek tek açılır/kapatılır, ayarları kendi panelinden düzenlenir.

## Şu an çalışan modüller

| Modül | Ne yapar |
|---|---|
| **Taskbar Araçları** | Orta tuşla taskbar düğmesinden pencere kapatma, tekerlekle ses, saatte saniye ve özel saat biçimi, arama/görev görünümü/widget/Copilot düğmeleri, pencere birleştirme, görev çubuğu hizası, "Görevi sonlandır", simge boyutu, saydamlık, vurgu rengi ve tüm tepsi simgelerini gösterme |
| **Görev Çubuğu Tasarımı** | WinForge'un yerel kabuk motoruyla Başlat düğmesini gizleme, renk düzenleri, simge/düğme boyutları, 48–96 DIP yükseklik, iki satırlı bildirim simgeleri ve görev çubuğuna özel saat/tarih biçimi |
| **Bildirim Konumu** | Tanınan Windows bildirim pencerelerini ekran/köşe ayarına göre taşır ve kapatılınca özgün sınırlarını geri yükler; gerçek Windows bildirimi doğrulamasının durumu doğrulama belgesinde tutulur |
| **Tarayıcı Tam Ekran İpucu** | Chromium tarayıcısının üst ortadaki etkileşimsiz tam ekran çıkış ipucunu gizler; gerçek Edge üzerinde gizleme/geri yükleme doğrulandı |
| **İnternet Hızı** | Anlık indirme/yükleme hızı görev çubuğunun üstünde (isteğe bağlı olarak içinde), bildirim alanına hizalı; otomatik/MB/s/Mbit/s birim, ayarlanabilir yenileme aralığı, taskbar gizliyken otomatik gizlenme |
| **Sistem Tepsisi Simgeleri** | Bildirim alanında hangi uygulama simgelerinin görüneceğini tek tek seçer; değişiklik anında uygulanır, kapatınca önceki durum geri gelir |
| **Her Zaman Üstte** | `Alt+F` ile aktif pencereyi üste sabitler, aynı kısayol bırakır; sabitlenen pencerenin çevresine renkli çerçeve çizer |
| **Ekrandan Metin Al** | `Alt+Q` ile bölge seçer, OCR ile metni panoya kopyalar |
| **Hızlı Başlatıcı** | `Win+Alt+Space` ile Başlat menüsü, kullanıcı klasörleri ve isteğe bağlı klasörlerde uygulama/dosya/klasör arar; uzantı filtresi, hesap makinesi ve web/Windows URI desteği |
| **Kısayol Rehberi** | `Win+Shift+/` ile açılır; Win tuşunu basılı tutunca odak çalmayan katman Windows ve WinForge kısayollarını kategorilere göre listeler ve aratır |
| **Klavye Yöneticisi** | Tuşları ve kısayolları genel olarak veya işlem adına göre uygulama bazında eşler, kısayolla Unicode metin yazdırır, 1–4 adımlı ardışık chord kısayollarıyla uygulama/URI başlatır; uygulamaya özel eşleme genel eşlemenin önceliğindedir |
| **Tam Ekran Pencere Yönetimi** | Açık pencereleri Ana + yığın, Izgara veya Sütunlar düzeninde otomatik yerleştirme; boşluk, ana pencere genişliği ve küçültmeyi engelleme ayarları |
| **FancyZones · Bölgeli Pencere Taşıma** | Yeni pencereleri en az dolu bölgeye otomatik yerleştirme; Shift ile sürükleyerek pencereyi iki, üç, dört ya da özelleştirilebilir 1–6 sütun × 1–4 satır ızgaraya taşıma; çizgi yakınında komşu bölgeleri, dört alan kesişiminde tüm alanları birleştirme; numara, renk, kenarlık kalınlığı ve birleştirme mesafesi ayarları; çoklu monitör ve ekrana göre ızgara profilleri |
| **Crop And Lock · Pencere Kırpma** | `Win+Ctrl+Shift+T` ile etkin pencerenin bir alanını sürükleyerek seçer ve alanı canlı, yeniden boyutlandırılabilir küçük pencerede gösterir; kaynak pencereye tıklama göndermez |
| **Pencere Kontrolü** | WinForge motoruyla Alt + fare ile taşıma/boyutlandırma; çoklu monitör, kenarlara oturtma, mıknatıslanma, ayarlanabilir eylemler ve uygulama/pencere istisnaları |
| **Uyanık Kal** | Modül açıkken sistemin uykuya geçmesini engeller; isteğe bağlı olarak ekranı da açık tutar |
| **Açık / Koyu Tema** | `Alt+Shift+L` ile temayı değiştirir veya isteğe bağlı yerel saat planında 07:00 / 19:00 varsayılanlarına göre otomatik geçiş yapar |
| **Renk Seçici** | `Win+Alt+C` ile seçim modunu açar; tıkladığınız noktanın rengini HEX, RGB veya HSL olarak panoya kopyalar |
| **Düz Metin Yapıştır** | `Win+Ctrl+Alt+V` ile panodaki metnin biçimlendirmesini temizleyip yapıştırır |
| **Ekran Cetveli** | `Ctrl+Alt+M` ile alan seçip piksel, santimetre veya inç ölçüsünü panoya kopyalar |
| **Fare Ayarları** | Hızlandırmayı kapatma, tekerlek kaydırma satırı ve çift tık hızı |
| **Fare İşaretçisi** | Çift Ctrl ile fareyi vurgular; sol, sağ, orta ve yan tuş tıklamalarını isteğe bağlı ayrı renk, halka çapı ve süreyle gösterir; `Win+Alt+.` ile imleç artısı açar |
| **Ekranlar Arası Fare Taşıma** | `Win+Alt+J` ile ekran önizlemesini açar, tıklanan noktaya veya klavyeyle seçilen ekran merkezine taşır |
| **Arayüz İnce Ayarları** | Pencere animasyonları, menü gecikmesi, görev çubuğu animasyonları ve Aero Shake |
| **Pencere Köşeleri** | Windows 11'in yuvarlak köşelerini kapatır veya küçük yuvarlak köşelere çevirir |
| **Klasik Sağ Tık Menüsü** | Windows 11'in kısa menüsü yerine Windows 10'un tam sağ tık menüsünü kullanır |
| **Sağ Tık Menüsü** | Gezgin menüsüne özel öğeler ekler ve kayıtlı kabuk uzantılarını gizler; yerleşik komutlar ve diğer uygulamaların kendi menüleri kapsam dışı |
| **Gezgin İnce Ayarları** | Uzantılar, gizli dosyalar, son/sık kullanılanlar, kompakt mod, tam yol, onay kutuları, ayrı süreç ve masaüstü simgeleri |
| **Gezgin Klasör Boyutları** | Ayrıntılar görünümünde yerel klasörlerin boyutlarını arka planda hesaplar, önbelleğe alır ve dosyalar değiştikçe günceller |
| **Toplu Yeniden Adlandırma** | Çoklu dosya seçimi, metin/regex önizlemesi, çakışma denetimi ve en son işlemi pencere/uygulama yeniden açıldıktan sonra da geri alma |
| **Görsel Boyutlandırıcı** | Birden fazla görsel için 854/1280/1920 px veya özel uzun kenar; en-boy oranı, PNG/JPEG biçimi ve JPEG kalitesi korunur; aynı biçimde EXIF/IPTC metadata aktarımı seçilebilir; arka planda ilerleme/iptal; özgün dosyalar saklanır |
| **Dosya Kilidi Bulucu** | Windows Restart Manager ile kilit kullanan uygulama ve hizmetleri listeler; kullanıcı uygulamaları ayrıca onayla sonlandırılabilir |
| **Dosya Önizleme** | `Win+Alt+P` ile metin/görselleri sınırlı bellekle gösterir; PDF'nin ilk sayfasını çizer, ses ve videoyu yerleşik denetimlerle oynatır |
| **Başlat Menüsü Temizliği** | Önerileri, yeni uygulama bildirimlerini ve son kullanılan öğeleri gizler |
| **Arama Temizliği** | Web (Bing) sonuçlarını, arama vurgularını ve kutu önerilerini kapatır |
| **Pano Geçmişi** | Pano geçmişini açar; isteğe bağlı cihazlar arası eşitleme, `Win+V` ile son kopyalananlar |
| **Gelişmiş Yapıştır** | `Win+Shift+V` ile metni Markdown/JSON'a dönüştürür, panodaki görselden OCR yapar veya TXT/HTML/PNG/JPEG/BMP/GIF dosyası olarak yapıştırır; JPEG kalite ayarı sunar |
| **Ortam Değişkenleri** | Kullanıcı değişkenlerini düzenler; geliştirme/test gibi profilleri etkinleştirir, `%...%` türünü ve önceki değerleri korur |
| **Hosts File Editor** | Windows hosts dosyasında IP-ad eşlemeleri ekler, düzenler, filtreler ve etkinleştirir/devre dışı bırakır; kaydetmeden önce yedek oluşturur ve UAC ister |
| **Power Display · Ekran Denetimi** | Dahili panel parlaklığını Windows WMI, harici ekranlarda desteklenen parlaklık/kontrast/ses/giriş/renk denetimlerini DDC/CI üzerinden yönetir; profilleri ve gündüz/gece zamanlamasını destekler, imleçteki harici ekran için isteğe bağlı parlaklık kısayolları sunar |
| **Fluent Medya Flyout’u** | `Win+Alt+M` ve `Ctrl+Alt+V`; ana ses/sessiz denetimi, donanım ses tuşlarında alt-orta ses OSD’si ve medya tuşlarında flyout; Windows Core Audio üzerinden algılanabilen harici kulaklık/aygıt ses değişimlerinde OSD; isteğe bağlı görev çubuğu üstü mini oynatıcı (sağ tıkta medya/ses/oynatıcı eylemleri ve isteğe bağlı ana ses tekerlek kontrolü), WASAPI çıkış sesinden gerçek zamanlı spektrum görselleştiricisi, filtrelenebilir medya uygulamaları ve oturum seçimi/sabitleme, albüm kapağı, oynat/önceki/sonraki, parça arama ve shuffle/repeat, kilit tuşu OSD’si, Up Next, köşe ve monitör seçimi, siyah koyu görünüm, belirme animasyonu, etkinlik sonrası otomatik kapanma ve tam ekran uygulama bastırması |
| **Çalışma Alanları** | Açık masaüstü uygulamalarını ve pencere konumlarını adlandırılmış bir düzen olarak kaydeder; seçilen düzeni uygulamaları yeniden açarak geri yükler; isteğe bağlı olarak komut satırı argümanlarını ve dosya yollarını da kaydedip yeniden kullanır |
| **Başlangıçta Aç** | Windows oturum açtığında WinForge'u arka planda (tepside) başlatır |

Kısayollar: `Alt+F` her zaman üstte · `Alt+Q` ekrandan metin · `Win+Alt+Space` hızlı başlatıcı · `Alt+Shift+L` tema ·
`Win+Alt+C` renk seçici · `Win+Ctrl+Alt+V` düz metin yapıştır · `Ctrl+Alt+M` ekran cetveli ·
`Ctrl` çift basış fareyi vurgular.

**Verimlilik → Klavye Yöneticisi** içindeki **Klavye eşlemelerini düzenle**
düğmesinde her satıra bir eşleme yazın: tuşlar sekmesinde `A = B`, kısayollar
sekmesinde `Ctrl+C = Ctrl+Insert`; **Metin dizileri** sekmesinde `Ctrl+Alt+T = Toplantı notları` veya `H = Hello!` gibi bir kısayola ya da tek tuşa yazı bağlayın. Metin en fazla 2048 karakterdir ve Unicode karakterleri destekler. **Uygulamaya özel** sekmesinde işlem adını belirtin: `msedge.exe: Ctrl+F = Ctrl+H` veya `notepad.exe: A = B`. Uygulama
eşlemesi yalnızca o uygulama öndeyken uygulanır ve aynı kaynak için genel
eşlemenin önüne geçer. İşlem adı `.exe` uzantısıyla veya uzantısız yazılabilir;
dosya yolu yazılmaz. `#` ile başlayan satırlar açıklama olarak kalır. Windows
tarafından ayrılmış kısayollar reddedilir. Modülü kapatmak tüm eşlemeleri anında
durdurur.

**Uygulama veya URI aç** sekmesinde kısayol eylemi ekleyin. URI örneği:
`Ctrl+Alt+W = uri|https://example.com`; desteklenen şemalar `http`, `https`,
`mailto`, `ms-settings`, `shell` ve `file` ile sınırlıdır. Uygulama örneği:
`Ctrl+Alt+N = app|"%WINDIR%\System32\notepad.exe"|--new-window|"%TEMP%"|elevated|show|hidden`.
Uygulama hedefi, argümanlar ve çalışma klasörü `%...%` ortam değişkenlerini
destekler. Son üç alan yetki (`normal`, `elevated`, `different-user`), uygulama
zaten açıksa davranış (`new`, `show`, `nothing`, `close`, `end-task`) ve görünürlük
(`visible`, `hidden`) seçeneğidir. `end-task` eşleşen süreci zorla kapatır ve
kaydedilmemiş veriyi kaybettirebilir. Uygulama başlatma klavye kancasını
geciktirmeyen arka plan işinde yapılır; hata olursa uygulama bildirim ve günlüğe
yazar.

**Görünüm → Açık / Koyu Tema** modülünde **Temayı saate göre değiştir**
ayarını açıp `HH:mm` biçiminde açık ve koyu tema saatlerini belirleyin. Plan yerel
saati kullanır, gece yarısını aşan aralıkları destekler ve dakika başına en çok
bir kez denetler. Zamanlama kapalıyken `Alt+Shift+L` ile manuel geçiş yapılır.

**Sistem → Power Display · Ekran Denetimi** modülünü etkinleştirip paneli açın.
Her ekran için DDC/CI yetenek bildirgesinde desteklenen kontroller görünür:
parlaklık, kontrast, monitör sesi, giriş kaynağı ve renk ön ayarı. **Parlaklık
seviyelerini bağla** kaydırıcısı uyumlu ekranlara aynı yüzdeyi uygular. Profil
adını girip **Profili kaydet** ile bağlı ekranların desteklenen parlaklık,
kontrast, monitör sesi ve renk ön ayarlarını saklayın; seçiciden bir profil
seçip **Uygula** ile geri yükleyin. Profiller WinForge ayarlarında kalıcıdır;
eşleşmeyen ekranlar ve artık desteklenmeyen denetimler atlanıp durum satırında
bildirilir. **Profili sil** seçili profili kaldırır. Renk ön ayarı değiştirilirken
uyarı gösterilir. Monitörün OSD menüsünde DDC/CI'yi açın; bazı dock/kablo
bağlantıları bu komutları iletmez.
İsteğe bağlı zamanlama ayarını etkinleştirerek gündüz/gece başlangıç saatlerini
(`HH:mm`) ve iki parlaklık seviyesini belirleyin. Ayarlar modül açıkken panel
kapalı olsa da uygulanır; gece başlangıcı gündüz başlangıcından önceyse gece
dönemi gece yarısını aşacak şekilde hesaplanır. Desteklenmeyen ekranlar atlanır.
İsteğe bağlı **İmleçteki ekran için parlaklık kısayolları** ayarını açınca
`Ctrl+Alt+F11` azaltır, `Ctrl+Alt+F12` artırır. Yalnızca imlecin bulunduğu
Windows ekranına bağlı destekli fiziksel monitörler değişir; **Kısayol adımı (%)**
1–20 aralığında ayarlanabilir (örneğin 10'ar 10'ar). Azaltma ve artırma
kısayollarını da ayrı ayrı değiştirebilirsiniz; `Ctrl+Alt+PageDown` gibi en az
bir değiştirici ve bir işlem tuşu kullanın. Fare tekerleği için
`Ctrl+Alt+MouseWheelDown` ve `Ctrl+Alt+MouseWheelUp` biçimini kullanabilirsiniz.
Klavye kısayolu başka uygulamada kayıtlıysa hata bildirilir.

**Sistem → Hosts File Editor** içinden hosts dosyasını açın. IP adresi, bir veya
daha fazla alan adı ve isteğe bağlı yorumla girdi ekleyin; seçili girdiyi
düzenleyin, silin veya etkinliğini değiştirin. Filtre IP, alan adı ve yorumda
çalışır. Yeni satırlar varsayılan olarak Windows başlık yorumlarından sonra
eklenir; modül ayarından dosya sonunu seçebilirsiniz. Kaydetme UAC ister;
dosya dışarıdan değişmişse kaydetme reddedilir, önce yeniden yükleyin. Her kayıt
öncesi aynı klasörde tarihli yedek alınır; en az son beş yedek tutulur.

**Sistem → Fluent Medya Flyout’u** modülünü etkinleştirip `Win+Alt+M` veya
`Ctrl+Alt+V` ile açın. İki kısayol da modül ayarlarından ayrı ayrı
kapatılabilir. Donanım ses (açma/kısma/sessiz) tuşları ekranın alt ortasında ses
OSD’sini ve medya panelini, medya (oynat/duraklat, önceki/sonraki) tuşları medya
flyout’unu gösterir.
Bu tuşların Windows’a giden normal işlevi engellenmez. Davranış,
**Donanım ses ve medya tuşlarında bildirimleri göster** ayarından kapatılabilir.
Up Next parça bildirimi varsayılan olarak açıktır ve flyout
açıkken yeni parçaya geçildiğinde dört saniye görünür.
İsteğe bağlı **Küçük medya oynatıcısını görev çubuğunun üstünde göster** ayarı,
etkin medya oturumu varken birincil görev çubuğunun hemen üzerinde başlık ve
önceki/oynat-duraklat/sonraki denetimlerini gösterir. Başlığa tıklamak tam
flyout’u açar. Mini oynatıcıya sağ tıklayarak filtreye uyan
medya oturumları arasında geçiş yapabilir, otomatik oturum takibini seçebilir,
seçimi kalıcı olarak sabitleyebilir; ayrıca medya flyout’unu, Windows Ses
Ayarları’nı açabilir veya oynatıcı penceresine geçebilirsiniz.
**Mini oynatıcıda tekerlek davranışı** ayarı açıkken tekerlek ana ses düzeyini
değiştirir. Medya oturumu yoksa ya da tam ekran uygulama öndeyse widget
gizlenir; oturum durumunu iki saniyede bir denetler. Medya uygulaması izin/engel
listesi bu mini oynatıcıya da uygulanır.
Caps Lock, Num Lock ve Scroll Lock göstergelerini **Kilit tuşu OSD** ana ayarına
ek olarak ayrı ayrı açıp kapatabilirsiniz.
**Görev çubuğu üstü ses görselleştiricisi** varsayılan olarak kapalıdır. Açıldığında
varsayılan Windows ses çıkışının paylaşılan sesini 24 bantlı canlı spektruma
dönüştürür; herhangi bir ses dosyası oluşturmaz. Ortada veya mini oynatıcının
solunda konumlandırılabilir. Sessizlikte otomatik gizleme ve taban çizgisi
ayarlanabilir; tam ekran uygulamalarda gizlenir. Gerçek Windows görev çubuğuna
yerleştirme desteklenmez; görselleştirici görev çubuğunun üzerinde ayrı,
etkileşimsiz bir pencere olarak çalışır.
Ana çıkış sesini ve sessizi yönetin. Medya oturumu seçildiğinde parça konumu,
albüm kapağı ve desteklenen karıştır/tekrar denetimleri görünür.
**Medya uygulaması filtreleme** ayarından tüm oturumları gösterebilir, izin
listesindeki uygulamalarla sınırlayabilir veya engel listesindekileri gizleyebilirsiniz.
Listeye `Spotify; msedge` gibi işlem adı ya da paket kimliği parçalarını noktalı
virgülle yazın; `.exe` uzantısı ve büyük/küçük harf farkı önem taşımaz. Filtre
medya oturumu seçicisi, geçerli parça denetimleri ve Up Next bildirimi için
ortak uygulanır. Boş engel listesi tüm uygulamaları gösterir; boş izin listesi
hiçbir medya oturumunu göstermez.

## Görünüm

Sol paneldeki **Görünüm** düğmesinden uygulamanın görünümü kişiselleştirilir.
Kategoriler sol panelde listelenir. Arama (`Ctrl+F`)
modül adı, açıklaması ve kategorisinde çalışır; `Esc` aramayı temizler.
**Yalnızca etkin** filtresi açık modülleri gösterir. Sonuç bulunamazsa
**Filtreleri temizle** düğmesi tüm modüllere geri döndürür.

| Ayar | Seçenekler |
|---|---|
| Ton | Siyah, Grafit |
| Vurgu rengi | Mavi, Yeşil, Kırmızı |
| Arka plan parlaması | Açık / kapalı; yeni kurulumlarda kapalı |

Seçimler anında uygulanır ve `%APPDATA%\WinForge\appearance.json` dosyasına yazılır.
Sürüm numarası üst çubukta gösterilir; `WinForge.App.csproj` içindeki `<Version>` ile artırılır.

## Çalıştırma

Derleme gereksinimleri: Windows 10/11 (x64), .NET 10 SDK, Node.js/npm ve Visual Studio C++ Build Tools ile Windows SDK. Uygulamayı çalıştırmak için WebView2 Runtime gerekir (Windows 11'de genellikle hazır bulunur). Yerel kabuk tasarımı Windows 11 görev çubuğunu hedefler. Çalıştırılabilir pakete SDK ve derleyici gerekmez; kendinden yeterli yayıma .NET Desktop çalışma zamanı dahil edilir.

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
25 modülün yüklenmesini ve ayar tanımlarını, arama/kategori/etkin filtrelerini,
açma-kapama düğmelerini, tema kaynaklarını ve en küçük pencere yerleşimini
doğrular. Gerçek modüller yüklenip ayar ekranları oluşturulur; Windows ayarlarını
değiştiren modüllerin etkinleştirme işlemleri bu testte çalıştırılmaz. Yaşam
döngüsü ve açma-kapama testleri yan etkisiz bir test modülüyle yapılır.
Ekran görüntüleri test çıktısının `artifacts` klasörüne kaydedilir.

Yeni altyapının ayar göçü, pencere geometrisi,
istisnaları ve PCM/float ses çözümlemesi de test edilir. Canlı Windows fare
kancası, WMI süreç sorgusu ve varsayılan ses çıkışında WASAPI başlangıç/kapanış
kontrolleri için:

```powershell
.\scripts\test.ps1 -NativeSmoke
```

Nashi/ClipboardLine modüllerinin gerçek Windows, yerel medya ve Office dönüşüm
kontrolleri `-NativeSmoke` ile çalışır. Nashi'nin tam medya ve belge motorunu da
gerçek FFmpeg/ffprobe ve LibreOffice araçlarıyla doğrulamak için:

```powershell
.\scripts\test.ps1 -ExtendedFileTools
```

Bu ek doğrulama sabitlenmiş araç paketlerini `artifacts` altındaki test
klasörlerine SHA-256 doğrulamasıyla indirir; Windows'a kurulum yapmaz. Normal
test koşusu paket indirmez.

Canlı kontroller kullanılabilir bir ses çıkışı gerektirir. Monitör parlaklığı,
yönetici pencereleri ve farklı Windows yapılarındaki kabuk davranışları hedef
makinelerde ayrıca doğrulanmalıdır.

## Pencere Yönetimi

Bu özellikler **Verimlilik** altında ayrı modüllerdir:

- **Tam Ekran Pencere Yönetimi:** Açık pencereler seçilen Ana + yığın, Izgara veya
  Sütunlar yerleşimine göre otomatik dizilir.
- **FancyZones · Bölgeli Pencere Taşıma:** Pencereyi sürüklerken Shift tuşunu basılı tutun.
  Ekranda bölgeler görünür; pencereyi istediğiniz bölgenin üzerinde bırakın.
  **Pencere Kontrolü** etkinse varsayılan Alt+sol tuş sürüklemesi de bölgeleri gösterir;
  bu hareket için Shift gerekmez.
  Yeni açılan pencereler varsayılan olarak o ekrandaki en az dolu bölgeye yerleştirilir;
  bütün bölgeler doluysa pencere büyütülür. Bunu **Yeni pencereleri boş bölgelere
  yerleştir** ayarından kapatabilirsiniz.
  Bölme çizgisine ayarladığınız mesafe kadar yaklaşınca komşu bölgeler birlikte
  seçilir; dört alanın kesişimine yaklaştığınızda dördü de seçilir. Fareyi
  çizgiden uzaklaştırınca tek bölge seçimine döner. Numara görünürlüğünü, etkin ve
  pasif alan renklerini paletten, kenarlık kalınlığını ve birleştirme mesafesini
  FancyZones ayarlarından değiştirebilirsiniz.
  Izgaradaki tüm bölgeler doluyken bir pencere bırakırsanız pencere büyütülür.
  Fareyi oynatmadan bölge değiştirmek için ok tuşlarını kullanın; **Ctrl+ok**
  seçili pencere alanını bitişik tam satır/sütun boyunca genişletir. Seçim ok
  tuşuyla yapıldıktan sonra fareyi hareket ettirmeniz vurguyu yeniden fareye bağlar.
  İki sütun, üç sütun ve dört bölge hazır düzenlerine ek olarak **Özel ızgara**
  düzenini seçip 1–6 sütun ve 1–4 satır belirleyebilirsiniz. Bölgeler etkin
  monitörün çalışma alanına sığacak şekilde boşlukları sınırlar.
  Farklı ekranlarda farklı özel ızgara kullanmak için **Monitör başına ızgara
  profilleri** alanına `DISPLAY1=3x2; DISPLAY2=2x4` yazın. `DISPLAY1` ve
  `DISPLAY2` kimliklerini Windows **Ayarlar → Sistem → Ekran** sayfasındaki
  numaralardan alın; tanımlanmayan ekranlar genel bölge düzenini kullanır.
  **Bölge etkinleştirme** ayarından Shift kullanmayı veya sürükleme başlayınca
  bölgeleri otomatik göstermeyi seçin. Aynı bölgeye yerleştirdiğiniz pencereler
  arasında `Win+PageDown` (sonraki) ve `Win+PageUp` (önceki) ile geçiş yapın;
  **Aynı bölgedeki pencereler arasında geçiş** ayarından kapatabilirsiniz.
  Etkin pencereyi sürüklemeden komşu bölgeye taşımak için `Win+Ctrl+Alt+ok`
  tuşlarını kullanın; bu dört kısayol **Etkin pencereyi kısayolla komşu bölgeye
  taşı** ayarından kapatılabilir.

**Çalışma Alanları** modülünde görünür masaüstü uygulamalarını ve pencere
konumlarını adlandırılmış bir düzene kaydedip sonra birlikte geri yükleyin.
İsteğe bağlı **Komut satırı bağımsız değişkenlerini kaydet** ayarı dosya yolları
ve uygulama argümanlarını da kaydeder; bazı uygulamalar bu alana hassas değerler
koyabildiği için ayar varsayılan olarak kapalıdır. Geri yükleme aynı argümanları
ayrı parametreler olarak başlatıcıya verir. Klasik masaüstü pencerelerinin yanında
Windows’un uygulama kimliği sağlayabildiği paketlenmiş uygulamalar da yakalanıp
etkinleştirilebilir; uygulamaya özgü açık belge sekmelerinin geri yüklenmesi henüz
desteklenmez.

## Pencere Kırpma

**Pencereler → Crop And Lock · Pencere Kırpma** modülünü etkinleştirin ve
`Win+Ctrl+Shift+T` tuşlarına basın. Etkin pencerenin içinde fareyle bir alan
çizin; seçim tamamlanınca alan ayrı, üstte duran ve yeniden boyutlandırılabilen
bir pencerede canlı olarak görünür. Kaynak pencere güncellendikçe kırpılmış
görüntü de güncellenir. Bu görünüm önizleme amaçlıdır; içindeki tıklamalar kaynak
uygulamaya aktarılmaz. Seçim sırasında `Esc` ile iptal edebilir, küçük pencereyi
kapat düğmesiyle kapatabilirsiniz. Ayarlar panelindeki **Etkin pencereyi kırp**
eylemi aynı akışı başlatır.

## Gezgin Klasör Boyutları

**Gezgin → Gezgin Klasör Boyutları** modülünü etkinleştirip Explorer'da
**Ayrıntılar** görünümünü ve **Boyut** sütununu açın. WinForge sabit yerel
sürücülerdeki klasörleri düşük öncelikli tek bir arka plan iş parçacığında tarar;
en fazla 64 klasörün sonucu önbellekte tutulur ve disk değişiklikleri izlenir.
Ağ ve çıkarılabilir sürücüler, klasör bağlantıları ve bağlantı olarak işaretli
bulut klasörleri taranmaz. Okunamayan, çok büyük veya ayarlanan süreyi aşan
klasörlerin sonucu boş kalır; eksik toplam gösterilmez. Sembolik bağlantılar
izlenmez. Tarama süresi ve önbellek süresi modül ayarlarından değiştirilebilir.

Explorer'a ilk bağlantıda ve Windows bileşeni güncellendiğinde WinForge, kurulu
Windows sürümüne ait hata ayıklama sembollerini Microsoft sunucusundan indirir
ve kimliğini doğrulayarak uygulamanın `data\FolderSizes\Symbols` klasöründe
önbelleğe alır. Eşleşen semboller hazır değilse bağlantı için internet gerekir.
Windows'un iç uygulaması sürümler arasında değişebildiğinden, uyumsuz bir
bağlantı noktası tespit edilirse modül hata bildirerek devreye girmez. Gerçek
Explorer ve yalıtılmış görünüm sınamaları için
`.\scripts\test.ps1 -FolderSizesLive` kullanılabilir.

## Toplu Yeniden Adlandırma

**Gezgin → Toplu Yeniden Adlandırma** aracında birden fazla dosya seçin, arama
ve değiştirme metnini girin ve önizlemedeki çakışmaları giderdikten sonra
uygulayın. Düz metin ve zaman sınırı olan düzenli ifade eşleştirmesi desteklenir.
Dosyalar önce aynı klasörde geçici adlara alınır; işlem tamamlanamazsa araç
başlangıç adlarına dönmeyi dener. En son başarılı toplu işlem kullanıcı
profilindeki geri alma kaydına yazılır ve hedef dosyalar hâlâ uygunsa araç yeniden
açıldığında **Geri al** ile tersine çevrilebilir. Modülü etkinleştirince Gezgin’deki dosya seçimine
sağ tıklayıp **Toplu yeniden adlandır (WinForge)** eylemiyle seçili dosyaları
doğrudan açabilirsiniz.

## Görsel Boyutlandırıcı

**Gezgin → Görsel Boyutlandırıcı** penceresinde PNG, JPEG, BMP, GIF, TIFF ve
WDP/JXR görsellerini seçin. 854, 1280 veya 1920 piksel presetini, özel uzun
kenar sınırını ya da kaynak boyutunu seçin. En-boy oranı korunur ve küçük
görseller büyütülmez. Çıktı kaynak klasöre `_resized` ekiyle yazılır; PNG/JPEG
dönüştürme ve JPEG kalite ayarı kullanılabilir. Mevcut çıktı dosyalarının
üzerine yazılmaz; özgün görseller korunur. **EXIF/IPTC metadatasını koru**
seçeneği aynı codec'te yeniden boyutlandırırken metadata'yı kopyalar; PNG↔JPEG
gibi biçim dönüşümlerinde uyumsuz metadata aktarılmaz. Toplu işlem arka planda
çalışır, dosya başına ilerleme gösterir ve **İptal** ile sıradaki dosyalardan
önce durdurulabilir. O anda yazılan çıktı geçici dosyadan hedef adına atomik
taşınır; iptal taşımadan önce gelirse geçici dosya temizlenir.

## Dosya Kilidi Bulucu

**Gezgin → Dosya Kilidi Bulucu** penceresinde dosyayı seçin. Araç Windows Restart
Manager'a dosya yolunu kaydeder ve bu API'nin ilişkilendirdiği uygulama/hizmetleri
süreç adı ve PID ile gösterir. Etkin modülde bir dosyaya sağ tıklayarak da
**Dosya kilitlerini bul (WinForge)** eylemini açabilirsiniz. **Yeniden tara** ile sorguyu tekrarlayın. Tarama
salt okunurdur. Seçilebilir normal kullanıcı uygulamaları için **Uygulamayı
sonlandır** ayrı bir onay ister, süreç başlangıç kimliğini yeniden doğrular ve
yalnızca o süreci kapatır. WinForge, Windows Gezgini, hizmetler, kritik süreçler
ve başka kullanıcı oturumlarındaki süreçler bu yolla sonlandırılamaz. Kaydedilmemiş
işler kaybolabilir. Bazı süreç bilgilerinin görünürlüğü Windows izinlerine bağlıdır.

## Dosya Önizleme

**Gezgin → Dosya Önizleme** modülünde **Dosya önizle** eylemini kullanın veya
`Win+Alt+P` kısayoluna basın. Modül etkinse dosyaya sağ tıklayıp **WinForge ile
önizle** eylemini de kullanabilirsiniz. Metin/kod dosyaları BOM algılamayla okunur;
metin, görsel ve PDF boyut sınırları ayarlardan değiştirilebilir. Görseller sınırlı
boyutta çözülür. PDF dosyalarında ilk sayfa gösterilir; ses ve video dosyaları
Windows'ta kullanılabilir codec'lerle yerleşik oynatıcıda açılır. Codec bulunamazsa
veya biçim desteklenmiyorsa varsayılan uygulamada açabilirsiniz.

## Fare İşaretçisi

**Fare → Fare İşaretçisi** modülünde `Win+Alt+.` kısayoluyla imleç artısını
açıp kapatın. Artı, imleci geçerli monitörde izler; rengini, saydamlığını, merkez
boşluğunu, çizgi/kenar kalınlığını ve sabit uzunluğunu ayarlayabilirsiniz. Sabit
uzunluk `0` olduğunda çizgiler monitörün kenarlarına kadar uzanır. Özelliği
modül açıldığında etkin başlatmak için **İmleç artısını başlangıçta göster**
ayarını kullanın.

## Ekranlar Arası Fare Taşıma

**Fare → Ekranlar Arası Fare Taşıma** modülünü `Win+Alt+J` ile açın ve
önizlemede gitmek istediğiniz noktaya tıklayın. `1`–`9` ilgili ekranın merkezine,
`P` birincil ekranın merkezine taşır. Ok tuşlarıyla ekran seçip `Enter` ile
merkezine gidebilir veya `Esc` ile kapatabilirsiniz. Ekran görüntüleri
yalnızca önizleme penceresinde gösterilir ve diske kaydedilmez.

## Gelişmiş Yapıştır

**Sistem → Gelişmiş Yapıştır** modülünü `Win+Shift+V` ile açın. Pano içeriğinin
önizlemesini görün, sonra düz metin, Markdown veya JSON biçimini seçin. Markdown
HTML panosundan başlıkları, bağlantıları, kalın/italik yazıyı ve listeleri
dönüştürür. JSON dönüşümü mevcut JSON'u biçimlendirir; CSV, XML ve INI verilerini
JSON nesnelerine çevirir; diğer metinleri satır dizisi yapar. Görselden metin
seçeneği Windows OCR ile yerel çalışır. Metin, HTML veya görseli `.txt`, `.html`,
`.png`, `.jpg`, `.bmp` veya `.gif` dosyası olarak panoya koyup hedef uygulamaya
yapıştırabilirsiniz. PNG kayıpsızdır; JPEG kalite değeri %10–100 aralığında
ayarlanır (varsayılan %90) ve saydam pikseller beyaz arka planla birleştirilir;
BMP ve GIF WPF'nin yerel kodlayıcılarıyla oluşturulur. Dönüşümler görselin piksel
boyutunu korur.
Üretilen dosyalar `%TEMP%\WinForge\AdvancedPaste` klasöründe tutulur; 30 günden
eski WinForge çıktıları yeni bir dosya oluşturulurken temizlenir. Dönüşümler
yereldir; bu akışta AI ve ses/video dönüştürme yoktur.

## Hızlı Başlatıcı

`Win+Alt+Space` penceresinde uygulama adlarını arayın veya mevcut bir dosya ya da
klasörün tam yolunu yazıp `Enter`'a basın. Tırnak içindeki yollar da açılır.
`https`, `http`, `mailto`, `ms-settings:` ve `shell:` URI'leri Windows'un kayıtlı
uygulama işleyicisine gönderilir. Diğer URI şemaları ve mevcut olmayan yollar
başlatıcı tarafından çalıştırılmaz. `=` ile başlayan ifadeler hesap makinesini
kullanır.
`ayarlar`, `ekran`, `ses`, `wifi`, `bluetooth`, `uygulamalar`, `başlangıç`,
`güncelleme`, `pano`, `gizlilik`, `fare` veya `güç` aramaları ilgili Windows
Ayarlar sayfasına giden yerleşik komutları gösterir.
**Çalışma Alanları** modülü etkinse kayıtlı alan adını veya içindeki uygulama
adını arayın; sonucu seçmek alanı yönetici penceresini açıp geri yüklemeyi başlatır.
İsteğe bağlı **Son kullanılan öğeleri göster** ayarı, Windows Son Kullanılanlar
klasöründeki en yeni 20 `.lnk`/`.url` öğesini sonuçların başına ekler. Bu ayar
varsayılan olarak kapalıdır; kısayollar seçildiğinde Windows'un kendi dosya
ilişkilendirmesiyle açılır.
Varsayılan dizinlere ek klasör eklemek için **Ek arama klasörleri** ayarına
tam yolları `;` ile ayırarak yazın; `%USERPROFILE%` gibi ortam değişkenleri
desteklenir. **Dosya türü filtresi** ayarına `pdf, docx` veya `*.pdf;*.docx`
yazın. Boş değer tüm dosya türlerini indeksler; klasörler ve Başlat menüsü
kısayolları filtre dışında tutulur. Özel klasörlerde gizli, sistem ve yeniden
ayrıştırma noktası dizinleri taranmaz; indeks 25.000 öğeyle sınırlıdır.
İsteğe bağlı **Dosya içeriklerinde ara** ayarı, desteklenen metin dosyalarının
ilk 128 KB bölümünde arar; 3 karakterden kısa sorgularda başlamaz ve sonuçları
yalnızca bellekte tutar. İçerik araması varsayılan olarak kapalıdır; arama yalnızca
indeksteki kullanıcı klasörleri ve eklediğiniz köklerle sınırlıdır.

## Ortam Değişkenleri

**Sistem → Ortam Değişkenleri** modülünü etkinleştirip **Kullanıcı
değişkenlerini düzenle** eylemini açın. Değişken ekleyin, ad/değer değiştirin,
genişletme türünü seçin veya satırı silin; **Kaydet** yalnızca geçerli
kullanıcının `Environment` kayıt defteri anahtarını günceller ve Windows'a ortam
değişikliği bildirimini gönderir. `%TEMP%` gibi ifadeler genişletilebilir türde
saklanabilir. **Değişken profillerini yönet** eyleminde profiller oluşturup
değişken kümelerini kaydedin; aynı anda tek profil etkin olur ve kapatınca
önceki kullanıcı değerleri geri yüklenir. Sistem değişkenlerini düzenlemek için
**Sistem değişkenlerini düzenle (yönetici)** eylemini açın. Değerleri hazırlayıp
kaydettiğinizde Windows yönetici onayı ister; kayıt başarısız olursa önceki
sistem değerleri geri yüklenir.

## Pencere Kontrolü

**Verimlilik → Pencere Kontrolü** WinForge'un kendi Windows fare kancası ve
pencere yerleştirme motorunu kullanır. Sol Alt + sol tuş pencereyi taşır;
Sol Alt + sağ tuş imlecin bulunduğu köşeden boyutlandırır; orta tuş pencerenin
sistem menüsünü açar. Etkinleştirme tuşu, alternatif tuş ve beş fare düğmesi
uygulamanın kendi ayar panelinden yapılandırılır. Varsayılan sol Alt seçimi
AltGr ile yazmayı korur.

Tekerleğe ses veya saydamlık eylemi bağlanabilir. Ekran kenarlarına oturtma,
köşelerde çeyrek alanlar, özelleştirilebilir bölme oranları ve pencere kenarlarına
mıknatıslanma desteklenir. Boyutlandırma uygulamanın minimum ölçülerini korur;
çoklu monitörlerde negatif koordinatlar ve görev çubuğu çalışma alanı dikkate
alınır. `Esc` devam eden hareketi iptal eder. Modül kapatılırken kanca kaldırılır,
yarım kalan hareket geri alınır ve değiştirilen çerçeve/saydamlık değerleri
geri yüklenir. Normal taşıma ve boyutlandırma sonuçları yerinde kalır.

Dışlama listelerinde exe adı veya `başlık|sınıf` kalıpları kullanılabilir;
`*` ve `?` jokerleri desteklenir. Masaüstü, görev çubuğu, WinForge pencereleri
ve varsayılan olarak tam ekran uygulamalar işleme alınmaz. Yönetici pencereleri
için WinForge'un da yönetici olarak çalışması gerekir. Bölge yerleşimleri için
mevcut **FancyZones · Bölgeli Pencere Taşıma** modülü kullanılır. Eski motorun INI düzenleyicisi,
süreç duraklatma ve özel parametreli eylemleri sunulmaz; taşınan ayarlardaki
bilinmeyen eylemler güvenli varsayılanlarına döner.
## YouTube eklentisi

YouTube tarayıcı özellikleri WinForge bileşenlerinden çıkarıldı ve Playlist Atölyesi tarayıcı eklentisine taşındı.

## Mimari

### Ekran geçmişi, sesle yazma ve ekran yakalama

Üç yeni bağımsız modül **Verimlilik** kategorisindedir ve ilk kurulumda kapalıdır.
**Ekran Geçmişi** yerel OCR araması, zaman sıralı görüntüler ve gerçek pencere geçişlerinden
ölçülen kullanım süreleri sunar. **Sesle Yazma**, `Shift+Space` ile yerel Whisper diktesini
hedef alana yazar; basılı tutma, Turbo/Large, CPU/CUDA ve İngilizceye çeviri desteklenir.
**Ekran Yakalama**, `Win+Alt+S` ile bölge seçimi; ayrıca pencere/monitör, kayan yakalama,
düzenleme, OCR, video/GIF ve mikrofon/sistem sesi kaydı sağlar.

RecallClone ve Whisper geçmişleri modül panelindeki **Eski kayıtları içe aktar** eylemiyle
aktarılır. Kaynaklar korunur; modeller ve görseller WinForge deposuna kopyalanır.
Otomatik silme varsayılan olarak kapalıdır. Ayrıntılar ve doğrulama komutları:
[Ekran ve ses modülleri](docs/CAPTURE_FEATURES.md).

```
WinForge.Core     ITweakModule sözleşmesi, modül yükleme, ayar deposu,
                  global kısayol, fare kancası, ses (Core Audio COM), OSD
WinForge.App      WPF uygulama kabuğu ve tepsi; React/WebView2 arayüzü,
                  modül listesi ve ayar paneli
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

Görev çubuğunun yerleşik araçları Windows API'leri ve kayıt defteriyle çalışır:
saat/saniye, tekerlekle ses, orta tuşla kapatma, simge boyutu, saydamlık/vurgu
rengi ve tek tek tepsi simgesi görünürlüğü. Bazı Windows 11 sürümleri eski
simge boyutu ayarını veya korumalı widget kayıt değerlerini uygulamayabilir.

Harici kabuk motoru desteği kaldırıldı. WinForge'un kendi C++ kabuk köprüsü,
Windows XAML tanılama arabirimiyle gerçek görev çubuğuna bağlanır; özgün değerleri
ve bağlamaları saklar, kapatılınca veya uygulamanın yaşam sinyali kesilince geri yükler.
Başlat görünürlüğü, renkler, simge/düğme boyutları, özel yükseklik ve yerel saat
biçimleri gerçek Explorer üzerinde sınanır. Tema seçenekleri WinForge'un renk
düzenleridir. Dikey/üst görev çubuğu ve dock büyüme animasyonu henüz
uygulanmamıştır. Ayrıntılı kapsam ve gerçek test kanıtları
[özellik doğrulama belgesinde](docs/FEATURE_VERIFICATION.md) bulunur.
Otomatik döşeme yalnızca normal üst düzey pencerelere uygulanır: büyütülmüş
(azami) pencereler, tam ekran uygulamalar, araç pencereleri, WinForge'ın kendi
pencereleri ve minimum boyut kısıtı olan uygulamalar atlanır. Boşluk ayarı
yalnızca pencereler arasında uygulanır; DWM görünmez çerçevesi telafi edilerek
pencereler ekran kenarlarına ve birbirine sıfıra sıfır oturur. Kapatınca pencereler son konumunda kalır.


## Bağımlılık politikası

WinForge; Windhawk, AltSnap, PowerToys veya NAudio motorunu çalıştırmaz, indirmez
ve dağıtmaz. Pencere kontrolü, ses yakalama ve analiz, WMI erişimi ve kısayollar
WinForge koduyla Windows'un Win32/COM/WinRT arayüzlerine bağlanır. Çalışma
platformu Windows ve .NET 10 Desktop/WPF'tir. Ana kontrol arayüzü React ve
TypeScript ile geliştirilir, uygulama içinde Microsoft WebView2 ile gösterilir.

Nashi ve ClipboardLine entegrasyonu bu genel bağımsızlık kuralının bir istisnasıdır:
WinForge içinde çalışan modüller üçüncü taraf PDF, görsel, arşiv, OCR ve ONNX
kütüphanelerini kullanır. Bazı medya ve belge işlemleri için kullanıcının kurduğu
veya Nashi araç kurulumuyla indirilen harici araçlar gerekir. Çevrimdışı görsel
arama modelleri derleme öncesinde `scripts/prepare-models.ps1` tarafından indirilir
ve SHA-256 ile doğrulanır. Üçüncü taraf kaynak ve lisans bilgileri
[lisans bildirimlerinde](docs/THIRD_PARTY_NOTICES.md) bulunur.
`Directory.Build.targets` doğrudan paket referanslarını yalnızca tanımlı projelerde
kabul eder.

### Masaüstü arayüz mimarisi

WinForge'un kullanıcı arayüzü React ve TypeScript bileşenlerinden oluşur; Vite bunları
derler, uygulama da yerel dosyaları Microsoft WebView2 içinde yükler. WPF uygulama
başlangıcı, pencere yaşam döngüsü, tepsi simgesi ve Windows'a özgü pencere/ekran
işlemleri için host katmanı olarak kalır. Modül mantığı ve sistem servisleri .NET'te
kalır; arayüz ile servisler sınırlı JSON mesajları ve durum güncellemeleri üzerinden
konuşur. Electron kullanılmaz, çünkü mevcut hedef Windows ve WebView2 zaten
uygulamanın arayüzünü çalıştırır; Electron eklemek ikinci bir masaüstü çalışma zamanı
ve ayrı bir ana-süreç/IPC katmanı getirir.

| Katman | Kullanılan teknoloji | Sorumluluk |
| --- | --- | --- |
| Görsel arayüz | React, TypeScript, CSS | Sayfalar, pencereler, durum ve kullanıcı etkileşimi |
| Masaüstü host | WPF, WebView2 | Pencere yaşam döngüsü, yerel UI barındırma ve mesaj köprüsü |
| İş mantığı | .NET modülleri | Dosya, medya, OCR, kısayol ve Windows servisleri |
| İşletim sistemi entegrasyonu | Win32, COM, WinRT | Ekran, imleç, görev çubuğu ve pencere koordinatları |

Bu seçim mevcut C# modüllerini korur, React bileşenlerini uygulama pencereleri arasında
paylaştırır ve Electron'a özgü ayrı bir uygulama süreci gerektirmez. Karşılığında
uygulama Windows ve WebView2 Runtime'a bağlıdır; çoklu monitör/DPI, şeffaf ekran
katmanları ve medya çizim yüzeyleri gerektiğinde native host kodu taşımaya devam eder.
Yeni normal ekranlar React görünümü olarak eklenir; native katman yalnızca Windows
özellikleri veya yüksek etkileşimli çizim alanı gerçekten gerektiğinde kullanılır.

Eski AltSnap modülünün WinForge'da kayıtlı ortak ayarları `window-control`
modülüne taşınır. Yeni modülün mevcut ayarları önceliklidir. Kaldırılan Windhawk
modülünün kayıtları yeniden etkinleştirilmez. Kullanıcının ayrıca kurduğu
programlara ve onların ayarlarına müdahale edilmez.


### Taşınabilir veri

WinForge ayarlarını, geçmişini, ekran görüntülerini, ses modellerini ve diğer uygulama verilerini çalıştırılabilir dosyanın yanındaki `data` klasöründe saklar. Taşımak için program klasörünün tamamını `data` ile birlikte kopyalayın; klasör yazılabilir olmalıdır. Eski varsayılan veri klasörleri ve özel geçmiş klasörleri ilk açılışta kopyalanır; farklı geçmiş veritabanları ayrı tutulur. Eski dosyalar silinmez. Windows başlangıç kaydı ve sistem ayarları bilgisayara özeldir ve başka bilgisayarda yeniden ayarlanmalıdır. Dışa aktarılan dosyalar seçilen konumda kalır.
