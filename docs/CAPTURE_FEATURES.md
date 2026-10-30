# Ekran ve ses modülleri

## Kullanım

**Verimlilik** kategorisinde Ekran Geçmişi, Sesle Yazma ve Ekran Yakalama ayrı ayrı etkinleştirilir.
Yeni kurulumda üçü de kapalıdır. Geçmiş görüntülemek veya eski kayıtları aktarmak kayıt başlatmaz.

| Modül | İş akışı |
|---|---|
| Ekran Geçmişi | Varsayılan 60 saniye ve etkin pencere değişiklikleri; imlecin monitörü; WebP kalite 80 veya PNG; boşta/kilit/uyku duraklatması; uygulama/pencere dışlama; arama, tarih, uygulama ve etiket filtreleri; uygulama, web sayfası ve klasör süreleri |
| Sesle Yazma | `Shift+Space` kısa basışla başlat/durdur, 300 ms veya daha uzun basılı tutup bırakınca durdur; Esc ile iptal; mikrofon, dil, Turbo/Large ve CPU/otomatik seçim; İngilizce çeviri için Large |
| Ekran Yakalama | `Win+Alt+S` bölge seçimi; panelden monitör, pencere, masaüstü, son bölge, kayan yakalama, video/GIF; pencereye gelince kenarlıkla mıknatıslı seçim, tıklamayla pencere veya sürüklemeyle serbest bölge |

Dikte sonuçları yalnız kayıt başındaki düzenlenebilir alan hâlâ odaktaysa yazılır. Hedef değişirse
metin geçmişte kalır. Ses bellekte tutulur; ham dikte sesi dosyaya kaydedilmez. GPU başarısızsa
CPU'ya geçilir ve sonraki aynı ayarlardaki kayıtlar yüklenen CPU modelini kullanır.

Yakalama panelinde tam ekran üzerinde metin, şekil veya serbest çizim başlatan düğmeler vardır;
sonrasında düzenleyicide “Alanı seç / kırp” ile istenen kısım alınabilir. Bölge seçicide
imlecin altındaki pencere altın kenarlıkla belirir; tıklama pencerenin tamamını, sürükleme
ise seçilen dikdörtgeni yakalar. Yakalama düzenleyicisi serbest çizim, şekil, ok, metin, numaralı adım, vurgulama, kırpma,
bulanıklaştırma, pikselleştirme ve opak kapatma sunar. Geri al/ileri al, PNG/JPEG dışa aktarma,
OCR ve ekrana sabitleme vardır. Yakalama sonrası düzenleme, kopyalama ve geçmişe kaydetme
ayrı ayarlanır. Video/GIF kontrollerinden duraklatma, devam, bitirme ve iptal yapılır.
Video mikrofonu ve sistem sesi birlikte kullanılabilir; GIF sessizdir.
Ekran Geçmişi açıkken aktif uygulama kullanımı bir saniyelik gözlemlerden ölçülür; ekran
görüntüsü aralığından bağımsızdır. Açık olan tarayıcının erişilebilir adres çubuğundan URL,
Dosya Gezgini'nden klasör yolu okunur. Arama sorguları ve URL sorgu/kimlik bilgileri
saklanmaz. Kullanım istatistikleri uygulama toplamlarını, yerel takvim günlerine göre
uygulama sürelerini, web sayfalarını ve klasörleri tarih aralığıyla gösterir. Bu ek takip
Ekran Geçmişi ayarından kapatılabilir; modül ilk kurulumda kapalıdır.

## Depolama ve aktarım

- Ayarlar: mevcut `%AppData%\WinForge\settings.json`.
- Geçmiş, modeller ve medya: varsayılan `%LocalAppData%\WinForge\History`.
- Otomatik silme kapalıdır. İsteğe bağlı gün sınırı eski Recall kayıtlarını temizler;
  GB sınırı veya düşük disk alanı yeni kaydı durdurur. Diğer modüllerin dosyaları silinmez.
