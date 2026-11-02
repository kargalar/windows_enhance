# Ana panel durum köprüsü

Ana pencerenin WebView2 iletişimi `MainWindow.StateBridge.cs`, istemci birleştirmesi `ui/src/dashboardStateBridge.ts` içinde tutulur. İlk açılış ve yeniden eşitleme tam katalog gönderir; normal işlemler alan farklarını gönderir. Özellik pencerelerinin `feature.ready`/`feature.state` sözleşmesi ayrıdır.

## Protokol 1

İstemci komutları `command`, `payload` ve sayfa ömrü içinde benzersiz `requestId` içerir. Yerel uygulama komutları sırayla yürütür; ayar uygulama, geri alma ve kalıcı yazım tamamlanmadan başarılı komut yanıtı göndermez. `requestId` yetkilendirme belirteci değildir; yanıtı ilgili isteğe bağlar.

Her durum iletisi `protocolVersion:1`, pencere ömrüne ait `sessionId` ve artan `version` taşır:

| Tür | İçerik |
|---|---|
| `state` | Bütün modüller, kategoriler, görünüm ve kayıt durumu. `ui.ready` ve `ui.resync` yanıtı. |
| `state.patch` | Son teslim edilen sürümü belirten `baseVersion`; değişen alanlar ve güncel kayıt/sayaç durumu. |
| `error` | Komut hatası, istek kimliği ve güncel tam snapshot. |
| `protocol.error` | Ayrıştırılamayan/geçersiz ileti için yalnız hata açıklaması; çalışan başka bir işlemin durumunu değiştirmez. |

Patch içeriğindeki `modules` yalnız tam değiştirilmesi gereken modüllerdir. Normal durumda `moduleChanges` içindeki `id` ve `changes` kullanılır. Her değişiklik `path` dizisi, `value` ve `remove` bayrağı taşır. Aynı uzunluktaki dizilerde yalnız değişen alanlar adreslenir; dizi uzunluğu değişirse dizi atomik olarak değiştirilir. JSON `null` ataması ile bir alanın kaldırılması farklı işlemlerdir.

Sunucu pencere başına her modülün yalnız son JSON snapshot'ını saklar. Değişiklik sayısı 4096'yı aşarsa tam modül değiştirmeye döner. Değişmeyen simge/metin dizgileri küçük alan düzenlemelerinde yeniden gönderilmez. Panel olayları aynı dispatcher turunda birleştirilir; komut devam ederken gelen olaylar yanıtına katılır. Kayıt bildirimi tek başına bütün panelleri yeniden oluşturmaz.

### Panel verisini gerektiğinde yükleme

Yeni istemci `ui.ready` yükünde `deferredPanels:true` gönderir. İlk tam katalog bütün modülleri ve ayar tanımlarını içerir; panel verileri `panel:null`, `panelAvailable` ve `panelLoaded:false` ile temsil edilir. Kart ayrıntıları açılınca istemci `module.inspect` ve `moduleId` gönderir. Sunucu yalnız o panelin verisini hazırlar ve mevcut sürümlü fark protokolüyle yanıtlar. Arama, ayar etiketlerini panel açılmadan kullanabilir.

Bir panelin okunması başarısızsa `panelError` ve hata açıklaması gönderilir. Arayüz açık bir yeniden yükleme eylemi sunar; otomatik istek döngüsü oluşturmaz. Açılmış paneller aynı sayfanın resync yanıtında korunur. Yeni gezinme veya renderer toparlanması incelenen panel kümesini sıfırlar; Hide/Show bu kümeyi korur. Panel komutu da hedef paneli açıkça incelenmiş sayar.

Bu yeteneği bildirmeyen eski istemciler ilk tam snapshot'ta dolu paneller almaya devam eder. Protokol 1'in eski tam-durum davranışı korunur. Gerçek WebView testi iki yolu, hatadan yeniden denemeyi, kapalı panel getter'larının çağrılmamasını ve Windows program listesinin yalnız ayrıntı açılışında hazırlanmasını doğrular.

İstemci, oturum veya ardışık sürüm uyuşmazlığında mevcut görünümü koruyup bir kez `ui.resync` ister. Tam snapshot gelene kadar devam patch'lerini uygulamaz. Eski sürümler ve terk edilmiş oturumlardan gelen yanıtlar reddedilir. Farklar önce yeni nesnelere uygulanır; geçersiz bir yol bütün güncellemeyi reddeder. Değişmeyen modüllerin nesne kimliği korunur. Nesne anahtarları prototype değiştirecek atamalarla uygulanmaz.

## Yaşam döngüsü ve hata toparlanması

Ana pencere ilk `Loaded` işleminden sonra handler'ı bırakır; Hide/Show sayfayı tekrar yönlendirmez ve ikinci komut aboneliği eklemez. Yeni gezinmede sayfa hazır işareti sıfırlanır. React yeniden açıldığında `ui.ready` ile native durum yeniden alınır. Renderer süreci kapanırsa ana panel yeniden yüklenir. Bu davranış bütün WebView2 browser sürecinin çökmesi için bir izolasyon iddiası değildir.

Kapanışta panel abonelikleri ve snapshot önbelleği bırakılır. Test, kendisine ait browser sürecinin çıktığını bekler ve yalnız kendi geçici profilini temizler. Henüz native'e gönderilmemiş düzenlemeler renderer çökmesinde kurtarılmaz; mevcut otomatik kaydın gecikme penceresi geçerlidir.

`Uygulama günlükleri` eylemi yazıcıyı flush eder ve veri klasörüne `diagnostics.json` yazar. Rapor modül etkinliği/hataları, kayıt sürümleri, log ve performans izi kayıp sayaçlarını içerir; ayar değerlerini içermez. Dosya yereldir; dışarı gönderilmez.

## Doğrulama

```powershell
./scripts/test.ps1 -Performance
./artifacts/optimization-build/bin/WinForge.RegressionTests/release/WinForge.RegressionTests.exe --context-menu-ui-only
```

Saf istemci testleri gecikmiş yanıtı, sürüm boşluğunu, oturum değişimini, geçersiz yolları, nesne kimliğini ve dizi/alan değişikliklerini kapsar. Core testleri fark üretimini, JSON null/kaldırma ayrımını ve değişiklik bütçesini doğrular. Gerçek WebView testleri kalıcı onay, sıralı sürüm, yeniden yükleme, gerçek renderer çökmesinden otomatik dönüş, Hide/Show ve büyük ikon kataloğunun tekrar gönderilmemesini kapsar.

Renderer hata enjeksiyonu, yalnız testin özel profiline ait WebView üzerinde Chromium'un [`Page.crash` protokol komutunu](https://raw.githubusercontent.com/ChromeDevTools/devtools-protocol/master/pdl/domains/Page.pdl) kullanır. Kullanıcının açık WinForge veya tarayıcı süreçleri hedeflenmez.

Ölçümler `artifacts/performance/dashboard-bridge.json` ve `context-bridge.json` dosyalarındadır. Baytlar alınan nesnelerin System.Text.Json ile kanonik yeniden yazımıdır. Komut p95'i tarayıcıdaki `performance.now()` ile postMessage → native onay arasında ölçülür; 20 ms aralıklı test yoklamasının süresi ayrı alan olarak raporlanır. Önceki test yoklaması süreleriyle yeni tarayıcı süreleri doğrudan karşılaştırılmamalıdır.
