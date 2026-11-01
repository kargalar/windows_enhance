# Görev çubuğu özelleştirmesi

`taskbar`, görev çubuğu araçlarını, tasarım motorunu ve uygulama tepsisi seçimlerini tek modül ve ayar panelinde sunar. Bileşenler bölümünden bu üç alan bağımsız açılıp kapatılabilir. Eski `shell-customization` ve `tray-icons` kayıtları ilk yüklemede taşınır; önceki bileşenlerin etkinlik durumu, tepsi seçimleri ve ayrı simge boyutları korunur. Yeni modülün mevcut ayarları geçiş sırasında önceliklidir.

Sistem simgeleri bölümünde ses, ağ, pil, mikrofon, konum/GPS, Studio Effects, Recall ve dil göstergesi gizlenebilir. Bildirim zili Windows'a bırakılabilir, sürekli gizlenebilir veya yeni bildirim olmadığında gizlenebilir. Son seçenek bilinen boş zil simgelerini izler; tanınmayan bir bildirim durumunu gizlemez. Ortak mikrofon/konum göstergesi yalnızca iki gösterge de gizlendiğinde kaldırılır. Saat ve uygulamaların bildirim simgeleri bu kontrollerden bağımsızdır.

Tasarım bölümündeki **Özel görev çubuğu arka planı** açıldığında renk seçiciden veya `#RRGGBB` koduyla arka plan rengi, %0–100 aralığında opaklık seçilebilir. %0 tamamen saydam, %100 tamamen opaktır; simge ve yazıların opaklığı değişmez. Özel arka plan, temanın renginin yerine geçer; Dock yerleşimi korunur. Anahtar kapatıldığında seçili temanın arka planı geri gelir, Varsayılan temada Windows arka planı kullanılır. Ayarlar kaydedilir ve tasarım bileşeni etkinken uygulanır.

Masaüstünü göster düğmesi gizlenebilir veya 1–100 mantıksal piksel genişliğinde ayarlanabilir. `0`, Windows genişliğini korur; gizleme açıkken genişlik uygulanmaz.

Dock efekti uygulama düğmesinin üzerine gelindiğinde simgeyi büyütür; imleç ayrılınca eski ölçeğine döner. Büyütme %105–160, süre 50–500 ms aralığındadır. Windows animasyonları kapalıysa büyütme/küçültme anında gerçekleşir. Efekt kapatıldığında özgün dönüşüm ve dönüşüm merkezi, varsa XAML bağlamalarıyla birlikte geri yüklenir. Kabuk motorunun sahiplik kalp atışı kesildiğinde de geri alma uygulanır.

Motor, Windows 11'in canlı XAML ağacını kullanır; sistem simgelerini yerelleştirilmiş erişilebilirlik metinleriyle eşleştirmez. Mevcut öğe tanıtıcıları, içerik türleri ve bilinen simge kodları kullanılır. Tanınmayan simgeler değiştirilmez. Mikrofon, konum, Studio Effects ve Recall ilgili donanım/özellik kullanıldığında görsel ağaçta belirebilir. Explorer yeniden başladığında motor yeniden bağlanır ve yeni tanıtıcılarla kuralları üretir.

## Doğrulama

Test uygulaması aşağıdaki seçenekleri sunar:

- `--taskbar-only`: ayar aktarımı, bileşenlerin etkinlik durumu, paneldeki anahtarların benzersizliği, sistem simgesi seçimi, boş/dolu zil geçişi, ortak gizlilik göstergesi ve sınırlandırma kontrolleri.
- `--taskbar-customization-live`: birleşik modül üzerinden gerçek Başlat, mevcut sistem simgeleri, masaüstü düğmesi ve dock üzerine gelme/ayrılma kontrolleri. Fare konumu test sonunda geri yüklenir.
- `--shell-only`: ayrı XAML penceresinde gerçek uygulama/geri alma, öğe tanıtıcısı seçimi, dönüşümün kalp atışı kaybında geri alınması ve bağlamaların korunması.
- `--tray-live`: yalnızca teste ait simgeler üzerinde tepsiye taşıma ve özgün durumun geri yüklenmesi.
- `--taskbar-panel-only`: gerçek WebView2 üzerinde birleşik panel, bölüm başlıkları, Başlat anahtarı, yerelleştirilmiş zil seçimi ve ayarların yerel köprü üzerinden kaydedilmesi. `artifacts/taskbar-panel.png` önizlemesini üretir.
- `--taskbar-background-live`: gerçek görev çubuğunda özel rengin %100, %50 ve %0 opaklıkla uygulanması ve özel arka plan kapatılınca tema renginin geri gelmesi.

Canlı testler makinedeki yatay alt görev çubuğunu kullanır. Farklı Windows derlemeleri, üst/dikey yerleşimler ve bu makinede bulunmayan donanım göstergeleri için canlı doğrulama ayrıca gereklidir. Sistemin animasyon tercihi kapalı olduğunda canlı dock testi anlık büyütme/küçültme yolunu doğrular.

1 Kasım 2026 doğrulaması: Release/x64 derlemesi sıfır uyarı ve sıfır hatayla tamamlandı. 40 görev çubuğu kural/aktarım kontrolü, 13 yerel kabuk kontrolü, 11 canlı özelleştirme kontrolü, 9 canlı uygulama tepsisi kontrolü ve 6 gerçek arayüz kontrolü geçti. Mikrofon, konum, Studio Effects, Recall ve zil durumları bu makinede canlı olarak doğrulanmadı; ilgili görünürlük kuralları görsel ağaç verileriyle çalışır.

Geniş kapsamlı `--interface-only` koşusu, görev çubuğu dışındaki `LauncherWindow` için testin kullandığı kurucu imzasının güncel pencereyle uyuşmaması nedeniyle durdu. Eski `--shell-live` koşusu Başlat uygulama/geri alma kontrollerini geçti, ancak tema arka plan renginin geri okunmasında zaman aşımına uğradı. Bu iki koşu başarılı sayılmadı; mevcut temaların tümünün doğrulandığı iddia edilmez.
