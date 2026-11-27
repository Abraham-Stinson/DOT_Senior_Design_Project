# M2.2.1-C · Ortak savaş oturumu ve Steam entegrasyonu

Unity 6000.3.26f1 · Untitled Pirate Game · Windows x64 Development

## Kapanış sonucu · 27 Kasım 2026

Üç offline mod MainMenu üzerinden gerçek Level_01 alanına ulaştı. Online PvP’de gerçek Steam lobisi, host Ready mesajının FishNet üzerinden gidiş dönüşü ve tek hostun savaş sahnesi üyeliği doğrulandı. Yeni gameplay build’inin iki fiziksel bilgisayardaki uçtan uca kabulü açık.

| Son doğrulama | Sonuç | Kayıt |
|---|---|---|
| EditMode | 43 geçti, 0 başarısız, 0 atlandı | [NUnit sonuçları](TestResults/EditMode.xml) |
| PlayMode | 14 geçti, 0 başarısız, 0 atlandı | [NUnit sonuçları](TestResults/PlayMode.xml) |
| Windows_Gameplay profili | StandaloneWindows64, Development. BuildReport 0 hata, 0 uyarı | [Build ve açılış kaydı](TestResults/WindowsGameplay.json) |
| İki uzak Steam hesabıyla gameplay | Kullanıcı kabulü bekliyor | [İki bilgisayarlı prosedür](#iki-bilgisayarlı-kabul-prosedürü) |

Teslim klasöründeki 319 dosyanın SHA-256 değerleri profil çıktısıyla eşleşti. Windows oyuncusunda pencere oluştu, Steam API başlatıldı ve normal pencere kapatmasında API temiz kapandı. İşlem çıkış kodu 0.

Test ve build işlemleri ayrı bir doğrulama kopyasında çalıştırıldı. Assets, Packages ve ProjectSettings içindeki proje dosyaları asıllarıyla karşılaştırıldı. Kopyaya yalnızca Editor doğrulama araçları eklendi. Paket sürümleri, teknik NetworkZeppelinTest sahnesi ve test spawner’ı korundu.

## Mevcut yapı ve alınan kararlar

Menü, Fight veya son diyalog satırından sonra kendi HUD önizlemesini açıyordu. Lobby Ready yerel bellekteydi. Level_01 içindeki BattleSessionBootstrap ise Inspector’da seçilmiş modu kullanıyordu. Teknik NetworkBattleSpawner, bağlantının başlangıç sahneleri hazır olduğunda hemen zeplin oluşturuyordu. Gerçek menü akışında bu olay henüz lobideyken gerçekleşebilir.

Kapanışta iki sorun düzeltildi. Harita kodu önceki uygulamada sınıf sabitiydi. Artık her BattleLaunchContext doğrulanmış MapId değerini saklıyor ve sahne işlemleri bu değeri kullanıyor. Tek hostlu sahne üyeliği testi ayrıca FishNet 4.7.4R sunucusu yükleme sırasında durdurulduğunda OnQueueEnd bildiriminin gelmeyebildiğini gösterdi. Savaş oturumu yükleme ve boşaltma işlemlerinin kendi başlangıç ve bitiş bildirimlerini izliyor. Aynı test artık hem sahnenin kaldırıldığını hem de Multiplayer ekranına dönüldüğünü doğruluyor.

NetworkFoundation prefabında `_dontDestroyOnLoad = 0`. MainMenu yüklü kaldığı sürece aynı NetworkManager, SteamRuntime, lobby callback’leri ve EventSystem kullanılabiliyor. Bu nedenle savaş alanı additive yüklenir. Teknik sahneler ve test spawner’ı mevcut giriş noktalarını korur. FishNet 4.7.4R, Steamworks.NET 2025.165.0 ve yerel yamalı FishySteamworks 4.1.1 kullanılıyor. Steam transport ISteamNetworkingSockets kullanır.

## Dört modun akışı

| Mod | Level_01 giriş noktası | Mavi ve kırmızı |
|---|---|---|
| Campaign | Campaign Map → L1 hazırlığı → iki diyalog satırı | Mavi WASD, kırmızı bekler |
| Battle vs AI | Skirmish → Battle vs AI → harita → zorluk ve kartlar → Fight | Mavi WASD, kırmızı bekler. Seçilen zorluk context’te taşınır |
| Local PvP | Skirmish → Local PvP → harita → P2 katılımı ve ayrı kartlar → Fight | Mavi WASD, kırmızı yön tuşları |
| Online PvP | Multiplayer → arkadaş lobisi → harita ve kartlar → iki Ready → host Start Match | Host mavi, konuk kırmızı. Her sahip kendi cihazında WASD kullanır |

BattleLaunchContext mod, harita, Campaign bölüm numarası, zorluk, mavi/kırmızı takımın oyuncu kimlikleri ve beşer kart slotunu taşır. Online oturum ayrıca Steam lobby ID’si ve hostun ürettiği benzersiz maç kimliği taşır. Tek harita Level_01’dir. Savaş sahnesi, menüden bağımsız olarak Editor’da açıldığında mevcut Inspector test modu çalışır.

Kartlar slot sırasını koruyan sayısal bir alanla aktarılır. Her slot boş, Territory Balloon, Mining Camp veya Cannon Tower olabilir. İlk slot Balloon olmalıdır ve aynı kart tekrarlanamaz. Sunucu geçersiz diziyi reddeder. Kalıcı ve güvenilir bir Campaign ilerleme kaynağı henüz yok. Gerçek L2 zaferi ve bu zaferi doğrulayacak sistem gelene kadar Shield Tower kapalıdır.

## Çevrimiçi hazırlık, sahne üyeliği ve sahiplik

1. M0 Steam lobby ve FishNet kimlik doğrulaması tamamlanır. BattleRoom mavi koltuğu server’ın kendi doğrulanmış client bağlantısından belirler. Diğer doğrulanmış bağlantı kırmızı koltuğa oturur.
2. Her istemci güvenilir FishNet broadcast ile kendi lobby ID’sini, gördüğü haritayı, beş kartı ve Ready isteğini yollar. Sunucu gönderenin gerçek bağlantısından koltuğu belirler, alanları doğrular ve kanonik PreparationState’i doğrulanmış üyelere yollar. Düzenlemeye dönmek veya eli değiştirmek o oyuncunun Ready durumunu kaldırır. Üyelik değişikliği iki onayı da kaldırır.
3. Harita seçimi host işlevidir. Fiilen bir arena bulunduğundan aynı seçimi tekrar onaylamak Ready’yi korur. Geçerli harita değişikliği iki onayı sıfırlar. Start Match yalnız host, iki doğrulanmış oyuncu, iki lobi üyesi, iki Ready, geçerli kartlar ve boş maç kimliğiyle açılır. Tıklamada sunucu koşulları yeniden kontrol eder. İstemciden maç başlatan bir mesaj türü yoktur.
4. Host kimliği ve elleri sabitler, GUID üretir, yapılandırmayı yayınlar ve iki bağlantıyı FishNet `LoadConnectionScenes` ile Level_01’e alır. Sunucu her iki oyuncunun `OnClientPresenceChangeEnd` bildirimini bekler. Ardından sahnede yerleştirilmiş iki başlangıç konumunda mevcut NetworkZeppelinBlue ve NetworkZeppelinRed örneklerini oluşturur. `ServerManager.Spawn` çağrısına sahip bağlantı ve savaş sahnesi açıkça verilir. Son olarak Active durumu yayınlanır.
5. Level_01 bootstrap’ı online modda önceden yerleştirilmiş offline zeplinleri kapatır. Hareket için aynı ZeppelinMotor ve mevcut sahiplik gerektiren ServerRpc kullanılır. Motor, sınırlar, NetworkTransform otoritesi, gönderim hızı ve input timeout değerleri korunmuştur.
6. Sahne geçişinde menü kökü ve Menu Camera kapatılır. Pause yerel ekranı açar. Offline yerel motorlar bekler, online süreçte yalnız o oyuncunun girdisi bastırılır. Leave Match çevrimiçi maçı ve lobiyi sonlandırır. Ayrılma, üyelik kaybı veya yükleme zaman aşımında ağ oturumu kapatılır, devam eden sahne işlemi tamamlanınca kalan Level_01 kaldırılır. Menü kamera ve girdileri geri gelir. Tekrar oynama yeni bir lobide yeni Ready onayları ve maç kimliğiyle başlar.

## Battle HUD ve sonraki mekanikler

Level_01 → UI → Battle Canvas altında [BattleHud prefabı](../../Assets/UI/Components/BattleHud.prefab) bulunur. Menüdeki wireframe dünya, sonuç ve dükkân önizlemeleri savaş sahnesine taşınmadı. BattleFieldHud yerel takımın beş slotunu mevcut BattleHudState’e çevirir. Ekonomi, gemi canı ve üretim değerleri için ekranda **—** görünür. Dükkân kapalıdır. Oynanabilir sahnede Victory/Defeat test düğmeleri bulunmaz.

Yeni mekanik aktif context’i BattleLaunchService üzerinden okuyabilir. `IsActive` yüklenmiş ve başlatılmış maçı, `IsAuthority` ise yerel offline simülasyon veya online server yetkisini belirtir. Yeni komutların sahibini ve geçerliliğini otorite denetlemeli, kabul edilen sonuçları online oyunculara iletmelidir. Sunum dışarıdan `BattleHud.Present(state)` ile beslenir. Kullanıcı eylemleri `Requested` olayından alınır. UI tarafında kalıcı ödül, AI, hasar, ekonomi veya arazi simülasyonu çalışmaz.

## Dosyalar

| Alan | Oluşturulan veya değiştirilen dosyalar |
|---|---|
| Ortak oturum | [BattleLaunchContext](../../Assets/Gameplay/Scripts/Core/BattleLaunchContext.cs), [BattleLaunchService](../../Assets/Gameplay/Scripts/Core/BattleLaunchService.cs), [BattleSessionBootstrap](../../Assets/Gameplay/Scripts/Core/BattleSessionBootstrap.cs), [Gameplay assembly](../../Assets/Gameplay/Scripts/PirateGame.Gameplay.asmdef) |
| Online | [BattleRoom](../../Assets/Gameplay/Scripts/Networking/BattleRoom.cs), [ZeppelinNetworkInput](../../Assets/Gameplay/Scripts/Networking/ZeppelinNetworkInput.cs) |
| Sunum | [BattleFieldHud](../../Assets/Gameplay/Scripts/Core/BattleFieldHud.cs), [ScreenRouter](../../Assets/UI/Runtime/ScreenRouter.cs), [LobbyPresenter](../../Assets/UI/Runtime/LobbyPresenter.cs), [MenuCommand](../../Assets/UI/Runtime/MenuCommand.cs), [UI assembly](../../Assets/UI/Runtime/PirateGame.UI.asmdef), [EditMode assembly](../../Assets/UI/Tests/EditMode/PirateGame.UI.EditMode.Tests.asmdef), [PlayMode assembly](../../Assets/UI/Tests/PlayMode/PirateGame.UI.PlayMode.Tests.asmdef) |
| Sahneler | [MainMenu](../../Assets/Scenes/MainMenu.unity), [Level_01](../../Assets/Gameplay/Scenes/Level_01.unity). Mevcut dünya nesneleri, kamera ayarları ve yerleşim korunur |
| Ekranlar | Assets/UI/Screens altındaki 05, 06, 07, 08, 10, 11 prefablarının yönlendirme metinleri ve lobi Start Match komutu |
| Build | [Windows_Gameplay](../../Assets/Settings/Build%20Profiles/Windows_Gameplay.asset), [EditorBuildSettings](../../ProjectSettings/EditorBuildSettings.asset). Projede teknik profil dosyasının adı Windows.asset ve hedefi NetworkZeppelinTest sahnesidir |
| Testler | [BattleConfigurationTests](../../Assets/UI/Tests/EditMode/BattleConfigurationTests.cs), [NavigationTests](../../Assets/UI/Tests/PlayMode/NavigationTests.cs), [TechnicalZeppelinTests](../../Assets/UI/Tests/PlayMode/TechnicalZeppelinTests.cs) |

## Editor ve Windows kullanımı

1. Unity 6000.3.26f1 ile MainMenu sahnesini açıp Play’e basın. Girişte yeni bir NetworkFoundation eklemek gerekmez.
2. File → Build Profiles altında Windows_Gameplay seçin. Profilin sahne sırası MainMenu (0), Level_01 (1). Windows x64 ve Development Build seçilidir. Teknik test için NetworkZeppelinTest’i hedefleyen eski profili kullanın.
3. Hazır [Windows build klasörünü](../../Builds/WindowsGameplay_M221C/) iki makineye bütünüyle kopyalayın. steam_appid.txt yanındaki geliştirme değeri 480’dir. Yayın sürümü kendi Steam App ID’sini gerektirir.
4. Editor'de tam regresyonu tekrarlarken ortak sahne listesini kullanan Windows platform profilini etkinleştirin. Test Runner’dan önce EditMode, ardından PlayMode paketlerinin tamamını çalıştırın. Eski regresyonlar NetworkTest ve SampleScene'i açar. Steam gerektiren testler için istemciyi açıp giriş yapın. Dağıtım build’i için tekrar Windows_Gameplay profilini kullanın.

## Gerçek çalışan ve doğrulanan özellikler

- Campaign diyaloğunun sonunda Level_01 yüklenir. Battle vs AI zorluk seçimini taşır. Local PvP’de farklı kart elleri korunur ve WASD mavi, yön tuşları kırmızı zeplini bağımsız hareket ettirir. Steam kapalıyken Campaign sahnesi de açılır.
- MainMenu yüklü kalır. Savaşta menü girdileri kapanır, savaş kamerası, AudioListener ve EventSystem tekildir. Offline sahne boşaltıldıktan sonra context temizlenir ve aynı menüden ikinci bir savaş başlatılabilir. BattleHud prefabı gerçek Level_01 içindedir.
- Tek hesapla Steam lobby oluşturma, kimlik doğrulama, host içi ağ mesajı, ayrılma ve yeniden lobby kurma çalıştı. Host Ready isteği sunucuya ulaşıp geri döndü. Geçersiz el reddedildi, geçerli el değişince Ready kaldırıldı. Tek oyuncuyla ardışık iki başlatma isteği maçı açmadı.
- Tek bağlantılı FishNet entegrasyon testinde gerçek host bağlantısı ve açık lobby kullanıldı. Testin verdiği maç context’iyle Level_01 üyeliği kuruldu, NetworkFoundation aynı kaldı, offline zeplinler kapandı ve eksik oyuncuyla gameplay zeplini oluşmadı. Teknik NetworkZeppelinTest sahnesindeki ayrı test bir adet host sahipli mavi ağ zeplininin oluştuğunu doğruladı.
- Derleyici gameplay, UI, ağ ve sunum assembly bağımlılıklarını hem Editor hem Windows Player için çözümledi. Gameplay, HUD sözleşmesi için PirateGame.UI.Presentation’a başvurur. Menü tarafındaki PirateGame.UI, gameplay oturumunu kullanır.

## Placeholder sistemleri

Kırmızı AI zeplini bekler. Zorluk verisi taşınır, AI davranışı sonraki çalışmaya aittir. Arazi, gemi ve zeplin görselleri mevcut savaş alanı yer tutucularıdır. HUD’daki ekonomi ve gemi canları veri kaynağı bekler. Kartların seçimi çalışır. Satın alma, yerleştirme, hasar, galibiyet, kalıcı Campaign ilerlemesi ve kart ödülleri henüz uygulanmadı.

## Bilinen sınırlamalar

- Yalnızca First Voyage (Level_01) mevcut. Farklı iki geçerli harita arasında geçiş ve Ready sıfırlaması bu içerikle oynanarak sınanamaz.
- Online maçtan ayrılmak lobby’yi kapatır. İkinci online maç için yeni lobby kurulur. Host migration bulunmuyor.
- Tek hostlu sahne üyeliği testi ikinci koltuk için örnek bir kimlik taşır. Uzak istemci, ikinci observer, kırmızı gameplay spawn’ı ve başarılı iki kişilik Start Match için kanıt aşağıdaki fiziksel kabulden gelmelidir.

## Açık kalan testler

İki ayrı Steam hesabıyla davetin karşı uçta kabulü, ID ile gerçek katılım, iki ekranda Ready ve kart tutarlılığı, konukta Start Match engeli, iki üyenin sahneye girişi, iki renk ve sahiplik, eş zamanlı hareket, online Pause, yüklemede bağlantı kaybı ve ikinci online maç açık. Önceki teknik build için kullanıcının doğruladığı 12 test kendi sürümüne aittir.

## İki bilgisayarlı kabul prosedürü

Kullanıcı önceki NetworkZeppelinTest build’inin 12 testini iki bilgisayarda doğruladı. Aşağıdaki liste yeni MainMenu → Level_01 entegrasyonuna aittir ve iki fiziksel hesapla kabul bekler.

1. Aynı gameplay build’ini iki PC’de iki arkadaş Steam hesabıyla başlatın. A lobi kursun. A tek başına Ready iken Start Match kapalı kalmalı.
2. B önce Overlay davetiyle katılsın. İki oyuncu adını, host/guest rollerini ve FishNet bağlantısını kontrol edin. B ayrılsın, ID ile yeniden katılsın. A eski Ready onayını taşımamalı.
3. Farklı kart elleri hazırlayın. Balloon ilk slottan çıkmamalı. Boş slot kabul edilmeli. A Ready olduktan sonra B kendi elini düzenlesin. A Ready kalmalı. B Ready/Edit yaptığında A tarafında karşı oyuncunun durumu değişmeli.
4. Harita ekranını A açabilmeli. B iki uçta da First Voyage görmeli. İki Ready tamamlandığında yalnız A’da Start Match etkin olmalı. İkinci bir harita olmadığı için haritalar arası geçiş manuel olarak sınanamaz.
5. A hızlıca iki kez Start Match’e bassın. Her iki PC’de aynı Level_01 açılmalı, yalnız bir mavi ve bir kırmızı zeplin görünmeli. Menü görünmemeli. Host ve konuk eş zamanlı WASD kullanarak yalnız kendi zeplinini hareket ettirebilmeli. Karşı ekran ve hareket sınırları kontrol edilmeli.
6. B Pause açsın. A hareket etmeye devam edebilmeli. Resume sonrası B kontrolü geri almalı. HUD’da test sonucu düğmeleri veya satın alma paneli bulunmamalı.
7. B Leave Match ile ayrılsın. A ve B multiplayer menüsüne dönebilmeli. Yeni lobi açıp ikinci maç başlatın. Önceki oyuncular, Ready değerleri ve zeplinler taşınmamalı.
8. Yeni bir maçta A oyunu kapatsın. B savaş sahnesinden çıkmalı. Ayrıca önce B’yi, sonra A’yı sahne yüklenirken kapatarak ayrı ayrı deneyin. Kalan taraf zaman aşımından sonra yeni lobi açabilmeli. Steam oturum kaybını da ayrı bir denemede kontrol edin.
9. Son olarak A ve B’nin rollerini değiştirin. İki uçta Ready ve savaş alanı görüntüsü alın. Her denemede host, konuk, beklenen sonuç ve gözlemi kaydedin. Bir sonraki açılıştan önce iki Player.log dosyasını ayrı adlarla saklayın. İki PC’deki PirateGame.Gameplay.dll SHA-256 değerini build kaydıyla karşılaştırın.

## Game View kayıtları

1920×1080 Unity Game View kayıtları. Lobi görüntüsü tek gerçek host hesabıyla alınmıştır.

[Ana menü](GameView/01_MainMenu.png) · [L1 hazırlığı](GameView/02_Preparation.png) · [Diyalog](GameView/03_Dialogue.png) · [Level_01 ve Battle HUD](GameView/04_Level01.png) · [Pause](GameView/05_Pause.png) · [Steam host Ready](GameView/06_HostReady.png)

| Mod | Hazırlık | Savaş |
|---|---|---|
| Campaign | [Level 1 hazırlığı](GameView/02_Preparation.png) · [Diyalog](GameView/03_Dialogue.png) | [Level_01](GameView/04_Level01.png) |
| Battle vs AI | [Zorluk ve kartlar](GameView/07_BattleVsAI_Preparation.png) | [Level_01](GameView/07_BattleVsAI_Level01.png) |
| Local PvP | [İki oyuncunun kartları](GameView/08_LocalPvP_Preparation.png) | [Level_01](GameView/08_LocalPvP_Level01.png) |
| Online PvP | [Gerçek Steam hostu Ready, konuk bekleniyor](GameView/06_HostReady.png) | İki oyunculu savaş görüntüsü manuel kabulde alınacak |
