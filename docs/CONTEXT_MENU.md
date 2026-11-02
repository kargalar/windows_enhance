# Sağ tık menüsü düzenleyicisi

**Gezgin → Sağ Tık Menüsü** içindeki öğeler, mevcut simgeleri ve okunabilir adlarıyla listelenir. Ad çözümlemesi yerelleştirilmiş Windows kaynaklarını, kabuk kaydını ve uzantının dosya açıklamasını kullanır. Menü açıldığında gerçek başlıklar ve uygun menü simgeleri de alınır. Kod biçimindeki kayıt kimlikleri görünen ad olarak kullanılmaz; çözülemeyen bir öğenin adını kendiniz düzenleyebilirsiniz.

- Aynı komutun farklı konumlardaki kayıtları ve menüden yakalanan sürümü tek kartta birleştirilir. Kartın altındaki anahtarlar **dosya**, **klasör**, **klasörün boş alanı**, **masaüstünün boş alanı** ve **sürücü** için görünürlüğü ayrı ayrı belirler. Windows'un ilgili konumda sunmadığı bir komut o konuma eklenmez.
- **Gruplar → Grup oluştur** ile gruba ad verin, ardından içine eklenecek öğeleri seçin. **Öğeleri düzenle** ile üyeleri veya grup adını değiştirin. Gruptan çıkarılan öğeler ana menüye döner; **Grubu kaldır** öğeleri silmeden grubu kaldırır. Boş gruplar da saklanır.
- **Simge ekle / Simgeyi değiştir**, mevcut bir öğeye ICO, PNG, JPEG, BMP veya EXE/DLL simgesi atar. Kaynak alanı `shell32.dll,3` gibi bir simge indeksini de kabul eder. **Özgün simge**, özel atamayı kaldırır.
- Görünen ad ve konuma göre görünürlük değişiklikleri otomatik kaydedilir. Grup üyeleri, grup formu gönderildiğinde kaydedilir. **Bu öğeyi varsayılana döndür**, birleştirilmiş öğenin düzenlemelerini kaldırır.
- Dinamik bir uzantının tek tek komutlarını görmek için ilgili dosya/klasör üzerinde menüyü bir kez açıp **Listeyi yenile** düğmesine basın. Kabuk uzantısı kayıtları ve bu uzantıların gerçek komutları ayrı türler olarak filtrelenebilir.

Örneğin `Diğer` grubunu oluşturup **Özellikler** ve **Yeni klasör** öğelerini seçtiğinizde komutlar, bulundukları menüde `Diğer → Özellikler` veya `Diğer → Yeni klasör` altında görünür. Windows her komutu her bağlamda sunmaz; düzenleyici bulunmayan bir komutu o bağlama eklemez. Komut kimlikleri, alt menüler, etkin/devre dışı durumları ve işaretleri korunur; komutlar yeniden taklit edilmez.

Gruplama ve öğe simgesi düzenlemeleri klasik Win32 Gezgin menüsünde ve Windows 11’in **Daha fazla seçenek göster** menüsünde uygulanır. Modern Windows 11 üst menüsü ayrı bir arayüzdür. Modül kapatıldığında özgün menü geri gelir. Windows Gezgini dışında kalan uygulama menüleri değiştirilmez.

## Ayırıcılar, hedef türleri ve yol kopyalama

- **Boşta kalan ayırıcı çizgileri otomatik temizle**, menünün başında/sonunda veya gizlenen öğeler arasında kalan art arda çizgileri kaldırır. Varsayılan olarak açıktır.
- **Menünün mevcut ayırıcı çizgilerini göster** ile mevcut çizgilerin tamamını açıp kapatabilirsiniz. Tek bir çizgi için menüyü bir kez açın, **Listeyi yenile** deyin ve **Ayırıcı çizgiler** filtresini seçin. Çizgiler, aralarında bulundukları komutların adlarıyla listelenir.
- Yeni çizgi eklemek için bir komutun **Diğer ayarlar** bölümündeki **Önüne ayırıcı çizgi ekle** veya **Arkasına ayırıcı çizgi ekle** kutusunu işaretleyin. Gruptaki öğeler için çizgi o grubun içine eklenir. Eklenen çizgiler modül kapatılınca kaldırılır ve özgün yerleşim geri gelir.
- Konum anahtarlarında **Kısayol**, **Uygulama / betik** ve **Sanal öğe** bulunur. Kısayol `.lnk` ve `.url` öğelerini; uygulama/betik `.exe`, `.com`, `.bat`, `.cmd`, `.msi` ve `.ps1` dosyalarını kapsar. Sanal öğeler dosya sistemi dışında kalan kabuk öğeleridir. Eski ayarlarda kısayollar ve uygulamalar dosya tercihini devralır. Karma seçimlerde komutun seçili türlerin hepsinde açık olması gerekir.
- **Yol olarak kopyala: Tırnak işaretlerini kaldır**, klasik menüdeki Copy as path / Yol olarak kopyala işleminin sonucunu `C:\Klasör Adı\dosya.txt` biçimine getirir. Boşluklar ve birden çok yoldaki satır sonları korunur. Yalnızca bu komut seçildiğinde, yeni pano metni seçimin tırnaklı yollarıyla bire bir eşleşirse dönüştürülür. Diğer pano metinleri değiştirilmez. Varsayılan olarak açıktır; bu anahtar kapatılarak Windows davranışına dönülebilir.

