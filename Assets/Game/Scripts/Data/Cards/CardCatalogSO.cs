using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.Data
{
    [CreateAssetMenu(fileName = "CardCatalog", menuName = "Game/Cards/Card Catalog")]
    public sealed class CardCatalogSO : ScriptableObject, ICardCatalogLookup
    {
        [SerializeField] private StructureCardSO _territoryBalloonCard;
        [SerializeField] private List<StructureCardSO> _cards = new();

        private Dictionary<ushort, StructureCardSO> _byId;

        public StructureCardSO TerritoryBalloonCard => _territoryBalloonCard;
        public ushort TerritoryBalloonCardId => _territoryBalloonCard != null ? _territoryBalloonCard.CardId : (ushort)0;
        public IReadOnlyList<StructureCardSO> AllCards => _cards;

        private void OnEnable()
        {
            RebuildLookup();
        }

        public void RebuildLookup()
        {
            _byId = new Dictionary<ushort, StructureCardSO>();
            if (_territoryBalloonCard != null && _territoryBalloonCard.CardId != 0)
            {
                _byId[_territoryBalloonCard.CardId] = _territoryBalloonCard;
            }

            if (_cards == null)
            {
                return;
            }

            for (int i = 0; i < _cards.Count; i++)
            {
                StructureCardSO card = _cards[i];
                if (card == null || card.CardId == 0)
                {
                    continue;
                }

                _byId[card.CardId] = card;
            }
        }

        public bool TryGetCard(ushort cardId, out StructureCardSO card)
        {
            if (_byId == null)
            {
                RebuildLookup();
            }

            return _byId.TryGetValue(cardId, out card);
        }

        public bool TryGetCardRules(ushort cardId, out CardRuleMetadata metadata)
        {
            if (TryGetCard(cardId, out StructureCardSO card))
            {
                metadata = card.ToRuleMetadata();
                return true;
            }

            metadata = default;
            return false;
        }

        public void ConfigureForTest(StructureCardSO territoryBalloonCard, IEnumerable<StructureCardSO> cards)
        {
            _territoryBalloonCard = territoryBalloonCard;
            _cards = cards != null ? new List<StructureCardSO>(cards) : new List<StructureCardSO>();
            RebuildLookup();
        }

        public List<StructureCardSO> GetDeckSelectableCards()
        {
            var result = new List<StructureCardSO>();
            if (_cards == null)
            {
                return result;
            }

            var seen = new HashSet<ushort>();
            for (int i = 0; i < _cards.Count; i++)
            {
                StructureCardSO card = _cards[i];
                if (card == null || card.CardId == 0 || !card.IsDeckSelectable || card.IsLockedInSlotZero)
                {
                    continue;
                }

                if (seen.Add(card.CardId))
                {
                    result.Add(card);
                }
            }

            return result;
        }

        public List<ushort> GetStarterUnlockedCardIds()
        {
            var result = new List<ushort>();
            var seen = new HashSet<ushort>();

            if (_territoryBalloonCard != null && _territoryBalloonCard.CardId != 0 && seen.Add(_territoryBalloonCard.CardId))
            {
                result.Add(_territoryBalloonCard.CardId);
            }

            if (_cards == null)
            {
                return result;
            }

            for (int i = 0; i < _cards.Count; i++)
            {
                StructureCardSO card = _cards[i];
                if (card == null || card.CardId == 0 || !card.UnlockedByDefault)
                {
                    continue;
                }

                if (seen.Add(card.CardId))
                {
                    result.Add(card.CardId);
                }
            }

            return result;
        }
    }
}
