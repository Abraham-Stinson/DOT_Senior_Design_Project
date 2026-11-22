# FishySteamworks 4.1.1 + M0-A düzeltmeleri

Kaynak: https://github.com/FirstGearGames/FishySteamworks/tree/21e858249249e2c322365fe9fefbe865f290b0d9

Tag `4.1.1`, BSD-2-Clause. Kaynak alt klasördeki package.json `4.1.1` bildirir;
depo kökündeki package.json yanlışlıkla `4.1.0` bildirir. Bu klasör bir Assets
kurulumudur; UPM tarafından yönetilmez. Ek SteamManager örneği alınmadı.

Kurulum öncesi kaynak incelemesinde saptanan sorunlar nedeniyle ham sürüm yerine
aşağıdaki yerel düzeltmeler uygulanmıştır. Bu bir upstream resmî yeni sürümü değildir.

- Eksik assembly tanımı: FishNet.Runtime ve Steamworks.NET açık referansları;
  yalnızca Editor ve masaüstü platformları.
- Client timeout: 8000 saniyelik/karışık saatli worker ve Thread.Abort yerine
  ana thread üzerinde 20 saniyelik unscaled-time kontrolü. Native Steam ve Unity
  olayları artık timeout worker'ından çağrılmaz; IL2CPP'de Thread.Abort gerekmez.
- Native callback'ler kendi connection/listen-socket handle'larına filtrelenir;
  başarısız bağlantılarda callback'ler de dispose edilir.
- Server stop: listen socket yanında kabul edilmiş tüm peer socket'leri ve
  bağlantı tabloları/yerel paket kuyruğu temizlenir. Valve'a göre listen socket'i
  kapatmak kabul edilmiş bağlantıları kapatmaz.
- GC finalizer'ında Steam/Unity çağrısı kaldırıldı; kapatma ana thread'de
  session lifecycle ve OnDestroy üzerinden yapılır.
- Host client state sorgusu yerel host socket'ini de kapsar.
- Host-local server kaydı client Started olayından önce hazırlanır; güncel
  FishNet'in olay sırasında gönderdiği version handshake paketi kaybolmaz.
- Kanal baytı için büyütülen gönderim dizisi gerçekten kullanılır; geçersiz veya
  buffer'dan büyük gelen paketler bırakılır ve native mesaj serbest bırakılır.
- Server'ın tükettiği host-local paketleri ByteArrayPool'a iade edilir.

Kaynak API: ISteamNetworkingSockets (CreateListenSocketP2P / ConnectP2P),
ISteamNetworkingUtils. Deprecated ISteamNetworking kullanılmaz.

Alternatif: farklı bir güncel Steam transport'una geçmek veya upstream düzeltmesi
beklemek. Bu görevde istenen FishNet + Steamworks.NET + FishySteamworks korunmuştur.
Gerçek iki hesap testi yapılmadan peer bağlantılarını doğrulanmış saymayın.
