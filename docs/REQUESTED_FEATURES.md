# Panel, kısayol ve kayıt davranışı

İlgili modüller kontrol panelinde açılır/kapanır üst gruplar altında listelenir. Her alt modülün kendi anahtarı ve kayıt kimliği korunur. Görev Çubuğu’nun mevcut birleşik panelinde davranış, tasarım ve uygulama tepsisi ayrı bileşen anahtarlarına sahiptir. Varsayılan Yollar ve Varsayılan Programlar doğrudan seçilen modülün panelinde düzenlenir.

Başlat Menüsü Tasarımı’nda Recent/öneriler, Categories, uygulama başlığı/görünüm seçici, kullanıcı, güç ve alt klasör alanı bağımsız anahtarlara sahiptir. Windows’un görsel ağacında bulunan alt klasör/ayar düğmeleri menü açılınca algılanır; her biri ayrıca gösterilip gizlenebilir. Windows ayarlarında hiç eklenmemiş düğmeler bu listede bulunmaz. Yalnızca sabitlenmiş uygulamalar seçeneği birden fazla bölümü birlikte gizleyen mevcut kısayol seçeneğidir.

Kısayol alanında Tuş ata veya Tuş/fare ata, Windows işlemeden önce yerel girdiyi yakalar. Win+D ve Alt+MouseX1 gibi birleşimler kaydedilir; yan fare tuşu Gezgin’de geri gitmez. Normal metin düzenlemesinde Windows girdisi engellenmez. Escape, alanı terk etme, pencereyi kapatma ve 30 saniye zaman aşımı yakalamayı sonlandırır.

## Kullanılan userdata

`userdata`, çalışan EXE’nin yanındadır; çalışma dizinine bağlı değildir. `scripts/build.ps1 -Configuration Release -Run` şu dizini kullanır:

`src/WinForge.App/bin/x64/Release/net10.0-windows10.0.19041.0/userdata`

Bu dizindeki mevcut ayarlarda etkin 27 modül kimliğinin tamamı kaynakta mevcut. `settings.json` modül ve kısayol tercihlerini tutar; History ve ClipboardLine geçmiş verileridir, WebView2 tarayıcı profilidir; modeller ve simge/önbellek dosyaları her biri ayrı birer ayar değildir. Başka bir çıktı klasöründen (örneğin Debug, publish veya work/build) çalıştırılan EXE kendi userdata dizinini kullanır. Görünüm → Kayıt konumu alanı çalışan örneğin gerçek dizinini gösterir.

Mevcut günlüklerde ayarların okunmasına rağmen bazı modüllerin çalışma hataları var: Ekran Geçmişi için yanlış `SHGetFileInfo` DLL bağlantısı düzeltildi. Başlat menüsü henüz başlamamışsa modül artık etkin kalır ve menü açılınca kayıtlı görünümü otomatik uygular. Windows’un izin vermediği kayıt anahtarları ve başka uygulamanın tuttuğu kısayollar ayrıca hata üretebilir; bu durumlar kayıt klasörünün kaybolduğu anlamına gelmez. Kullanıcının mevcut userdata içeriği değiştirilmedi.

## Spotify

Mevcut entegrasyon Windows GSMTC medya oturumlarını kullanır. Bu oturumlar Spotify kitaplığı/oynatma listesi üyeliğini sunmaz. Spotify Web API kitaplığa ekleme/çıkarma için ayrı OAuth yetkisi ister. Bu nedenle kullanıcının koşullu isteği doğrultusunda işlevsiz bir liste düğmesi eklenmedi.

## Doğrulama

Release derlemesi hatasız. İlgili kontroller: 11 React testi, 42 varsayılan ayar kontrolü, 26 yerel yakalama/menü/hazır eylem kontrolü, 33 gerçek Windows menüsü fixture kontrolü ve 19 gerçek WebView2 panel kontrolü.

Geniş `--interface-only` paketi, bu işten önce değiştirilmiş LauncherWindow kurucusunu eski argümanlarla çağırdığı için tamamlanmıyor (`MissingMethodException`). Eski tam kısayol paketinin fiziksel SendInput/WM_HOTKEY senaryosu da bu oturumda olay alamadı. Computer Use ile normal uygulama açma denemesi `GetCursorPos: Access is denied` döndürdü. Win+D ve Alt+MouseX1 kancalarının girdiyi tüketip doğru birleşimi oluşturması yerel callback testinde; kayıtların UI’ya iletilmesi React testinde doğrulandı. Bu iki geniş paketin tamamının geçtiği iddia edilmiyor.
