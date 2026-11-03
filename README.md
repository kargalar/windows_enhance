**Modül mimarisi:** Özellikler ana WinForge sürecinde DLL modülleri olarak çalışır. Dikte motoru da artık uygulama içindedir; ayrı worker EXE ve named pipe kaldırıldı. Kabuk bağlantısı, PDF ve medya araçlarının süreç sınırları ve doğrulama bilgileri: [Çalışma mimarisi](docs/RUNTIME_ARCHITECTURE.md).

**Panel ve kısayol güncellemesi:** Açılır paneller farenin bulunduğu ekranda açılır. Ayarlar ve kısayol kartları otomatik kaydedilir. Medya Kısayolları’na **MouseX1 → sesi 5 azalt**, **Tuş Kombinasyonu Çevirici** modülüne **Ctrl+D → Alt+W** örneği eklendi. Otomasyon kartlarında isteğe bağlı odak exe’si; program kısayollarında Gezgin klasör yolu desteği bulunur. [Kullanım ve testler](docs/SHORTCUT_ACTIONS.md).

# WinForge

Son modül düzeltmeleri, klasör seçenekleri ve doğrulama sınırları: [Modül düzeltmeleri](docs/MODULE_FIXES.md).

**Gezgin → Sağ Tık Menüsü** artık mevcut simgeleri, okunabilir adları, özel simge atamasını ve otomatik kaydedilen alt menü gruplarını içerir. Özellikler ve Yeni klasör gibi yerleşik komutlar da gruplandırılabilir. [Kapsam ve kullanım](docs/CONTEXT_MENU.md).

**Verimlilik → Yerel kısayol modülleri:** monitör düzenleri, medya/ses, pencere, sanal masaüstü, şifreli metin yazma ve program açma için ayrı kartlar eklendi. Klavye, fare düğmeleri ve tekerlek kombinasyonları atanabilir. Monitör düzenleri doğrudan Windows üzerinden kaydedilip uygulanır; harici program veya cfg dosyası gerekmez. [Kullanım ve doğrulama](docs/SHORTCUT_ACTIONS.md).

## Kısayol komutları ve otomatik tıklama

Aç/kapat şeklinde tekrarlanan otomasyonlar çalıştıkları sürece ekranın sol üstünde **F1 aktif** gibi bir göstergeyle görünür. Birden fazla çalışan otomasyon ayrı satırlarda listelenir. Gösterge odağı almaz ve fare tıklamalarını engellemez; durdurma, duraklatma, ayar değişikliği veya modülün kapanmasıyla temizlenir. Tek seferlik komutlar bu kalıcı göstergeye eklenmez.

**Verimlilik → Otomasyon · Kısayol Komutları** içindeki otomasyon kartları doğrudan ana panelde düzenlenir; ayrı bir düzenleyici penceresi yoktur. Önceki metin komutları otomatik olarak kartlara aktarılır.

Her kartta ayrı **aç/kapat anahtarı**, **başlat/durdur kısayolu**, **çalışma şekli**, **tekrar aralığı** ve **komut adımları** bulunur. Kısayolu yazabilir veya **Tuş ata** ile kaydedebilirsiniz. Aralığı kaydırıcıyla ya da sayı alanına örneğin **500** yazarak değiştirebilirsiniz (10–3600000 ms). Sayı alanı ve kaydırıcı değişiklikleri otomatik kaydedilir. Alanlar yazmayı bıraktıktan yaklaşık 450 ms sonra otomatik kaydedilir; Kaydet düğmesi gerekmez. Yeni otomasyonlar kapalı eklenir.

Hazır **F1** otomasyonu yaklaşık 100 ms aralıkla sol tıklar; F1 tekrar basılınca durur. Kısayolunu, sol/sağ/orta tıklama komutunu ve aralığını ayrı ayrı değiştirebilirsiniz. **Alt+S** otomasyonu seçili metni kopyalayıp varsayılan tarayıcıda Google’da bir kez arar; seçim kopyalanamazsa eski pano içeriğini aramaz. Tuş, metin, bekleme ve Google araması adımları da eklenebilir. Google araması yalnızca tek seferlik çalışır.

