# Sonlandırma denetimi — 2 Kasım 2026

**Karar: doğrulanan kod değişiklikleri ve paket içeriği geçti; tüm yol haritası için başarıyla tamamlandı onayı verilmedi.** [Özgün kapsam](OPTIMIZATION_ROADMAP.md) ve [ilerleme kaydı](OPTIMIZATION_PROGRESS.md) korunur. Bu belge kapsamı daraltan bir kapanış değildir.

## Bu denetimde düzeltilenler

- Köprü testinin özel panel probe'u, regresyon assembly'si plugin olarak yüklendiğinde yanlışlıkla ikinci modül olarak keşfediliyordu. Parametresiz constructor sözleşmesini karşılaması engellendi; gerçek modül keşfi ve lifecycle kontrolleri yeniden geçti.
- Genel regresyondaki kaldırılmış WPF ModuleList/DetailPanel/SearchBox testleri, üretim React/WebView arayüzünü işleten DashboardInterfaceChecks ile değiştirildi. Gerçek katalog ve tüm ayar etiketleri, kategori/boş arama, filtreyi sıfırlama, ayar kaydı, lifecycle, scrolling, minimum pencere genişliği ve dokuz tema/vurgu birleşiminde gerçek CSS kontrastı denetlenir. Kaldırılmış ürün filtreleri uydurularak geri eklenmedi; güncel seçim/katalog davranışı test edilir.
- Aynı etiketli pencere eylemleri bölüm adıyla seçilir; JSON'dan gelen Türkçe metinler decode edilerek karşılaştırılır. Test profili kapanırken kendi browser'ı beklenir. Geçici DLL kopyalarını yükleyip temizlik sırasında kilitli bırakmak yerine üretim modülleri mevcut derleme dizininden yüklenir.
- Arayüzdeki `mod?l etkin` yazımı `modül etkin` olarak düzeltildi.
- Runner sözleşme betiğinin normal çıkışı açıkça 0 oldu. Beklenen başarısız alt süreçler başarılı betiğin sonucuyla karıştırılmaz.
- `verify-publish.ps1` eklendi: managed/native/UI dosyaları derleme hash'leriyle, native giriş noktaları x64 PE başlıklarıyla, çevrimdışı modeller/lisanslar kaynak hash'leriyle karşılaştırılır. Eksik/eski UI dosyaları ve canlı ayar/geçmiş/log verileri reddedilir; doğrulama başarısızsa JSON da failed olur.

## İncelenen kanıt

Ortak komut: `./scripts/test.ps1 -Performance -InteractiveInput`

Rapor: `artifacts/test-results/run-20261102-164844-148.json`; ayrıntı: `artifacts/test-results/final-verification.log`.

| Seçili grup | Sonuç |
|---|---:|
| Core / DefaultSettings / Automation / ShortcutAction fast | 51 / 48 / 74 / 218 kontrol geçti |
| UI | 38 test ve üretim derlemesi geçti |
| Gerçek SendInput/WM_HOTKEY | 16 kontrol geçti |
| 10k/100k geçmiş | 33 kontrol geçti |
| Native/React köprü | 120 kontrol geçti |
| Güncel dashboard ve lifecycle | 481 kontrol geçti |
| Runner sözleşmesi | 14 ayrı stub kontrolü geçti; ürün test sayısına eklenmez |

Seçili ürün grupları toplam 1079 kontrol/test içerir; bunlar tüm ürünün bağımsız 1079 senaryosu veya genel entegrasyon projelerinin geçtiği anlamına gelmez. Raporda seçilmeyen beş entegrasyon projesi açıkça skipped görünür; seçili gruplarda failed/not-run satırı yoktur.

Yayın komutu: `dotnet publish src/WinForge.App -c Release -p:Platform=x64 --artifacts-path artifacts/publish-build -o artifacts/publish-audit`. Paket derlendi; `./scripts/verify-publish.ps1` geçti. İçerik raporu `artifacts/test-results/publish-audit.json`: 29 gerekli dosya ve 11 çevrimdışı kaynak doğrulandı. Bu, mevcut makinede paket içeriği denetimidir; temiz Windows'ta kurulum/açılış değildir.

## Özgün kapsamın kapanış durumu

| İş | Durum / eksik kanıt |
|---|---|
| R01 | Envanter, hata sözleşmesi, hızlı/native ve güncel UI grupları geçti. Tüm entegrasyon projeleri ve uzak CI doğrulaması açık. |
| R02 | Gecikmiş sorgu/son istek/snapshot/kapanış kontrolleri mevcut ve geçti. |
| R03 | Kalıcılık, hata, kurtarma, batch ve eski rollback izolasyonu kontrolleri geçti; yayın/recovery matrisi ayrıca açık. |
| R04 | Release/Debug bayrakları ve native fixture'lar doğrulandı. Gerçek tarama için optimizasyon öncesi/sonrası performans karşılaştırması açık. |
| R05 | Kontrollü geometri ve mevcut oturum testleri geçti. DPI/monitör, Explorer restart ve tam auto-hide matrisi açık. |
| R06 | Sürümlü fark, kalıcı onay, panel erteleme, eski istemci, renderer crash ve resync kontrolleri geçti. |
| R07 | Bileşen ölçümleri var; son süreç/katalog p95 yaklaşık 2,95 sn ve hedefin üstünde. Gerçek App soğuk/sıcak açılışı ve 2 dk ısınma + 10 dk tepsi profilleri açık. |
| R08 | Sınırlı log/rotasyon/flush/hata ve yerel tanı raporu kontrolleri geçti. |
| R09 | UI paket ayrımı, üretim derlemesi ve gerçek WebView lazy yükleme doğrulandı. |
| R10 | Abone hata izolasyonu doğrulandı. Gerçek hook sayısı/callback profili ve merkezileştirme kararı açık. |
| R11 | 100k veri/sorgu planları ölçüldü; 200 ms sorgu bütçesi karşılandı. Bu kapsamda pooling/keyset değişikliği gerekmedi. |
| R12 | Modül/parlaklık, Tiling ve pano ayrımları yapıldı. Kalan FeatureApp görünüm grupları ve medya oturumu/arayüz sınırı açık. |
| Faz 5 | Paket içeriği geçti. Temiz makine, önceki sürüme dönüş, desteklenen Windows/DPI/donanım matrisi, uyku/kilit/aygıt değişimi, gerçek CPU/CUDA ve 8 saat + 100 lifecycle döngüsü tamamlanmadı. |

NavigationTests'in bu denetimdeki gerçek koşusu, çalışan uygulamanın Gezgin stilleri etkin olduğu için kendi korumasında durdu: `Disable navigation styling before running these tests.` Bu koruma kaldırılmadı; kullanıcı uygulaması zorla kapatılmadı. Başarısız deneme, başarılı entegrasyon kanıtı olarak kullanılmaz.

Tam kapanış ancak açık satırlar özgün kapsamlarına uygun ölçüm ve çalıştırma kanıtlarıyla kapatıldığında verilebilir. Özellikle paket derlemesi veya kısa UI koşusu, 8 saatlik dayanıklılık ve donanım matrisinin yerine geçirilmez.
