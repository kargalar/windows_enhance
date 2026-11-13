# Türkçe dikte değerlendirmesi

Bu deney, model seçiminin hız ve doğruluk etkisini görmeye yönelik küçük bir kontroldür;
genel kalite veya gerçek kullanım başarısı ölçümü değildir. Kaynak: [Google FLEURS](https://huggingface.co/datasets/google/fleurs), Türkçe test bölümü, CC BY 4.0. `scripts/prepare-dictation-benchmark.py` arşiv sırasındaki ilk beş farklı metni alır; bunlar rastgele seçilmemiştir ve kaynak metaverisine göre tamamı erkek konuşmacıdır. Haber ve ansiklopedi cümleleri içerir, günlük dikteyi temsil etmez.

13 Kasım 2026 tarihinde Ryzen 7 8845HS CPU, whisper.cpp v1.8.3 ve sıcak modelle çalıştırıldı. Büyük/küçük harf ve noktalama yok sayıldı; sayıların yazıyla veya rakamla yazılması normalize edilmedi. Beş kayıt, 97 referans kelimeden oluşur:

| Model | Kayıt başına ortalama | Kelime düzenleme hatası |
| --- | ---: | ---: |
| Large-v3 Turbo | 10,86 sn | 8/97 (%8,2) |
| Small | 2,43 sn | 22/97 (%22,7) |

Örneğin Small, Goethe/Fichte/Schlegel adlarını yanlış yazdı; Turbo bu cümleyi doğru aktardı. Turbo da "on yedi"yi "17" yazdığı için basit ölçümde ceza aldı ve bazı teknik sözcüklerde hata yaptı. Bu bulgular Small'ın hız karşılığında özellikle özel isimlerde doğruluk kaybedebileceği uyarısını destekler; model seçimini değiştirmek için yeterli evrensel kanıt değildir.

Tekrar çalıştırma:

```powershell
python scripts/prepare-dictation-benchmark.py
dotnet run --project tests/WinForge.CaptureTests -- --benchmark-dictation <motor-klasörü> <turbo-model-dosyası> <small-model-dosyası> artifacts/capture-verification/fleurs-mini/manifest.tsv
```

Ölçüm, yerel motor ve model dosyaları gerektirir. Ses örnekleri `artifacts` altında tutulur; uygulama çalışma zamanı verisi olarak kullanılmaz.
