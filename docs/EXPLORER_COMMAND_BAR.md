# Gezgin komut çubuğu

Gezgin → Gezgin Komut Çubuğu → Üst komut çubuğunu gizle seçeneği,
Windows 11 Dosya Gezgini'nin Kes, Kopyala, Yapıştır, Sırala ve Görünüm
düğmelerini gizler. Adres çubuğu ve sekmeler kullanılmaya devam eder.
Seçenek veya modül kapatılınca önceki görünüm geri yüklenir.

Modül, açık Gezgin pencerelerinin WinUI XAML bağlantısını kullanır ve yeni
pencereleri de izler. Komut çubuğu daraltılır; gezinme alanı kalan satırı
kaplar. Yerel pencere düzeni, dosya listesini boşalan alana genişletir.
Özgün XAML değerleri ve dosya görünümü boyutları kapatırken geri alınır.
WinForge'un kalp atışı kesilirse yerel bağlantı görünümü geri yükler.
Kayıt defteri veya Windows dosyaları değiştirilmez.

Doğrulama (açık, simge durumuna küçültülmemiş Gezgin penceresi gerekir):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-native.ps1
$env:WINFORGE_NATIVE_DIRECTORY = Join-Path $PWD 'native/WinForge.Shell/bin/x64/Release'
dotnet run --project tests/WinForge.RegressionTests -p:Platform=x64 -- --explorer-command-bar-live
dotnet run --no-build --project tests/WinForge.RegressionTests -p:Platform=x64 -- --shell-only
```
