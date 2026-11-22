# M0-A — Steam Network Foundation

Bu teslimat yalnızca ağ altyapısıdır. Gameplay, modlar, sanat ve nihai UI içermez.
Proje klasörü, mevcut SampleScene ve render pipeline korunmuştur.
Unity: **6000.3.26f1**, revision `de8167e7440c`. Geçici ürün adı **Untitled Pirate Game**.

## Bağımlılıklar ve uyumluluk kararı

| Paket | Kurulan sürüm | Konum |
| --- | --- | --- |
| FishNet | 4.7.4R, package.json 4.7.4 | Packages/com.firstgeargames.fishnet |
| Steamworks.NET | 2025.165.0 / Steamworks SDK 1.65 | Packages/com.rlabrecque.steamworks.net |
| FishySteamworks | 4.1.1 + belgelenmiş M0-A yerel düzeltmeleri | Assets/ThirdParty/FishySteamworks |
| Newtonsoft JSON | 3.2.2, FishNet dolaylı bağımlılığı (alt sınır 3.2.1) | Unity Package Manager |

Sürümler, kaynak commit'leri ve arşiv SHA-256 değerleri `dependencies.json` içindedir.
Mevcut UGUI 2.0.0, Input System 1.20.0, URP 17.3.0 ve diğer paketler korunur.
FishNet'in UGUI 1.0.0 alt sınırı mevcut 2.0.0'ı düşürmez. Unity Multiplayer Center
mevcut bir Editor aracıdır; bu altyapı Unity Relay/Lobby/Netcode veya dedicated server gerektirmez.

FishNet resmî uyumluluk tablosu Unity 6 LTS'yi destekler. Steamworks.NET minimum
Unity sürümü 2019.4, FishySteamworks paket alt sınırı 2021.3'tür. FishySteamworks
tam `6000.3.26f1` için üretici sertifikası sunmaz; bu projedeki API/derleme ve
çalışma testleri ile gerçek iki cihaz testini ayrı değerlendirmek gerekir.

