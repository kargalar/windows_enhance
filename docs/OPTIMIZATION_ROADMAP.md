# WinForge iyileştirme ve optimizasyon yol haritası

Tarih: 2 Kasım 2026. İnceleme tabanı: `0ced9ed` ve çalışma ağacındaki mevcut değişiklikler.

## Karar özeti

Önce doğruluk ve ölçüm altyapısı, ardından boşta çalışma maliyeti ve ayar/arayüz veri akışı iyileştirilmeli. Mevcut modüler .NET/WPF + React/WebView2 + yerel C++ mimarisi korunarak ilerlemek yeterli; inceleme büyük bir yeniden yazımı gerektiren kanıt ortaya koymadı.

İlk teslimat paketi: ortak test komutunun kapsamını tamamlamak, performans başlangıç ölçümlerini almak, geçmiş ekranında kaybolabilen yenileme isteğini düzeltmek ve ayar kaydetme hatalarını görünür yapmak. Bunlardan sonra yerel Release derlemesi, 50 ms konum zamanlayıcısı ve tam arayüz durumu gönderimi ele alınmalı.

Bu belge bir uygulama planıdır. Ürün kodunda optimizasyon yapılmadı. CPU/RAM/açılış için çalışma zamanı profili alınmadığından performans kazanımları henüz doğrulanmış değildir. Aşağıdaki hedefler önerilen kabul eşikleridir.

## İnceleme kapsamı ve mevcut güçlü taraflar

- Ana uygulamanın açılışı, modül keşfi ve yaşam döngüsü; ayar ve log yazımı; WebView2 köprüsü ve React giriş noktası incelendi.
- Girdi kancaları, ağ hızı göstergesi, medya yenilemesi, geçmiş sorguları ve klasör boyutu motorunun kaynak bütçeleri örneklendi.
- Derleme betikleri, test giriş noktaları ve mevcut mimari/özellik belgeleri karşılaştırıldı. Bu, tüm özelliklerin uçtan uca denetimi değildir.
- Ayar yazımı zaten geçici dosya ve taşıma kullanıyor. React tarafında 450 ms otomatik kayıt birleştirmesi ve geciken onaylara karşı kontroller var; bunlar korunmalı.
- Modül yaşam döngüsünde asenkron yol ve hata sonrası temizleme mevcut. WebView2 ortamı paylaşılıyor; yerel içerik eşlemesi, gezinme kısıtlaması ve üretim CSP'si var.
- Geçmiş deposunda WAL, FTS5, indeksler ve sayfalama; geçmiş ekranında arka plan sorgusu mevcut. Bunlar yeniden yapılacak işler değildir.
- Klasör boyutu motoru zaten 4 aktif tarama, 256 klasör önbelleği ve 32 MiB snapshot sınırı içeriyor. Öncelik daha fazla iş parçacığı eklemek değil, mevcut bütçeler altında davranışı ölçmektir.

## Doğrulama tabanı

Bu inceleme sırasında çalıştırılanlar:

| Kontrol | Sonuç |
|---|---|
| UI: `node --test tests/*.test.mjs` | 24/24 geçti |
| `WinForge.ShortcutActionTests`, Release/x64 | 234 kontrol geçti |
| `WinForge.AutomationTests`, Release/x64, varsayılan mod | 74 kontrol geçti |
| `WinForge.DefaultSettingsTests`, Release/x64 | 42 kontrol geçti; gerçek Windows ayarları değiştirilmedi |
| UI: `npx tsc -b` | Başarılı |
| UI: Vite üretim derlemesi | Başarılı; JS 413,09 kB / gzip 115,13 kB, CSS 166,86 kB / gzip 30,44 kB |

Toplam 374 test/kontrol geçti. UI testleri sırasında `Port 24678 is already in use` uyarısı oluştu; test sonucu başarılıydı. Test sunucularının HMR portlarını paylaşması ayrıca temizlenmeli.

UI derlemesi `artifacts/analysis-roadmap-ui` dizinine alındı. Bu boyutlar açılış süresi veya RAM ölçümü değildir; gzip değerleri karşılaştırma içindir, yerel WebView2 aktarım maliyeti olarak yorumlanmamalı.

Tam çözüm/publish, genel regresyon, canlı Explorer/shell, gerçek CUDA, donanım ses/ekran ve uzun süreli kararlılık testleri bu analizde çalıştırılmadı. `RUNTIME_ARCHITECTURE.md` önceki genel regresyon koşusundaki fixture sorununu kaydediyor; bu tarihsel kayıt güncel başarısızlık olarak kabul edilmemeli, yeniden doğrulanmalı.

