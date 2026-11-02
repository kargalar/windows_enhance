# Optimizasyon uygulama takibi

Kapsam: [OPTIMIZATION_ROADMAP.md](OPTIMIZATION_ROADMAP.md), R01–R12 ve Faz 0–5. Hedefin tamamı aktiftir; aşağıdaki ilk teslimatlar tüm planın tamamlandığı anlamına gelmez.

## 2 Kasım 2026 — ilk uygulama

| İş | Güncel durum | Kanıt / kalan doğrulama |
|---|---|---|
| R01 test envanteri | Hızlı yol ve ayrı native girdi grubu doğrulandı; genel entegrasyon açık | Manifest tüm projeleri ve interactive alt grubunu listeler. Son ortak Fast 429 kontrol ve UI build ile geçti; ayrı gerçek SendInput/WM_HOTKEY grubu 16 kontrol ile geçti. Native girdi Fast içinde açık atlama olarak, -InteractiveInput/Integration içinde seçili görünür; test silinmedi. 14 runner sözleşme kontrolü 17/23/29 hata kodlarını, erken durmayı ve grup seçimini doğrular. CI tanımı mevcut; uzak CI doğrulanmadı. |
| R02 geçmiş yenileme | Uygulandı, odaklı test geçti | 7 geciktirilmiş sorgu kontrolü: A/B/C birleşimi, sayfa snapshot'ı, eski sonucu yayınlamama, kapanış, eski sorgu hatasından sonra son isteği çalıştırma. Testler varsayılan CaptureTests koşusuna da bağlı. |
| R03 ayar kalıcılığı | Uygulandı, çekirdek ve UI testleri geçti; yayın doğrulaması sürüyor | Şema sürümü, atomik/flush edilmiş yazım, sağlam `.bak`, bozuk dosyayı koruma, hata/pending/saved sürümleri ve 150 ms birleştirme eklendi. Ana panel disk yazımı tamamlanmadan kayıt onayı vermiyor; hata/kurtarma bildirimi gösteriyor. Ayar apply/rollback işlemleri yaşam döngüsü kilidi ve yazım batch'i altında; geçici değerler apply sürerken diske yazılmıyor. CoreTests toplam 24 kontrol; ayrıca UI hata ve başarılı tekrar onayı testi geçti. |
| R04 yerel derleme | Uygulandı; Release/Debug doğrulandı, performans karşılaştırması bekliyor | Release `/O2 /DNDEBUG /MT`, Debug `/Od /Z7 /D_DEBUG /MTd`; betik, kaynak/header ve araç zinciri hash'leri artımlı derlemeye dahil. Release ve Debug derlemeleri başarılı; her iki yapılandırmada 23 taskbar geometri + 15 klasör boyutu kontrolü geçti. Betik değişikliği Release'i yeniden derletti; değişikliksiz Debug tekrarı kaynak derlemedi. Önce/sonra tarama performansı ve kapsamlı native/live profilleri bekliyor. |
| R05 ağ göstergesi | Uygulandı; fixture ve mevcut Windows oturumu doğrulandı | Taskbar konum/görünürlük, foreground ve ekran değişimi olayları; 250 ms güvenlik kontrolü; geometri/Show/Hide önbelleği; idempotent kapatma. 9 kontrollü yerleşim testi ve 15 gerçek WebView/oturum kontrolü geçti. Karışık DPI, Explorer yeniden başlatma ve tam auto-hide geçiş matrisi hâlâ yayın kapısında. |
| R06 köprü/state | Sürümlü fark ve gerektiğinde panel okuma doğrulandı | İlk tam katalog ayar tanımlarını korur; deferredPanels yeteneği olan istemci ayrıntı açılınca module.inspect gönderir. Eski istemci tam paneller almaya devam eder. 120 gerçek WebView kontrolü; 20 context ve 8 taskbar panel kontrolü geçti. Son canonical katalog 156323 bayt (bir instrumented probe dahil), ayar farkı medyan 408 bayt; onay p95 3,8 ms. Sözleşme: DASHBOARD_BRIDGE.md. |
| R07 açılış | Panel verisi ayrıntı açılışına ertelendi; üretim açılışı/tepsi ölçümü açık | İki yeni 30 süreçlik seride kapalı panel/Windows veri okuması sıfır. Son seride BuildState p95 49,40 ms, Show/katalog 1,51 sn, test süreci/katalog 2,95 sn; 90 kontrol geçti. Son değer 2 sn hedefini aşar; yalnız hızlı tekrar seçilerek tamamlanmış sayılmaz. STARTUP_PERFORMANCE.md tüm tekrarları ve hash manifestini içerir. |
| R08 log | Uygulandı; kontrollü testler geçti | Ana uygulamada 1024 satırlık kuyruk, tek arka plan yazıcısı, 4096 karakterlik satır üst sınırı, 2 MiB aktif dosya ve 3 dönen arşiv. Kapanış flush/dispose ile tamamlanır; taşma/hata sayıları ve son hata okunabilir. 10 test: bloke yazıcıda üretici devamı, tam taşma sayısı, flush, subscriber izolasyonu, UTF-8 boyut sınırı ve dosya hatasından toparlanma. |
| R09 UI paketleri | Uygulandı; üretim WebView doğrulandı | FeatureApp JS/CSS dinamik yükleniyor. Güncel ilk JS 413090 → 285863 bayt (%30,80 azalma), CSS 166862 → 57312 bayt (%65,65 azalma). Gerçek dosya hash/ölçüleri ui-bundle.json içinde. Üretim CSP ve native lazy özellik akışı doğrulandı; bunlar RAM veya tam uygulama açılışı iddiası değildir. |
| R10 girdi kancaları | İlk güvenilirlik değişikliği uygulandı; ölçüm/karar bekliyor | Klavye aboneleri tek tek korunuyor; hatalı abone/reporting sonraki key-down/up'ı engellemiyor (2 test). Mouse/keyboard native callback süreleri opt-in trace'e eklendi; tuş/metin/koordinat kaydedilmez. Gerçek callback p95, kanca sayıları ve merkezileştirme kararı hâlâ bekliyor. |
| R11 büyük geçmiş | Ölçüldü; mevcut stratejiyi koruma kararı verildi | En az 1024 karakter Türkçe metinli 10k/100k kayıt, 8 sorgu şekli, 30 örnek ve gerçek SQL planları. İki tekrarda 100k en yüksek p95 28,19 ve 19,09 ms; 33 kontrol geçti. Pooling/keyset değişikliği bu kapsamda gerekmedi. `HISTORY_PERFORMANCE.md` karar ve sınırları içerir. |
| R12 bakım/belgeler | Medya/parlaklık, Tiling ve pano görünümü ayrımları uygulandı; kapsam açık | Medya/parlaklık türleri ayrı dosyalarda. Tiling geometri ve pencere yerleştirme bölümleri partial dosyalara taşındı; 18 gerçek fixture kontrolü ayrım öncesi/sonrası geçti ve kullanıcı değişiklikleri dahil kaynak birebir yeniden kuruldu. ClipboardViews ayrımı öncesi/sonrası 4 davranış testi geçti; UI toplam 38. Kalan FeatureApp grupları ve medya oturumu/arayüz sınırı açık. MODULE_RESPONSIBILITIES.md. |
| Faz 5 yayın kapısı | Bekliyor | Windows/DPI/donanım matrisi, paket/publish, kurtarma ve 8 saat dayanıklılık henüz tamamlanmadı. |