## Doğrulama

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-native.ps1
native/WinForge.Shell/bin/x64/Release/WinForge.ContextMenuFixture.exe --shell
dotnet run --project tests/WinForge.ShortcutActionTests -c Release -p:Platform=x64
```

UI klasöründe `npm run test:shortcuts`; derlenmiş regresyon testinde `WinForge.RegressionTests.exe --context-menu-ui-only` çalıştırılabilir. Yerel fixture, gerçek Windows dosya/arka plan menülerindeki Özellikler ve Yeni Klasör komutlarını geçici gruba taşır ve geri alır. Kullanıcının menü tercihlerini değiştirmez veya komutları çalıştırmaz.

## Hazır eylemler

**Gizli öğeleri göster / gizle** ve **Güç modu** eylemleri de Hazır eylemler filtresinde bulunur. Kullanmak için istediğiniz konumu (örneğin masaüstü veya klasörün boş alanı) açın. Başlangıçta kapalıdırlar; adlarını, simgelerini ve gruplarını düzenleyebilirsiniz.

Gizli öğeler komutu Gezgin'in gizli dosya ve klasör görünürlüğünü değiştirir ve açık pencereleri yeniler. Seçili dosyaların Hidden özniteliğini veya korunan sistem dosyalarının görünürlüğünü değiştirmez. **Güç modu** alt menüsünde **En iyi performans**, **Dengeli** ve **Güç tasarrufu** bulunur. Windows güç modu API'si kullanılır; desteklenmeyen cihazlarda karşılık gelen klasik güç planı denenir. Mod veya plan kullanılamıyorsa hata gösterilir.

Güç modu alt menüsü gruba taşınmadan önce özgün konumunda doldurulur; böylece grubun içinde de üç seçenek korunur. Her öğenin konum seçimlerinde **Masaüstü** ayrı bulunur; **Tüm konumları aç / kapat** işlemleri masaüstünü de kapsar.

**Klasör simgesini değiştir** eylemi varsayılan olarak klasörlerin sağ tık menüsünde açıktır. Seçildiğinde Windows'un simge seçme penceresi doğrudan açılır; seçilen simge klasöre uygulanır. İptal işlemi klasörü değiştirmez. Eylem diğer hazır eylemler gibi düzenleyiciden kapatılabilir veya gruba taşınabilir. Windows 11'de klasik menüye **Daha fazla seçenek göster** ile ulaşılır.

Tür filtresindeki **Hazır eylemler** altında **Masaüstü kısayolu oluştur** ve **Dosyayı / kısayolu gizle veya göster** bulunur. Başlangıçta kapalıdır. Dosya veya klasör anahtarını açarak etkinleştirin; adını ve simgesini diğer öğeler gibi düzenleyin, gruba grup formundan ekleyin. Dosya veya klasör seçiminde çalışırlar. Kısayol oluşturma mevcut masaüstü dosyalarını ezmez; aynı ad için numara ekler. Gizle/göster yalnızca seçilen öğenin Hidden özniteliğini değiştirir. Gizlenmiş öğeyi yeniden seçmek için Gezgin’in gizli öğeleri göster ayarı açık olmalıdır.

Girdi göndermeden yerel yakalama ve eylem kontrolleri:

```powershell
dotnet run --project tests/WinForge.ShortcutActionTests -c Release -- --capture-context-only
```

Grup satırındaki **Simge ekle / Simgeyi değiştir** ile gruba dosyadan simge atayabilirsiniz. Grup formundaki **Grup simgesi** alanı `shell32.dll,3` gibi kaynakları da kabul eder. **Simgeyi kaldır** yalnızca grubun simgesini temizler. Grup yeniden adlandırıldığında simgesi korunur; silindiğinde simge ataması da kaldırılır. Grup başlıkları koyu temada açık yazıyla, açık ve yüksek kontrast temalarında sistem renkleriyle çizilir.