FishNet **4.7.4 Git etiketi derlenmiyor**: Yak Pro kodunun kısmen ayıklanması
[resmî #1110 hata kaydında](https://github.com/FirstGearGames/FishNet/issues/1110)
belgelenmiştir. Bu kaynak kurulumu değiştirilmiştir. Kurulan FishNet dosyaları aynı
sürümün **resmî 4.7.4R.unitypackage release asset** içeriğidir; Yak paketlemesi
release asset'te doğru ayıklanmıştır. FishNet kaynak koduna yerel yama eklenmedi.

Editor ortamında Git PATH'te bulunmadığı için iki ana paket embedded UPM olarak
depolanır. Git URL'si ya da hareketli main dalı kullanılmaz. Steam native DLL'leri
Steamworks.NET'in kendi SDK sürümüyle eşleşen dosyalardır; başka SDK'dan DLL kopyalamayın.
FishySteamworks'ün depo kökündeki package.json 4.1.0, gerçek kaynak alt klasöründeki
4.1.1'dir. Assembly tanımı ve lifecycle/timeout/buffer düzeltmeleri
`Assets/ThirdParty/FishySteamworks/LOCAL-PATCHES.md` içinde tek tek açıklanmıştır.
Ham 4.1.1 yerine bu belgelenmiş yerel varyant kuruldu; upstream yeni sürümü değildir.

## Kullanılan Steam Networking API

Transport **ISteamNetworkingSockets** kullanır: `CreateListenSocketP2P`, `ConnectP2P`,
mesaj gönderme/alma ve `CloseConnection`. Relay erişimi **ISteamNetworkingUtils**
ile hazırlanır. İletişim Steam kimliğiyle P2P çalışır; Steam ağının seçtiği doğrudan
veya relay yolu kullanılabilir. Ayrı sunucu veya port yönlendirme kurulumu yapılmaz.
Deprecated **ISteamNetworking / SendP2PPacket / AcceptP2PSessionWithUser** kullanılmaz.
Steamworks.NET paketinde eski API wrapper tanımlarının bulunması, oyunun bunları çağırdığı anlamına gelmez.

## Mimari ve NetworkManager

- `Core/SessionContracts.cs`: Steam/FishNet referansı olmayan `INetworkSession`,
  roller, durumlar, lobby üye verisi ve kabul/protokol kuralları.
- `SteamRuntime`: API InitEx, oturum ve App ID kontrolü, her frame RunCallbacks,
  anlaşılır hata ve sıralı Shutdown. Tek API sahibi vardır.
- `SteamLobbyService`: friends-only lobby, en çok iki üye, owner/members,
  Create/Join CallResult'ları, davet/membership/data/persona callback'leri.
  Geç gelen iptal edilmiş başarılı işlemler lobby'den ayrılarak temizlenir.
- `FishNetSession`: Steam API çağırmadan server/client bağlantıları ve olayları.
- `LobbyConnectionAuthenticator`: istemcinin gönderdiği kimliğe güvenmez; transport'un
  Steam kimliğini owner, üyelik, tekrar kimlik ve kapasite kurallarıyla doğrular.
  Lobby'den ayrılan bağlantıların ağ erişimi de periyodik olarak iptal edilir.
- `OnlineSessionCoordinator`: lobby owner ile gerçek host'u eşler; host_ready
  yayımlanmadan client bağlanmaz; host hem server hem local client açar.
- `NetworkProbe`: reliable client isteği → server tarafından numaralandırılmış ACK.
  Mesaj sıklığı server tarafında sınırlandırılır. Bu bir gameplay işlemi değildir.
- `NetworkTestPanel`: geçici IMGUI test paneli; final UI değildir.

`Assets/Networking/Prefabs/NetworkFoundation.prefab` ayrı NetworkTest sahnesinde
kullanılır. Tek NetworkManager, tek FishySteamworks transport, P2P açık, virtual
port **0**, tick rate **30 Hz**. Headless otomatik başlatma kapalıdır.
Transport kapasitesi **1 remote Steam client + host'un local client'ı = 2 oyuncu**;
Steam lobby limiti ayrıca 2'dir. FishySteamworks host-local socket'i remote limitine
dahil etmediği için transport alanını 2 yapmak üçüncü oyuncuya yer açardı.
NetworkManager'a ayrıca lobby authenticator ve boş, açıkça atanmış
`NetworkTestPrefabs.asset` verilir; test mesajı NetworkObject gerektirmez.
Teknik sahne kapanınca servisler de kapanır (`DontDestroyOnLoad` kapalı).

Lobby verileri: `game`, `protocol`, `transport`, `host_steam_id`, `host_ready`.
480 ortak bir test uygulaması olduğundan başka oyunların lobby'leri bu anahtarlarla
reddedilir. Metadata owner kimliğiyle karşılaştırılır. Lobby'ye girmek ve FishNet
istemcisinin doğrulanması iki ayrı aşamadır.

Host ayrılırsa Steam owner'ı otomatik değiştirse bile oturum sonlandırılır.
**Host migration yoktur.** Client yeni owner olduğu için yanlışlıkla server açmaz.
Steam kapanması, bağlantı reddi veya timeout görünür hata üretir ve lobby temizlenir.
Kapatma sırası: host hazır/joinable bilgisini kapat → client/server/peer socket'leri
kapat → lobby'den ayrıl → callback/CallResult dispose → SteamAPI.Shutdown.

Gelecekte hareket, altın, harcama, yapı, mermi/hasar, yıkılabilir zemin ve sonuçlar
server tarafından doğrulanmalıdır. Client yalnızca işlem isteği gönderir; sahiplik,
kaynak, sınır ve sonuç server tarafında belirlenir. Bu işlemler ve Campaign,
Skirmish, Local PvP/Online modları bu milestone'da uygulanmamıştır. Ortak gameplay
assembly'leri ileride Core sözleşmelerine dayanabilir; Steam adaptörünü çağırmamalıdır.

## Steam App ID

Tek kaynak `Assets/Networking/Configuration/SteamNetworkSettings.asset` içindeki
**App Id** alanıdır. Şu an **480 / Spacewar**, yalnızca geliştirme içindir.
Editor Play öncesi ve development build sonrasında `steam_appid.txt` bu ayardan
üretilir. Root dosyası üretilmiş geliştirme kopyasıdır; elle ayrı ID yönetmeyin.
Geliştirme süreç ortamı da aynı ayardan hazırlanır; gerçek API App ID'si kontrol edilir.
480 ile development olmayan build engellenir. Gerçek App ID'ye geçince bu asset'i
değiştirin, Sync Development Steam App ID menüsünü çalıştırıp yeniden build alın.
Steam'e gönderilecek release içinde `steam_appid.txt` dağıtmayın; production
başlatmasında `RestartAppIfNecessary` desteği hazırdır.

## Unity Editor'de kullanım

1. Mevcut projeyi Unity **6000.3.26f1** ile açın; paket import/derlemesinin bitmesini bekleyin.
2. Steam'i aynı Windows kullanıcısıyla açıp hesabınıza çevrimiçi giriş yapın.
3. `Assets/Scenes/NetworkTest.unity` sahnesini açın. Mevcut sahnenizde kaydedilmemiş
   değişiklik varsa önce kendiniz kaydedin. Play'e basın.
4. Teknik panelde Steam kullanıcı adı ve oturum durumunu kontrol edin. Steam
   hazır değilse hata metnini izleyip Retry Steam'e basın.
5. `Tools > Networking > M0-A > Validate NetworkTest` yapılandırmayı kontrol eder.
   Ensure NetworkTest Assets mevcut sahneyi veya prefab'ı yeniden yazmaz.
6. Windows test build'i için `Tools > Networking > M0-A > Build Windows NetworkTest`.
   Çıktı: `Builds/WindowsNetworkTest/Untitled Pirate Game.exe` (Windows x64,
   development, yalnızca NetworkTest). Normal build listesinde SampleScene korunur;
   test sahnesi listeye eklenir. Bu özel build komutu yalnızca test sahnesini açar.
7. Test Runner'da `PirateGame.Networking.EditMode.Tests` ve
   `PirateGame.Networking.PlayMode.Tests` çalıştırılabilir. Steam yoksa native
   host smoke testi **Skipped** olur; başarılı sayılmaz.

2D URP kurulumu ve renderer değişimi bu teslimatta yapılmadı; daha sonra manuel yapılacak.

## İki cihaz / iki Steam hesabı kabul testi

İki farklı bilgisayarda iki farklı, birbirini arkadaş eklemiş Steam hesabı kullanın.
İkisine **aynı Windows test build klasörünün tamamını** kopyalayın; yalnız exe yetmez.
Her ikisinde Steam açık, çevrimiçi ve Overlay etkin olmalı. Tek Steam hesabıyla iki
process açmak bu testin yerine geçmez. Steam App ID 480 nedeniyle Steam'deki ad Spacewar görünebilir.

1. A'da exe'yi açın; kullanıcı adının A olduğunu doğrulayın. Create Lobby'ye basın.
   1/2 üye, owner/host A, Server Started + Client Started + Authenticated görünmeli.
2. A'da Invite Friend ile Steam Overlay davet ekranını açın ve B'yi davet edin.
   B oyunu açıkken Steam'den daveti kabul etsin. Alternatif olarak A'nın Copy Lobby ID
   değerini B'nin Join Lobby alanına girin.
3. İki panelde de 2/2 üye ve aynı owner/host Steam ID'si görünmeli. B'de Role Client,
   Server Stopped, Client Started, Authenticated true olmalı.
4. Her iki tarafta Send Test Message kullanın. B'nin gönderilen ve ACK sayacı artsın;
   A'nın **Uzak client** sayacı artsın. Host'un kendi ACK'sı yalnızca yerel socket
   kontrolüdür. İki ekranı/Player.log dosyalarını kaydedin; başarı kanıtı bu iki yöndür.
5. Üçüncü bir hesapla giriş deneyin: lobby dolu yanıtı veya server kapasite/üyelik
   reddi görülmeli; üçüncü doğrulanmış oyuncu oluşmamalı.
6. B Leave Lobby yapsın: A lobby'de 1/2 kalsın. B yeniden davetle katılabilsin.
7. A Leave Lobby yapsın veya exe'yi kapatsın: B'de bağlantı hatası ve lobby'den
   ayrılma görülmeli; B otomatik yeni host olmamalı.
8. Her iki hesapla rolleri değiştirip tekrarlayın. Aynı anda Create/Join'a art arda
   basma, bağlantı sırasında Leave, yanlış lobby ID, başka 480 oyununun lobby'si,
   host hazır değilken katılma ve internet kesilmesi senaryolarını deneyin.
9. B oyun kapalıyken davet kabulünü ayrıca kontrol edin. Steam launch seçeneklerinde
   `+connect_lobby <id>` ile cold-start katılımı desteklenir. Gerçek App ID / Steam
   launch kurulumu tamamlanmadan 480 ile exe'ye doğru yönlendirme garanti değildir;
   480'in Spacewar uygulamasını açması kabul başarısı sayılmaz.
10. Steam kapalıyken exe'yi başlatın: anlaşılır hata gösterilmeli, ağ açılmamalı.
    Steam'i açıp giriş yaptıktan sonra Retry Steam ile düzelmeli. Oyun açıkken
    Steam oturumunu kapatma/internet kaybı senaryolarını ayrıca kontrol edin.

Steam Overlay Editor'de her ortamda çalışmayabilir; davet kabulü için standalone
build esas alınmalıdır. Bu otomatik görev gerçek iki ayrı hesabı/cihazı sağlamaz.

## Test sonuçları ve riskler

22 Kasım 2026, Unity 6000.3.26f1 ile mevcut projenin geçici doğrulama kopyasında:

| Kontrol | Sonuç |
| --- | --- |
| C# derleme ve NetworkTest asset/config doğrulaması | Başarılı |
| EditMode | 13/13 başarılı, 0 skipped |
| PlayMode | 2/2 başarılı, 0 skipped |
| Gerçek Steam API init, kullanıcı oturumu, lobby create | Bu makinedeki tek hesapla başarılı |
| Host server + local client authentication, reliable probe/ACK | Başarılı; iki create/leave döngüsü |
| Lobby/socket/callback kapatma, API shutdown ve tekrar init | Host-local yaşam döngüsünde başarılı |
| Windows x64 Development / Mono build | Başarılı, 0 hata, 144 uyarı, raporlanan boyut 171.965.528 bayt |
| İki hesap remote P2P/relay, Overlay daveti, host kaybı ve cold-start | Manuel kabul bekliyor |
| Steam kapalıyken gerçek standalone çalıştırma / Retry | Manuel kabul bekliyor |
| IL2CPP build, macOS/Linux build | Çalıştırılmadı |
| Açık Editor paneli ve standalone UI görsel kontrolü | Windows pencere etkinleştirme aracı timeout verdi; görsel kontrol yapılmadı |

Ham kanıtlar: `Logs/M0A-Setup-Verified.log`, `Logs/M0A-EditMode.xml`,
`Logs/M0A-EditMode.log`, `Logs/M0A-PlayMode.xml`, `Logs/M0A-PlayMode.log`,
`Logs/M0A-WindowsBuild.log`, `Logs/M0A-WindowsBuild.txt`.
Özet: `Documentation/Multiplayer/validation.json`. Logs ve Builds mevcut .gitignore
kurallarıyla Git dışında kalır. Build raporundaki geçici çıktı yolu doğrulama
kopyasına aittir; teslim edilen aynı build `Builds/WindowsNetworkTest` altındadır.
Exe SHA-256: `b7cca8b82003185f22644d0ee1c23b3640de1b996802d4f5d0501e287011afa6`.

144 build uyarısı FishNet'in Synapse/Tugboat/LOD/demo kaynaklarındandır
(CS8632 nullable context, CS0162 ve kullanılmayan alan/event uyarıları).
Üçüncü taraf kaynakları yalnız uyarıları gizlemek için değiştirilmedi.
PlayMode testlerinde beklenmeyen Unity hata logu oluşmadı. Derleme/testler pencere
etkinleştirme yardımcı aracı yerine batch mode ile yapıldı; açık Editor'de eski
import hataları görünürse Assets > Refresh ve Console Clear sonrası yeniden kontrol edin.
Doğrulanan 25 kod/assembly dosyası asıl projeyle byte düzeyinde eşleşir;
kurulu FishNet dosyaları resmî release asset ile byte düzeyinde eşleşir.

Gerçek iki hesaplı davet, remote Steam P2P/relay, NAT ve cold-start testleri manuel
kabul tamamlanana kadar **doğrulanmamış** kalır. Yerel smoke test bunların yerine geçmez.

FishySteamworks upstream sürümü eski olduğundan yerel yamalar sonraki güncellemede
yeniden değerlendirilmelidir. Paket güncellemelerini otomatik main'e çevirmeyin.
Steam 480 üretim, oyun sahipliği ve final mağaza/davet deneyimi için kullanılamaz.
Dedicated server, otomatik yeniden bağlanma, host migration, maç ve gameplay yoktur.

## Oluşturulan ve değiştirilen proje dosyaları

- `Assets/Scenes/NetworkTest.unity` ve meta dosyası.
- `Assets/Networking/Configuration/SteamNetworkSettings.asset`, `NetworkTestPrefabs.asset`.
- `Assets/Networking/Prefabs/NetworkFoundation.prefab`.
- `Assets/Networking/Core/SessionContracts.cs` ve Core assembly tanımı.
- `Assets/Networking/Runtime/SteamNetworkSettings.cs`, `SteamRuntime.cs`,
  `SteamLobbyService.cs`, `FishNetSession.cs`, `LobbyConnectionAuthenticator.cs`,
  `OnlineSessionCoordinator.cs`, `NetworkProbe.cs`, `NetworkTestPanel.cs` ve assembly tanımı.
- `Assets/Networking/Editor/NetworkFoundationEditor.cs` ve Editor assembly tanımı.
- `Assets/Networking/Tests/EditMode/NetworkFoundationTests.cs`,
  `Tests/PlayMode/NetworkLifecycleTests.cs` ve test assembly tanımları.
- `Assets/ThirdParty/FishySteamworks/`: transport kaynakları, lisans, upstream readme,
  assembly tanımı ve LOCAL-PATCHES.md; orijinal meta kimlikleri korunur.
- İki embedded paket, `Packages/manifest.json` ve `Packages/packages-lock.json`.
- Root `steam_appid.txt`: merkezî asset'ten üretilmiş geliştirme dosyası.
- `ProjectSettings/EditorBuildSettings.asset`: mevcut sahneler korunarak NetworkTest eklendi.
- `ProjectSettings/ProjectSettings.asset`: yalnız görünen productName değiştirildi.
- Bu rapor, `dependencies.json`, `validation.json`; üretilen Unity asset/folder meta dosyaları.

Başlangıçta bulunan `.vscode/settings.json` kullanıcı değişikliği korunmuştur.
GraphicsSettings, QualitySettings, mevcut renderer/pipeline asset'leri ve SampleScene
değiştirilmedi. Otomatik Git commit, push veya staging yapılmadı.

## Resmî kaynaklar

- [FishNet 4.7.4R release](https://github.com/FirstGearGames/FishNet/releases/tag/4.7.4)
- [FishNet Unity uyumluluğu](https://fish-networking.gitbook.io/docs/overview/readme/features/unity-compatibility)
- [FishNet Broadcasts](https://fish-networking.gitbook.io/docs/guides/features/network-communication/broadcasts)
- [FishySteamworks 4.1.1](https://github.com/FirstGearGames/FishySteamworks/releases/tag/4.1.1)
- [Steamworks.NET 2025.165.0](https://github.com/rlabrecque/Steamworks.NET/releases/tag/2025.165.0)
- [Steamworks.NET kurulum](https://steamworks.github.io/installation/)
- [Valve ISteamNetworkingSockets](https://partner.steamgames.com/doc/api/ISteamNetworkingSockets)
- [Valve deprecated ISteamNetworking](https://partner.steamgames.com/doc/api/ISteamNetworking)
- [Valve ISteamMatchmaking](https://partner.steamgames.com/doc/api/ISteamMatchmaking)
