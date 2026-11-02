# Geçmiş sorgusu ölçümü ve karar

2 Kasım 2026 ölçümü: Windows build 26300, .NET 10, Ryzen 7 8845HS, yaklaşık 32 GiB RAM. Donanım ve ekran sınırları ham JSON raporunda kayıtlıdır. Kullanıcının gerçek geçmişi okunmadı veya değiştirilmedi.

## Veri ve yöntem

`HistoryPerformanceChecks` ayrı geçici SQLite veritabanlarında 10 bin ve 100 bin kayıt üretir. Her kayıtta en az 1024 karakterlik Türkçe metin bulunur; kayıtların %90'ı recall, %10'u dictation'dır. Test gerçek `HistoryStore.Add`, `Search` ve `Open` yöntemlerini, mevcut WAL/FTS/indeksleri ve parametre bağlamayı kullanır. Veritabanları yaklaşık 16,3 ve 162,5 MiB boyutundadır.

Her sorgu için ilk süre ayrı, ardından 30 tekrarın medyan/p95/maksimum süreleri alınır. Bu bir işletim sistemi disk önbelleğini soğutma testi değildir. Sonuç sınırı, benzersizlik, modül ayrımı, sıralama ve tekrarlanabilirlik de denetlenir. Açıklama planı, üretim sorgusuyla aynı SQL ve parametre yapılandırmasından alınır; bunun için sorgu hazırlama kodu ortaklaştırılmıştır.

## 100 bin kayıt sonuçları

| Sorgu | p95 |
|---|---:|
| Son kayıtlar | 3,24 ms |
| Seçici FTS | 6,67 ms |
| Yaygın sözcük FTS | 18,20 ms |
| Türkçe/Unicode FTS | 19,09 ms |
| Uygulama filtresi | 4,70 ms |
| Etiket filtresi | 5,97 ms |
| Ortadaki tarih aralığı | 10,04 ms |
| Son sayfalara OFFSET | 16,10 ms |

Her iki koşuda da bütün sorgular önerilen 200 ms p95 sınırını karşıladı; ilk 1 KiB profilinde en yüksek p95 28,19 ms, son ortak komut tekrarında 19,09 ms idi. 33 veri/doğruluk/bütçe kontrolü geçti. Bu tablonun ham örnekleri ve planları `artifacts/performance/runs/20261102-125339/history-baseline.json` içinde arşivlidir. `artifacts/performance/history-baseline.json` en son koşu için yenilenir. Sonuçlar başka donanım veya daha büyük arşivler için garanti değildir.

Planlar `items_time` indeksini, uygulama/etiket için mevcut kapsayan indeksi ve FTS sanal tablosunu gösterir. Koşullu FTS alt sorgusunun planda bulunması, boş aramalarda mutlaka yürütüldüğünü kanıtlamaz; burada yürütme profili yerine gerçek uçtan uca sorgu süreleri kullanılmıştır.

## Karar

Bu ölçüm kapsamında bağlantı havuzu veya anahtar tabanlı sayfalamaya geçmek gerekmedi. `Pooling=false`, `secure_delete=ON`, mevcut indeksler ve LIMIT/OFFSET korunuyor. Bağlantı açma maliyeti küçük, derin sayfa ve metin araması da bütçenin belirgin biçimde altında. Böylece medya temizleme, taşınabilir veri ve yedekleme davranışları için gereksiz bir bağlantı ömrü değişikliği yapılmadı.

Arşiv 100 bin kaydı aştığında, farklı disklerde veya sorgu p95'i 200 ms'yi geçtiğinde aynı ölçüm tekrar alınmalı. Anahtar tabanlı sayfalama gerekirse önce/sonra sıralama, tarih eşitliği ve sayfa geçişi testleriyle teslim edilmeli. Bu çalışma görüntü çözme/OCR süresini veya bütün geçmiş penceresinin resim render süresini ölçmez.

## Yeniden çalıştırma

```powershell
./scripts/test.ps1 -Performance
# Yalnız veri ölçümü, derlemeden sonra:
./artifacts/optimization-build/bin/WinForge.CaptureTests/release/WinForge.CaptureTests.exe --history-benchmark artifacts/performance/history-baseline.json
```

Ortak komut hızlı testler, performans derlemesi, geçmiş ölçümü ve gerçek WebView köprü ölçümünü ayrı sonuç satırlarında raporlar. Bu profil, bütün canlı shell/donanım entegrasyon paketinin geçtiği anlamına gelmez.
