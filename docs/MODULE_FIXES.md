# Modül düzeltmeleri

- Klavye Yöneticisi artık listelenmez ve eski etkinleştirme kaydı kaldırılır. Tuş Kombinasyonu Çevirici kendi dahili eşleme motorunu kullanmaya devam eder.
- Ekrandan Metin Al, WebView yüklenmesini beklemeden yerel seçim yüzeyini açar. Fareyle sürükleyerek seçin; Esc veya sağ tıkla iptal edin. Modül kapatılırsa seçim de kapanır.
- Power Display, ekran düzeni değişince önbellekteki monitör tutamaçlarını yeniler. DDC/CI okumaları geçici hatalarda tekrar denenir. Tek bağlı ekranda başarılı parlaklık değişimi için monitör sayısı gösterilmez. Çoklu ekran durumunda başarısız/desteksiz ekranlar başarı sayısına eklenmez.
- Dock büyüme animasyonu mevcut ölçekten devam eder, yumuşak hız azalması kullanır ve çıkışta daha yavaş küçülür. Windows animasyon tercihi korunur.
- Görev çubuğu yüksekliği 24–96 mantıksal piksel arasında uygulanır. Küçük yüksekliklerde düğmelerin alt sınırları ve varsayılan simge boyutu da küçülür. `0` Windows boyutunu geri yükler.
- İnternet Hızı, negatif ekran koordinatlarında ve görev çubuğu ekran kenarından hafif taşarken görünür kalır; başlangıçta sıfır hızlarını gösterir ve üstteki konumunu yeniler.
- Ekran Yakalama → **Yakalanan görseller için klasör seç** PNG çıktılarının konumunu belirler. Sağ alttaki önizlemeye tıklamak görüntüyü kaydeder ve Dosya Gezgini’nde seçer. Aynı görüntü yeniden dışa aktarılırken çoğaltılmaz.
- PC Geçmişi → **Görseller ve geçmiş için klasör seç** veritabanının konumunu belirler; görseller bu klasördeki `media` alt klasörüne yazılır. Eski klasörün içeriği taşınmaz veya silinmez. Önceki klasörü yeniden seçerek eski kayıtlara erişebilirsiniz.
- Mikrofon adında dost ad bulunmuyorsa mikrofon türü ve ses aygıtı adı birlikte gösterilir; teknik aygıt kimliği ad olarak kullanılmaz.
- Etkileşimli saydam pencereler fare girdisini alacak yerel arka plan piksellerine sahiptir. Ses/parlaklık kaydırıcıları ve ses simgesine tıklayarak sessize alma bu pencere üzerinden çalışır.
- Medya Kısayolları kartlarında ses artırma/azaltma miktarı ayrı ayrı 1–100 yüzde puan olarak ayarlanır. Eski boş değerler 2 puanlık adımı, eski “Sesi 5 azalt” kartı 5 puanlık adımı korur.

## Doğrulama

Release/x64 uygulama ve yerel C++ kabuk bileşeni derlemesi başarılıdır.

- Arayüz: `npm run test:shortcuts` — 15 kontrol.
- `WinForge.CaptureTests.exe` — 121 kontrol; özel görsel/geçmiş klasörleri ve aynı dosyanın tekrar kullanılmasını kapsar.
- `WinForge.ShortcutActionTests.exe` — 187 kontrol.
- `WinForge.RegressionTests.exe --module-fixes-only` — yerel metin seçimi/Esc, kaldırılan modül ayarları, ses adımları ve etkileşimli pencere girdi yüzeyi.
- Hedefli regresyon kipleri: `--taskbar-only`, `--display-brightness-only`, `--volume-overlay-only`, `--net-speed-only`, `--combined-modules-only`, `--panel-placement-only`.

Donanım okuma kontrolünde iki masaüstü ekranı listelenirken yalnızca birinde DDC/CI parlaklık denetimi okunabildi; WMI parlaklık sağlayıcısı desteklenmedi. İki fiziksel ekranın birlikte değişmesi bu ortamda doğrulanamadı. Desteksiz ekranlar için donanım parlaklığı taklit edilmez.

Computer Use geçici test pencerelerini listeleyemediği için gerçek fare sürüklemesi görsel olarak doğrulanamadı. Otomatik panel testleri komutları, sürükleme sırasında açık kalmayı ve kapanmayı doğrular.

Geniş regresyon çalıştırmasında bu değişikliklerin dışındaki pencere kontrolü testi (`Native wheel zoom anchors the actual clicked corner and grows the opposite edges`) başarısız oldu. Hedefli modül testlerinin sonuçları bundan bağımsızdır.
