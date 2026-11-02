# Modül yönetimi arayüzü

2 Kasım 2026 tarihli güncelleme, ana paneldeki 47 modülün her biri için seçenek yapısına göre tasarlanmış detay sayfaları içerir. `moduleDesigns.ts` her modülün amacını, kullanım adımlarını, sayfalarını, ayar gruplarını ve özel kontrol türlerini açıkça tanımlar. Sayfalar eski `SettingDescriptor.Section` sıralamasından otomatik türetilmez. Native ayar anahtarları aynı kalır; eksik bir tasarım eşleştirmesi kapsam denetiminde hata üretir.

Modül içi arama, ayar adı, açıklaması, eski bölüm adı ve yeni sayfa/grup adları üzerinde çalışır. Özel yönetim panellerinde kayıt adları, kısayollar, menü öğeleri, programlar ve klasörler de aranabilir. **Ctrl+F**, açık modülün aramasına; katalogda genel aramaya gider. Geri dönüş odağı açılan modül kartına taşır. Katalogda etkin, kapalı ve hata durumları filtrelenebilir. Panel hatalarında yeniden yükleme eylemi bulunur.

Görev çubuğu, Başlat, Gezgin paneli, pencere bölgeleri, fare vurgusu, üste sabitleme, ağ hızı, tema planı ve medya için anlamlı önizlemeler bulunur. Bunlar Windows'un gerçek görüntü yakalaması değildir; ekran bunu açıklar. Önizleme katlanabilir ve seçenek düzeninin yerini almaz.

## Modüle uygun çalışma alanları

- **Pencere Kontrolü:** yedi fare girdisi, normal/alternatif/başlık çubuğu/başlık alternatifi için dört sütunlu eylem tablosu; etkinleştirme tuşları, oturtma, opaklık ve istisnalar ayrı sayfalardır.
- **Görev Çubuğu:** bileşen kartları, görünüm/dock, simgeler/tepsi, saat ve fare davranışı ayrı sayfalardır. Bilgisayarda keşfedilen uygulama simgeleri dinamik listeye eklenir.
- **Başlat / Fare / Gezgin:** görünüm, içerik ve davranışlara göre ayrı gruplar; renkler, ölçüler ve bileşen anahtarları ilişkili alanlarda tutulur.
- **Ekran Yakalama:** başlatma, çıktı akışı, video/ses ve kayıt konumu ayrıdır. İşlem sırası 24 ham metin seçeneği yerine yukarı/aşağı taşınan dört adımla düzenlenir; native seçenekler dışına çıkılmaz.
- **Ekran Geçmişi / Sesle Yazma:** kayıt girişleri, çıktı, model/dil, dışlamalar ve depolama birbirinden ayrıdır. Teknik bir araç açma eylemi ilgili alanda kalır.
- **Tema / Ekran Parlaklığı:** gündüz ve gece ayrı kartlardır; saat alanları HH:mm seçici kullanır.
- **Bildirim / Flyout:** ekrandaki konum bir yerleşim haritasından seçilir. Native konum değerleri değiştirilmez.
- **FancyZones / Otomatik Yerleşim:** düzenler görsel seçeneklerle seçilir. Monitör profilleri ekran kimliği ve ızgara boyutlarından oluşan tabloyla düzenlenir.
- **Otomatik Düzeltme:** kişisel sözlük ve dışlama listeleri öğe listesi; metin kısayolları iki sütunlu eşleştirme tablosudur. Ham ayırıcı biçimini kullanıcının elle kurması gerekmez.
- **Kısayolların sekiz türü:** seçilebilir kayıt listesi ve seçili kayıt düzenleyicisi; ses adımı, kaynak/hedef tuş birleşimi, metin, program yolu veya ekran profili alanları modülün eylemine göre gösterilir.
- **Otomasyon:** kompakt genel kontroller, otomasyon listesi ve numaralanmış adımlar. Kayıt değiştirirken düzenleyiciler yerinde tutulur, taslaklar kaybolmaz; yeni kayıt seçilir.
- **Sağ Tık Menüsü:** öğeler, gruplar, davranış ve menü önizlemesi ayrı sekmelerdir. Özel komutlar ad/komut tablolarıyla, her bağlam için ayrı düzenlenir.
- **Varsayılan Yollar / Programlar:** klasör kartlarında geçerli konum ve yeni hedef; dosya türlerinde uzantı, program ve değiştirme eylemi açıkça ayrılır.
- **Araç açıcılar ve tek seçenekli modüller:** işlemi başlatan eylem, gerçek kullanım adımları ve ilgili tercih gösterilir. Başlangıç ve Modern Klasör Seçici gibi ek ayarı olmayan modüller boş bir ayar listesi yerine durum ve kullanım paneli gösterir.

Kapalı bir alt bileşene bağlı seçenekler düzenlenebilir kalır; uygulanması için hangi anahtarın açılması gerektiği yanında açıklanır. Eksik eşleştirme satırları veya geçersiz JSON native tarafa gönderilmez. XAML düzenleyicisi native kural listesi şemasını ve 100 kural sınırını denetler.

## Teknoloji kararı

