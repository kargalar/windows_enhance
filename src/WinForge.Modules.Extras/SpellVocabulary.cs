namespace WinForge.Modules.Extras;

/// <summary>Curated multi-thousand word vocabularies for Turkish and English.</summary>
internal static class SpellVocabulary
{
    private static readonly Lazy<HashSet<string>> LazyTurkishWords = new(() =>
        new HashSet<string>(TurkishWordsText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase));

    private static readonly Lazy<HashSet<string>> LazyEnglishWords = new(() =>
        new HashSet<string>(EnglishWordsText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase));

    internal static HashSet<string> TurkishWords => LazyTurkishWords.Value;
    internal static HashSet<string> EnglishWords => LazyEnglishWords.Value;

    private const string TurkishWordsText =
        // Pronouns, Conjunctions, Basic Particles
        "ben sen o biz siz onlar beni seni onu bizi sizi onları bana sana ona bize size onlara " +
        "bende sende onda bizde sizde onlarda benden senden ondan bizden sizden onlardan " +
        "benim senin onun bizim sizin onların ve veya ya ile ama fakat lakin ancak çünkü " +
        "halbuki oysa madem gerek hem ne de da ki için gibi kadar göre karşı rağmen doğru " +
        "dolayı ötürü beri yana ait dair üzere karşın diye yani zira mademki öyleyse binaenaleyh " +
        // Questions
        "ne neden niçin niye nasıl nerede nereye nereden ne zaman kim kimi kime kimde kimden kimin " +
        "hangisi hangi kaç kaçta kaça ne kadar nasıl bir " +
        // Numbers & Quantifiers
        "bir iki üç dört beş altı yedi sekiz dokuz on yirmi otuz kırk elli altmış yetmiş seksen " +
        "doksan yüz bin milyon milyar ilk son birinci ikinci üçüncü dördüncü beşinci altıncı " +
        "yedinci sekizinci dokuzuncu onuncu tek çift yarım çeyrek az çok biraz fazla oldukça " +
        "bütün tüm her hiç hiçbir hepsi bazı çoğu birkaç birçok pek gayet tam büsbütün " +
        // Greetings, Courtesies, Interjections
        "merhaba selam günaydın tünaydın iyi akşamlar geceler günler hoş geldiniz bulduk çakalın " +
        "hoşça kalın teşekkürler teşekkür ederim rica sağol varol kolay gelsin tebrikler tebrik " +
        "geçmiş olsun afiyet lütfen özür dilerim kusura bakmayın affedersiniz pardon tamam oldu " +
        "peki hayhay elbette tabii kesinlikle asla hayır evet yoo maalesef inşallah maşallah " +
        "eyvallah hayırlı uğurlu rastgele bravo yaşasın aman vah yazık eyvah hey vay " +
        // Common Verbs (Stems and Common Forms)
        "ol yap et git gel gör bak al ver de bil iste bul çık gir kal dur duy konuş söyle " +
        "anlat anla düşün çalış başla bitir yaz oku sev aç kapa otur kalk yürü koş bekle sor " +
        "cevapla ye iç uyu uyan taşı getir götür dön koy tut ara buluş tanış gül ağla kork " +
        "kaç geç kazan kaybet öğren öğret dene başar paylaş değiştir hatırla unut dinle izle " +
        "hisset güven inan yardım destek katıl ayrıl gönder al sat öde kazan harca kurtar sakla " +
        "bırak seç seçil koru temizle yıka hazırla pişir kes kır dik yapıştır boya çiz çöz yazdır " +
        "doldur boşalt çevir bağla çöz durdur başlat ilerle gerile yüksel alçal büyü küçül yaşa " +
        "öl doğ uyan uyu dinlen eğlen gez dolaş selamla kutla davet et kabul et reddet vazgeç " +
        "olmak yapmak etmek gitmek gelmek görmek bakmak almak vermek demek bilmek istemek " +
        "bulmak çıkmak girmek kalmak durmak duymak konuşmak söylemek anlatmak anlamak düşünmek " +
        "çalışmak başlamak bitirmek yazmak okumak sevmek açmak kapatmak oturmak kalkmak yürümek " +
        "koşmak beklemek sormak cevaplamak yemek içmek uyumak uyanmak taşımak getirmek götürmek " +
        "dönmek koymak tutmak aramak buluşmak tanışmak gülmek ağlamak korkmak kaçmak geçmek " +
        "kazanmak kaybetmek öğrenmek öğretmek denemek başarmak paylaşmak değiştirmek hatırlamak " +
        "unutmak dinlemek izlemek hissetmek güvenmek inanmak geldim gittim gördüm bildim aldım " +
        "verdim yaptım ettim baktım kaldım durdum sordum dedim buldum çıktım girdim koştum " +
        "geldi gitti gördü bildi aldı verdi yaptı etti baktı kaldı durdu sordu dedi buldu " +
        "çıktı girdi koştu geldiğimiz gittiğimiz yaptığımız ettiğimiz bildiğimiz gördüğümüz " +
        "geliyor gidiyor görüyor biliyor alıyor veriyor yapıyor ediyor bakıyor kalıyor duruyor " +
        "geliyorum gidiyorum görüyorum biliyorum alıyorum veriyorum yapıyorum ediyorum bakıyorum " +
        "geliyoruz gidiyoruz görüyoruz biliyoruz alıyoruz veriyoruz yapıyoruz ediyoruz bakıyoruz " +
        "gelecek gidecek görecek bilecek alacak verecek yapacak edecek bakacak kalacak " +
        "geleceğim gideceğim göreceğim bileceğim alacağım vereceğim yapacağım edeceğim bakacağım " +
        "gelmiş gitmiş görmüş bilmiş almış vermiş yapmış etmiş bakmış kalmış durmuş " +
        // Time & Calendar
        "gün hafta ay yıl saat dakika saniye zaman vakit asır çağ dönem an mevsim sabah " +
        "öğle ikindi akşam yatsı gece şafak alacakaranlık bugün dün yarın şimdi sonra önce " +
        "daima her zaman hiçbir zaman bazen sıklıkla nadiren erken geç pazartesi salı çarşamba " +
        "perşembe cuma cumartesi pazar ocak şubat mart nisan mayıs haziran temmuz ağustos " +
        "eylül ekim kasım aralık ilkbahar yaz sonbahar kış bahar gündüz gecelik haftalık aylık yıllık " +
        // People, Family, Society
        "insan kişi adam kadın çocuk bebek genç yaşlı ihtiyar delikanlı kız erkek anne baba " +
        "anneanne babaanne dede nine nineci torun evlat oğul kızkardeş erkek kardeş ağabey abi " +
        "abla amca dayı hala teyze yeğen kuzen enişte yenge gelin damat kayınvalide kayınpeder " +
        "arkadaş dost yoldaş komşu akraba tanıdık yabancı millet ulus halk toplum vatandaş " +
        "birey topluluk grup ekip takım dernek vakıf kulüp meclis birlik örgüt cemiyet " +
        // Home, Places, Geography
        "ev daire konut bina apartman oda salon mutfak banyo tuvalet balkon bahçe teras çatı " +
        "kapı pencere duvar tavan zemin koridor merdiven asansör bodrum garaj sokak cadde mahalle " +
        "semt ilçe il şehir kasaba köy mezra memleket vatan ülke devlet yurt dünya kıta bölge " +
        "yol kavşak köprü tünel meydan park alan çarşı pazar dükkan mağaza avm market bakkal " +
        "okul üniversite lise fakülte kampüs sınıf amfi kütüphane hastane klinik eczane sağlık ocağı " +
        "ofis büro işyeri fabrika atölye şantiye şirket holding kurum bakanlık belediye valilik " +
        "deniz okyanus göl nehir ırmak çay dere havuz sahil kıyı plaj ada yarımada dağ tepe vadi " +
        "ova orman koru çöl bozkır tarla bağ bahçe ova koy körfez boğaz şelale pınar kaynak " +
        // Nature, Weather, Animals
        "hava gökyüzü güneş ay yıldız bulut yağmur kar dolu rüzgar fırtına tipi boran şimşek " +
        "yıldırım gök gürültüsü sis duman pus kırağı çiy gölge ışık aydınlık karanlık ısı sıcaklık " +
        "ağaç çiçek yaprak dal kök gövde çimen ot çalı yosun tohum meyve sebze gül lale karanfil " +
        "çam meşe gürgen kavak söğüt zeytin elma armut kiraz ceviz fındık dut incir üzüm karpuz kavun " +
        "hayvan kuş böcek balık memeli sürüngen kedi köpek at inek koyun keçi kuzu buzağı eşek katır " +
        "tavuk horoz civciv ördek kaz hindi güvercin serçe kartal şahin karga martı leylek bülbül " +
        "aslan kaplan kurt tilki ayı çakal tavşan geyik fil maymun zürafa zebra yılan kertenkele " +
        "kaplumbağa timsah balina yunus köpekbalığı hamsi istavrit alabalık sazan sinek arı karınca " +
        // Objects, Tools, Technology
        "eşya alet araç gereç cihaz makine motor donanım yazılım bilgisayar telefon tablet ekran " +
        "klavye fare kulaklık hoparlör yazıcı tarayıcı kamera mikrofon kablo şarj batarya pil " +
        "internet web site sayfa ağ bağlantı sinyal sunucu veri bellek dosya klasör kod program " +
        "uygulama sistem pencere sekme düğme menü simge imleç arama ileti mesaj e-posta bildirim " +
        "masa sandalye koltuk kanepe yatak dolap sehpa ayna halı perde lamba avize saat tablo " +
        "kitap defter kalem silgi cetvel makas yapıştırıcı zımba ataş kağıt zarf dosya karton " +
        "çanta bavul cüzdan anahtar kilit para kart hesap cüzdanı gözlük saat yüzük kolye küpe " +
        "araba otomobil otobüs minibüs kamyon kamyonet tır tren tramvay metro vapur gemi feribot " +
        "uçak helikopter bisiklet motosiklet scooter tekne kayık sandal motor vagon istasyon liman " +
        // Food, Drinks, Cooking
        "yemek gıda besin yiyecek içecek sofra tabak çanak kaşık çatal bıçak bardak fincan sürahi " +
        "tencere tava çaydanlık tepsi ocak fırın buzdolabı mikrodalga bulaşık ekmek su çay kahve " +
        "süt ayran meyve suyu çorba salata et tavuk balık köfte pilav makarna börek pide pizza " +
        "kebap döner mantı dolma sarma fasulye nohut mercimek patates soğan sarımsak domates biber " +
        "patlıcan kabak salatalık marul maydanoz nane tuz şeker karabiber pulbiber yağ zeytinyağı " +
        "tereyağı peynir zeytin yoğurt kaymak bal reçel pekmez yumurta un maya un helvası tatlı " +
        "pasta kek börek kurabiye çikolata dondurma baklava sütlaç muhallebi lokum kahvaltı öğle yemeği akşam yemeği " +
        // Body, Health, Medicine
        "beden vücut vücut baş kafa saç yüz göz kulak burun ağız dudak diş dil çene yanak alın " +
        "boyun omuz kol dirsek bilek el parmak tırnak avuç göğüs karın sırt bel bacak diz ayak " +
        "topuk kemik kas eklem deri ten kan damar kalp ciğer mide karaciğer böbrek beyin sinir " +
        "can sağlık sıhhat nefes ömür ömürlü hasta hastalık mikrop virüs bakteri ağrı sancı acı sızı " +
        "ateş öksürük grip nezle yara bere kırık çıkık kanama tansiyon nabız reçete ilaç hap şurup " +
        "iğne aşı pansuman tedavi ameliyat muayene doktor hekim hemşire hasta bakıcı tahlil röntgen " +
        // Adjectives (Quality, Quantity, Feeling, Color)
        "büyük küçük iri minik uzun kısa geniş dar yüksek alçak derin sığ kalın ince ağır hafif " +
        "hızlı çabuk yavaş serin ılık sıcak soğuk ayaz kuru ıslak nemli temiz pak kirli pis " +
        "dolu boş tıkabasa sağlam berk bozuk çürük taze bayat lezzetli nefis acı tatlı ekşi tuzlu " +
        "parlak ışıltılı donuk mat karanlık aydınlık loş sessiz sakin gürültülü sesli hoş güzel " +
        "çirkin sevimli tatlı şirin korkunç fena kötü berbat doğru dürüst yanlış hatalı eğri düz " +
        "düzgün kolay basit rahat güç zor çetin zahmetli yeni gıcır eski antika çağdaş modern " +
        "genç körpe taze yaşlı ihtiyar kocamış zengin varlıklı fakir yoksul fukara güçlü kuvvetli " +
        "zayıf cılız güçsüz şişman etli zayıf narin narin mutlu sevinçli neşeli şen üzgün dertli " +
        "kederli keder kızgın öfkeli sinirli hırçın sakin durgun heyecanlı coşkulu cesur yürekli " +
        "korkak ürkek çalışkan gayretli tembel uyuşuk cömert eli açık cimri hasis kibar nazik " +
        "zarif kaba hoyrat akıllı zeki uslu akılsız ahmak deli çılgın mantıklı ussal doğru haklı " +
        "haksız masum suçlu günahkar önemli mühim önemsiz kıymetli değerli ucuz pahalı fahiş " +
        "özgür hür serbest tutsak esir bağlı bağımsız meşgul yoğun serbest boş müsait hazır " +
        "farklı değişik ayrı aynı farksız benzer türdeş eşit denk yakın ırak uzak dip dibe " +
        "beyaz ak kara siyah al kırmızı sarı yeşil mavi lacivert mor pembe turuncu kahverengi " +
        "gri külrengi gümüş altın ela ela gözlü ela sarışın esmer kumral ak pak " +
        // Work, Business, Education, Abstract
        "iş görev vazife meslek uzmanlık çalışma çaba emek gayret başarı verim kazanç gelir gider " +
        "masraf bütçe para sermaye fon yatırım hisse senet tahvil fatura dekont makbuz sözleşme " +
        "anlaşma protokol imza kaşe mühür onay izin ruhsat lisans tapu senet dosya rapor belge " +
        "evrak kayıt sicil liste tablo grafik plan proje taslak fikir görüş düşünce teklif öneri " +
        "karar yargı hüküm kural kaide yasa kanun yönetmelik tüzük genelge talimat emir rica arzu " +
        "istek hedef gaye amaç vizyon misyon strateji taktik yöntem metot usul yol tarz biçim " +
        "eğitim öğretim ders konu ünite bölüm fasıl kitap sınav imtihan test not puan karne diploma " +
        "derece rütbe unvan kıdem tecrübe deneyim araştırma inceleme analiz sentez bulgu sonuç " +
        "özet giriş gelişme sonuç kaynak kaynakça dipnot alıntı intihal bilim sanat edebiyat tarih " +
        "coğrafya matematik fizik kimya biyoloji felsefe sosyoloji psikoloji mantık hukuk iktisat " +
        "dil lisan gramer sözcük kelime cümle paragraf metin şiir roman hikaye öykü tiyatro sinema " +
        "müzik şarkı beste melodi resim heykel mimari dans spor futbol basketbol voleybol güreş yüzme";