- Aynı görseller içerik özetli tek dosyayı paylaşır. Kayıt silme, yalnız artık kullanılmayan
  medya dosyalarını temizler; aktarılmakta veya kaydedilmekte olan dosyalar korunur.
- Depolama klasörü değişikliği sonraki etkinleştirmede uygulanır. Önceki klasör otomatik
  taşınmaz; o klasöre dönüldüğünde eski geçmiş açılır.

**Eski kayıtları içe aktar** kaynak klasördeki veritabanlarını bulur ve birden fazla kaynak
varsa seçim sunar. Önizlemede toplam/zaten aktarılmış kayıt, gerekli ek alan ve eksik dosya
sayısı gösterilir. Kaynak SQLite veritabanı salt okunur açılıp backup API ile tutarlı anlık
kopyası alınır. İptal edilen aktarım yeniden başlatılabilir. Aynı kayıt farklı derleme
klasörlerinde bulunsa da tarih ve eski kimlik üzerinden çoğaltılmaz.

Recall görüntüleri, ikonları, OCR metinleri, etiketleri ve ek metadata korunur. Eski kullanım
süreleri **tahmini** olarak işaretlenir; yeni süreler monotonik saat ve pencere olaylarından
ölçülür. Eksik görsel kaydın aktarılmasını engellemez. Whisper metinleri, model bilgisi ve
uyumlu ayarlar aktarılır. Modeller bağımsız depoya kopyalanıp sabit SHA-256 değerleriyle doğrulanır.

Bu çalışma sırasında 91.754 Recall ve 511 Whisper kaydı aktarıldı; bütün hedef medya yolları
kontrol edildi. Kaynak uygulamaların daha sonra oluşturduğu kayıtlar için aktarım yeniden çalıştırılabilir.
İki model de bağımsız WinForge deposuna kopyalandı. Yeni kayıt modülleri etkinleştirilmedi.

## İlave yakalama ve dikte davranışları

Bölge yakalamada varsayılan hızlı mod fare bırakıldığında hemen yakalar. İsteğe bağlı onay modunda
seçim sürüklenerek taşınır, kenar/köşelerinden yeniden boyutlandırılır ve Enter ile alınır.
Çoklu bölge aracı Enter ile biten en fazla 20 alanı ayrı kayıtlar olarak işler. Düzenleyicide
işaretler seçilip taşınabilir/boyutlandırılabilir; siyah-beyaz, sepya ve negatif efektleri
silme/öne-arkaya taşıma ve geri al/ileri al desteklenir. Kaydet/kopyala/düzenle eylem sırası
ayarlanabilir; düzenleme onaylanınca sonraki eylemler düzenlenmiş görselde çalışır. Video kayıt hızı
5–120 FPS aralığında ayarlanır; GIF en fazla
30 FPS üretir. Sesle Yazma, Whisper ile aynı davranış için dikte sırasında etkin ses çıkışlarını
varsayılan olarak sessize alır ve yalnızca kendi değiştirdiği çıkışları kayıt bitince geri açar.
Yakalama iş akışı kaydetme, kopyalama, düzenleme ve HTTP yükleme adımlarının 24 farklı sırasını
sunur. Yükleme ilk kurulumda kapalıdır; açıldığında PNG, ayarlanan özel http/https adresine
multipart `file` alanıyla gönderilir. Sunucu yanıtı URL, Location başlığı veya `url`, `link`,
`direct_url` JSON alanı içerir; dönen bağlantı panoya kopyalanır. Hazır üçüncü taraf servis profilleri
bulunmaz; hedef adres ve yanıt biçimi kullanıcı tarafından seçilir.

## Derleme ve motorlar

```powershell
# CPU motoru; normal uygulama derlemesi eksikse bunu çağırır.
./scripts/build-dictation.ps1 -Configuration Release

# İsteğe bağlı CUDA motoru; CUDA Toolkit gerektirir.
./scripts/build-dictation.ps1 -Configuration Release -Cuda

# Açık uygulamanın dosyalarını değiştirmeden bağımsız paket üretme
dotnet publish src/WinForge.App/WinForge.App.csproj -c Release -p:Platform=x64 --artifacts-path artifacts/capture-build -o artifacts/WinForge-capture
```

