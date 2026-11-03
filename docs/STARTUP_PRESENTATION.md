# Pencerelerin ilk görünümü

Explorer sol paneli yeni gezinme ağacının `WM_CREATE` işlemi tamamlanınca
biçimlendirilir. Yeni Explorer UI iş parçacıkları için periyodik keşif beklenmez.
Bkz. [sol panel bileşeni](../native/WinForge.Shell/EXPLORER-NAVIGATION.md).

Windows bildirim konumu `WinForge.NotificationsPlacementTap.dll` ile bildirim
penceresinin kendi UI iş parçacığında uygulanır. Oluşturma veya başlık atama
tamamlanınca pencere alt sınıflanır; `WM_WINDOWPOSCHANGING` sırasında istenen
koordinatlar, Windows hareketi göstermeden önce değiştirilir. Sonraki kabuk
taşıma ve boyutlandırmaları da aynı yoldan geçer. Yönetilen olay motorunun
40 ms beklemesi kaldırılmıştır.

Yalnız sistem dizinindeki ShellExperienceHost, bilinen CoreWindow sınıfı ve
Türkçe/İngilizce bildirim başlığı birlikte kabul edilir. Bildirim merkezi ve
diğer uygulamalar hedeflenmez. Ekran seçimi, DPI, kenar boşluğu ve aynı UI
iş parçacığındaki bildirim yığını korunur. Seçilen ekranın tam sınırları mevcut
modül davranışıyla uyumludur. Modül kapanınca ilk özgün sınırlar geri yüklenir;
kalp atışı altı saniye kesilirse yerel zamanlayıcı aynı geri almayı yapar.
Yerel hatalar modülün mevcut hata bildirimine iletilir.

Paketli kabuk süreçleri genel DLL kancasını engelleyebildiği için mevcut
ShellExperienceHost GUI iş parçacıklarına ayrıca doğrudan kanca kurulur.
Bağlantı kurulamayan pencereler mevcut olay motoruyla, ek zamanlayıcı beklemeden
konumlandırılır. Yerel ve olay motorları pencere sahipliğini işaretleyerek aynı
bildirimi iki kez yönetmez. Windows'un yerel bağlantıyı engellediği durumda
olaylar gösterim sonrasında geldiğinden ilk kare için aynı garanti verilemez.
Bkz. [Windows kanca kapsamı](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowshookexw).

Ses, parlaklık, uygulama bildirimi ve kilit tuşu panelleri konum ve içeriği
hazırlanmadan gösterilmez. Dosya bırakma tekerleği, pencere yerleşim tekerleği
ve yerleşim önizlemesinin HWND'si gizliyken oluşturulur ve konumu gösterimden
önce ayarlanır.

Doğrulama:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-native.ps1
$native = Join-Path $PWD native/WinForge.Shell/bin/x64/Release
& "$native/WinForge.ExplorerNavigationFixture.exe" "$native/WinForge.ExplorerNavigationTap.dll"
& "$native/WinForge.NotificationsPlacementFixture.exe" "$native/WinForge.NotificationsPlacementTap.dll"
```

Yerel fixture'lar ayrı kayıt anahtarlarında çalışır. Bildirim fixture'ı ilk
görünür konumu mesaj işlenirken gözlemler; odak, yeniden taşıma, boyut değişimi,
yığın, ilgisiz pencere, geri alma ve kalp atışı kaybını da denetler. Bunlar
kontrollü HWND testleridir; gerçek ShellExperienceHost'un her Windows
sürümündeki ilk kompozitör karesi bu testlerle doğrulanmış sayılmaz.
`--panel-placement-only` regresyon grubu panel gösterilmeden konum ve içeriğin
hazır olmasını ve çoklu ekran yerleşimini denetler. Yerel fixture'lar ayrıca
`scripts/test-integration.ps1 -NativeSmoke` akışına dahildir.