## Ölçüm altyapısı

`WINFORGE_PERF_PATH` mutlak bir JSONL dosya yoluna ayarlandığında açılış, modül ve ana panel köprü süreleri/baytları yerel dosyaya yazılır. Kapalıyken zamanlayıcı ve dosya yazımı yoktur. Kuyruk 4096 kayıtla sınırlıdır; taşmalar sayılır. Ayar değerleri, kullanıcı metni ve dosya içerikleri kaydedilmez. Bu izler henüz tüm performans senaryolarını kapsamaz.

Örnek, uygulamayı başlatmadan önce:

```powershell
$env:WINFORGE_PERF_PATH = 'C:\temp\winforge-performance.jsonl'
```

Mevcut uygulamaya salt okunur süreç ölçümü:

```powershell
./scripts/measure-performance.ps1 -RootProcessId <process-id> -DurationSeconds 600 -Profile daily-idle
```

İlk 30 saniyelik mevcut oturum ölçümü `artifacts/performance/baseline-current-session.json` dosyasında: 36 etkin modül, 25 örnek, süreç ağacında ortalama normalize CPU yaklaşık %1,73; son toplam private bytes yaklaşık 4049,5 MiB. Oturumda 13 süreç gözlendi. **Bu kontrollü boşta testi, sızıntı kanıtı veya optimizasyon sonrası karşılaştırma değildir.** Çalışan uygulama yeniden başlatılmadı; yeni kod bu ölçüm sırasında çalışmıyordu.