Panelin üstündeki anahtar veya varsayılan **Ctrl+Alt+F8** genel kısayolu tüm otomasyonları duraklatır/devam ettirir. Bu kısayol değiştirilebilir. Duraklatma çalışan komutları durdurur ve otomasyon kısayollarını serbest bırakır; genel devam kısayolu kayıtlı kalır. Devam etmek komutları otomatik başlatmaz, her otomasyon kendi kısayolunu bekler. **Çalışan tüm komutları durdur** düğmesi de tüm dizileri durdurur.

Kısayollar tam eşleşir: **Shift+F1 düz F1 otomasyonunu tetiklemez**. Ctrl, Alt, Shift veya Win basılıyken komutlar bekler. Ayar değişikliği, modülün kapanması ve uygulamadan çıkış çalışan komutları durdurur. Kısayol kaydederken kayıtlı kısayollar geçici olarak bırakılır; Esc ile iptal edilir ve 20 saniyede otomatik geri açılır. Geçersiz/kullanımdaki kısayollarda önceki yapılandırma korunur. Bu modül AutoHotkey betik dili yorumlayıcısı değildir.

Doğrulama: `dotnet run --project tests/WinForge.AutomationTests -c Release -p:Platform=x64`. Testler ayrıştırma, zamanlama, başlat/durdur, temizleme ve seçili metin aramasını kapsar. Gerçek Windows kısayollarında ek değiştirici / tuş tekrarı testleri için klavyeye dokunmadan `-- --native-hotkeys` ekleyin.

**Gezgin → Modern Klasör Seçici** klasik “Browse For Folder” penceresini modern Windows klasör seçicisiyle değiştirir. [Kapsam ve doğrulama](docs/FOLDER_DIALOG.md).

**Başlat → Başlat Menüsü Tasarımı → Görünüm → Simge altındaki adları gizle** seçeneği, sabitlenmiş uygulama ve klasörlerin adlarını gizleyerek yalnızca simgelerini gösterir. Simge boyutu ve yalnızca sabitlenmiş uygulamaları gösterme ayarlarıyla birlikte kullanılabilir; seçenek veya modül kapatılınca adlar geri gelir.

**Başlat → Başlat Menüsü Tasarımı → Bölümler → Yalnızca sabitlenmiş uygulamaları göster** seçeneği, yeni Windows 11 Başlat menüsündeki **Tüm uygulamalar / All** başlığını, görünüm seçicisini, uygulama listesini/kategorilerini ve önerileri gizler. Sabitlenmiş uygulamalar görünür kalır. Seçenek veya modül kapatıldığında özgün görünüm geri yüklenir.

Aynı modüldeki **Boyut → Uygulama simgesi boyutu (0: Windows)** ayarı, sabitlenmiş uygulamaların ve Tüm uygulamalar bölümünün simgelerini **16–48 mantıksal piksel** arasında değiştirir. **0** özgün boyutları geri yükler. Klasörlerin küçük önizleme simgeleri korunur; ayar yalnızca sabitlenmiş uygulamaları gösterme seçeneğiyle birlikte de çalışır.

## Arayüz

47 modülün detayları seçenek yapısına göre 79 ayrı sayfa ve iki kullanım paneliyle tasarlanmıştır. Fare eylemleri karşılaştırmalı tablo; konumlar ekran haritası; günlük planlar saat seçici; kelime, komut ve ekran profilleri düzenlenebilir listeler kullanır. Kısayol ve otomasyonda kayıt listesiyle seçili düzenleyici ayrıdır. Modül içinde **Ctrl+F** ile ayar ve yönetim kaydı aranır. Kayıt durumu disk yazma onayına göre gösterilir. Açık, koyu ve grafit temalar ortak renk değişkenlerini kullanır. Araç pencereleri ve OSD’ler kendi Island yüzeylerini korur. [Arayüz mimarisi, modül tasarımları ve doğrulama](docs/UI_DESIGN.md).

