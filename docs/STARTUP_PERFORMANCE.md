# Açılış bileşenleri ölçümü

R07 için iki farklı kapsam izlenir: üretim uygulamasının gerçek açılışı ve modül/dashboard bileşenlerinin izole ölçümü. İkincisi, birincinin yerine geçmez.

```powershell
dotnet build tests/WinForge.RegressionTests -c Release -p:Platform=x64 --artifacts-path artifacts/optimization-build
./scripts/measure-startup-components.ps1 -Iterations 30
```

Her örnek yeni RegressionTests süreci, yeni ayar dosyası ve yeni WebView profili kullanır. Üretim modülleri keşfedilir ve üretim dashboard'u gerçek native snapshot ile açılır. Katalogdaki bütün kartlar, ayar açma düğmesi ve arama alanı DOM'da oluştuğunda zaman durdurulur. DOM yoklaması sorgu başına en fazla bir 10 ms aralık ekler. Windows dosya önbelleği temizlenmez; bunlar işletim sistemi önbelleği sıcak, süreçleri yeni açılışlardır.

`report.json`, ham `sample-*.json`, modül/komut sürelerini içeren `sample-*.jsonl` dosyaları birlikte tutulur. Rapor Windows/runtime, CPU/RAM, ekran sınırları, pencere DPI, WebView2 sürümü ve test/app/Extras assembly hash'lerini kaydeder. Sayılar minimum/medyan/p95/maksimum olarak ayrılır. Örnek dosyaları üzerine yazılmaz; başarısız alt süreçte seri durur.

Ölçülen süreler:

- `hostLoadMilliseconds`: gerçek ModuleHost.Load, DLL keşfi ve modül Initialize işlemleri.
- `showToCatalogMilliseconds`: pencere Show öncesi → kullanılabilir katalog.
- `processToCatalogMilliseconds`: test sürecinin başlangıcı → kullanılabilir katalog; test uygulamasının WPF hazırlığını da içerir.

Test `App.OnStartup`, gerçek tek-instance mutex'i, tray ve etkin modül başlatmasını çalıştırmaz. Hiçbir modülün Activate metodu çağrılmaz; kalıcı Windows yapılandırması zaten etkinse bir modül bunu IsActive ile raporlayabilir. Bu durum rapordaki `activeModules` alanında görünür; testin açtığı modüller `activatedModules: []` olarak ayrıca belirtilir. Browser kapanışı ve sahip olunan profil temizliği beklenir.

## İlk inceleme ve karar

Değişiklik öncesi tek bileşen izinde ModuleHost.Load yaklaşık 292 ms, DefaultAppsModule constructor/Initialize yaklaşık 134 ms, ilk BuildState yaklaşık 72 ms idi (`artifacts/performance/startup-component-before.jsonl`). Bu tek gözlem kararlı p95 veya tam uygulama baz profili değildir.

İlk aşamada varsayılan programlar ve klasör yollarının Windows okumaları Initialize'dan ilk panel isteğine ertelendi. İlk sonraki örnekte DefaultAppsModule Initialize 0,26 ms, ilk `defaults.read` yaklaşık 197 ms oldu. Aşağıdaki ilk seri bu aşamaya aittir: ilk snapshot hâlâ bütün panelleri hazırlıyordu. Sonraki değişiklikte panel verisi ayrıntı açılışına ertelendi; güncel sonuçlar belgenin sonundadır. [Sorumluluk belgesi](MODULE_RESPONSIBILITIES.md) akışı açıklar.

30 örnekli sonucun, gerçek tepsi profilleri ve üretim açılışının soğuk/sıcak 30 örnekli ölçümüyle tamamlanması gerekir. Hâlen açık kullanıcı uygulaması eski sürümdür; onun CPU/RAM verileri yeni kodun sonucu sayılamaz. Gerçek App açılışı için 2 saniyelik p95 hedefi bu bileşen ölçümünden geçirilmiş sayılmaz.

## 30 süreçlik sonuç

Ham çıktı: `artifacts/performance/startup-components-20261102-130908/report.json`; yanında 30 JSON örneği ve 30 JSONL izi bulunur. Bütün örnekler aynı test/app/Extras hash'lerini taşıdı. Her süreçte iki katalog/doğrulama kontrolü geçti; toplam 60 kontrol. Her süreç kendi browser'ını kapattı ve profilini temizledi.

| Kapsam | Minimum | Medyan (üst orta örnek) | p95 | Maksimum |
|---|---:|---:|---:|---:|
| ModuleHost.Load | 185,92 ms | 334,90 ms | 367,59 ms | 376,73 ms |
| Show → kullanılabilir katalog | 740,47 ms | 1232,51 ms | 1351,72 ms | 1369,74 ms |
| Test süreci → kullanılabilir katalog | 1330,14 ms | 2547,81 ms | 3555,44 ms | 3558,92 ms |

DefaultAppsModule Initialize 30 örnekte 0,23–0,89 ms, ortalama 0,42 ms oldu. Ertelenen `defaults.read` ortalaması 223,05 ms, aralığı 129,68–308,71 ms. İşin kaybolmadığı ve ilk snapshot'a taşındığı açıkça görülüyor. Süreçten katalog p95'i **2 saniyeyi aşmaktadır**; R07 tamamlanmış sayılmaz. Ölçüm sırasında eski kullanıcı uygulaması da çalışıyordu. Önceki tek gözlemle bu seriden yüzde kazanç hesaplanmıyor.

## Panel verisi ertelendikten sonraki tekrarlar

| Seri | Örnek | İlk BuildState p95 | Show/katalog p95 | Test süreci/katalog p95 |
|---|---:|---:|---:|---:|
| İlk erteleme öncesi `130908` | 30 | 433,84 ms | 1351,72 ms | 3555,44 ms |
| Panel erteleme `132335` | 30 | 28,64 ms | 752,27 ms | 1842,40 ms |
| Uyumluluk ve bakım sonrası `133304` | 30 | 49,40 ms | 1509,49 ms | 2946,16 ms |

Yollar `artifacts/performance/startup-components-20261102-<seri>/report.json` biçimindedir. Son seride 90 kontrol geçti; 30 süreçte `defaults.read` ve `bridge.readPanel` sayısı **sıfır** oldu. 33 managed/UI dosyasının hash manifest'i bütün örneklerde aynı kaldı; sonraki ortak derlemedeki dosyalarla da eşleşti. Bu manifest modellerin veya bütün publish bağımlılıklarının doğrulanması değildir.

İlk durum hazırlığındaki iş azaldı; buna rağmen süreç/katalog p95'i son tekrarda 2 saniyeyi geçti. Makinedeki zamanlama farkı yüzünden yalnız hızlı olan seri seçilerek genel açılış başarısı ilan edilmez. Üretim App açılışı, soğuk/sıcak ayrımı ve günlük/tepsi profilleri hâlâ gereklidir. İlk snapshot baytları köprü raporunda yaklaşık 156 KB; açılmış context panelinin resync yanıtı daha büyük olabilir.
