using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game.Core;
using Game.Data;
using Game.Gameplay;
using Game.Persistence;
using UnityEngine;

namespace Game.UI
{
    public sealed class DraftLoadoutUI : MonoBehaviour
    {
        [SerializeField] private ArenaSessionBootstrap _bootstrap = null;
        [SerializeField] private CardCatalogSO _cardCatalogOverride = null;
        [SerializeField] private int _initialSoftCurrency = 300;

        private ProgressionService _progression;
        private CardCatalogSO _catalog;
        private readonly ushort[] _p1Deck = new ushort[DeckRules.DeckSize];
        private readonly ushort[] _p2Deck = new ushort[DeckRules.DeckSize];
        private readonly string[] _seatFeedback = { string.Empty, string.Empty };
        private string _armoryFeedback = string.Empty;
        private bool _disposed;
        private Vector2 _armoryScroll;
        private Vector2 _p1Scroll;
        private Vector2 _p2Scroll;

        private void OnEnable()
        {
            if (_bootstrap != null)
            {
                _bootstrap.DraftFeedbackReceived += HandleDraftFeedback;
            }
        }

        private void OnDisable()
        {
            if (_bootstrap != null)
            {
                _bootstrap.DraftFeedbackReceived -= HandleDraftFeedback;
            }
        }

        private void Start()
        {
            _catalog = _cardCatalogOverride != null
                ? _cardCatalogOverride
                : (_bootstrap != null ? _bootstrap.CardCatalog : null);

            if (_catalog == null)
            {
                _armoryFeedback = "CardCatalog atanmamış. Tools > Game > Setup MainMenu & CannonBrawlArena (M1-1) çalıştırın.";
                return;
            }

            var saveService = new JsonFileSaveService();
            _progression = new ProgressionService(saveService, _catalog, _initialSoftCurrency);
            _ = InitializeProgressionAsync();
        }

        private void OnDestroy()
        {
            _disposed = true;
        }

        private async Task InitializeProgressionAsync()
        {
            try
            {
                await _progression.LoadAsync();
                if (_disposed)
                {
                    return;
                }

                if (_bootstrap != null)
                {
                    _bootstrap.SetLocalUnlockedCardProvider(_progression);
                }

                ushort[] defaultDeck = _progression.GetLastValidLoadoutOrDefault();
                Array.Copy(defaultDeck, _p1Deck, DeckRules.DeckSize);
                Array.Copy(defaultDeck, _p2Deck, DeckRules.DeckSize);
            }
            catch (Exception ex)
            {
                _armoryFeedback = "Profil yüklenemedi: " + ex.Message;
            }
        }

        private async Task UnlockAndSaveAsync(ushort cardId)
        {
            if (_progression == null)
            {
                return;
            }

            try
            {
                CardUnlockResult result = await _progression.TryUnlockCardAsync(cardId);
                if (_disposed)
                {
                    return;
                }

                _armoryFeedback = result switch
                {
                    CardUnlockResult.Success => "Kart açıldı ve kaydedildi (Local PvP'de P1 ve P2 kullanabilir).",
                    CardUnlockResult.InsufficientCurrency => "Yetersiz bakiye.",
                    CardUnlockResult.AlreadyUnlocked => "Bu kart zaten açık.",
                    CardUnlockResult.NotUnlockable => "Bu kart mağazadan açılamaz.",
                    _ => "Kart bulunamadı."
                };
            }
            catch (Exception ex)
            {
                _armoryFeedback = "Kayıt hatası: " + ex.Message;
            }
        }

        private async Task SaveLastLoadoutAsync(ushort[] deck)
        {
            if (_progression == null || !_progression.TrySaveLastLoadout(deck))
            {
                return;
            }

            try
            {
                await _progression.SaveAsync();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[M1-2] Son deste kaydedilemedi: " + ex.Message);
            }
        }