Windows 11 için kendi motorlarını kullanan modüler kişiselleştirme kutusu. Her özellik ayrı bir modüldür,
uygulama içinden tek tek açılır/kapatılır, ayarları kendi panelinden düzenlenir.

## Şu an çalışan modüller

| Modül | Ne yapar |
|---|---|
| **Görev Çubuğu** | Tek panelde fare/ses kısayolları, Windows görev çubuğu tercihleri, Başlat ve sistem simgelerini gizleme (ses, ağ, pil, mikrofon, konum, Studio Effects, Recall, dil, bildirim zili), masaüstünü göster düğmesi görünürlüğü/genişliği, dock büyüme animasyonu, renk ve boyutlar, saat/tarih biçimleri ve uygulama tepsisi seçimleri. Önceki modüllerin ayarları ve açık/kapalı durumları otomatik aktarılır. |
| **Bildirim Konumu** | Tanınan Windows bildirim pencerelerini ekran/köşe ayarına göre taşır ve kapatılınca özgün sınırlarını geri yükler; gerçek Windows bildirimi doğrulamasının durumu doğrulama belgesinde tutulur |
| **Chrome Enhance** | Tam ekran çıkış uyarısını özellik anahtarıyla gizler; Chrome ve Chromium tabanlı seçili tarayıcılarda çalışır |
| **İnternet Hızı** | Anlık indirme/yükleme hızı görev çubuğunun üstünde (isteğe bağlı olarak içinde), bildirim alanına hizalı; otomatik/MB/s/Mbit/s birim, ayarlanabilir yenileme aralığı, taskbar gizliyken otomatik gizlenme |
| **Her Zaman Üstte** | `Alt+F` ile aktif pencereyi üste sabitler, aynı kısayol bırakır; sabitlenen pencerenin çevresine renkli çerçeve çizer |
| **Ekrandan Metin Al** | `Alt+Q` ile bölge seçer, OCR ile metni panoya kopyalar |
| **Tam Ekran Pencere Yönetimi** | Açık pencereleri Ana + yığın, Izgara veya Sütunlar düzeninde otomatik yerleştirme; boşluk, ana pencere genişliği ve küçültmeyi engelleme ayarları |
| **FancyZones · Bölgeli Pencere Taşıma** | Yeni pencereleri en az dolu bölgeye otomatik yerleştirme; Shift ile sürükleyerek pencereyi iki, üç, dört ya da özelleştirilebilir 1–6 sütun × 1–4 satır ızgaraya taşıma; çizgi yakınında komşu bölgeleri, dört alan kesişiminde tüm alanları birleştirme; numara, renk, kenarlık kalınlığı ve birleştirme mesafesi ayarları; çoklu monitör ve ekrana göre ızgara profilleri |
| **Crop And Lock · Pencere Kırpma** | `Win+Ctrl+Shift+T` ile etkin pencerenin bir alanını sürükleyerek seçer ve alanı canlı, yeniden boyutlandırılabilir küçük pencerede gösterir; kaynak pencereye tıklama göndermez |
| **Pencere Kontrolü** | WinForge motoruyla Alt + fare ile taşıma/boyutlandırma; çoklu monitör, kenarlara oturtma, mıknatıslanma, ayarlanabilir eylemler ve uygulama/pencere istisnaları |
| **Açık / Koyu Tema** | `Alt+Shift+L` ile temayı değiştirir veya isteğe bağlı yerel saat planında 07:00 / 19:00 varsayılanlarına göre otomatik geçiş yapar |
| **Renk Seçici** | `Win+Alt+C` ile seçim modunu açar; tıkladığınız noktanın rengini HEX, RGB veya HSL olarak panoya kopyalar |
| **Ekran Cetveli** | `Ctrl+Alt+M` ile alan seçip piksel, santimetre veya inç ölçüsünü panoya kopyalar |
| **Fare** | Hızlandırma, kaydırma satırı ve çift tık hızı; çift Ctrl ile fareyi bulma, tıklama vurguları ve `Win+Alt+.` imleç artısı tek modülde. Önceki ayarlar ve etkin durumlar otomatik aktarılır. |
| **Arayüz İnce Ayarları** | Pencere animasyonları, menü gecikmesi, görev çubuğu animasyonları ve Aero Shake |
| **Klasik Sağ Tık Menüsü** | Windows 11'in kısa menüsü yerine Windows 10'un tam sağ tık menüsünü kullanır |
| **Sağ Tık Menüsü** | Gezgin menüsüne özel öğeler ekler ve kayıtlı kabuk uzantılarını gizler; yerleşik komutlar ve diğer uygulamaların kendi menüleri kapsam dışı |
| **Gezgin İnce Ayarları** | Uzantılar, gizli dosyalar, son/sık kullanılanlar, kompakt mod, tam yol, onay kutuları, ayrı süreç ve masaüstü simgeleri |
| **Gezgin Sol Panel Tasarımı** | Yatay/dikey padding, yazı tipi/boyutu/kalınlığı, panel/yazı/seçim/hover renkleri, simge boyutu ve aralığı, girinti, seçim kenar boşluğu ve köşeleri; Home, Galeri, OneDrive, Kitaplıklar ve Ağ öğelerini gizleme |
| **Gezgin Klasör Boyutları** | Ayrıntılar görünümünde yerel klasörlerin boyutlarını arka planda hesaplar, boyuta göre sıralar ve dosyalar değiştikçe günceller |
| **Toplu Yeniden Adlandırma** | Çoklu dosya seçimi, metin/regex önizlemesi, çakışma denetimi ve en son işlemi pencere/uygulama yeniden açıldıktan sonra da geri alma |
| **Görsel Boyutlandırıcı** | Birden fazla görsel için 854/1280/1920 px veya özel uzun kenar; en-boy oranı, PNG/JPEG biçimi ve JPEG kalitesi korunur; aynı biçimde EXIF/IPTC metadata aktarımı seçilebilir; arka planda ilerleme/iptal; özgün dosyalar saklanır |
| **Dosya Kilidi Bulucu** | Windows Restart Manager ile kilit kullanan uygulama ve hizmetleri listeler; kullanıcı uygulamaları ayrıca onayla sonlandırılabilir |
| **Başlat Menüsü** | Tasarım, boyut, görünür bölümler, öneriler, yeni uygulama bildirimleri, son kullanılan öğeler ve Windows arama temizliği tek modülde. Önceki ayarlar ve etkin durumlar otomatik aktarılır. |
| **Varsayılan Yollar** | Masaüstü, Belgeler, İndirilenler, Resimler, Müzik ve Videolar konumlarını onayla değiştirir; ilk konuma dönüş kaydı saklar; yeni uygulama/içerik depolama ayarlarını açar |
| **Varsayılan Programlar** | Kayıtlı dosya uzantılarını ve geçerli uygulamalarını arar; uzantıyı kopyalayıp program seçimi için Windows varsayılan uygulamalar ekranını açar |
| **Power Display · Ekran Denetimi** | Dahili panel parlaklığını Windows WMI, harici ekranlarda desteklenen parlaklık/kontrast/ses/giriş/renk denetimlerini DDC/CI üzerinden yönetir; profilleri ve gündüz/gece zamanlamasını destekler, imleçteki harici ekran için isteğe bağlı parlaklık kısayolları sunar |
| **Fluent Medya Flyout’u** | `Win+Alt+M` ve `Ctrl+Alt+V`; ana ses/sessiz denetimi, donanım ses tuşlarında alt-orta ses OSD’si ve medya tuşlarında flyout; Windows Core Audio üzerinden algılanabilen harici kulaklık/aygıt ses değişimlerinde OSD; isteğe bağlı görev çubuğu üstü mini oynatıcı (sağ tıkta medya/ses/oynatıcı eylemleri ve isteğe bağlı ana ses tekerlek kontrolü), WASAPI çıkış sesinden gerçek zamanlı spektrum görselleştiricisi, filtrelenebilir medya uygulamaları ve oturum seçimi/sabitleme, albüm kapağı, oynat/önceki/sonraki, parça arama ve shuffle/repeat, kilit tuşu OSD’si, köşe ve monitör seçimi, siyah koyu görünüm, belirme animasyonu, etkinlik sonrası otomatik kapanma ve tam ekran uygulama bastırması |
| **Başlangıçta Aç** | Windows oturum açtığında WinForge'u arka planda (tepside) başlatır |