    private const string EnglishWordsText =
        // Pronouns, Prepositions, Conjunctions
        "the of and to a in that is was he for it with as his on be at by i this had not are but " +
        "from or have an they which one you were her all she there would their we him been has " +
        "when who will more no if out so said what up its about into than them can only other " +
        "new some could time these two may then do first any my now such like our over man me " +
        "even most made after also did many before must through back years where much your way " +
        "well down should because each just those people how too little state good very make " +
        "world still own see men work long get here between both life being under never day same " +
        "another know while last might great us go come since against came right used take " +
        "three off himself few house use during without again place American around however " +
        "home small found mrs thought went say part once general high upon school every don't " +
        "does got united left number course war until always away something fact water though " +
        "public less put think almost hand enough far took head yet government system better " +
        "set told nothing night end called why didn't eyes find going look asked later knew " +
        "point next program city group name case need letter lead power children boy let side " +
        "present without open together next white company become often given face true order " +
        "read whole nothing woman country area money problem line family seemed kind began " +
        "room light car keep word help early sure means side change interest best form large " +
        "table general body felt several space question across room hold view common clear top " +
        "matter young field sense pay door build second street friend major black white short " +
        "class reason ground surface action remember road full air line mother Father daughter " +
        "son sister brother parent child human girl lady wife husband girl baby friend party " +
        "game music story film show picture photo camera image book page paper note letter " +
        "word phrase speech language English Turkish voice sound noise silence peace quiet " +
        // Actions & Common Verbs
        "ask answer speak tell say talk call shout scream whisper write read draw paint send " +
        "receive get give take bring carry hold catch throw drop put place keep save leave " +
        "start begin stop finish end pause continue repeat wait stay remain remain live die " +
        "grow change become happen occur cause create produce make build fix repair destroy " +
        "break cut tear fold bind pack unpack open close shut lock unlock hide show reveal " +
        "search find lose miss discover explore invent learn teach study test check inspect " +
        "watch see look stare gaze glance listen hear smell taste touch feel sense perceive " +
        "walk run march jog sprint crawl jump hop leap skip dance slip slide fall rise stand " +
        "sit lie kneel lean rest relax sleep wake dream breathe cough sneeze laugh smile cry " +
        "eat drink chew swallow bite taste cook bake fry roast boil grill wash clean sweep " +
        "buy sell trade pay spend cost charge lend borrow rent hire employ fire work operate " +
        "lead guide direct follow chase escape flee avoid prevent defend attack fight beat " +
        "win lose draw match tie score count measure weigh estimate calculate compute solve " +
        "think believe know understand doubt wonder imagine expect hope wish dream prefer " +
        "love like adore care respect admire hate dislike fear dread worry regret forgive " +
        "help aid assist serve support protect rescue deliver supply provide furnish equip " +
        "travel journey trip visit tour cruise fly drive ride sail steer park moor board land " +
        // Common Adjectives & Qualities
        "good bad great fine excellent wonderful terrible awful pleasant nice lovely gorgeous " +
        "beautiful pretty handsome ugly attractive cute neat clean dirty filthy messy tidy " +
        "hot cold warm cool freezing chilly mild dry wet damp moist humid rainy snowy windy " +
        "bright light dark dim shiny dull brilliant glowing pale colorful vibrant faded " +
        "loud noisy quiet silent calm peaceful stormy violent harsh gentle smooth rough soft " +
        "definite definitely absolute complete total entire partial certain sure probable possible " +
        "hard firm stiff solid rigid flexible elastic tough tender delicate weak strong powerful " +
        "fast quick rapid swift speedy slow gradual sudden prompt delayed late early timely " +
        "easy simple plain basic clear obvious hard tough difficult complex tricky painful " +
        "big large huge vast giant massive tiny small little miniature short tall high low " +
        "wide broad narrow tight loose deep shallow thick thin slender slim fat plump heavy " +
        "rich wealthy poor broke cheap expensive costly priceless valuable precious worthless " +
        "happy glad joyful cheerful merry sad sorrowful depressed gloomy cheerful angry mad " +
        "furious irate cross wild tame gentle fierce bold brave courageous timid shy scared " +
        "clever smart bright intelligent wise foolish silly dumb stupid crazy mad insane " +
        "safe secure dangerous risky harmful harmless healthy sick ill well fit diseased injured " +
        "alive dead dying mortal fatal lethal fresh stale raw cooked ripe rotten spoiled sour " +
        "sweet salty bitter spicy tasty delicious bland savory greasy oily dry crisp crunchy " +
        "new old modern ancient antique young adult elderly aged former current present future " +
        "true false real genuine fake artificial natural pure correct right wrong accurate exact " +
        "free busy empty full occupied vacant idle available ready prepared complete entire " +
        // Digital, Computing, Workspace Terms
        "computer laptop desktop server screen monitor display keyboard mouse cursor pointer " +
        "window tab button icon menu list folder directory file path link shortcut address " +
        "data database code script program software hardware memory storage disk drive processor " +
        "network internet web website page site browser portal domain cloud system platform " +
        "input output error warning notice message dialog prompt query command process thread " +
        "service client host connection session socket packet port request response header payload " +
        "account user profile password key token credential license permission access security " +
        "update upgrade install remove delete erase clear undo redo cut copy paste select " +
        "format design layout style theme font color size margin border frame grid align " +
        "document text paragraph sentence heading title caption footer index section chapter " +
        "table row column cell header data chart graph diagram image figure canvas draw " +
        "audio video media sound voice music clip stream record play pause stop rewind skip " +
        "volume brightness contrast mute speed track playlist album artist genre duration " +
        "search filter sort group find replace match highlight inspect debug trace log alert";
}