Sampler çocuk süreçleri dahil eder; örnekler arasında doğup kapanan süreçleri kaçırabilir. Explorer ayrıca ölçülmelidir. Ekran sınırları, Windows build'i ve etkin modüller kaydedilir; DPI ve WebView2 runtime sürümü koşu notlarına eklenmelidir.

## Yeniden çalıştırma

```powershell
./scripts/test.ps1 -List
./scripts/test.ps1
./scripts/test.ps1 -Group Integration
./scripts/test.ps1 -NativeSmoke
```

Integration/canlı testler model, native motor, uygulama penceresi veya registry fixture'ları kullanabilir. Fast sonuçları bu testlerin geçtiğini iddia etmez; atlanan her proje raporda görünür. Entegrasyon derlemeleri açık uygulamanın dosyalarını değiştirmemek için `artifacts/integration-build` altında tutulur.

Odaklı geçmiş testi için doğrulanmış komut:

```powershell
dotnet build tests/WinForge.CaptureTests -c Release -p:Platform=x64 --artifacts-path artifacts/optimization-build
./artifacts/optimization-build/bin/WinForge.CaptureTests/release/WinForge.CaptureTests.exe --history-refresh
```

Bu derleme 0 uyarı ve 0 hatayla tamamlandı. Üretim UI testlerindeki Vite WebSocket çakışması `hmr:false, ws:false` ile giderildi; yeniden koşulan 24 UI testinde port uyarısı oluşmadı.

R03 testleri gerçek dosya kilidi, bozuk JSON, sağlam yedeğin korunması, desteklenmeyen gelecek şema, kontrollü depolama sınırında simüle disk doluluğu, 50 düzenlemenin tek yazıma birleşmesi, kapanışta son değer ve geciken apply hatasından sonra yeni düzenlemeyi kapsar. Gerçek diski doldurma testi yapılmadı. Güncel değişikliklerle CaptureTests'in 128 kontrolü ve uygulama/test derlemesi yeniden geçti (0 uyarı/0 hata).

Bir paralel doğrulamada `npm ci` bağımlılıkları yenilerken UI derlemesi tip dosyalarını bulamadı. Kurulum bitince derleme ve CaptureTests sırayla tekrar çalıştırıldı ve geçti; bu bağımlı işler sonraki koşularda paralelleştirilmemeli.

Son başarılı hızlı rapor: `artifacts/test-results/run-20261102-113155-167.json`. Runner sözleşmesi: `./scripts/test-runner-checks.ps1` (10 kontrol). Son ilgili kaynaklarda `git diff --check` geçti. R01'in gerçek entegrasyon profili ve uzak CI sonucu hâlâ bekliyor.

Sonraki zorunlu işler: R04 Debug/derleme geçersiz kılma ve performans karşılaştırması; R05/R08 ve R06/R07; gerçek entegrasyon koşusu ve kontrollü baz profilleri. R09–R12 ile Faz 5 kapsamı da açık tutuluyor. Kontrollü baz profilleri kurulmadan performans hedefleri tamamlandı sayılmamalı.

## 2 Kasım 2026 — hedefli performans teslimatı

