# M1 — UI & Navigation / Revision 02

**Unity 6000.3.26f1 · uGUI + TextMeshPro · Untitled Pirate Game**

Bu belge, önceki M1 tasarım ve istem paketinin yerine geçer. Aynı klasördeki 18 PNG/SVG, kullanıcı tarafından gönderilen Cannon Brawl ekranlarının yerleşim prensiplerine göre yeniden oluşturulmuş düşük detaylı referanslardır. Bunlar nihai sanat veya doğrudan kullanılacak ekran görselleri değildir. Oyunun kendi teması korsan/deniz; kendi varlıklarını ileride üreteceğiz. Referans oyunun logo ve sprite'larını kopyalama.

## 1. Kapsam ve teknik kurallar
- M0 ağ altyapısı çalışıyor: FishNet 4.7.4R, Steamworks.NET 2025.165.0, yerel yamalı FishySteamworks 4.1.1. İki ayrı Steam hesabıyla uzak client mesajları alındı. NetworkTest ve NetworkFoundation prefab'ını, transport yamalarını, paket sürümlerini, URP ve 3D template ayarlarını koru. App ID 480 geliştirme içindir. Önce gerçek API ve dosyaları incele.
- Yalnızca UI ve navigasyon. Yeni gameplay, AI, arazi, ekonomi, kayıt/ödül sistemi, art ve animasyon geliştirme. Var olan işlevleri bozma. Git commit/push yapma.
- uGUI Canvas, RectTransform, Image, Button, Slider, LayoutGroup, ScrollRect ve TextMeshProUGUI. Yeni oyun ekranlarında IMGUI/OnGUI/UI Toolkit yok. Mevcut teknik NetworkTest IMGUI ekranını koru.
- Canvas Screen Space Overlay; CanvasScaler Scale With Screen Size, 1920×1080, Match 0.5. Anchor'lar ile 1280×720 ve 1920×1080 doğrula. Tek aktif EventSystem; projedeki input backend ile uyumlu UI input module. Fare ve klavye navigasyonu; Esc geri, Space bağlama göre onay. Gamepad desteği varsa title ve UI girişlerini bağla, yoksa gerçek destek varmış gibi sunma. Input field odaktayken oyun kısayolları tetiklenmesin.
- Her UI nesnesi Inspector/Hierarchy üzerinden düzenlenebilir olmalı; ekran PNG'sini bir bütün olarak Canvas'a koyma. Editör üretim aracı kullanırsan güvenli, tekrar çalıştırılabilir ve kullanıcı düzenlemelerini ezmeyen bir araç olsun.
- Ortak ScreenRouter ve panel prefab'ları önerilir; MainMenu sahnesi title + menü akışını, ayrı BattleUI preview sahnesi HUD/etkileşim/diyalog/sonuç/pause akışlarını temsil edebilir. Gerçek gameplay sahnesi henüz yok. NetworkFoundation tek örnek, Steam yaşam döngüsünü bozmadan adapter ile bağla. Sahne ve klasör adları mevcut projeye göre uyarlanabilir. Örnek veriler ayrı presenter/model'den gelsin.
- Kullanıcının kesin çıkardığı özellikler: **Public Lobby yok, görev/quest bayrakları yok, Armory yok.** Ana menüde veya alt menülerde bunları ekleme. Gemi/zeplin/balon kozmetiği ileriki milestone; burada yeni özelleştirme ekranı yapma. Oyunun görünen geçici adı Untitled Pirate Game; DOT veya Gemi Savaşı yazma. Arayüz metinleri İngilizce.