## Bulgular ve iş listesi

Öncelik tanımı: **P0**, optimizasyon sürümünün doğruluk ve doğrulama kapısı; **P1**, ilk performans kazanımları; **P2**, ölçümle gerekçelendirilecek bakım ve ölçeklenme işleri.

| ID | Öncelik | Koddan doğrulanan durum | Etki ve önerilen iş |
|---|---|---|---|
| R01 | P0 | [test.ps1](../scripts/test.ps1), Automation/ShortcutAction/DefaultSettings projelerini ve UI testlerini çağırmıyor. | Ortak komut geçtiğinde tüm testlerin geçtiği sanılabilir. Test envanteri ve tek giriş komutu oluştur; hızlı, native ve canlı grupları ayrı seçilebilir yap. |
| R02 | P0 | [HistoryWindow.RefreshAsync](../src/WinForge.Modules.Capture/HistoryWindow.cs) `busy` iken isteği bırakıyor; `history.filter` ve `history.page` aynı sırada filtre/offset alanlarını değiştiriyor. | Yavaş bir sorgu sırasında son filtre isteği kaybolabilir; eski sonuç ile yeni sayfa etiketi eşleşmeyebilir. Son isteği bekleten ve sonuçları sürümle doğrulayan yenileme akışı kur. Bu, kod akışından çıkarılan doğruluk riskidir; geciktirilmiş sorgu testiyle yeniden üret. |
| R03 | P0 | [SettingsStore.Save](../src/WinForge.Core/SettingsStore.cs) okuma/yazma hatalarını yutuyor; `Set` her değişiklikte tüm JSON'u senkron yazıyor. | Arayüz kayıt başarılı sanabilir. Önce hata sonucu ve kurtarma davranışı ekle; ardından tutarlı snapshot üzerinden birleştirilmiş yazım, şema sürümü ve doğrulanmış yedekten dönüş tasarla. |
| R04 | P1 | [build-native.ps1](../scripts/build-native.ps1) aynı `$common` bayraklarını kullanıyor; Release için açık `/O2` ayrımı yok. Artımlı kontrol betiğin veya derleyici seçeneklerinin değişmesini girdi saymıyor. | Release optimizasyonlarını açıkça tanımla; ayar/araç zinciri değişince yeniden derlemeyi garanti et. Kazancı native fixture ve gerçek tarama ölçümüyle doğrula. |
| R05 | P1 | [NetSpeedWindow](../src/WinForge.Modules.NetSpeed/NetSpeedWindow.cs) 50 ms aralıkla `Reposition` çağırıyor; her çağrı taskbar/DPI/ekran bilgisi okuyor ve `SetBounds`/`Show` yoluna giriyor. | Modül açıkken saniyede yaklaşık 20 konum kontrolü. Konum/görünürlük olayları ve son geometri önbelleği kullan; otomatik gizleme/Explorer yeniden başlatma için düşük sıklıkta güvenlik kontrolü bırak. Gerçek CPU maliyeti ölçülmeli. |
| R06 | P1 | [MainWindow](../src/WinForge.App/MainWindow.xaml.cs) komut yanıtlarında tüm modülleri, panelleri ve ayarları `BuildState` ile yeniden hazırlıyor. | Küçük bir ayar değişikliği geniş JSON ve React güncellemesi oluşturabiliyor. İstek kimliği/sürüm, modül bazlı yanıt ve yalnız değişen durum gönderimini ekle; ilk açılışta tam snapshot korunsun. |
| R07 | P1 | [App.OnStartup](../src/WinForge.App/App.xaml.cs), ana pencereyi oluşturmadan `Host.Load` çağırıyor. [ModuleHost.Load](../src/WinForge.Core/ModuleHost.cs) DLL keşfi, tip taraması, constructor ve `Initialize` işlemlerini senkron yapıyor. | Önce modül bazlı süre ölç. Pahalı hazırlığı ihtiyaç anına ertele; WPF/COM iş parçacığı gereksinimlerini koru. `StartEnabledAsync` tek başına senkron `Activate` işlemlerini arka plana taşımaz. |
| R08 | P1 | [FileLogger](../src/WinForge.Core/Log.cs) her satırı kilit altında `AppendAllText` ile yazıyor; boyut/rotasyon sınırı yok. | Yoğun hata durumunda disk ve çağıran iş parçacığı yükü büyüyebilir. Sınırlı kuyruk, tek yazıcı, rotasyon ve kapanışta flush ekle; kuyruk taşmasını sayısal olarak raporla. |
| R09 | P2 | [main.tsx](../src/WinForge.App/ui/src/main.tsx) özellik ekranlarını statik içe aktarıyor; üretim çıktısı tek JS/CSS paketi. | Ana panel, kullanılmayan araç ekranlarının kodunu da alıyor. Önce ana panel/özellik girişini ayır, ardından ağır ekranları gerektiğinde yükle. Dosya boyutu tek başına ciddi darboğaz kanıtı değil. |
| R10 | P2 | Birden fazla bileşen bağımsız global kanca kuruyor. GlobalKeyboardHook aboneleri aynı çağrı içinde çalışıyor; GlobalMouseHook aboneleri tek tek korurken klavye olayında toplu `Invoke` kullanılıyor. | Önce kanca sayısı ve callback sürelerini ölç; yavaş işi callback dışına çıkar. Bir abonenin hatasının diğerlerini engellemesini test et. Merkezi yönlendiriciye yalnız ölçülen fayda ve giriş davranışı eşdeğerliği kanıtıyla geç. |
| R11 | P2 | [HistoryStore.Open](../src/WinForge.History/HistoryStore.cs) `Pooling=false`; arama `LIMIT/OFFSET` kullanıyor. | Büyük arşivlerde bağlantı maliyeti ve ileri sayfalar ölçülsün. Gerekirse anahtar tabanlı sayfalama ve bağlantı stratejisi iyileştirilsin; medya temizleme/yedekleme semantiği korunmalı. |
| R12 | P2 | MediaFlyoutModule, TilingModule, DisplayBrightnessModule ve FeatureApp büyük dosyalar. [FEATURE_ROADMAP](FEATURE_ROADMAP.md) bazı mevcut shell özelliklerini hâlâ yapılmamış gösteriyor. | Bakım riski yüksek. Önce değiştirilecek akışların davranış testleri; sonra UI, motor, oturum ve yaşam döngüsü ayrımı. Özellik durumu ile performans işlerini ayrı belgelerde tut ve birbiriyle bağla. |

