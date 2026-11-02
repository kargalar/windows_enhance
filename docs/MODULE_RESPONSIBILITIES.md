# Modül sorumlulukları ve yaşam döngüsü

Bu belge optimizasyon yol haritasındaki R12 ayrımlarını izler. [İlerleme kaydı](OPTIMIZATION_PROGRESS.md) henüz tamamlanmayan kapsamı belirtir.

## Medya ve ses

| Dosya | Sorumluluk ve kaynak sahibi |
|---|---|
| `MediaFlyoutModule.cs` | Ayar tanımları, modül Activate/Apply/Deactivate, kısayollar ve medya penceresinin oluşturulması/kapatılması. |
| `MediaFlyoutWindow.cs` | Medya oturumu gözlemcileri, seçili oturum, artwork, oynatma denetimleri, sunum ve pencere zamanlayıcıları. Mevcut tek pencere nesnesi bu kaynakları sahiplenmeye devam eder. |
| `LockKeysFlyoutHook.cs` | Native klavye kancası; ses tuşlarının iki kenarını tüketir ve işi dispatcher'a bırakır. Dispose kancayı kaldırır. |
| `LockKeysStatusWindow.cs` | Kilit tuşu durum yüzeyi ve 1,5 saniyelik kapatma zamanlayıcısı. |

Bu ayrımda tür adları, görünürlük, constructor'lar ve metot gövdeleri değişmedi. Modül, pencere ve girdi kancası ayrı dosyalarda incelenebilir. Medya penceresinin içindeki oturum/arayüz ayrımı hâlâ sonraki bakım kapsamındadır; dosya taşınması tek başına motorun ayrıldığı iddiasını taşımaz.

Davranış kontrolleri: `--volume-keys-only` (press/repeat/release, mute ve playback yönlendirmesi), `--volume-overlay-only` (ortak kapanma süresi, hover, slider drag ve eşlikçi pencere yaşam döngüsü). Bunlar gerçek ses aygıtı değişimi veya tüm medya sağlayıcıları matrisi değildir.

## Ekran parlaklığı

| Dosya | Sorumluluk ve kaynak sahibi |
|---|---|
| `DisplayBrightnessModels.cs` | Monitör/VCP snapshot modelleri, profil serileştirme, capabilities parser, yüzde dönüşümü ve günlük plan hesabı. |
| `DisplayBrightnessSession.cs` | Monitör keşfi, fiziksel monitör handle'ları, DDC/CI okuma/yazma ve Dispose. |
| `DisplayBrightnessModule.cs` | Modül ayarları, kısayollar, plan zamanlayıcısı ve panel oturumu yaşam döngüsü. |
| `DisplayBrightnessWindow.cs` | Arayüz komutları, bekleyen slider yazımlarının birleştirilmesi ve panel durumu. |

Taşınan gövdeler özgün kaynakla karşılaştırıldı; bu aşama davranışı değiştirmez. `--display-brightness-only` kontrollü fiziksel monitör fixture'larıyla kararlı aygıt kimliği, discovery önbelleği, yüzde dönüşümü, kardeş aygıtta hata izolasyonu ve native enum genişliğini denetler. Donanım doğrulaması ayrıca gereklidir.

## Varsayılan Windows ayarları

`DefaultSettingsModule.Initialize` yalnızca context ve boş panel durumunu hazırlar. Activate Windows program/klasör listesini okumaz. İlk `GetPanelState` veya panel komutu veriyi hazırlar; sonraki snapshot'lar aynı veriyi kullanır. `defaults.reload` açık yenileme yapar. İlk okuma hatası sonraki istekte yeniden denenebilir. Bu işler WPF dispatcher üzerinde kalır; COM/Clipboard çağrıları arka plan thread'ine taşınmadı.

Yeni ana panel `deferredPanels` yeteneğini bildirir ve kapalı panel verilerini ilk snapshot'ta istemez. Windows okumaları ilgili modül ayrıntıları açılınca yapılır. Eski istemciler için ilk snapshot'taki dolu panel davranışı korunur. `defaults.read`, `bridge.readPanel`, `module.initialize`, `modules.load` ve `bridge.buildState` izleri hazırlık aşamalarını ayırır. Sözleşme [DASHBOARD_BRIDGE.md](DASHBOARD_BRIDGE.md) içindedir.

48 DefaultSettings kontrolü mevcut program/klasör davranışına ek olarak ilk okumayı erteleme, snapshot öncesi komut, tekrar kullanım, explicit reload ve hata sonrası yeniden denemeyi kapsar.

## Tiling ve özellik görünümleri

`TilingModule.ZoneGeometry.cs` bölge sınırları/birleştirme, monitör profili ayrıştırma ve ızgara hesabını; `TilingModule.WindowPlacement.cs` yerleştirme düzenlerini, frame/placement değişimini ve fullscreen geri yüklemesini içerir. Ana partial sınıf modül yaşam döngüsü, gözlemciler, drag ve giriş koordinasyonunun sahibi olarak kalır. Ayrı bir engine nesnesi veya ikinci kaynak sahibi oluşturulmadı. Taşınan bölümlerle kalan dosya birleştirilerek kullanıcının değişiklikleri dahil özgün kaynak birebir yeniden elde edildi. Ayrım öncesi/sonrası 18 gerçek fixture kontrolü geçti.

`features/ClipboardViews.tsx` pano rafı, ayarlar, metin/medya önizleme, isimlendirme ve kayıt kırpma görünümleri ile durum tiplerini sahiplenir. FeatureApp yönlendirmesi ve `feature.ready`/`feature.state` aboneliği aynı yerde kalır. Taşınan gövdeler değişmedi. Ayrım öncesi ve sonrası dört davranış testi hızlı kopyalama/çoklu seçim, düzenleme klavyesi, kaydetme/iptal, explicit gizlilik tercihi ve üç klasör sınırını denetler. UI paketi toplam 38 testtir.

## Açık ayrımlar

TilingModule'deki drag/giriş koordinasyonu, FeatureApp'in kalan görünüm grupları ve MediaFlyoutWindow içindeki oturum/arayüz sınırı üzerinde bakım çalışması hâlâ açıktır. Bu dosya ayrımları tüm mimari bakımın tamamlandığı anlamına gelmez.
