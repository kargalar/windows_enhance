# Otomatik düzeltme

Motor Türkçe ve İngilizce için yerel sözlükler, klavye yakınlığı, yazım uzaklığı
ve iki dile özel korpuslardan eğitilmiş unigram/bigram/trigram frekans modelleri
kullanır. Eğitim verisi tüm gündelik yazımı kapsamadığından bağlam hatasız değildir.

- Önceki iki kelime bağlama alınır. En kısa kelime ayarı, kısa kelimelerin bağlamda
  tutulmasını engellemez. Cümle/noktalama, imleç hareketi, fare tıklaması ve pencere
  değişimi bağlamı temizler.
- Açık kullanıcı kısayolları ve bilinen yazım hataları tahmini önerilerden önce
  değerlendirilir. Doğru kelimeler korunur; `her ver → her yer` için mevcut açık
  karışıklık kuralı sürdürülür. İki dil birlikte seçildiğinde herhangi birinde doğru
  kabul edilen kelime tahmini düzeltmeye uğramaz.
- Birbirine yakın puanlı adaylar varsa motor değiştirmekten vazgeçer. Bağlam veya
  kişisel kullanım sıklığı yeterli ayrım sağladığında uygun aday seçilir.
- Öneri şeridi etkinse yazarken Türkçe ve İngilizce kelime tamamlama/yazım önerileri,
  kelime sonundaki boşluktan sonra da n-gram bağlamına göre sonraki kelimeler
  imlecin yanında gösterilir. Öneri tıklanınca yazı odağı korunarak metne eklenir;
  özellik ayarlardan kapatılabilir.
- Türkçe kelimeler önce eğitilmiş sözcük biçimleri ve yerel sözlüklerle kontrol
  edilir. Bunlarda bulunmayan biçimler Zemberek biçimbilim çözümleyicisiyle
  doğrulanır; böylece haber korpusunda geçmeyen geçerli ekli kelimeler korunur.
  Yanlış yazılmış ekli kelimeler için kök adayları da değerlendirilir
  (`kitplar → kitaplar`).
- Kişisel öğrenme kapalıysa öğrenilmiş sözlük, kelime, bigram ve trigram frekansları
  kullanılmaz. Bilinmeyen bir kelimeyi tekrarlamak onu sözlüğe eklemez; bu, aynı
  yazım hatasının zamanla kalıcı doğru kabul edilmesini önler. Yalnızca açıkça
  eklenen kelimeler öğrenilir. Öğrenme açıkken değişen sıralama sonuçları
  önbelleğe alınmaz. Testler geçici kişisel sözlük dosyaları kullanır.
- Yeni tuş veya fare girdisi eski bekleyen düzeltmeyi geçersiz kılar. Backspace ile
  geri almada fiziksel tuşun zaten sildiği ayırıcı ikinci kez silinmez.
- Türkçe ve İngilizce dışındaki önceden kayıtlı dil seçimleri korunur, ancak
  İngilizceye sessizce çevrilerek düzeltme yapılmaz.

## Türkçe model verisi