        private void HandleDraftFeedback(
            byte slotId,
            bool accepted,
            DraftRejectReason rejectReason,
            LoadoutValidationError loadoutError,
            int failingSlotIndex)
        {
            if (slotId >= _seatFeedback.Length)
            {
                return;
            }

            if (accepted)
            {
                bool isReady = _bootstrap != null && _bootstrap.IsSeatDraftReady(slotId);
                _seatFeedback[slotId] = isReady ? "Deste doğrulandı — Hazır." : "Deste güncellendi (Hazır değil).";
                return;
            }

            _seatFeedback[slotId] = $"Reddedildi ({rejectReason} / {loadoutError}, slot {failingSlotIndex}).";
        }

        private void OnGUI()
        {
            if (_bootstrap == null || !_bootstrap.IsDraftAvailable || _catalog == null)
            {
                return;
            }

            if (_progression == null || !_progression.IsLoaded)
            {
                GUILayout.BeginArea(new Rect(16, 16, 420, 90), GUI.skin.box);
                GUILayout.Label("Profil ve Kart Kataloğu yükleniyor...");
                if (!string.IsNullOrEmpty(_armoryFeedback))
                {
                    GUILayout.Label(_armoryFeedback);
                }
                GUILayout.EndArea();
                return;
            }

            float panelWidth = Mathf.Clamp(Screen.width - 450f, 520f, 980f);
            float panelHeight = Mathf.Max(420f, Screen.height - 32f);
            GUILayout.BeginArea(new Rect(16f, 16f, panelWidth, panelHeight), GUI.skin.box);

            GUILayout.Label($"5-Slot Deste Seçimi ({_bootstrap.ActiveMode}) · Bakiye: {_progression.SoftCurrency} Altın");
            GUILayout.Label("Kural: Slot 0 kalıcı Territory Balloon · Slot 1..4 seçilebilir açık kartlar (Gold Mine isteğe bağlıdır).");
            if (_bootstrap.ActiveMode == MatchMode.LocalPvP)
            {
                GUILayout.Label("Local PvP: Her iki oyuncu da hesap sahibinin açık kart havuzunu paylaşır.");
            }

            DrawArmoryStrip();

            GUILayout.Space(8);
            if (_bootstrap.ActiveMode == MatchMode.LocalPvP)
            {
                GUILayout.BeginHorizontal();
                DrawSeatPanel(0, "Player 1 (TeamA)", _p1Deck, ref _p1Scroll);
                GUILayout.Space(12);
                DrawSeatPanel(1, "Player 2 (TeamB)", _p2Deck, ref _p2Scroll);
                GUILayout.EndHorizontal();
            }
            else
            {
                DrawSeatPanel(0, "Yerel Oyuncu", _p1Deck, ref _p1Scroll);
            }

            GUILayout.EndArea();
        }

        private void DrawArmoryStrip()
        {
            List<StructureCardSO> selectable = _catalog.GetDeckSelectableCards();
            bool anyLocked = false;
            for (int i = 0; i < selectable.Count; i++)
            {
                if (!_progression.IsCardUnlocked(selectable[i].CardId))
                {
                    anyLocked = true;
                    break;
                }
            }

            if (!anyLocked && string.IsNullOrEmpty(_armoryFeedback))
            {
                return;
            }

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("Armory (Kilitli Kartları Aç):");
            _armoryScroll = GUILayout.BeginScrollView(_armoryScroll, GUILayout.Height(46f));
            GUILayout.BeginHorizontal();
            for (int i = 0; i < selectable.Count; i++)
            {
                StructureCardSO card = selectable[i];
                if (_progression.IsCardUnlocked(card.CardId))
                {
                    continue;
                }

                bool canAfford = _progression.SoftCurrency >= card.UnlockCostSoftCurrency;
                GUI.enabled = canAfford;
                if (GUILayout.Button($"{card.DisplayName} Aç ({card.UnlockCostSoftCurrency} Altın)", GUILayout.Height(28f)))
                {
                    _ = UnlockAndSaveAsync(card.CardId);
                }
                GUI.enabled = true;
            }
            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();

            if (!string.IsNullOrEmpty(_armoryFeedback))
            {
                GUILayout.Label(_armoryFeedback);
            }
            GUILayout.EndVertical();
        }

        private void DrawSeatPanel(byte seatId, string title, ushort[] deck, ref Vector2 scrollPos)
        {
            bool isReady = _bootstrap.IsSeatDraftReady(seatId);
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true));
            GUILayout.Label($"{title} — Durum: {(isReady ? "HAZIR" : "Seçim Yapıyor")}");

