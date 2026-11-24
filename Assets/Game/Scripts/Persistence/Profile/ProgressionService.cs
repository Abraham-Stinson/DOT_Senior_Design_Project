using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Game.Core;
using Game.Data;

namespace Game.Persistence
{
    public sealed class ProgressionService : IUnlockedCardProvider
    {
        private readonly ISaveService _saveService;
        private readonly CardCatalogSO _catalog;
        private readonly int _initialSoftCurrency;
        private readonly HashSet<ushort> _unlockedCardIds = new();

        private PlayerProfileData _profile = new();

        public ProgressionService(ISaveService saveService, CardCatalogSO catalog, int initialSoftCurrency = 300)
        {
            _saveService = saveService ?? throw new ArgumentNullException(nameof(saveService));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _initialSoftCurrency = Math.Max(0, initialSoftCurrency);
        }

        public int SoftCurrency => _profile.SoftCurrency;
        public IReadOnlyCollection<ushort> UnlockedCardIds => _unlockedCardIds;
        public bool IsLoaded { get; private set; }
        public IReadOnlyList<ushort> LastUsedLoadout => _profile.LastUsedLoadout;

        public event Action ProfileChanged;

        public void Load()
        {
            PlayerProfileData loaded = _saveService.Load();
            ApplyLoadedProfile(loaded);
        }

        public void Save()
        {
            _profile.UnlockedCardIds = new List<ushort>(_unlockedCardIds);
            _saveService.Save(_profile);
        }

        public async Task LoadAsync(CancellationToken ct = default)
        {
            PlayerProfileData loaded = await _saveService.LoadAsync(ct).ConfigureAwait(false);
            ApplyLoadedProfile(loaded);
        }

        public Task SaveAsync(CancellationToken ct = default)
        {
            _profile.UnlockedCardIds = new List<ushort>(_unlockedCardIds);
            return _saveService.SaveAsync(_profile, ct);
        }

        private void ApplyLoadedProfile(PlayerProfileData loaded)
        {
            _profile = loaded ?? new PlayerProfileData();
            _profile.UnlockedCardIds ??= new List<ushort>();
            _profile.LastUsedLoadout ??= new List<ushort>();

            bool isFreshProfile = _profile.UnlockedCardIds.Count == 0;
            if (isFreshProfile)
            {
                _profile.SoftCurrency = Math.Max(_profile.SoftCurrency, _initialSoftCurrency);
                List<ushort> starters = _catalog.GetStarterUnlockedCardIds();
                for (int i = 0; i < starters.Count; i++)
                {
                    if (!_profile.UnlockedCardIds.Contains(starters[i]))
                    {
                        _profile.UnlockedCardIds.Add(starters[i]);
                    }
                }
            }

            _unlockedCardIds.Clear();
            for (int i = 0; i < _profile.UnlockedCardIds.Count; i++)
            {
                ushort id = _profile.UnlockedCardIds[i];
                if (id != 0 && _catalog.TryGetCard(id, out _))
                {
                    _unlockedCardIds.Add(id);
                }
            }

            IsLoaded = true;
            ProfileChanged?.Invoke();
        }

        public bool IsCardUnlocked(ushort cardId)
        {
            return cardId != 0 && _unlockedCardIds.Contains(cardId);
        }

        public CardUnlockResult TryUnlockCard(ushort cardId)
        {
            if (cardId == 0 || !_catalog.TryGetCard(cardId, out StructureCardSO card))
            {
                return CardUnlockResult.UnknownCard;
            }

            if (!card.IsDeckSelectable || card.IsLockedInSlotZero)
            {
                return CardUnlockResult.NotUnlockable;
            }

            if (_unlockedCardIds.Contains(cardId))
            {
                return CardUnlockResult.AlreadyUnlocked;
            }

            int cost = Math.Max(0, card.UnlockCostSoftCurrency);
            if (_profile.SoftCurrency < cost)
            {
                return CardUnlockResult.InsufficientCurrency;
            }

            _profile.SoftCurrency -= cost;
            _unlockedCardIds.Add(cardId);
            _profile.UnlockedCardIds = new List<ushort>(_unlockedCardIds);
            ProfileChanged?.Invoke();
            return CardUnlockResult.Success;
        }

        public async Task<CardUnlockResult> TryUnlockCardAsync(ushort cardId, CancellationToken ct = default)
        {
            CardUnlockResult result = TryUnlockCard(cardId);
            if (result == CardUnlockResult.Success)
            {
                await SaveAsync(ct).ConfigureAwait(false);
            }

            return result;
        }

        public void AddSoftCurrency(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            _profile.SoftCurrency += amount;
            ProfileChanged?.Invoke();
        }

        public bool TrySaveLastLoadout(IReadOnlyList<ushort> fiveCards)
        {
            if (!LoadoutValidator.Validate(fiveCards, _catalog, this).IsValid)
            {
                return false;
            }

            _profile.LastUsedLoadout = new List<ushort>(fiveCards);
            ProfileChanged?.Invoke();
            return true;
        }

        public ushort[] GetLastValidLoadoutOrDefault()
        {
            if (_profile.LastUsedLoadout != null
                && LoadoutValidator.Validate(_profile.LastUsedLoadout, _catalog, this).IsValid)
            {
                return _profile.LastUsedLoadout.ToArray();
            }

            var deck = new ushort[DeckRules.DeckSize];
            deck[DeckRules.LockedBalloonSlotIndex] = _catalog.TerritoryBalloonCardId;

            int writeIndex = DeckRules.FreeSlotStartIndex;
            List<StructureCardSO> selectable = _catalog.GetDeckSelectableCards();
            for (int i = 0; i < selectable.Count && writeIndex < DeckRules.DeckSize; i++)
            {
                ushort id = selectable[i].CardId;
                if (IsCardUnlocked(id))
                {
                    deck[writeIndex++] = id;
                }
            }

            return deck;
        }
    }
}