whisper.cpp v1.8.3, revision `2eeeba56e9edd762b4b38467bab96c2517163158` kullanılır.
Kaynaklar `.upstream` altında; WinForge C ABI köprüsü `native/WinForge.Whisper` altındadır.
Yeni worker kullanıcıya özel named pipe kullanır; üst süreç bitince çıkar. İptal ve modül
kapanışı worker'ı sonlandırır. Eski RecallClone/Whisper çalıştırılabilirleri çağrılmaz.

FFmpeg/FFprobe mevcut medya araç deposu ve PATH üzerinden ortak kullanılır. Yakalama araç
kurulumu yalnız bu iki aracı ister; yt-dlp/Deno indirmez. Mevcut indirme sürüm/özet denetimleri
korunur. SQLite NuGet istisnası yalnız `WinForge.History` projesi içindir.
SQLite çalışma kitaplıkları, worker, CPU motoru ve hazırlanmışsa CUDA motoru publish paketine
dahil edilir. Whisper/OpenAI lisansları ve CUDA dağıtım bildirimi paketlenir.

## Doğrulama

```powershell
dotnet run --project tests/WinForge.CaptureTests
dotnet run --project tests/WinForge.CaptureTests -- --native-capture
dotnet run --project tests/WinForge.CaptureTests -- --recording
dotnet run --project tests/WinForge.CaptureTests -- --text-output
dotnet run --project tests/WinForge.CaptureTests -- --scrolling
dotnet run --project tests/WinForge.CaptureTests -- --render
dotnet run --project tests/WinForge.CaptureTests -- --discovery artifacts/WinForge-capture/modules
dotnet run --project tests/WinForge.CaptureTests -- --audit
dotnet run --project tests/WinForge.CaptureTests -- --real-worker artifacts/WinForge-capture/dictation "$env:LOCALAPPDATA/WinForge/History/models" gpu
```

- 52 otomatik geçmiş/aktarım/ses/kısayol/yaşam döngüsü kontrolü geçti.
- Ayrı test sürecindeki düzenlenebilir alana Türkçe metnin tam yazılması, alan değişince
  yazmama, salt okunur/parola alanlarını reddetme kontrolleri geçti.
- Gerçek Türkçe örnek, Windows'un yerel Türkçe ses senteziyle oluşturuldu; CPU ve CUDA ile
  Turbo diktesi ve Large İngilizce çevirisi doğrulandı. Worker iptali ve GPU motoru yokken CPU'ya geçiş ayrıca sınandı.
- MP4 sistem sesi, MP4 mikrofon+sistem sesi ve GIF çıktıları; çözünürlük, ses parçası ve
  duraklatma süresinin hariç tutulması üzerinden doğrulandı.
- Mevcut pano kütüphanesi testleri (109), yerel pano/hedef alan testleri (94) ve paketli
  Türkçe/İngilizce OCR kontrolleri (3) geçti.
- Yayın paketinin üç modülü ve SQLite native bağımlılığı ayrı assembly context içinde yüklendi.
- Bağlı monitörde yalnız teste ait pencerenin fiziksel pikselleri yakalanarak doğrulandı.
  Negatif koordinatlar ayrıca otomatik testtedir. Farklı DPI'lı çoklu monitör donanımıyla
  uçtan uca deneme bu oturumda yapılmadı.
- Kayan yakalama içerik eşleştirme testinden ve gerçek kaydırılabilir test penceresinden geçti. Dinamik/sabit öğeleri olan bütün uygulamalarda
  kusursuz birleştirme garanti edilmez; düşük güvenli sonuç kısmi olarak işaretlenir.
- Genel regresyon çalıştırması medya flyout'undaki **“Media Flyout can persistently pin or unpin
  a selected media session”** kontrolünde durdu. Bu çalışma medya flyout kodunu değiştirmez.

Sentetik arayüz önizlemeleri ve medya test çıktıları `artifacts/capture-verification` altındadır.