## 2. Ekranlar ve ekranlar arası kurallar
| Referans | Ekran ve davranış |
|---|---|
| 01 TitleScreen | Ortada büyük geçici oyun başlığı, altta Press Any Key or Button to Start. Bir kez basıldığında MainMenu. |
| 02 MainMenu | Büyük çevresel arka planın önünde ortalanmış 3 ana buton: Campaign, Skirmish, Multiplayer. Alt sırada Settings, Credits, Quit. Quit onay modalı. Sol Public Lobby, sağ görevler, Armory yok. |
| 03 CampaignMap | Tam ekran deniz rotası; yön tuşları ile gemi işaretçisi izinli düğümler arasında hareket eder; seçili bölümün önizleme kartı alt ortada harita, bölüm adı, 3 yıldız ve Fight. Ayrı Level Preview sayfası yok. Başlangıçta L1 açık; L2 L1 galibiyetinden sonra. L3–10 ileride, kilitli/geliştirilmemiş. Örnek progress bar planlanan 30 yıldızı gösterir, final sayı henüz kesin değil. Fight → Campaign preparation → kısa diyalog → BattleUI preview. |
| 04 Skirmish | Local PvP / Battle vs AI seçimi, sonra Map Selection. |
| 05 MapSelection | Ortada seçilen arenanın büyük önizlemesi, önceki/sonraki okları, adı ve Continue. İlk arena örnek olarak mevcut; yapılmamış haritalar seçilemez. |
| 06 LocalPvP_Preparation | Seçilen arena arka planda; solda P1 kart paneli, sağda P2 input/join paneli. P2 katıldıktan sonra sağ panel de aynı 5 slotlu loadout bileşenine dönüşür. İki oyuncu seçimleri ayrı. Yerel ikinci oyuncu input cihazı ve ilerleme kaynağı henüz kesinleşmedi; cihaz zorunluluğu icat etme. Mevcut sistemle desteklenmeyen join işlemi preview olarak belirtilsin. |
| 07 AI_Preparation | Aynı sol loadout paneli; sağda AI difficulty (Easy/Normal/Hard) seçimi ve Fight. AI henüz yok; bu sadece hazırlık UI. |
| 08 Campaign_Preparation | Aynı sol loadout paneli; sağda bölüm hedefleri ve önizleme, Fight. |
| 09 Multiplayer | Gerçek Steam durumu, Create Friends Lobby, mevcut destek varsa Join Lobby ID, daveti Steam Overlay'den kabul etme açıklaması. Public Lobby veya matchmaking ekleme. |
| 10 SteamLobby | Sol kendi loadout ve Ready/Edit durumları; sağ rakip paneli, Steam adı, bağlantı ve hazırlık durumu. Rakip yokken Waiting for friend durumu ve Invite Friend aktif. Mevcut lobi işlevlerinden Create, Invite, Join, Copy ID, Leave ve hata durumlarını bağla. Host map selection ile 05'e gidebilsin. Host/guest rolleri, iki oyuncu ready olmadan Start Match etkin değil. Mevcut Ready/Start protokolü yoksa açıkça preview/disabled; gerçek maç başlatma yok. |
| 11 PreBattleDialogue | Yalnız Campaign. Savaş alanı dim overlay altında; solda Prince, sağda Pirate Captain placeholder portreleri, aktif konuşmacıya yakın alt konuşma kutusu. Space / click ile ilerle. |
| 12 Battle_HUD | Sol üst küçük altın ve üretim. HP barları gemilerin üzerinde; etkileşim ipuçları yapının yanında. Campaign 3 hedef sağ altta. Sürekli kart barı yok. |
| 13 Battle_ShipShop | Zeplin gemiyle etkileşime geçtiğinde gemiye yakın açılan 5 kartlık kompakt panel, fiyat, seçili kart, satın alma ve çıkış tuşları; blueprint placeholder'ları. Bu ekran UI state preview'dur; gerçek satın alma, ekonomi ve inşa yok. |
| 14 Pause | Resume, Settings, Leave Match. Online maç global durdurulmaz; menü yalnız yerel overlay. |
| 15 Victory | Campaign örneğinde yıldızlar ve yalnızca uygun ilk galibiyette Shield Tower unlock görünümü; Continue/Replay. Skirmish ve online'da yıldız/kart ödülü yok. |
| 16 Defeat | Retry (uygun offline modlar), Back to Map veya ilgili menü/lobi. Online host ayrılırsa maç/lobi durumunu gerçek bağlantı olayına göre göster. |
| 17 Settings | Audio, Display, Controls; yalnız uygulanabilir seçenekleri bağla, diğerlerini geçici/disabled göster. |
| 18 Credits | Düzenlenebilir katkı alanları; gerçek isimler henüz verilmedi. |

## 3. Kart ve ekran durumları
- 5 slot, ilk Territory Balloon sabit ve çıkarılamaz. Başlangıçta Mining Camp ve Cannon Tower da açık. Boş slotlara izin ver; bir kart loadout içinde tekrarlanmaz, savaşta birden fazla yapı kurulabilir. Kart kategorisi yok. Kilitler: Shield 2, Repair 4, Missile 6, Laser 8; henüz 3–10 geliştirilmedi. Her oyuncu kendi campaign unlock durumuna göre seçim yapar. Yerel ikinci profil ilerleme kaynağı henüz kararlaştırılmadı. Test örneği kayıt dosyasına yazılmasın. AI kartları oyuncunun açtığı kartlarla sınırlanmak zorunda değil (Campaign ve Skirmish için ileride senaryo tanımlarına bağlanacak).
- Kartların fiyat/can/cooldown değerlerini tek veri kaynağından göster; kesin olmayan değerleri wireframe'den kod içine sabitleme. Kart detay kutusu, selected ve choices ayrı; hover/select ile detay güncellenir. Kilitli kartta açılma şartı gösterilir. Panelin ortasındaki Ready modalı yalnız uygun durumda açılır, geri ile düzenlemeye dönülür. Z basılı iken hazırlık panelleri geçici gizlenir ve map görünür; bırakınca geri gelir. Klavye ve fareyle ulaşılabilir alternatif de düşün.
- Referans 06 ve 10, çok durumlu ekranların örnek halleridir; ayrı ekran üretmek yerine aynı panelin state'lerini kullan. Online tarafta oyuncu adı ve bağlantı durumları gerçek; Ready ve maç başlatma protokolü yoksa açıkça eksik. Match preview asla gerçek sonuç veya gerçek unlock üretmez. Mock UI yalnız belirgin Preview/Test etiketiyle erişilebilir. Harita seçimi online host'a özel; iki tarafın map bilgisi senkronizasyonu yoksa mock olarak raporla.

## 4. Test ve teslim
- Tüm ekran geçişleri, Esc/Back, title giriş, Quit onayı, panel state'leri, 5 slot ve kilit kontrolü, hold-to-show-map davranışı, HUD shop toggle, modal davranışı ve çözünürlük kontrolleri.
- Gerçek Steam Create/Invite/Join/Leave ve hata/bağlantı durumlarının UI üzerinden erişimini test et. NetworkTest regresyonu ve mevcut testler. İki hesaplı UI testi yapılmadıysa bunu bildir. Offline Steam yokken diğer ekranlar çalışmalı.
- Windows x64 build mümkünse oluştur. Console ve Game View ekran görüntülerini 1280×720 ve 1920×1080 kontrol et. Görsel yerleşim referanslarını kontrol et; gerçek art ekleme.
- Türkçe rapor: dosyalar, sahneler, gerçek/mock işlevler, mevcut Steam API bağlantıları, geçen/geçmeyen testler, UI düzenleme yolu, riskler. M1 bitince dur, M2 gameplay'e geçme.