- Son hızlı rapor: `artifacts/test-results/run-20261102-115306-148.json`; 45 core + 350 diğer .NET kontrolü + 25 UI testi, toplam **420**; UI build başarılı. Önceki 399 sayısı önceki teslimata aittir.
- Core paketine 10 log, 9 ağ yerleşimi ve 2 klavye abonesi kontrolü eklendi. Sabit geometride 20 manuel yenileme yalnız tek SetBounds/Show üretti. 1,1 saniyelik gerçek dispatcher koşusunda 3–5 güvenlik kontrolü görüldü; eski 50 ms sıklığı bu sınırı karşılamazdı.
- R08'in asenkron modu ana uygulamada etkin; eski kısa ömürlü test çağrıları uyumluluk için senkron kalır. Kuyruk taşması `DroppedMessages`, disk sorunları `FailedMessages` ve `LastError` ile izlenir. Bu sayaçlar henüz kullanıcı tanı raporuna bağlanmadı.
- Son `--net-speed-only` koşusu **15 kontrol** ile geçti. Gerçek WebView2'de dinamik JS ve CSS, üretim CSP ve native `feature.ready`/state alışverişi doğrulandı; 3 Hide/Show çevriminde sayfa yeniden yüklenmedi.
- Mevcut masaüstünde görev çubuğu 2 piksellik reveal şeridinde gizliydi. Önceki canlı fixture'ın her durumda görünür taskbar varsayımı düzeltildi; yeni test gerçek monitor/taskbar kesişimini esas alıyor. Tam göster/gizle geçişi ve DPI matrisi bu tek koşuyla kanıtlanmış değildir.
- Yeni asenkron DOM yoklaması bir mevcut test yardımcı kusurunu ortaya çıkardı: yardımcı başarılı predicate'i ikinci kez değerlendirip yeni bir async isteği başlatıyordu. Başarıda hemen dönecek şekilde düzeltildi; önceki zaman aşımı bir uygulama yükleme hatası değildi.
- Debug doğrulaması `_DEBUG` ile `/MT` uyuşmazlığını ortaya çıkardı; `/MTd` düzeltmesi sonrası tüm Debug native hedefleri ve 38 kontrol geçti. Bu betik değişikliği Release'in de yeniden üretilmesini sağladı.
- Gerçek başlangıç paket karşılaştırması: JS **413090 → 282050** bayt, CSS **166862 → 57312** bayt. `scripts/measure-ui-bundle.ps1` HTML'in doğrudan yüklediği varlıkları, dosya hash'lerini ve ertelenen dosyaları raporlar. İlk baz çıktı göreli yol nedeniyle proje üstündeki `artifacts` dizinindeydi; aynı özgün çıktı çalışma alanındaki belgelenmiş `artifacts/analysis-roadmap-ui` konumuna kopyalandı.
- `FEATURE_ROADMAP.md` shell uygulama durumuyla hizalandı; geniş Windows uyumluluğu iddia edilmedi. Çalışan kullanıcı uygulaması kapatılmadı veya yeni sürümle değiştirilmedi.

**Sıradaki açık işler:** R06 sürümlü/kısmi durum köprüsü, R07 kontrollü açılış/boşta ölçümü, R11 10k/100k sorgu ölçümü ve planı; R04 tarama karşılaştırması; R10 gerçek kanca profili; R12 sorumluluk ayrımları; tam entegrasyon ve Faz 5 yayın matrisi/dayanıklılık. Hedef tamamlanmış değildir.

## 2 Kasım 2026 — köprü ve büyük arşiv teslimatı