Kısayollar: `Alt+F` her zaman üstte · `Alt+Q` ekrandan metin · `Alt+Shift+L` tema ·
`Win+Alt+C` renk seçici · `Ctrl+Alt+M` ekran cetveli ·
`Ctrl` çift basış fareyi vurgular.

**Görünüm → Açık / Koyu Tema** modülünde **Temayı saate göre değiştir**
ayarını açıp `HH:mm` biçiminde açık ve koyu tema saatlerini belirleyin. Plan yerel
saati kullanır, gece yarısını aşan aralıkları destekler ve dakika başına en çok
bir kez denetler. Zamanlama kapalıyken `Alt+Shift+L` ile manuel geçiş yapılır.

**Sistem → Varsayılan Yollar** panelinde mevcut bir hedef klasör seçip **Uygula**
ve ardından **Konumu değiştir** düğmesine basın. Bu işlem yalnızca Windows’un
kullanıcı klasörü konumunu değiştirir; mevcut dosyaları taşımaz. İlk konum
WinForge ayarlarında saklanır ve **Önceki konuma dön** ile geri uygulanabilir.
Modülü kapatmak değişikliği geri almaz. Yeni uygulamalar için depolama düğmesi
Windows’un yeni içeriklerin kaydedileceği yer ayarını açar; klasik masaüstü
programlarının kurulum klasörü yükleyicilerinden seçilir.