### İlk düzeltmelerin tamamlanma ölçütleri

- **R01:** Tek komut, beklenen tüm test gruplarını ve atlama nedenlerini raporlar; ilk hatada doğru çıkış kodu verir. Varsayılan grup canlı donanım/Explorer müdahalesi gerektirmez. Windows üzerinde otomatik koşu yapılandırması eklenir; depoda izlenen `.github` iş akışı görülmedi.
- **R02:** A sorgusu sürerken B/C filtreleri gönderildiğinde son sonuç C olur. Eski sorgu sonradan bitse bile sonucu ezemez. Sayfa etiketi sonuç snapshot'ının offset'inden hesaplanır; pencere kapandıktan sonra sonuç uygulanmaz.
- **R03:** Disk dolu, erişim reddi ve bozuk JSON senaryoları açık sonuç üretir; son sağlam ayarlar korunur. Hızlı düzenlemelerde son değer yeniden başlatmada geri gelir. Ayar güncelleme, uygulama ve rollback aynı sürümle ilişkilendirilir; eski hata yeni düzenlemeyi geri alamaz.
- **R04:** Derleme bayrağı veya betik değişikliği mevcut çıktıyı geçersiz kılar. Release ve Debug seçenekleri ayrıdır. Native fixture'lar geçer; işlem süresi, sonuç doğruluğu ve shell kararlılığı önce/sonra karşılaştırılır.
- **R05:** Sabit taskbar konumunda yinelenen geometri gönderimi yapılmaz. Auto-hide, DPI/monitör değişimi ve Explorer yeniden başlatma kontrolleri geçer. Boşta konum kontrol sayısı başlangıca göre en az %80 azalır; görünürlük gecikmesi belirlenen UX eşiğini aşmaz.
- **R06:** Tek modül ayar değişikliğinin yanıtı ilgisiz modül panellerini taşımaz. Gecikmiş onaylar kullanıcının daha yeni düzenlemesini ezmez. WebView2 yeniden bağlanınca tam durumla toparlanır.

## Ölçüm planı ve önerilen bütçeler

Her koşuda Windows build'i, CPU/RAM, ekran sayısı/DPI, WebView2 sürümü, etkin modül listesi ve paket türü kaydedilmeli. Uygulama ile WebView2 süreçlerinin bellek/CPU kullanımı ayrı ve toplam raporlanmalı. Klasör boyutu motoru için Explorer içindeki ek maliyet de ölçülmeli.