- `MainWindow.StateBridge.cs` komut sıralaması, DTO üretimi, sürümleme, fark üretimi ve abonelik yaşam döngüsünün sahibidir. WPF pencere/ayar eylemleri ayrı kalır. Ana pencerenin tekrar `Loaded` ile yönlendirilmesi düzeltildi.
- Tek modülü tam göndermek, büyük sağ tık menüsünde yeterli değildi. `JsonStateDelta` ve istemci uygulayıcısı değişmeyen ikonları/kataloğu taşımayan alan farkları üretir; 4096 işlem sınırında tam modüle döner. JSON null/kaldırma, dizi boyutu, geçersiz yollar, eski/eksik sürümler ve prototype anahtarları test edildi.
- Core paketi **51**, UI paketi **34** kontrole çıktı. Son ortak `-Performance` koşusu başarılı: `artifacts/test-results/run-20261102-123924-593.json`. Bu koşuda hızlı gruplar, derleme ve iki performans grubu ayrı ayrı geçti; gerçek tam entegrasyon grupları seçilmedi ve raporda atlandı.
- Son odaklı köprü koşusunda **105 kontrol** geçti: kalıcı onay, gerçek DOM'da farkın görünmesi, sıralı sürümler, yanlış komutta güncel tam durum, renderer reload, gerçek renderer crash sonrası otomatik dönüş, Hide/Show ve kullanıcı ayar değerlerini içermeyen tanı raporu. Testin sahip olduğu browser kapanışı beklendi; profil kilidiyle yarışan temizlik düzeltildi.
- Yeni tarayıcı-içi ölçüm postMessage → native onay p95'ini yaklaşık **5,9 ms** buldu; 20 ms test yoklamalı eski yaklaşık 80 ms ölçümüyle doğrudan kıyaslanmaz. Son ham rapor `dashboard-bridge.json`, ilk tam snapshot **332283**, medyan fark **408** bayt (%99,88 azalma). Büyük context seçenek değişikliği **421** bayt ve ikon tekrarı yok (`context-bridge.json`).
- Sağ tık menüsünün eski fixture'ı artık olmayan kart tıklama ve aramasız liste varsayımlarını kullanıyordu. Mevcut arama/filtre ve birleşik native satır kimliklerine uyarlandı; **20** gerçek React/native kontrolü geçti. Ürün akışı bu teste uydurulmadı; gerçek Explorer menüsü değiştirilmedi.
- `Uygulama günlükleri` eylemi asenkron yazıcıyı tamamlayıp `diagnostics.json` üretir. Log/trace kayıpları ve kayıt sürümleri görülebilir; ayar değerleri dışarı alınmaz. R08'in önceki sayaç raporlama açığı kapatıldı.
- Geçmiş ölçümü önce kısa metinle yapıldı, ardından en az **1024 karakterlik Türkçe belge** ve ek Unicode FTS profiliyle güçlendirildi. Son 100k veritabanı yaklaşık **162,5 MiB**, en yüksek p95 **28,19 ms**; **33 kontrol** geçti. Mevcut indeks/sayfalama/bağlantı stratejisi korunuyor. Önceki kısa metinli yaklaşık 18 ms sayısı son veri profilini temsil etmez.
- Performans profilinin tek komutu `./scripts/test.ps1 -Performance`; 10 runner sözleşme kontrolü de tekrar geçti. `--history-benchmark`, `--bridge-only` ve `--context-menu-ui-only` odaklı tekrarlar için kullanılabilir.

**Açık kalan kapsam:** R07 kontrollü açılış ve tepsi profilleri; R04 gerçek tarama önce/sonra karşılaştırması; R10 gerçek callback/kanca ölçümü; R12 kalan büyük sınıfların sorumluluk ayrımı ve belge tutarlılığı; eski genel regresyon fixture'larının güncellenmesi, tam entegrasyon, farklı Windows/DPI/donanım matrisi ve 8 saat dayanıklılık. Çalışan eski kullanıcı uygulaması hâlâ değiştirilmedi. Tüm hedef tamamlandı sayılmıyor.

Son doğrulama: `./scripts/test.ps1 -Performance` güncel kodla tekrar **başarılı**; rapor `artifacts/test-results/run-20261102-125339-549.json`. Hızlı bölüm **435** kontrol (51 core, 350 diğer .NET, 34 UI); ayrı performans bölümlerinde **33** geçmiş ve **105** gerçek WebView köprü kontrolü geçti. Sadece kapsamı seçilmeyen genel entegrasyon grupları atlandı. Son tarayıcı onay p95'i **6,6 ms**, test yoklama p95'i ayrı ölçülüyor. UI köprüsü ve geçmiş belgeleri eklendi; kaynaklar ve belgelerde `git diff --check` geçti.

Ölçüm çıktıları `artifacts/performance/runs/20261102-125339/` altında arşivlendi. Context ölçümü aynı teslimatın ayrı odaklı koşusundandır. Güncel ilk JS paketi köprü kodu dahil **285347 bayt** (%30,92 küçülme); önceki 282050 bayt paket köprünün alan-farkı kodunu içermiyordu. CSS **57312 bayt** olarak kaldı. Bunlar yalnız varlık boyutlarıdır.