            deck[DeckRules.LockedBalloonSlotIndex] = _catalog.TerritoryBalloonCardId;

            string balloonName = _catalog.TryGetCard(_catalog.TerritoryBalloonCardId, out StructureCardSO balloonCard)
                ? balloonCard.DisplayName
                : "Territory Balloon";

            GUI.enabled = false;
            GUILayout.Button($"Slot 0 [KİLİTLİ]: {balloonName}", GUILayout.Height(26f));
            GUI.enabled = true;

            for (int slot = DeckRules.FreeSlotStartIndex; slot < DeckRules.DeckSize; slot++)
            {
                ushort id = deck[slot];
                if (id != 0 && _catalog.TryGetCard(id, out StructureCardSO card))
                {
                    if (GUILayout.Button($"Slot {slot}: {card.DisplayName} ({card.BuildCostGold}g) [Çıkar]", GUILayout.Height(26f)))
                    {
                        deck[slot] = 0;
                        if (isReady)
                        {
                            _bootstrap.SubmitSeatLoadout(seatId, (ushort[])deck.Clone(), false);
                        }
                    }
                }
                else
                {
                    GUI.enabled = false;
                    GUILayout.Button($"Slot {slot}: — Boş (Aşağıdan Kart Seçin) —", GUILayout.Height(26f));
                    GUI.enabled = true;
                }
            }

            GUILayout.Space(6);
            GUILayout.Label("Kart Havuzu:");
            scrollPos = GUILayout.BeginScrollView(scrollPos, GUILayout.Height(180f));
            List<StructureCardSO> selectable = _catalog.GetDeckSelectableCards();
            for (int i = 0; i < selectable.Count; i++)
            {
                StructureCardSO card = selectable[i];
                bool unlocked = _progression.IsCardUnlocked(card.CardId);
                bool alreadyInDeck = ContainsCard(deck, card.CardId);
                int emptySlot = FindFirstEmptyFreeSlot(deck);

                if (!unlocked)
                {
                    GUI.enabled = false;
                    GUILayout.Button($"[KİLİTLİ] {card.DisplayName} (Armory: {card.UnlockCostSoftCurrency})");
                    GUI.enabled = true;
                }
                else if (alreadyInDeck)
                {
                    GUI.enabled = false;
                    GUILayout.Button($"[DESTEDE] {card.DisplayName}");
                    GUI.enabled = true;
                }
                else
                {
                    GUI.enabled = emptySlot >= DeckRules.FreeSlotStartIndex;
                    if (GUILayout.Button($"+ {card.DisplayName} ({card.BuildCostGold}g)"))
                    {
                        deck[emptySlot] = card.CardId;
                        if (isReady)
                        {
                            _bootstrap.SubmitSeatLoadout(seatId, (ushort[])deck.Clone(), false);
                        }
                    }
                    GUI.enabled = true;
                }
            }
            GUILayout.EndScrollView();

            GUILayout.Space(6);
            string readyLabel = isReady ? "Hazır İptal Et" : "Desteyi Onayla (Hazır)";
            if (GUILayout.Button(readyLabel, GUILayout.Height(34f)))
            {
                bool nextReady = !isReady;
                ushort[] payload = (ushort[])deck.Clone();
                _bootstrap.SubmitSeatLoadout(seatId, payload, nextReady);

                if (nextReady && seatId == 0)
                {
                    _ = SaveLastLoadoutAsync(payload);
                }
            }

            if (seatId < _seatFeedback.Length && !string.IsNullOrEmpty(_seatFeedback[seatId]))
            {
                GUILayout.Label(_seatFeedback[seatId]);
            }

            GUILayout.EndVertical();
        }

        private static bool ContainsCard(ushort[] deck, ushort cardId)
        {
            for (int i = 0; i < deck.Length; i++)
            {
                if (deck[i] == cardId)
                {
                    return true;
                }
            }

            return false;
        }

        private static int FindFirstEmptyFreeSlot(ushort[] deck)
        {
            for (int i = DeckRules.FreeSlotStartIndex; i < DeckRules.DeckSize; i++)
            {
                if (deck[i] == 0)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