Türkçe unigram, bigram ve trigram ağırlıkları, Ali Safaya ve katkıcılarının
[trnews-64 eğitim bölümü](https://github.com/tdd-ai/trnews-64) kullanılarak
üretilir. Eğitim bölümü 59,7 milyon kelimelik haber ve köşe yazısı metni içerir;
ayrılmış 2,1 milyon kelimelik doğrulama ve test bölümleri eğitime katılmaz.
Kaynak verinin lisansı CC BY 4.0'dır. Bu model verisiyle dağıtılan sürümlerde
kaynak atfı korunmalıdır: *Safaya et al., Mukayese: Turkish NLP Strikes Back,
2022; trnews-64, DOI 10.5281/zenodo.5180654; CC BY 4.0.* WinForge projesi veya
uygulaması veri üreticileri tarafından onaylanmamıştır.

Modeli aynı eğitim bölümüyle tekrar üretmek için kaynak arşivini açıp şu komutu
çalıştırın:

```powershell
dotnet run --project tools/WinForge.SpellModelTrainer/WinForge.SpellModelTrainer.csproj -c Release -- work/corpus/trnews-64.train.raw src/WinForge.Modules.Extras/Resources/TurkishNgramModel.tsv.gz tr-TR
```

Arşiv `https://zenodo.org/record/5180654/files/trnews-64.tar.bz2?download=1`
adresinden alınabilir. Kaynak dağıtımında `trnews-64.train.raw` dosyasının yanında
`README.md` ve `LICENSE` dosyalarını da saklayın.

Ayrı test bölümündeki sözcük biçimleriyle belirli aralıklı harf yer değiştirmelerini
ölçmek için regresyon çalıştırıcısına `--autocorrect-benchmark` ve
`work/corpus/trnews-64.test.raw` yolunu verin. Ölçüm öneri kesinliğini, sentetik
transpozisyon doğruluğunu ve korpusun doğrulanmamış biçimlerine yanlış düzeltmeleri
raporlar. Bu ölçüm dar bir sentetik hata kontrolüdür; gerçek kullanıcı yazımını
veya Gboard karşılaştırmasını temsil etmez.

İngilizce bağlam ve sonraki kelime sıralaması, Salesforce'un
[WikiText-103 eğitim bölümü](https://huggingface.co/datasets/Salesforce/wikitext)
ile eğitilir. Kaynak, doğrulanmış Wikipedia Good ve Featured makalelerinden
alınmıştır ve CC BY-SA 4.0 lisanslıdır. Eğitim dışı doğrulama ve test bölümleri
model üretiminde kullanılmaz. Kaynak atfı: *Merity, Xiong, Bradbury ve Socher,
Pointer Sentinel Mixture Models, 2016; WikiText-103; CC BY-SA 4.0.* Model bu
paylaşım benzeri lisans koşuluyla dağıtılır.

## İngilizce yazım hatası modeli

Yüksek güvenli tek kelimelik hata eşleşmeleri, [TOEFL-Spell anotasyonlarından](https://github.com/EducationalTestingService/TOEFL-Spell)
üretilir. Kaynak 6.121 gerçek öğrenci yazım hatasını içerir ve CC BY-SA 4.0
lisanslıdır. Essay dosyaları SHA-256 ile kararlı biçimde %80 eğitim ve
%20 held-out bölümüne ayrılır. Kaynak modele yalnızca eğitim bölümünden en az
iki kez görülen ve düzeltme eşleşmesi en az %90 tutarlı olan hatalar girer.
Bu kuralla 578 düzeltme üretildi. Model CC BY-SA 4.0 koşuluyla dağıtılır;
atıf: *Flor, Fried ve Rozovskaya, A Benchmark Corpus of English Misspellings
and a Minimally-supervised Model for Spelling Correction, 2019; TOEFL-Spell.*

Yerel TSV anotasyon dosyasıyla modeli tekrar üretmek için:

```powershell
dotnet run --project tools/WinForge.SpellModelTrainer/WinForge.SpellModelTrainer.csproj -c Release -- typo-pairs work/corpus/TOEFL-Spell-Annotations.tsv src/WinForge.Modules.Extras/Resources/EnglishSpellingErrorModel.tsv.gz
```

Essay dosyası bazlı held-out test benchmark'ı:

```powershell
& tests/WinForge.RegressionTests/bin/x64/Release/net10.0-windows10.0.19041.0/WinForge.RegressionTests.exe --autocorrect-annotated-benchmark work/corpus/TOEFL-Spell-Annotations.tsv en-US --heldout
```

Bu testte 1.136 hata üzerindeki exact correction accuracy %51,50'den %62,15'e,
coverage %59,86'dan %70,42'ye ve öneri precision %86,03'ten %88,25'e çıktı.
Doğru aday, öneri şeridinde top-1 %67,34 ve top-3 %74,74 doğrulukla göründü;
şerit kapsamı %84,51 oldu. Belirsiz durumlarda otomatik düzeltme dururken adaylar
kullanıcı seçimi için gösterilir.
Anotasyonlarda tam deneme metni bulunmadığı için bu ölçüm bağlamı hesaba katmaz;
TOEFL öğrencisi hataları da telefon mesajı yazımını temsil etmez.

WikiText Parquet eğitim parçalarını UTF-8 metne dönüştürmek için `pyarrow`
gereklidir:

```powershell
python -m pip install pyarrow
python tools/extract_wikitext_parquet.py work/corpus/wikitext-103.train.raw work/corpus/wikitext-103-train-00000.parquet work/corpus/wikitext-103-train-00001.parquet
dotnet run --project tools/WinForge.SpellModelTrainer/WinForge.SpellModelTrainer.csproj -c Release -- work/corpus/wikitext-103.train.raw src/WinForge.Modules.Extras/Resources/EnglishNgramModel.tsv.gz en-US
```

İngilizce Parquet test bölümünü aynı dönüştürücüyle ayrı bir dosyaya çıkarın;
eğitim modelini değerlendirmek için dil etiketini `en-US` verin:

```powershell
python tools/extract_wikitext_parquet.py work/corpus/wikitext-103.test.raw work/corpus/wikitext-103-test.parquet
& tests/WinForge.RegressionTests/bin/x64/Release/net10.0-windows10.0.19041.0/WinForge.RegressionTests.exe --autocorrect-benchmark work/corpus/wikitext-103.test.raw en-US
& tests/WinForge.RegressionTests/bin/x64/Release/net10.0-windows10.0.19041.0/WinForge.RegressionTests.exe --autocorrect-nextword-benchmark work/corpus/wikitext-103.test.raw en-US
```

İngilizce model 503.317 kelime biçimini tanır ve yazım düzeltme adaylarını en
sık görülen 100.000 biçimle, bigram ve trigram tablolarını ise en çok 250.000
ve 150.000 kayıtla sınırlar.

## Günlük konuşma tahmini

Genel haber/Wikipedia modelleri kısa gündelik konuşmaları yeterince yansıtmaz.
İngilizce konuşma modeli [English Daily Dialogues 10K](https://huggingface.co/datasets/3nesdeniz/english-daily-dialogues-10k)
eğitim bölümünden; Türkçe konuşma modeli [Turkish Daily Dialogues 5K](https://huggingface.co/datasets/3nesdeniz/turkish-daily-dialogues-5k)
eğitim bölümü ile [Turkish Chat Max 25K](https://huggingface.co/datasets/cisimcik/turkish-chat-max-25k)
veri kümesinin yalnızca `casual_conversation` bölümündeki Türkçe kullanıcı ve
asistan iletilerinden üretilir. İlk iki kaynak sentetik ve CC BY 4.0, son kaynak
Apache 2.0 lisanslıdır. Günlük Diyaloglar'ın `test` bölümü ve Turkish Chat Max
eğitim verisi dışında tutulmuş Daily Dialogues test benchmark'ı değerlendirme için
ayrı kalır. Genel ve konuşma model adayları tahminde sırasıyla %55 ve %45 ağırlıkla
birleştirilir. Üçlü olasılık, bağlamda görülen örnek sayısına göre bigram olasılığıyla
yumuşatılır; birleştirme en güçlü 64 adayı inceler.
Seçilen önerilerden kişisel bigram
ve trigram frekansları öğrenilir; iki kelimelik geçmiş için yeterli trigram verisi
varsa kişisel tahmin bu bağlamı kullanır, yoksa bigrama döner. Kişisel bigramlar da ayrıca
öneri sıralamasına katılır. Korpus bağlamı bulunmazsa en sık görülen 64 kelime
genel tahmin olarak kullanılır; böylece öneri şeridi boş kalmaz. Kaynak atfı:
*3nesdeniz, English Daily Dialogues
10K ve Turkish Daily Dialogues 5K, 2026; CC BY 4.0; Cisimcik AI Lab, Turkish Chat
Max 25K, `casual_conversation` alt kümesi, 2026; Apache 2.0.*

```powershell
python tools/extract_dialogue_parquet.py work/corpus/english-dialogues.train.raw work/corpus/english-dialogues-train.parquet
python tools/extract_dialogue_parquet.py work/corpus/turkish-dialogues.train.raw work/corpus/turkish-dialogues-train.parquet
python tools/extract_turkish_chat_parquet.py work/corpus/turkish-chat-casual-train.parquet work/corpus/turkish-chat-casual-train.raw
Get-Content work/corpus/turkish-dialogues.train.raw,work/corpus/turkish-chat-casual-train.raw | Set-Content -Encoding utf8 work/corpus/turkish-dialogue-combined.train.raw
dotnet run --project tools/WinForge.SpellModelTrainer/WinForge.SpellModelTrainer.csproj -c Release -- work/corpus/english-dialogues.train.raw src/WinForge.Modules.Extras/Resources/EnglishDialogueNgramModel.tsv.gz en-US
dotnet run --project tools/WinForge.SpellModelTrainer/WinForge.SpellModelTrainer.csproj -c Release -- work/corpus/turkish-dialogue-combined.train.raw src/WinForge.Modules.Extras/Resources/TurkishDialogueNgramModel.tsv.gz tr-TR
```

Test bölümünü ayrı çıkarıp sonraki kelime tahmin benchmark'ını çalıştırmak için:

```powershell
python tools/extract_dialogue_parquet.py work/corpus/english-dialogues.test.raw work/corpus/english-dialogues-test.parquet
python tools/extract_dialogue_parquet.py work/corpus/turkish-dialogues.test.raw work/corpus/turkish-dialogues-test.parquet
& tests/WinForge.RegressionTests/bin/x64/Release/net10.0-windows10.0.19041.0/WinForge.RegressionTests.exe --autocorrect-nextword-benchmark work/corpus/english-dialogues.test.raw en-US
& tests/WinForge.RegressionTests/bin/x64/Release/net10.0-windows10.0.19041.0/WinForge.RegressionTests.exe --autocorrect-nextword-benchmark work/corpus/turkish-dialogues.test.raw tr-TR
```

Eğitim korpusunda görülen 845.007 Türkçe kelime biçimi tanınır; bulanık yazım
düzeltmesinde taranan adaylar en sık kullanılan 100.000 biçimle sınırlandırılır.
Model ayrıca en çok 250.000 bigram ve 150.000 trigram saklar. Biçimbilim
çözümleyicisi korpusta görülmeyen geçerli çekimleri tanımaya yardımcı olur;
aday havuzu ise her tuşta büyümez. Haber dili günlük sohbeti tam temsil etmez;
kişisel bigramlar ve mevcut yazım örnekleri bu açığı kısmen giderir.

2026-10-28 tarihinde test bölümünden sabit tohumla seçilen 1.000 kelimelik
örneklemde 902 sentetik bitişik harf yer değişikliğinin %97,45'i asıl kelimeye
düzeltildi; sunulan önerilerin %100'ü hedef kelimeydi. Doğru kabul edilen test
biçimlerinin 9/1.000'i değişti (%0,90). Bu ölçüm sentetik ve sınırlıdır; günlük
yazım, sonraki kelime tahmini, gecikme ve Gboard ile eşdeğer bir karşılaştırma
ölçmez.

2026-10-28 tarihinde WikiText-103 test bölümünden sabit tohumla seçilen
1.000 kelimelik örneklemde 924 sentetik bitişik harf yer değiştirmesinin
%92,75'i asıl kelimeye düzeltildi; sunulan önerilerin %98,85'i hedef kelimeydi.
Doğru kabul edilen test biçimlerinin 11/1.000'i değişti (%1,10). Bu sonuç da
sentetik ve sınırlı bir ölçümdür; günlük yazım veya Gboard ile doğrudan
karşılaştırma değildir.

Sonraki kelime tahmini için benchmark, 100.000 bağlama kadar sabit tohumlu
rezervuar örneklemi kullanır. WikiText-103 test bölümündeki 176.941 bağlamdan
100.000'i ölçüldü: öneri kapsamı %100,00, top-1 %16,11 ve top-3 %24,58. Aynı
karışım trnews-64 test bölümündeki 1.433.539 bağlamdan 100.000'inde %100,00
kapsama, %9,53 top-1 ve %15,75 top-3 verdi.

Konuşma testlerinde örneklem sınırına ulaşılmadı; tüm bağlamlar ölçüldü. English
Daily Dialogues test bölümündeki 60.765 bağlamda kapsam %100,00, top-1 %11,49,
top-3 %17,55 oldu. Turkish Daily Dialogues test bölümündeki 12.977 bağlamda
kapsam %100,00, top-1 %77,38, top-3 %87,55 oldu. Türkçe diyalog korpusu
yapay olarak üretilmiştir; bu sonuçlar gerçek mesaj yazımı veya Gboard karşılaştırması sayılmaz.

Kişisel sözlük ve n-gramların ayrı tutulması Android'in açık kaynak LatinIME
[kullanıcı unigram/bigram sözlükleri](https://android.googlesource.com/platform/packages/inputmethods/LatinIME/+/52fbad88756a46c4c8142034aa19a13deb1e387b/java/src/com/android/inputmethod/latin/LatinIME.java)
tasarımına dayanır. WinForge seçilen önerileri kişisel kelime, bigram ve trigram
sıklıklarına ekler; bilinmeyen kelimeler kendiliğinden doğru kabul edilmez.

Odaklı doğrulama:

```powershell
dotnet build tests/WinForge.RegressionTests/WinForge.RegressionTests.csproj -c Release -p:Platform=x64
& tests/WinForge.RegressionTests/bin/x64/Release/net10.0-windows10.0.19041.0/WinForge.RegressionTests.exe --autocorrect-only
```

Bu kontroller motoru, ayarları, aday sıralamasını, kelime geçmişini ve geri alma
metin hesabını sınar. Gerçek uygulamalara klavye kancası üzerinden yazım göndermez.

Gerçek Türkçe sosyal medya yazımını ölçmek için [#Turki$hTweets veri kümesi](https://github.com/atubakoksal/annotated_tweets)
yerel olarak `work/corpus/TurkishTweets-data.txt` konumuna indirilebilir. Veri kümesi
uygulama paketine dahil edilmez. Yalnızca token düzeyinde hizalanan ve `OOV-ill_formed`
olarak etiketlenen tek kelimelik düzeltmeler puanlanır; hizalanmayan satırlar atlanır.

```powershell
Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/atubakoksal/annotated_tweets/main/data.txt' -OutFile 'work/corpus/TurkishTweets-data.txt'
& tests/WinForge.RegressionTests/bin/x64/Release/net10.0-windows10.0.19041.0/WinForge.RegressionTests.exe --autocorrect-turkish-tweets-benchmark work/corpus/TurkishTweets-data.txt
```

2026-10-28 tarihinde veri kümesinin boşlukla ayrılmış token hizalaması korunarak
2.000 satırın tamamı ölçüldü. 4.061 tek kelimelik hatada tam otomatik düzeltme
doğruluğu %54,72, kapsama %59,84 ve düzeltme precision'ı %91,44 oldu. Öneri
şeridinde kapsam %90,40, top-1 %68,53, top-3 %72,86; aday havuzu recall'ı %86,78
çıktı. Aynı satırlardaki 11.474 sağlam kelimenin 71'i değişti (%0,62). Önceki tam
ölçüme göre otomatik doğruluk 4,71 puan ve öneri top-1 4,75 puan yükseldi.
Biçimbilim tarafından geçerli sayılan kelimelerde en fazla iki Türkçe diakritik
değişimi, ancak aday skoru ve ayrımı güçlü olduğunda otomatik düzeltilir. Sonda
uzatılmış harfler, kısaltılmış biçim biliniyor ve sıklık ya da bağlam destekliyorsa
normalleştirilir; içteki çift harfler tek başına yeterli sayılmaz. Önceki ilk ölçümde
doğruluk %32,48 ve öneri top-1 %41,81'di.
Bu sonuç gerçek tweet yazımına sentetik testlerden daha yakındır; Gboard üzerinde eş
zamanlı test değildir.
Kaynak veri pakete kopyalanmaz; yeniden dağıtım için kaynak deposunun kullanım
koşulları ayrıca incelenmelidir.

Eksik Türkçe karakterler artık aday puanlamasında düşük maliyetli yazım farkı sayılır;
adaylar yine yerel sözlükte, eğitim korpusunda veya Zemberek biçimbilim
çözümleyicisinde bulunmalıdır. Aynı değişikliklerden sonra trnews-64 test
bölümündeki temiz 1.000 kelimede 902 sentetik harf yer değişikliğinin %97,12'si
düzeltildi, öneri precision'ı %100,00 kaldı ve geçerli biçimlerin 9'u değişti
(%0,90). Bu ikinci ölçüm de sentetiktir.

N-gram önerileri aynı Türkçe tweet kümesinin düzeltilmiş metninde de ölçülür.
Türkçe konuşma modeline Turkish Chat Max'in gündelik konuşma alt kümesi eklendikten
sonra TurkishTweets ölçümünde öneri kapsamı %100, top-1 %3,68 ve top-3 %6,50 oldu
(önceki model: %3,48 / %5,89). Aynı değişiklik ayrı Turkish Daily Dialogues test
bölümünde top-1 %77,24 ve top-3 %87,42 verdi (önceki model: %77,38 / %87,55).
İyileşme sosyal medya ölçümünde küçük, diyalog benchmark'ında ise yaklaşık aynı
seviyede; bu sonuç Gboard düzeyini göstermiyor. Türkçe sohbet tahmini hâlâ büyük,
gerçek kullanıcı yazımından oluşan lisansı net bir korpusa ve kişiye göre öğrenmeye
ihtiyaç duyuyor.