R07 için sıradaki bağımsız inceleme: süreç/modül açılışını ve ilk kullanılabilir paneli ayrı ölç; büyük panellerin (özellikle context ikon kataloğu) ilk katalog yanıtında istenmeden hazırlanmasını incele. Gerekçelendirilirse panel verisini ayrıntı açılışına ertele; aynı protokolün ilk açılış/resync/toparlanma davranışlarını koru. Bu henüz uygulanmadı veya tamamlandı sayılmadı. Gerçek tepsi/günlük profil ve uzun süreli testler, açık kullanıcı uygulamasının durumunu koruyan kontrollü bir yaşam döngüsü gerektiriyor.

## 2 Kasım 2026 — açılış bileşenleri ve sorumluluk ayrımı

- R07: `measure-startup-components.ps1` ve `--startup-probe` eklendi. 30 ayrı süreç, yeni ayar/WebView profili ve aynı assembly hash'leriyle 60 katalog kontrolü geçti. Ham rapor `artifacts/performance/startup-components-20261102-130908/report.json`. Host.Load p95 **367,59 ms**, Show/katalog **1351,72 ms**, test süreci/katalog **3555,44 ms**. Son değer 2 saniyelik hedefi aşar; gerçek App.OnStartup/aktif modüller ayrıca ölçülmelidir.
- DefaultSettingsModule Windows okumaları Initialize/Activate sırasında yapılmıyor. İlk panel isteği veya komut hazırlıyor; explicit reload ve hatadan yeniden deneme korunuyor. **48** kontrol geçti (önceki 42 + 6 davranış kontrolü). Bu iş ilk tam dashboard snapshot'ına taşınır; toplam maliyetin yok olduğu iddia edilmez.
- R12: Medya modülü, klavye kancası, kilit tuşu yüzeyi ve medya penceresi; parlaklık modelleri, native oturumu, modülü ve penceresi ayrı dosyalara alındı. Gövdeler özgün kaynakla karşılaştırıldı ve değişmedi. Uygulama/test derlemesi 0 uyarı/0 hata; ayrım öncesi ve sonrası **11** parlaklık, **5** ses tuşu, **14** panel yaşam döngüsü kontrolü geçti. MediaFlyoutWindow hâlâ büyük; TilingModule ve FeatureApp ayrımları açık.
- Görev çubuğu WebView fixture'ı yeni karta uyarlandı; kullanıcı WebView profilini paylaşmak yerine sahip olduğu izole profili kullanıyor. Browser/profil kapanışı bekleniyor. Native yazım sonrasında React onayı henüz işlenmemiş olabiliyor; test hem kalıcılığı hem DOM onayını bekleyecek şekilde düzeltildi. Ürün akışı değiştirilmedi. **8** gerçek panel kontrolü geçti.
- Son odaklı dashboard köprü koşusu **105** kontrol ile geçti; **334757 → 408 bayt**, native onay p95 **5,9 ms**. **34** UI testi ve üretim derlemesi de geçti.
- Son ortak `./scripts/test.ps1 -Performance` koşusu **başarısız**: `artifacts/test-results/run-20261102-131252-703.json`. Core 51, DefaultSettings 48 ve Automation 74 geçti; ShortcutActionTests gerçek SendInput/WM_HOTKEY kontrolünde beklenen medya olayı yerine 0 gördü. Salt okunur kontrolde foreground süreç **LockApp** idi (Windows kilit ekranı); giriş masaüstü adı Default, modifier'lar basılı değildi. Bu koşu başarılı sayılmadı veya atlanarak yeşile çevrilmedi. UI/build/köprü kontrolleri sonrasında ayrı çalıştırılıp doğrulandı. Geçici süre bekletme denemesi sorunu çözmedi ve geri alındı; kullanıcının ShortcutAction test değişiklikleri korundu. Kilit açık oturumda ortak koşu yeniden gereklidir.
- İlgili değişen kaynaklarda diff/PowerShell parse kontrolü geçti. Depo genelinde `todo.md:4` kullanıcı değişikliğindeki mevcut trailing whitespace ayrıca görünüyor; kullanıcı satırı değiştirilmedi.

