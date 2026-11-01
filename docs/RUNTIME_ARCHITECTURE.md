# WinForge çalışma mimarisi

WinForge modüler bir masaüstü uygulamasıdır. Modül projeleri ayrı uygulamalar
değildir: `ModuleHost`, `WinForge.Modules.*.dll` dosyalarını ana sürece yükler.
Etkinleştirme, ayar uygulama ve kapatma aynı uygulamanın yaşam döngüsündedir.
Birden fazla pencere veya DLL projesi olması ayrı program çalıştırıldığı anlamına gelmez.

## Dikte motoru

Önceden sürekli açık kalan `WinForge.Dictation.Worker.exe` ve named pipe protokolü
kaldırıldı. Whisper, mutlak yoldan yüklenen yerel DLL üzerinden çağrılır. CPU/CUDA
dosyaları `dictation/cpu` ve `dictation/cuda` altında bulunur; bunlar uygulama değildir.
Derleme çıktısının kaynağı `native/WinForge.Whisper/runtime/<Configuration>` dizinidir.
Normal derleme eski worker dosyalarını kendi çıktı dizininden temizler.

- Çözümleme arka planda yapılır; arayüz iş parçacığı bekletilmez.
- Aynı model tekrar kullanılır; bir istemci aynı anda tek çözümleme yapar.
- GPU başlatılamaz veya çözümleme başarısız olursa CPU kullanılır.
- İptal ve modül kapatma, çalışan ve kuyruktaki işleri iptal eder. Yerel çağrı
  dönmeden model veya DLL serbest bırakılmaz. Modül yeniden açılabilir.
- Whisper model yüklemesi ortasında zorla durdurulmaz. Çözümlemede abort ve encoder
  callback'leri kullanılır; iptal süresi yerel motorun kontrol noktalarına bağlıdır.

Süreç içi çalışma IPC ve ek çalışma zamanı maliyetini kaldırır; yerel motordaki
bir erişim ihlali artık ana uygulamayı da etkileyebilir. C++ istisnaları C ABI
sınırında hata koduna çevrilir; bu, donanım/sürücü kaynaklı çökmelerin izolasyonu değildir.

## Korunan süreç sınırları

| Bileşen | Çalışma şekli ve gerekçe |
|---|---|
| Görev çubuğu, Başlat, Gezgin tasarımları | Modül ana süreçtedir. `ShellHost` yalnız bağlantı kurarken çalışıp çıkar. Hedef Windows sürecindeki XAML çalışma zamanını yükler; farklı Windows/WinUI çalışma zamanlarını ana WPF sürecine taşımamak için bu sınır korunur. |
| Gezgin klasör boyutları | `FolderSizeHost` sembol hazırlama ve bağlantı için geçicidir. Sürekli klasör taraması Gezgin'e yüklenen DLL'dedir; ayrı sürekli WinForge programı yoktur. |
| Video, ses, belge ve indirme araçları | FFmpeg, FFprobe ve diğer komut satırı motorları yalnız işlem sırasında çağrılır. Modül ve arayüz WinForge içindedir. Bu motorları tek sürece zorlamak büyük bir yeniden yazım ve bağımlılık değişimi gerektirir. |
| Program/klasör açma kısayolları | Kullanıcının seçtiği uygulamanın veya Gezgin'in açılması özelliğin kendisidir. |
| WebView2 | Arayüz uygulama içinde barındırılır. Microsoft WebView2'nin tarayıcı/işleyici süreçleri kendi çalışma mimarisinin parçasıdır. |

Varsayılan yaklaşım süreç içi modüldür. Yeni bir sürekli yardımcı uygulama ancak
somut bir izolasyon veya yetki gereksinimi varsa eklenmelidir; performans işleri
önce iptal edilebilir arka plan işleri ve ortak hizmetler olarak tasarlanmalıdır.

## Doğrulama

`dotnet run --project tests/WinForge.CaptureTests -c Release -p:Platform=x64`
model tekrar kullanımı, yapılandırma değişimi, GPU/CPU geçişi, iptal, kuyruk,
güvenli serbest bırakma ve yeniden etkinleştirmeyi kapsar.

Gerçek motor testi (modeller gerekir):

```powershell
dotnet run --project tests/WinForge.CaptureTests -c Release -p:Platform=x64 -- --real-engine native/WinForge.Whisper/runtime/Release src/WinForge.App/bin/x64/Release/net10.0-windows10.0.19041.0/userdata/History/models cpu
```

Son argüman `fallback` olduğunda CUDA bulunmayan dizinle CPU'ya geçiş sınanır;
`gpu` için hazırlanmış CUDA motoru ve uygun donanım gerekir.

1 Kasım 2026 doğrulaması: 94 yakalama/geçmiş/motor kontrolü, gerçek Türkçe
CPU çözümlemesi ve iptali, gerçek CPU fallback çözümlemesi ve iptali,
paket içinden dört modül/SQLite yükleme kontrolü geçti. Release build ve publish
başarılı; paketin `dictation` dizininde yalnız motor DLL'si ve manifest bulunuyor.
CUDA Toolkit olmadığı için gerçek GPU yolu bu makinede doğrulanamadı.
Genel regresyon paketi, bu değişikliklerden bağımsız eski
`TestInterface` başlatıcı fixture'ında constructor uyuşmazlığı nedeniyle tamamlanamadı.

Kaynaklar: [Microsoft NativeLibrary.Load](https://learn.microsoft.com/dotnet/api/system.runtime.interopservices.nativelibrary.load),
[DLL arama sırası](https://learn.microsoft.com/en-us/windows/win32/dlls/dynamic-link-library-search-order),
[Whisper v1.8.3 callback sözleşmesi](https://github.com/ggml-org/whisper.cpp/blob/v1.8.3/include/whisper.h).
