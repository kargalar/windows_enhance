# Gezgin sol panel tasarımı

`explorer-navigation` modülü Gezgin kategorisinde otomatik keşfedilir. Görünüm
ayarları WinForge çalışırken mevcut ve yeni Explorer pencerelerinin
`NamespaceTreeControl` altındaki gezinme ağacına uygulanır. Renkler renk seçiciyle
veya `#RRGGBB` olarak girilir. Ölçüler DIP kullanır ve pencerenin DPI değerine göre
ölçeklenir. Diğer uygulamaların ağaç denetimleri hedeflenmez.

Yerel bileşen yalnızca Explorer gezinme ağacının UI iş parçacığında alt sınıflama
ve custom draw kullanır; sistem dosyalarını değiştirmez. Explorer'ın tema
yenilemelerinde yazı, renk ve satır ölçüleri korunur. Modül kapatıldığında özgün
yazı tipi, satır yüksekliği, girinti ve renkler geri yüklenir. WinForge'un kalp
atışı altı saniye boyunca yenilenmezse yerel zamanlayıcı görünümü geri alır.
Callback kodu Explorer kapanana kadar bellekte kalır; sürüm özetine göre
önbelleğe alınması sonraki derlemelerin dosyalarını kilitlemesini önler.

Yeni ağaçlar `WM_CREATE` tamamlandığında, ilk çizimden önce biçimlendirilir.
Masaüstü kapsamındaki `WH_CALLWNDPROCRET` kancası yalnız Explorer içinde çalışır;
yeni pencere veya UI iş parçacığı için iki saniyelik tarama beklenmez. Mevcut
ağaçlar etkinleştirmede zaman aşımı sınırlı mesajla eşzamanlı bağlanır. İki
saniyelik yönetilen zamanlayıcı yalnız kalp atışını yeniler.

Görünürlük seçenekleri mevcut kullanıcı için CLSID
`System.IsPinnedToNameSpaceTree` değerini değiştirir. Home/Galeri davranışı Windows
sürümüne bağlıdır; açık pencereleri yeniden açmak gerekebilir. Dosyalar, klasörler
ve sabitlenmiş kişisel kısayollar silinmez. Özgün kayıt değerleri ilk yazmadan
önce uygulama ayarlarında saklanır; modülü kapatmak bu değerleri geri getirir.
Uygulama zorla sonlandırılırsa görünürlük değişiklikleri sonraki etkinleştirme ve
kapatmaya kadar kalabilir. Bu kurtarma davranışı görünümün otomatik geri
alınmasından ayrıdır.

**Gezinme → Sık ziyaret edilen klasörlerin otomatik eklenmesini engelle** seçeneği,
Explorer'ın `ShowFrequent` tercihini kapatır. Otomatik sık kullanılan klasörleri
Hızlı erişimden ve Home sayfasından gizler; elle sabitlenen klasörleri ve ziyaret
geçmişini silmez. Seçenek veya modül kapatıldığında önceki tercih geri yüklenir.
Eski sürümden kalan kurtarma kayıtlarına bu tercihin yedeği de eklenir.

Doğrulama:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-native.ps1
dotnet run --project tests/WinForge.NavigationTests -c Release -p:Platform=x64
& native/WinForge.Shell/bin/x64/Release/WinForge.ExplorerNavigationFixture.exe (Join-Path $PWD native/WinForge.Shell/bin/x64/Release/WinForge.ExplorerNavigationTap.dll)
# Açık Explorer pencerelerinde geçici renk/boşluk uygulama ve geri alma:
dotnet run --project tests/WinForge.NavigationTests -c Release -p:Platform=x64 -- --live
```

Yerel fixture satır ölçülerini, gerçek arka plan/metin çizimini, kapatma/geri alma,
yeniden bağlanma ve kalp atışı kesilmesini doğrular. `--live` gerçek Explorer
pencerelerinde renk ve padding uygulamasını ve önceki renge dönüşü denetler.
Home/Galeri gizlemenin her Windows sürümündeki görünür sonucu bu test kapsamında
doğrulanmaz. Yerel fixture ayrı bir kayıt anahtarı kullanır ve yeni ağacın
oluşturma çağrısı döndüğünde biçimlendirilmiş olduğunu da denetler. Yönetilen
`NavigationTests` ve `--live` için sol panel modülünü kapalı tutun.
