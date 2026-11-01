# Ekran görüntüsü düzenleyici

Ekran Yakalama modülünü açın ve `Win+Alt+S` ile bölge seçin. **Yakalama sonrası
düzenle** açıkken görüntü düzenleyicide açılır.

- Şekiller sürükleme sırasında önizlenir. Kırpma, bulanıklaştırma ve pikselleştirme
  için seçilen alanın sınırı gösterilir.
- Çizgi kalınlığı 1–32 piksel arasında ayarlanabilir.
- **Sığdır**, %50, %100 ve %200 yakınlaştırma seçenekleri görüntünün kayıt
  çözünürlüğünü değiştirmez. Büyük görüntüler yakınlaştırıldığında kaydırılabilir.
- `Ctrl+Z` geri alır; `Ctrl+Shift+Z` veya `Ctrl+Y` yineler. `Ctrl+C` görüntüyü
  kopyalar; `Ctrl+S` dışa aktarma penceresini açar. Metin alanları kendi klavye
  davranışlarını korur.
- Bulanıklaştırma yumuşatma filtresi uygular. Pikselleştirme blokların ortalama
  rengini kullanır. İki efekt de seçili alanla sınırlıdır; alfa kanalı korunur.
- Pencereyi kapatma veya modülü iptal etme, bekleyen düzenleme akışını sonlandırır.

Geri alma geçmişi en fazla 20 görüntü tutar; büyük yakalamalarda 128 MiB geçmiş
bütçesine göre eski adımlar bırakılır. Son geri alma görüntüsü her zaman tutulur.

## Doğrulama

UI derlemesi: `npm run build` (`src/WinForge.App/ui` içinde).

Görüntü işleme testleri (Node.js 24):
`node --test tests/captureImageEffects.test.mjs` (aynı dizinde).

`tests/WinForge.CaptureTests` projesi Release/x64 olarak derlendikten sonra test
çalıştırıcısı parametresiz çağrılarak görüntü, geçmiş ve düzenleyici yaşam döngüsü
kontrolleri çalıştırılır. `--editor-ui`, gerçek WebView2 üzerinde fare girdisiyle
önizleme, kırpma, geri alma/yineleme, klavye davranışı, kenar sınırları ve
yakınlaştırmayı doğrular. Önizleme `artifacts/capture-verification/editor.png`
konumuna yazılır.