| Senaryo | Ölçüm yöntemi | Önerilen kabul hedefi |
|---|---|---|
| Açılış | Soğuk ve sıcak açılışı ayır; süreç başlangıcı → ilk kullanılabilir panel, modüller hazır zamanı ve modül başına süre. İlk turda 10 koşu; kararlı p95 için en az 30 koşu. | İlk kullanılabilir panel p95 ≤ 2 sn; modül hazırlığı ayrıca raporlanır. Baz ölçümden sonra donanıma göre kesinleştir. |
| Tepside boşta | 2 dk ısınma + 10 dk örnekleme; tüm modüller kapalı, günlük profil, medya ve Explorer profilleri ayrı. CPU saniyesi/duvar saati ve sistemdeki toplam CPU payı açıkça ayrılır. | Günlük profilde toplam normalize ortalama CPU ≤ %0,5; arka plan ağır işi bu pencereye karıştırma. |
| Arayüz tepkisi | Ayar komutu → native onay, dispatcher gecikmesi, JSON baytı ve React render sayısı. | Disk/donanım işi gerektirmeyen ayar etkileşimi p95 ≤ 100 ms; tek değişiklikte köprü yükü ≥ %50 azalsın. |
| Kalıcılık | Yazma sayısı ve süresi; hızlı düzenleme, ani kapanış, disk hatası; eski/yeni onay sırası. | Seri düzenlemeler birleştirilsin; normal kapanışta son sürüm flush edilsin. Ani kesintide kayıp penceresi tanımlı olsun ve kaydedildi bildirimi gerçeği yansıtsın. |
| Uzun kullanım | 8 saat günlük profil + 100 modül aç/kapat ve araç penceresi aç/kapat döngüsü. Private bytes, handle ve thread eğilimleri. | Isınma sonrası sürekli kaynak artışı olmasın; model/tarayıcı önbelleği ile sızıntı ayrıştırılsın. |
| Geçmiş | Sentetik 10 bin/100 bin kayıt; FTS filtre, tarih aralığı, ileri sayfa ve hızlı ardışık filtre. | Son istek daima kazanır; 100 bin kayıtta sorgu p95 ≤ 200 ms başlangıç hedefi. Önce sorgu planını doğrula. |
| Klasör boyutu | Soğuk/sıcak yerel klasör, küçük dosya yoğunluğu, değişiklik fırtınası, erişim reddi ve iptal. | Mevcut tarama/bellek bütçeleri korunur; Explorer UI callback'i dosya taramasını beklemez; önbellek isabeti ve kuyruk derinliği raporlanır. |

Bu sayılar ölçülmüş mevcut değerler veya garanti değildir. Faz 0 sonunda hedef makine/profil için onaylanmış bütçelere dönüştürülmelidir. Anlamlı kazanç göstermeyen değişiklik sırf kodu değiştirmek için sürdürülmemeli.

## Teslimat sırası

Tahminler bir geliştirici, mevcut ortam ve kademeli PR'lar içindir; kişi-gün belirtir. Donanım temini ve başka Windows sürümlerindeki test beklemeleri hariçtir. Tam program yaklaşık 32–46 kişi-gün; Faz 4 işleri ölçüm sonucuna göre ertelenebilir.

| Faz | Efor | İşler ve bağımlılıklar | Çıkış koşulu |
|---|---|---|---|
| 0 — Güvenilir başlangıç | 3–4 gün | R01; ölçüm senaryoları, modül/komut süreleri ve performans rapor şablonu. Özellik belgesindeki en belirgin durum çelişkilerini işaretle. | Tüm test grupları görünür; testler tekrar üretilebilir; açılış/boşta/ayar akışı için baz rapor var. |
| 1 — Doğruluk ve kalıcılık | 5–7 gün | R02 ve R03. Geciken sorgu/onay, bozuk ayar, disk hatası, kayıt sonrası yeniden açılış testleri. Faz 0 test girişine bağlı. | Eski sonuç yeni durumu ezmiyor; kayıt hatası sessiz değil; son sağlam ayarlardan kurtarma çalışıyor. |
| 2 — Hedefli performans | 6–9 gün | R04, R05, R08. Her biri ayrı değişiklik ve önce/sonra ölçümü. R04'te yeniden derleme doğruluğu, optimizasyon bayrağıyla birlikte teslim edilmeli. | Native testler ve kaynak bütçeleri geçiyor; ölçülmüş kazanç var; görünürlük/iptal/kapanış davranışı korunuyor. |
| 3 — Açılış ve UI veri akışı | 7–10 gün | R06, R07 ve gerekli olursa R09. R03'ün ayar sürümleme sözleşmesine ve Faz 0 profiline bağlı. | Panel ve modül hazır süreleri ayrı izleniyor; küçük değişiklikler küçük mesaj üretiyor; autosave testleri geçiyor. |
| 4 — Ölçeğe göre bakım | 6–9 gün | Ölçüm gerektiriyorsa R10/R11; R12'yi ilgili dosyada değişiklik yapıldıkça parçalara böl. | Global giriş davranışı aynı; büyük arşiv bütçesi karşılanıyor; kaynak yaşam döngüsünün sahibi açık. |
| 5 — Yayın kapısı | 5–7 gün | Desteklenecek Windows build/DPI/çoklu ekran matrisi; Explorer yeniden başlatma, uyku/uyanma, oturum kilidi, ses aygıtı değişimi, gerçek CPU/CUDA ve 8 saat dayanıklılık. | Destek matrisi ve bilinen sınırlar yazılı; temiz makinede paket doğrulaması ve önceki sürüme dönüş denenmiş. |