**Sistem → Varsayılan Programlar** panelinde `.pdf` gibi bir uzantı arayın.
**Kopyala ve program seç** uzantıyı panoya kopyalar ve Windows’un varsayılan
uygulamalar sayfasını açar. Dosya türü aramasına yapıştırıp istediğiniz programı
seçin; ardından WinForge panelindeki **Yenile** düğmesine basın. Eşleştirmeler
Windows’un desteklediği kullanıcı seçimi akışıyla değiştirilir.

Bu iki modülün Windows ayarlarını değiştirmeyen kontrolleri:
`dotnet run --project tests/WinForge.DefaultSettingsTests -p:Platform=x64`.

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

**Sistem → Fluent Medya Flyout’u** modülünü etkinleştirip `Win+Alt+M` veya
`Ctrl+Alt+V` ile açın. İki kısayol da modül ayarlarından ayrı ayrı
kapatılabilir. Donanım ses (açma/kısma/sessiz) tuşları ekranın alt ortasında ses
OSD’sini ve medya panelini, medya (oynat/duraklat, önceki/sonraki) tuşları medya
flyout’unu gösterir.
Bu tuşların Windows’a giden normal işlevi engellenmez. Davranış,
**Donanım ses ve medya tuşlarında bildirimleri göster** ayarından kapatılabilir.
Ses ve medya panelleri birlikte açılır ve ortak süre sonunda birlikte kapanır.
Panellerden biriyle etkileşim sürerken ikisi de açık kalır. Medya oturumu yoksa
yalnızca ses paneli gösterilir; duraklatılmış ve arka plandaki medya da desteklenir.
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
medya oturumu seçicisi, geçerli parça denetimleri için
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
en fazla 256 klasörün sonucu önbellekte tutulur ve disk değişiklikleri izlenir.
Boyuta göre artan veya azalan sıralamada klasörler kendi aralarında hesaplanan
boyutlarına göre sıralanır; sonuçlar değiştiğinde sıralama güncellenir.
Ağ ve çıkarılabilir sürücüler, klasör bağlantıları ve bağlantı olarak işaretli
bulut klasörleri taranmaz. Okunamayan, çok büyük veya ayarlanan süreyi aşan
klasörlerin sonucu boş kalır; eksik toplam gösterilmez. Sembolik bağlantılar
izlenmez. İzleyici çalışırken önbellekteki sonuçlar süre dolduğu için yeniden
taranmaz; yalnızca değişiklik bildirimleriyle güncellenir. Ayarlanabilir önbellek
süresi, izlenemeyen sonuçlar ve başarısız taramaların yeniden denenmesi içindir.
Boyuta göre sıralama istendiğinde ilgili taramalar öncelik kazanır. Tamamlanan
bir üst klasör taraması alt klasör boyutlarını da önbelleğe alır; klasör içeriği
değiştiğinde yalnız etkilenen alt ağaç yeniden taranıp toplamlar güncellenir.
Anlık görüntüler ve izleyiciler bellek sınırlarına tabidir. Anlık görüntü eksikse,
değişiklik bildirimi taşarsa veya yeni alt ağaç güvenle birleştirilemezse WinForge
ilgili klasörü baştan tarar; bu sırada eksik boyut göstermez.

