# Sağ tık menüsü düzenleyicisi

**Gezgin → Sağ Tık Menüsü** içindeki öğeler, mevcut simgeleri ve okunabilir adlarıyla listelenir. Ad çözümlemesi yerelleştirilmiş Windows kaynaklarını, kabuk kaydını ve uzantının dosya açıklamasını kullanır. Menü açıldığında gerçek başlıklar ve uygun menü simgeleri de alınır. Kod biçimindeki kayıt kimlikleri görünen ad olarak kullanılmaz; çözülemeyen bir öğenin adını kendiniz düzenleyebilirsiniz.

- **Göster** öğenin görünürlüğünü belirler.
- **Alt menü / grup** alanına `Diğer` gibi bir ad yazmak öğeyi gerçek menüde bu alt menüye taşır. Aynı adı taşıyan öğeler aynı grupta toplanır. Alanı boş bırakmak öğeyi ana menüye döndürür.
- **Simge ekle / Simgeyi değiştir**, mevcut bir öğeye ICO, PNG, JPEG, BMP veya EXE/DLL simgesi atar. Kaynak alanı `shell32.dll,3` gibi bir simge indeksini de kabul eder. **Özgün simge**, özel atamayı kaldırır.
- Görünen ad, görünürlük ve grup değişiklikleri otomatik kaydedilir. **Bu öğeyi varsayılana döndür**, öğe düzenlemelerini kaldırır.
- Dinamik bir uzantının tek tek komutlarını görmek için ilgili dosya/klasör üzerinde menüyü bir kez açıp **Listeyi yenile** düğmesine basın. Kabuk uzantısı kayıtları ve bu uzantıların gerçek komutları ayrı türler olarak filtrelenebilir.

Örneğin **Özellikler** ve **Yeni klasör** öğelerinin grup alanına `Diğer` yazıldığında komut, bulunduğu menüde `Diğer → Özellikler` veya `Diğer → Yeni klasör` altında görünür. Windows her komutu her bağlamda sunmaz; düzenleyici bulunmayan bir komutu o bağlama eklemez. Komut kimlikleri, alt menüler, etkin/devre dışı durumları ve işaretleri korunur; komutlar yeniden taklit edilmez.

Gruplama ve öğe simgesi düzenlemeleri klasik Win32 Gezgin menüsünde ve Windows 11’in **Daha fazla seçenek göster** menüsünde uygulanır. Modern Windows 11 üst menüsü ayrı bir arayüzdür. Modül kapatıldığında özgün menü geri gelir. Windows Gezgini dışında kalan uygulama menüleri değiştirilmez.

## Doğrulama

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-native.ps1
native/WinForge.Shell/bin/x64/Release/WinForge.ContextMenuFixture.exe --shell
dotnet run --project tests/WinForge.ShortcutActionTests -c Release -p:Platform=x64
```

UI klasöründe `npm run test:shortcuts`; derlenmiş regresyon testinde `WinForge.RegressionTests.exe --context-menu-ui-only` çalıştırılabilir. Yerel fixture, gerçek Windows dosya/arka plan menülerindeki Özellikler ve Yeni Klasör komutlarını geçici gruba taşır ve geri alır. Kullanıcının menü tercihlerini değiştirmez veya komutları çalıştırmaz.