Bağımlılık zinciri: **Faz 0 → Faz 1 → Faz 3**. Faz 2, ölçüm tabanı kurulduktan sonra bağımsız küçük teslimatlar olabilir. Faz 5'in kısa uyumluluk kontrolleri her fazda tekrarlanmalı; yalnız son haftaya bırakılmamalı.

### İlk 10 iş günü için somut plan

1. **Gün 1–2:** Test envanteri ve ortak komut; ayrı UI test sunucularının port çakışmasını kaldır. Önceki genel regresyon fixture kaydını yeniden kontrol et.
2. **Gün 3–4:** Açılış, modül, köprü ve boşta CPU ölçümleri; üç sabit etkin modül profili ve baz rapor.
3. **Gün 5–6:** Geçmiş ekranında son isteği koruyan yenileme ve gecikmeli sorgu testi (R02).
4. **Gün 7–9:** Ayar kayıt sonucunun görünür olması, bozuk dosyayı koruma ve son sağlam kayıttan dönüş; sürümlü işlem/rollback tasarımı (R03).
5. **Gün 10:** Yerel derleme seçenekleri ve artımlı geçersiz kılma düzeltmesi için küçük teslimat (R04); test/maliyet beklenenden büyükse Faz 2'ye taşınır.

## Uygulama sınırları ve yayın kontrolü

- Modül keşfini veya tüm `Activate` çağrılarını topluca `Task.Run` içine taşımak uygun bir varsayılan değil; WPF/COM iş parçacığı gereksinimleri modül bazında ele alınmalı.
- Yaşam döngüsündeki ortak kilit doğrudan kaldırılmamalı. Önce bekleme süreleri ölçülmeli; gerekirse modül bazlı seri kuyruk ve açık ortak kaynak bağımlılıkları tasarlanmalı.
- Kullanıcı girdisini tüketme kararı callback içinde kalmalı; dosya, süreç ve UI hazırlığı gibi ağır işler dışarı alınmalı. Gönderilen yapay girdiler, modifier durumu, uygulama odağı ve kısayol kaydı regresyonları izlenmeli.
- Shell enjeksiyonu ve süreç içi Whisper mevcut mimarinin risk sınırlarıdır. Desteklenmeyen Windows build'inde güvenli devre dışı bırakma, özgün durumun geri yüklenmesi ve tanı kaydı doğrulanmalı. Yeni sürekli yardımcı süreç ekleme kararı ölçülmüş izolasyon ihtiyacına dayanmalı.
- Pooling veya `secure_delete` gibi depolama davranışları yalnız hız kazanımı için körlemesine değiştirilmemeli; medya silme, yedekleme ve taşınabilir veri davranışı test edilmeli.
- Yeni özellik genişletmeleri, ilk üç fazın doğruluk ve performans kapıları geçtikten sonra mevcut özellik yol haritasıyla birlikte önceliklendirilmeli.
- Her teslimat: değişiklik öncesi/sonrası aynı profil, ilgili davranış testleri, ölçüm sonucu ve geri alma adımı. Tek PR'da mimari yeniden düzenleme ile birden çok performans değişikliği birleştirilmemeli.

İlk uygulama adımı **R01 + Faz 0 ölçüm tabanı**; hemen ardından **R02/R03** olmalıdır. En erken performans adayları **R04/R05**, daha geniş değişiklikler ise ölçümlerle gerekçelendirilen **R06/R07**'dir.