React 19 + TypeScript + Vite, WPF içinde WebView2 ile kullanılmaya devam eder. Native modüller, pencere yönetimi ve işletim sistemi bağlantıları .NET tarafında kalır. React'in [sürüm belgeleri](https://react.dev/versions) ve Microsoft'un [WPF/WebView2 belgeleri](https://learn.microsoft.com/en-us/microsoft-edge/webview2/get-started/wpf) bu yapının desteklenen temelini açıklar. Bu uygulama için başka bir masaüstü çerçevesine geçişi gerektiren ölçülmüş bir eksiklik bulunmadığından iyileştirme bileşen mimarisi, etkileşim, okunabilirlik ve doğrulama üzerinde yapılmıştır.

- `main.tsx`: giriş, araç pencerelerinin yönlendirilmesi ve hata sınırı.
- `App.tsx`: katalog, uygulama gezinmesi ve native durum aboneliği.
- `ModuleDetails.tsx`: bölüm gezinmesi, ayar arama ve yönetim panellerinin gerektiğinde yüklenmesi.
- `moduleDesigns.ts`, `settingDependencies.ts`: modül bazında gözden geçirilmiş sayfalar, ayar sahipliği ve bağımlı seçenek açıklamaları.
- `DesignedSetting.tsx`, `ActionMatrix.tsx`, `ModulePreview.tsx`: yerleşim haritası, eşleştirme tabloları, işlem sırası ve modüle özel görseller.
- `RuleWorkbench.tsx`: kayıt listesi ve seçili kaydın düzenleyicisi.
- `SettingControl.tsx`, `NumericSettingControl.tsx`, `Toggle.tsx`: ortak ve etiketli ayar kontrolleri.
- `types.ts`, `bridge.ts`, `dashboardStateBridge.ts`: native iletişimin türleri, istek kimlikleri ve sıralı durum birleştirme.
- `dashboard.css`, `module-design.css`: ana panelin tema değişkenleri, modül çalışma alanları ve dar pencere davranışı. Araç pencerelerinin stilleri ayrı kalır.

Özel yönetim panelleri ayrı JavaScript parçaları olarak yüklenir. React render hataları kurtarma ekranına düşer. İlk bağlantı gecikirse yeniden bağlanma eylemi gösterilir. Mevcut native sürüm/sıra denetimi, kalıcı kayıt onayları ve renderer çökmesinden sonra yeniden yükleme davranışı korunur.

Sayısal kontroller, yazmayı bırakınca ve modülden ayrılırken kaydedilir. Eski native yanıtlar yeni taslağı ezmez. Tam değer alanı boş/geçici girişe ve negatif alt sınırlara izin verir; odak kaybında sınırlar uygulanır. Değişmemiş taslağı yeniden kaydetmek ikinci bir komut oluşturmaz. `BuildAppearance` artık kayıtlı dili de arayüze gönderir; belge dili aynı tercihle güncellenir.

## Doğrulama

```powershell
# Bir defalık tarayıcı kurulumu
cd src/WinForge.App/ui
npm ci
npx playwright install chromium

# Bileşen ve üretim paketi tarayıcı testleri
npm test
npm run test:e2e
npm run design:audit
```

Proje kökünden ortak test komutu:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test.ps1 -BrowserUi
```

Gerçek Windows/WebView2 testleri:

```powershell
dotnet build tests/WinForge.RegressionTests -c Release -p:Platform=x64 --artifacts-path artifacts/ui-modernization
artifacts/ui-modernization/bin/WinForge.RegressionTests/release/WinForge.RegressionTests.exe --dashboard-interface-only
artifacts/ui-modernization/bin/WinForge.RegressionTests/release/WinForge.RegressionTests.exe --bridge-only
```

Doğrulama 51 arayüz testi ve üretim paketi üzerinde 55 Playwright senaryosu içerir. 47 gerçek modülün 79 tasarlanmış sayfası ve ek ayarı olmayan iki modülün kullanım panelleri açılır; her native anahtarın tam bir kez yer aldığı denetlenir. Açık/koyu temalar axe ile taranır; 980 ve 560 pikselde içerik taşması kontrol edilir. Yönetim panelleri örnek veriyle doldurulur ve bütün kısayol/otomasyon kayıtları ayrı ayrı seçilir. Ortak gezinme senaryoları ayrıca 1536, 1280, 980, 768 ve 560 piksel genişliklerini, klavye odağını ve kayıt sonrası geri dönüşü kapsar.

Gerçek Windows/WebView2 doğrulaması 788 kontrol içerir: 47 gerçek modül ve bir test modülü; bütün sayfalara erişim, bütün kontrollerin yerleşimi, üç tema/üç vurgu kontrastı ve native kalıcı kayıt. Pencere eylemi, konum haritası, saat seçici, uygulama listesi, ızgara profili ve işlem sırası gerçek modüllerin geçici ayar depolarına yazılarak kontrol edilir. Modüller etkinleştirilmez; gerçek Windows ayarları değiştirilmez. Native iletişim için ek 121 kontrol; sıralı yamalar, disk yazma onayı ve renderer çökmesinden toparlanmayı kapsar.

Test raporları `artifacts/test-results`, `artifacts/ui-e2e` ve `artifacts/performance/dashboard-bridge.json` altındadır. Modül bazında inceleme galerisi `artifacts/module-design-review/index.html`; ayar sahipliği raporu aynı klasörde `coverage.json` dosyasıdır. `design:audit` tüm modülleri, anahtarları ve görsel dosyalarının varlığını kontrol ederek galeriyi üretir. Native katalog `artifacts/ui-review/module-catalog.json` dosyasına dışa aktarılır; tarayıcı fixture'ı bunun kullanıcıya/makineye özel değerleri çıkarılmış sürümüdür. DOM testlerinin Vite önbelleği ayrı tutulur. `npm ci` çalışırken aynı dizinde geliştirme sunucusu veya ikinci bir UI derlemesi çalıştırılmamalıdır.

Bu doğrulama modüllerin bütün donanım ve Windows sürümü birleşimlerini ya da her native özelliğin gerçek işletim sistemi etkisini kapsamaz. Arayüz kontrolleri gerçek kişiselleştirme motorlarını etkinleştirmeden yürütülür; bu motorların testleri projenin ilgili entegrasyon gruplarındadır.
