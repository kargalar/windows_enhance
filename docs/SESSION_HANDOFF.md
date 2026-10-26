# Oturum devri — 24 Ekim 2026

Kullanıcının isteğiyle çalışma burada durduruldu. Yeni kapsam açılmadı;
devam eden düzeltmeler derlenip test edildi, süreçler ve geçici sistem
değişiklikleri temizlendi. Değişiklikler çalışma ağacında; commit oluşturulmadı.

## Tamamlananlar

- Windhawk/AltSnap entegrasyonu, gömülü AltSnap dosyaları ve NAudio gibi doğrudan
  üçüncü taraf paket gereksinimleri kaldırıldı. Pencere, görev çubuğu, ses yakalama,
  ses oturumu ve spektrum işlevleri WinForge koduyla yürütülüyor. Windows/.NET
  platform API'leri kullanılmaya devam ediyor; yeni NuGet paketlerini engelleyen
  derleme kuralı var.
- Görev çubuğu tasarımı, iki satırlı tepsi düzeni, gerçek boyutlandırma ve geri alma
  doğrulandı. Ortak kabuk bağlantısı kopunca yeniden kurulması düzeltildi.
- Pencere menüsü seçimi sırasında odak kaybı ve PDF önizlemesi sonrasında kapanış
  çökmesi giderildi. PDF için kendi yerel yardımcı sürecimiz açıkça Windows WARP
  yazılımsal çizimini kullanıyor.
- Ekran denetiminde aygıt kimliği, VCP çıktı boyutu ve dahili parlaklık aralığı
  düzeltildi. Bağlı iki harici ekranın gerçek değerleri okundu; donanıma yazılmadı.
- Tepsi ayarının geliştirme sürecinde özel kayıt defteri görünümüne yazıldığı
  kanıtlandı. Explorer'dan normal başlatılan gerçek modül, kendi iki test simgesini
  gösterdi/gizledi ve özgün durumuna döndürdü. Üretime ek kayıt defteri vekili veya
  harici motor eklenmedi.

## Son doğrulama

| Kontrol | Sonuç |
|---|---|
| Release/x64 derleme | 0 hata, 0 uyarı |
| Tam `--native-smoke` | 802 kontrol, süreç çıkışı 0 |
| Yerel kabuk `--shell-only` | 11 kontrol, süreç çıkışı 0 |
| Normal masaüstü bağlamında `--tray-live` | 9 kontrol, süreç çıkışı 0 |
| Önceki gerçek görev çubuğu testi | 17 kontrol geçti |
| Önceki gerçek Edge / Core Audio testleri | 4 / 6 kontrol geçti |
| Bağımsız x64 yayın | Başarılı; .NET çalışma zamanı pakette |

Çalıştırılabilir paket: `artifacts/WinForge-win-x64/WinForge.exe`.
Son kayıtlar: `%TEMP%/WinForge-pdf-full-build.log`,
`%TEMP%/WinForge-pdf-full-native-smoke.log`, `%TEMP%/WinForge-shell-final.log`,
`artifacts/desktop-tests/tray-live.log` ve `%TEMP%/WinForge-final-publish.log`.

Test/derleme süreçleri sonlandırıldı, Frida oturumu kapatıldı. Test tepsi simgeleri
ve kendi kayıtları temizlendi. Geçici küçültülen pencere özgün durumuna döndürüldü.
Windhawk, PowerToys ve AltSnap yeniden başlatılmadı. Kullanıcının diğer
uygulamaları durdurulmadı.

## Devam sırası

1. Gerçek Windows bildirimi: Bildirim geçmişe ulaşıyor, fakat ekrandaki kendi
   bildiriminin penceresi bulunamadı. Kullanıcı Rahatsız Etmeyin'in kapalı olduğunu
   doğruladı. Normal Explorer başlatması ve bildirim durumu `ACCEPTS` ile de görünür
   bildirim elde edilemedi. Son eklenen kısayol özelliklerini geri okuma ve
   `AppsFolder` hazırlık kontrolleri derlendi; canlı olarak henüz çalıştırılmadı.
   `FEATURE_VERIFICATION.md` içindeki sınırlı kayıt bekletme adımıyla yalnızca
   kendi AUMID/kısayol görünürlüğünü kontrol ederek devam et. Başka bildirimleri
   taşımadan önce benzersiz test metniyle kendi pencere kimliğini doğrula.
2. Eski AltSnap'in ileri INI seçeneklerinin eşitliği: Tamamlanmayan seçenekler
   `src/WinForge.Modules.WindowControl/PARITY.md` içinde açıkça listeli.
3. Diğer donanım/uygulama uçtan uca senaryoları: Ekran yazma/geri alma, oyuncuya
   göre medya komutları, başlangıçta açılma, yönetici izni isteyen işlemler ve
   işletim sistemi sürümüne bağlı kayıt defteri ayarlarının gerçek etkileri.
   Ayrıntılı doğrulanmamış kapsam `FEATURE_VERIFICATION.md` içindedir.

802 kontrolün geçmesi tüm eski harici motorların bütün seçeneklerinin veya her
donanımın eksiksiz doğrulandığı anlamına gelmez. Devam ederken bu sınır korunmalı.
Geliştirme ortamının kayıt defteri/dosya görünümü Explorer'dan farklı olabilir;
gerçek kabuk etkisini normal masaüstü başlatmasıyla sınamak gerekir.