Explorer'a ilk bağlantıda ve Windows bileşeni güncellendiğinde WinForge, kurulu
Windows sürümüne ait hata ayıklama sembollerini Microsoft sunucusundan indirir
ve kimliğini doğrulayarak uygulamanın `userdata\FolderSizes\Symbols` klasöründe
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

**Gezgin → Dosya Kilidi Bulucu** modülünü etkinleştirin. Bir dosya veya klasöre
sağ tıklayıp **Dosya kilitlerini bul (WinForge)** seçeneğini seçtiğinizde tarama
doğrudan açılır. Windows 11'de bu seçenek **Daha fazla seçenek göster** altındadır.
Araç Windows Restart Manager'a dosya yollarını kaydeder ve bu API'nin ilişkilendirdiği
uygulama/hizmetleri süreç adı ve PID ile gösterir. Klasörlerde erişilebilir alt
klasörler dahil en fazla 5.000 dosya taranır; bağlantı noktaları izlenmez.
**Yeniden tara** ile sorguyu tekrarlayın. Tarama
salt okunurdur. Seçilebilir normal kullanıcı uygulamaları için **Uygulamayı
sonlandır** ayrı bir onay ister, süreç başlangıç kimliğini yeniden doğrular ve
yalnızca o süreci kapatır. WinForge, Windows Gezgini, hizmetler, kritik süreçler
ve başka kullanıcı oturumlarındaki süreçler bu yolla sonlandırılamaz. Kaydedilmemiş
işler kaybolabilir. Bazı süreç bilgilerinin görünürlüğü Windows izinlerine bağlıdır.

## Fare İşaretçisi

**Fare → Fare → Fare işaretçisi** bölümünde `Win+Alt+.` kısayoluyla imleç artısını
açıp kapatın. Artı, imleci geçerli monitörde izler; rengini, saydamlığını, merkez
boşluğunu, çizgi/kenar kalınlığını ve sabit uzunluğunu ayarlayabilirsiniz. Sabit
uzunluk `0` olduğunda çizgiler monitörün kenarlarına kadar uzanır. Özelliği
modül açıldığında etkin başlatmak için **İmleç artısını başlangıçta göster**
ayarını kullanın.

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

### PC geçmişi, sesle yazma ve ekran yakalama

Üç yeni bağımsız modül **Verimlilik** kategorisindedir ve ilk kurulumda kapalıdır.
**PC Geçmişi** yerel OCR araması, zaman sıralı görüntüler ve gerçek pencere geçişlerinden
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

WinForge ayarlarını, geçmişini, ekran görüntülerini, ses modellerini ve diğer uygulama verilerini çalıştırılabilir dosyanın yanındaki `userdata` klasöründe saklar. Taşımak için program klasörünün tamamını `userdata` ile birlikte kopyalayın; klasör yazılabilir olmalıdır. Eski `data` klasörü, AppData altındaki eski veri klasörleri ve özel geçmiş klasörleri ilk açılışta kopyalanır; mevcut `userdata` dosyalarının üzerine yazılmaz; farklı geçmiş veritabanları ayrı tutulur. Eski dosyalar silinmez. Windows başlangıç kaydı ve sistem ayarları bilgisayara özeldir ve başka bilgisayarda yeniden ayarlanmalıdır. Dışa aktarılan dosyalar seçilen konumda kalır.