Sıradaki kapsam: ilk tam dashboard snapshot'ında ihtiyaç olmadan okunan panellerin ölçümle ertelenmesi; gerçek App açılışı ve 2 dk + 10 dk tepsi profilleri; R04 scan karşılaştırması; R10 callback profili; R12 kalan ayrımlar; eski genel regresyon fixture'ları/tam entegrasyon; Windows/DPI/donanım, publish/recovery ve 8 saat + 100 döngü yayın kapısı. Eski kullanıcı uygulaması PID 23448 çalışmaya devam ediyor. Hedef tamamlanmadı; kilit ekranı diğer bağımsız işleri engelleyen genel bir blok olarak değerlendirilmedi.

## 2 Kasım 2026 — kapalı panelleri erteleme ve bakım kontrolleri

- Yeni arayüz `ui.ready` ile `deferredPanels:true` bildirir. İlk tam snapshot tüm modül/ayar tanımlarını içerir; kullanılmayan panel getter'ları çağrılmaz. Ayrıntı açıldığında `module.inspect` ilgili paneli yükler. Hata sonrası açık yeniden deneme, resync'te açık panelleri koruma ve gezinme/crash sonrasında yeni sayfa için sıfırlama doğrulandı. Yeteneği bildirmeyen eski istemciye dolu panel yanıtı korunur.
- Native köprü fixture'ına bir instrumented panel eklendi: ilk katalogda sıfır getter, tek panel açma, diğer Windows verisini okumama, hata sonrası otomatik döngü oluşturmama, explicit retry, gerçek Windows program listesini ayrıntıda render etme ve eski istemci uyumluluğu. Son koşu **120** kontrol geçti. Ayrı **20** context ve **8** taskbar panel kontrolü de geçti.
- Son ortak koşu başarılı: `artifacts/test-results/run-20261102-133818-593.json`. Fast **429** kontrol: Core 51, DefaultSettings 48, Automation 74, ShortcutAction fast 218, UI 38; UI/performans derlemeleri geçti. Ayrı performans grupları History **33**, Dashboard **120** kontrol geçti. Genel entegrasyon projeleri ve interactive giriş grubu bu komutta seçilmedi; atlama nedenleri JSON'da açık.
- R01: Gerçek fiziksel girdi kuyruğuna bağlı 16 kontrol Fast profilden ayrı `Shortcut native input` grubuna taşındı; hiçbir kontrol kaldırılmadı. Varsayılan ShortcutActionTests çağrısı eski tam kapsamını korur; `--fast` dar kapsamı açıkça bildirir. Ortak runner `-InteractiveInput` veya Integration ile native grubu seçer. 14 stub sözleşme kontrolü geçti. Ürün testinin ayrı gerçek tekrarı da **16 kontrol** ile geçti: `artifacts/test-results/native-input-latest.json`. Önceki kilit ekranı başarısızlığı tarihsel kayıttadır; native grubun başarısı Fast sonucundan çıkarılmadı.
- R12: ClipboardViews dosyasına pano rafı/ayar/önizleme/prompt/kayıt kırpma görünümleri ve tipleri taşındı. Dört davranış testi ayrımdan önce ve sonra geçti: hızlı kopyalama/çoklu seçim, metin düzenleme klavyesi, kaydetme/iptal, gizlilik tercihi ve üç izleme klasörü sınırı. Gövdeler ve FeatureApp yönlendirmesi değişmedi. TilingModule.ZoneGeometry ve WindowPlacement dosyalarına toplam 496 satır taşındı. Kaynak, taşınan parçalarla birleştirilerek kullanıcı değişiklikleri dahil özgün dosyayla birebir karşılaştırıldı. 18 drag/fullscreen/yeni pencere kontrolü öncesi ve sonrası geçti.
- İlk panel erteleme serisi `startup-components-20261102-132335` 30 süreçte Show/katalog p95 **752,27 ms**, test süreci/katalog **1842,40 ms** verdi. Uyumluluk/bakım sonrası tekrar `startup-components-20261102-133304` 30 süreçte **1509,49 ms** ve **2946,16 ms** verdi; 90 kontrol geçti. Son seride 33 managed/UI dosyasının hash manifesti bütün örneklerde aynı ve sonraki ortak derlemeyle eşleşti. İki yeni seride defaults.read/bridge.readPanel sayısı sıfır. BuildState p95 ilk serinin **433,84 ms** değerinden yeni iki seride **28,64 / 49,40 ms** oldu. Genel açılış p95 dalgalı; 2 sn hedefi son tekrarda aşılır. R07 tamamlanmış sayılmadı.
- Son köprü raporu ilk canonical snapshot **156323**, medyan fark **408** bayt ve tarayıcı onay p95 **3,8 ms** içerir. Bir test paneli gerçek katalogla birlikte raporlanır. Son 100k geçmiş en yüksek sorgu p95 **26,90 ms**; 200 ms bütçe içinde. Güncel ilk JS **285863**, CSS **57312** bayt; lazy özellik akışı korundu.

Tam kapsam hâlâ açık: üretim App soğuk/sıcak ve tepsi profilleri; R04 gerçek tarama karşılaştırması; R10 gerçek callback/kanca ölçümü; R12 kalan görünüm/oturum sınırları; eski genel regresyon fixture'ları ve tam entegrasyon; Windows/DPI/donanım matrisi; publish/geri dönüş ve gerçek 8 saat + 100 döngü dayanıklılık. Kullanıcının eski uygulaması değiştirilmedi. İlgili kaynaklarda diff kontrolü ve runner sözleşmesi geçti; depo genelindeki kullanıcı todo satırındaki whitespace ayrı tarihsel bulgudur.

## 2 Kasım 2026 — kullanıcı isteğiyle sonlandırma denetimi

Sonlandırma kararı ve R01–R12/Faz 5 kapsam karşılaştırması `FINAL_VERIFICATION.md` dosyasına yazıldı. **Tüm plan tamamlandı onayı verilmedi.** Eksik yazılım/test noktaları düzeltildi: private panel probe'unun plugin olarak keşfedilmesi; kaldırılmış WPF kontrol adlarına bağlı genel arayüz testleri; geçici DLL/browser profil temizliği; Türkçe JSON metin ve yinelenen kontrol etiketi seçimi; arayüz yazımı ve runner'ın açık başarılı çıkışı.

Yeni `--dashboard-interface-only` rotası 481 gerçek native/React kontrolünü Settings/Lifecycle ile birlikte çalıştırır; eski TestInterface'in dashboard bölümü de bu helper'ı kullanır. Genel donanım/native feature kontrolleri atlanarak tüm RegressionTests geçtiği iddia edilmez. Ortak `-Performance` profili yeni dashboard grubunu ayrıca çalıştırır.

Son ortak komut `./scripts/test.ps1 -Performance -InteractiveInput` **başarılı**: `artifacts/test-results/run-20261102-164844-148.json`. Fast 429 + gerçek giriş 16 + geçmiş 33 + köprü 120 + dashboard/lifecycle 481 = 1079 seçili kontrol/test; beş genel entegrasyon projesi skipped olarak görünür. 14 ayrı runner stub kontrolü geçti. Derleme ve ilgili kaynaklarda diff/PowerShell parse kontrolleri geçti.

`dotnet publish` ile `artifacts/publish-audit` paketi üretildi. Yeni `verify-publish.ps1` mevcut makinede 29 gerekli dosya, x64 native giriş noktaları, güncel UI dosya seti ve 11 çevrimdışı model/lisans kaynağını hash karşılaştırmasıyla doğruladı; canlı ayar/geçmiş/log yok. Rapor `artifacts/test-results/publish-audit.json`. Bu, temiz makine açılışı veya rollback testi değildir.

NavigationTests gerçek denemesi çalışan uygulamadaki Gezgin stilleri etkin olduğu için korumada durdu. Kullanıcı uygulamasını zorla kapatma veya korumayı kaldırma yapılmadı. Açık kapsam: tüm entegrasyon ve uzak CI; gerçek açılış/tepsi profilleri; R04 tarama ve R10 hook ölçümleri; R12 kalan ayrımlar; Windows/DPI/donanım/CPU-CUDA, uyku/aygıt/Explorer, temiz makine/rollback ve 8 saat + 100 döngü yayın kapısı. Hedef aktif ve tamamlanmamıştır.
