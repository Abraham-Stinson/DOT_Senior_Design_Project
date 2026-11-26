using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using Game.Core;
using Game.Data;
using UnityEngine;

namespace Game.Gameplay
{
    public static class ActiveBuildControllerRegistry
    {
        public static StructureBuildController Current { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            Current = null;
        }

        public static void Register(StructureBuildController controller)
        {
            if (controller != null)
            {
                Current = controller;
            }
        }

        public static void Unregister(StructureBuildController controller)
        {
            if (ReferenceEquals(Current, controller))
            {
                Current = null;
            }
        }
    }

    /// <summary>
    /// Server-authoritative placement validator and spawner supporting both Carry & Place (pick up, preview,
    /// deploy or cancel with refund) and direct server placement from a player's 5-slot loadout.
    /// </summary>
    public sealed class StructureBuildController : NetworkBehaviour
    {
        [SerializeField] private float _maxBuildRange = 10f;
        [SerializeField] private float _maxGroundPlacementDistance = 3.25f;
        [SerializeField] private float _sellRefundRatio = StructureSellRules.DefaultSellRefundRatio;
        [SerializeField] private StructureBase _defaultStructurePrefab;
        [SerializeField] private CardCatalogSO _cardCatalog;

        private readonly Dictionary<byte, double> _nextAllowedBuildTimeBySeat = new();
        private MatchContext _context;
        private GameModeController _gameMode;

        public float MaxBuildRange => _maxBuildRange;
        public float MaxGroundPlacementDistance => _maxGroundPlacementDistance;
        public float SellRefundRatio => _sellRefundRatio;
        public CardCatalogSO CardCatalog => _cardCatalog;

        public event Action<BuildFeedback> BuildFeedbackReceived;
        public event Action<BuildFeedback> StructureBuilt;
        public event Action<byte, ushort, int> CarryCancelled;
        public event Action<byte, ushort, int> StructureSold;

        private void OnEnable()
        {
            ActiveBuildControllerRegistry.Register(this);
        }

        private void OnDisable()
        {
            ActiveBuildControllerRegistry.Unregister(this);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ActiveArenaBootstrapRegistry.FindFor(NetworkManager)?.RegisterReplicatedBuildController(this);
        }

        public override void OnStopClient()
        {
            ActiveArenaBootstrapRegistry.FindFor(NetworkManager)?.UnregisterReplicatedBuildController(this);
            base.OnStopClient();
        }

        public void InitializeServer(
            MatchContext context,
            GameModeController gameMode,
            CardCatalogSO cardCatalog,
            StructureBase defaultStructurePrefab = null)
        {
            if (!IsServerInitialized)
            {
                return;
            }

            _context = context;
            if (_context != null)
            {
                _context.BuildController = this;
            }
            _gameMode = gameMode;
            if (cardCatalog != null)
            {
                _cardCatalog = cardCatalog;
            }
            if (defaultStructurePrefab != null)
            {
                _defaultStructurePrefab = defaultStructurePrefab;
            }

            _nextAllowedBuildTimeBySeat.Clear();
        }

        public void RequestBeginCarryFromSlot(byte seatId, int slotIndex)
        {
            if (!IsClientInitialized)
            {
                return;
            }

            SubmitBeginCarrySlotServerRpc(seatId, slotIndex);
        }

        public void RequestBeginCarryCard(byte seatId, ushort cardId)
        {
            if (!IsClientInitialized)
            {
                return;
            }

            SubmitBeginCarryCardServerRpc(seatId, cardId);
        }

        public void RequestDeployCarried(byte seatId, Vector2 requestedWorldPosition)
        {
            if (!IsClientInitialized)
            {
                return;
            }

            SubmitDeployCarriedServerRpc(seatId, requestedWorldPosition);
        }

        public void RequestCancelCarry(byte seatId)
        {
            if (!IsClientInitialized)
            {
                return;
            }

            SubmitCancelCarryServerRpc(seatId);
        }

        public void RequestBuildFromSlot(byte seatId, int slotIndex, Vector2 requestedWorldPosition)
        {
            if (!IsClientInitialized)
            {
                return;
            }

            SubmitBuildSlotServerRpc(seatId, slotIndex, requestedWorldPosition);
        }

        public void RequestBuildCard(byte seatId, ushort cardId, Vector2 requestedWorldPosition)
        {
            if (!IsClientInitialized)
            {
                return;
            }

            SubmitBuildCardServerRpc(seatId, cardId, requestedWorldPosition);
        }

        [ServerRpc(RequireOwnership = false)]
        private void SubmitBeginCarrySlotServerRpc(byte seatId, int slotIndex, NetworkConnection sender = null)
        {
            if (!IsServerInitialized)
            {
                return;
            }

            ushort cardId = ResolveSlotCardIdServer(seatId, slotIndex);
            ProcessBeginCarryServer(seatId, cardId, slotIndex, sender);
        }

        [ServerRpc(RequireOwnership = false)]
        private void SubmitBeginCarryCardServerRpc(byte seatId, ushort cardId, NetworkConnection sender = null)
        {
            if (!IsServerInitialized)
            {
                return;
            }

            int slotIndex = ResolveCardSlotIndexServer(seatId, cardId);
            ProcessBeginCarryServer(seatId, cardId, slotIndex, sender);
        }

        [ServerRpc(RequireOwnership = false)]
        private void SubmitDeployCarriedServerRpc(byte seatId, Vector2 requestedWorldPosition, NetworkConnection sender = null)
        {
            if (!IsServerInitialized)
            {
                return;
            }

            ProcessDeployCarriedServer(seatId, requestedWorldPosition, sender);
        }

        [ServerRpc(RequireOwnership = false)]
        private void SubmitCancelCarryServerRpc(byte seatId, NetworkConnection sender = null)
        {
            if (!IsServerInitialized)
            {
                return;
            }

            ProcessCancelCarryServer(seatId, sender);
        }

        [ServerRpc(RequireOwnership = false)]
        private void SubmitBuildSlotServerRpc(byte seatId, int slotIndex, Vector2 requestedWorldPosition, NetworkConnection sender = null)
        {
            if (!IsServerInitialized)
            {
                return;
            }

            ushort cardId = ResolveSlotCardIdServer(seatId, slotIndex);
            ProcessBuildRequestServer(seatId, cardId, requestedWorldPosition, sender);
        }

        [ServerRpc(RequireOwnership = false)]
        private void SubmitBuildCardServerRpc(byte seatId, ushort cardId, Vector2 requestedWorldPosition, NetworkConnection sender = null)
        {
            if (!IsServerInitialized)
            {
                return;
            }

            ProcessBuildRequestServer(seatId, cardId, requestedWorldPosition, sender);
        }

        public bool BeginCarryFromDockedBaseServer(byte seatId, int slotIndex, NetworkConnection sender)
        {
            if (!IsServerInitialized)
            {
                return false;
            }

            ushort cardId = ResolveSlotCardIdServer(seatId, slotIndex);
            ProcessBeginCarryServer(seatId, cardId, slotIndex, sender);
            return _context != null
                && _context.TryGetAirship(seatId, out AirshipController ship)
                && ship != null
                && ship.IsCarrying
                && ship.CarryState.SlotIndex == slotIndex;
        }

        public int ComputeSellRefundForCard(ushort cardId)
        {
            if (_cardCatalog != null && cardId != 0 && _cardCatalog.TryGetCard(cardId, out StructureCardSO card) && card != null)
            {
                return StructureSellRules.ComputeSellRefund(card.BuildCostGold, _sellRefundRatio);
            }

            return 0;
        }

        public bool TrySellStructureServer(byte seatId, StructureBase structure, NetworkConnection sender, out int refundedGold)
        {
            refundedGold = 0;
            if (!IsServerInitialized || structure == null)
            {
                return false;
            }

            MatchPhase phase = _gameMode != null ? _gameMode.Phase : MatchPhase.Playing;
            if (phase != MatchPhase.Playing)
            {
                return false;
            }

            if (_context == null || !_context.TryGetSeat(seatId, out PlayerSeatController seat) || seat == null)
            {
                return false;
            }

            if (sender != null && seat.Owner != sender)
            {
                return false;
            }

            if (!StructureSellRules.CanSellStructure(structure.Category, structure.IsAlive, structure.Team, seat.Team))
            {
                return false;
            }

            ushort soldCardId = structure.CardId;
            refundedGold = ComputeSellRefundForCard(soldCardId);
            if (!structure.SellAndDespawnServer())
            {
                refundedGold = 0;
                return false;
            }

            if (refundedGold > 0)
            {
                RefundSeatGold(seat, seatId, refundedGold);
            }

            if (sender != null && sender.IsActive)
            {
                NotifyStructureSoldTargetRpc(sender, seatId, soldCardId, refundedGold);
            }

            return true;
        }

        private void ProcessBeginCarryServer(byte seatId, ushort cardId, int slotIndex, NetworkConnection sender)
        {
            MatchPhase phase = _gameMode != null ? _gameMode.Phase : MatchPhase.Playing;
            PlayerSeatController seat = null;
            bool seatExists = _context != null && _context.TryGetSeat(seatId, out seat) && seat != null;
            bool callerOwnsSeat = seatExists && sender != null && seat.Owner == sender;
            AirshipController airship = null;
            bool airshipExists = _context != null && _context.TryGetAirship(seatId, out airship) && airship != null;
            StructureCardSO card = null;
            bool cardExists = _cardCatalog != null && cardId != 0 && _cardCatalog.TryGetCard(cardId, out card) && card != null;
            bool cardInLoadout = seatExists && cardId != 0 && seat.IsCardInActiveLoadout(cardId);
            bool dockedAtBase = airshipExists && airship.Docking != null && airship.Docking.IsDockedAtBase;

            Vector2 shipPos = airshipExists ? (Vector2)airship.transform.position : Vector2.zero;

            if (!airshipExists && seatExists && phase == MatchPhase.Playing && callerOwnsSeat)
            {
                SendFeedbackToCaller(sender, BuildFeedback.CreateRejected(seatId, cardId, BuildRejectReason.InvalidSeat, shipPos));
                return;
            }

            int currentGold = _context?.Economy != null
                ? _context.Economy.GetGold(seatId)
                : (seatExists ? seat.Gold : 0);
            int existingReserved = airshipExists && airship.IsCarrying ? airship.CarryState.ReservedGold : 0;
            int effectiveGold = currentGold + existingReserved;
            int cost = cardExists ? card.BuildCostGold : 0;

            BuildRejectReason reason = StructurePlacementValidator.Validate(new BuildValidationInput(
                phase: phase,
                seatExists: seatExists,
                callerOwnsSeat: callerOwnsSeat,
                cardExists: cardExists,
                cardInActiveLoadout: cardInLoadout,
                isOnCooldown: false,
                currentGold: effectiveGold,
                buildCostGold: cost,
                withinBuildRange: true,
                insideTeamTerritory: true,
                validTerrain: true,
                overlapsExistingStructure: false,
                requiresGoldDeposit: false,
                hasValidGoldDeposit: true,
                depositOccupied: false,
                withinGroundProximity: true,
                mustDockAtBaseSatisfied: dockedAtBase));

            if (reason != BuildRejectReason.None)
            {
                SendFeedbackToCaller(sender, BuildFeedback.CreateRejected(seatId, cardId, reason, shipPos));
                return;
            }

            if (airship.IsCarrying)
            {
                if (existingReserved > 0)
                {
                    RefundSeatGold(seat, seatId, existingReserved);
                }

                airship.ClearCarryStateServer();
            }

            if (!TrySpendSeatGold(seat, seatId, cost))
            {
                SendFeedbackToCaller(sender, BuildFeedback.CreateRejected(seatId, cardId, BuildRejectReason.InsufficientGold, shipPos));
                return;
            }

            airship.SetCarryStateServer(AirshipCarryState.Create(seatId, card.CardId, slotIndex, cost));
            SendFeedbackToCaller(sender, BuildFeedback.CreateAccepted(seatId, card.CardId, shipPos));
        }

        private void ProcessDeployCarriedServer(byte seatId, Vector2 requestedPosition, NetworkConnection sender)
        {
            MatchPhase phase = _gameMode != null ? _gameMode.Phase : MatchPhase.Playing;
            PlayerSeatController seat = null;
            bool seatExists = _context != null && _context.TryGetSeat(seatId, out seat) && seat != null;
            bool callerOwnsSeat = seatExists && sender != null && seat.Owner == sender;
            AirshipController airship = null;
            bool airshipExists = _context != null && _context.TryGetAirship(seatId, out airship) && airship != null;

            if (phase != MatchPhase.Playing)
            {
                SendFeedbackToCaller(sender, BuildFeedback.CreateRejected(seatId, 0, BuildRejectReason.NotPlaying, requestedPosition));
                return;
            }

            if (!seatExists)
            {
                SendFeedbackToCaller(sender, BuildFeedback.CreateRejected(seatId, 0, BuildRejectReason.InvalidSeat, requestedPosition));
                return;
            }

            if (!callerOwnsSeat)
            {
                SendFeedbackToCaller(sender, BuildFeedback.CreateRejected(seatId, 0, BuildRejectReason.UnauthorizedSender, requestedPosition));
                return;
            }

            if (!airshipExists || !airship.IsCarrying)
            {
                SendFeedbackToCaller(sender, BuildFeedback.CreateRejected(seatId, 0, BuildRejectReason.NotCarrying, requestedPosition));
                return;
            }

            AirshipCarryState carry = airship.CarryState;
            StructureCardSO card = null;
            bool cardExists = _cardCatalog != null && carry.CardId != 0 && _cardCatalog.TryGetCard(carry.CardId, out card) && card != null;
            bool cardInLoadout = cardExists && seat.IsCardInActiveLoadout(carry.CardId);

            Vector2 footprintSize = cardExists ? card.FootprintSize : new Vector2(1.8f, 1.4f);
            Vector2 resolvedPos = ResolvePlacementPosition(
                requestedPosition,
                card,
                footprintSize,
                out IGoldDeposit matchedDeposit,
                out bool foundOccupiedOnly);
            Bounds footprint = new(resolvedPos, new Vector3(footprintSize.x, footprintSize.y, 1f));

            bool withinRange = IsWithinBuildRange(seatId, resolvedPos);
            double now = TimeManager != null ? TimeManager.Tick * TimeManager.TickDelta : Time.timeAsDouble;
            bool isOnCooldown = _nextAllowedBuildTimeBySeat.TryGetValue(seatId, out double readyAt) && now < readyAt;

            bool insideTerritory = _context?.Territory == null
                || _context.Territory.IsInsideTeamTerritory(seat.Team, resolvedPos);

            bool validTerrain = !cardExists
                || !card.RequiresGroundSupport
                || _context?.Terrain == null
                || _context.Terrain.HasGroundSupport(footprint, 0.5f);

            bool overlapsExisting = OverlapsExistingStructure(footprint);
            bool requiresDeposit = cardExists && card.RequiresGoldDeposit;
            bool withinGroundProximity = IsWithinGroundProximity(seatId, requestedPosition, resolvedPos, card);

            BuildRejectReason reason = StructurePlacementValidator.Validate(new BuildValidationInput(
                phase: phase,
                seatExists: seatExists,
                callerOwnsSeat: callerOwnsSeat,
                cardExists: cardExists,
                cardInActiveLoadout: cardInLoadout,
                isOnCooldown: isOnCooldown,
                currentGold: 0,
                buildCostGold: 0,
                withinBuildRange: withinRange,
                insideTeamTerritory: insideTerritory,
                validTerrain: validTerrain,
                overlapsExistingStructure: overlapsExisting,
                requiresGoldDeposit: requiresDeposit,
                hasValidGoldDeposit: matchedDeposit != null,
                depositOccupied: foundOccupiedOnly,
                withinGroundProximity: withinGroundProximity));

            if (reason != BuildRejectReason.None)
            {
                SendFeedbackToCaller(sender, BuildFeedback.CreateRejected(seatId, carry.CardId, reason, resolvedPos));
                return;
            }

            StructureBase prefabToSpawn = ResolveStructurePrefab(card);
            if (prefabToSpawn == null)
            {
                SendFeedbackToCaller(sender, BuildFeedback.CreateRejected(seatId, carry.CardId, BuildRejectReason.UnknownCard, resolvedPos));
                return;
            }

            SpawnStructureInstanceServer(seat, seatId, card, footprintSize, resolvedPos, matchedDeposit, now);
            airship.ClearCarryStateServer();

            BuildFeedback accepted = BuildFeedback.CreateAccepted(seatId, card.CardId, resolvedPos);
            SendFeedbackToCaller(sender, accepted);
            NotifyStructureBuiltObserversRpc(accepted);
        }

        private void ProcessCancelCarryServer(byte seatId, NetworkConnection sender)
        {
            PlayerSeatController seat = null;
            bool seatExists = _context != null && _context.TryGetSeat(seatId, out seat) && seat != null;
            bool callerOwnsSeat = seatExists && sender != null && seat.Owner == sender;
            AirshipController airship = null;
            bool airshipExists = _context != null && _context.TryGetAirship(seatId, out airship) && airship != null;

            if (!seatExists)
            {
                SendFeedbackToCaller(sender, BuildFeedback.CreateRejected(seatId, 0, BuildRejectReason.InvalidSeat, Vector2.zero));
                return;
            }

            if (!callerOwnsSeat)
            {
                SendFeedbackToCaller(sender, BuildFeedback.CreateRejected(seatId, 0, BuildRejectReason.UnauthorizedSender, Vector2.zero));
                return;
            }

            if (!airshipExists || !airship.IsCarrying)
            {
                SendFeedbackToCaller(sender, BuildFeedback.CreateRejected(seatId, 0, BuildRejectReason.NotCarrying, Vector2.zero));
                return;
            }

            AirshipCarryState carry = airship.CarryState;
            if (carry.ReservedGold > 0)
            {
                RefundSeatGold(seat, seatId, carry.ReservedGold);
            }

            airship.ClearCarryStateServer();

            if (sender != null && sender.IsActive)
            {
                NotifyCarryCancelledTargetRpc(sender, seatId, carry.CardId, carry.ReservedGold);
            }
        }

        private void ProcessBuildRequestServer(byte seatId, ushort cardId, Vector2 requestedPosition, NetworkConnection sender)
        {
            MatchPhase phase = _gameMode != null ? _gameMode.Phase : MatchPhase.Playing;
            PlayerSeatController seat = null;
            bool seatExists = _context != null && _context.TryGetSeat(seatId, out seat) && seat != null;
            bool callerOwnsSeat = seatExists && sender != null && seat.Owner == sender;
            StructureCardSO card = null;
            bool cardExists = _cardCatalog != null && cardId != 0 && _cardCatalog.TryGetCard(cardId, out card) && card != null;
            bool cardInLoadout = seatExists && cardId != 0 && seat.IsCardInActiveLoadout(cardId);

            int cost = cardExists ? card.BuildCostGold : 0;
            int currentGold = _context?.Economy != null
                ? _context.Economy.GetGold(seatId)
                : (seatExists ? seat.Gold : 0);

            Vector2 footprintSize = cardExists ? card.FootprintSize : new Vector2(1.8f, 1.4f);
            Vector2 resolvedPos = ResolvePlacementPosition(
                requestedPosition,
                card,
                footprintSize,
                out IGoldDeposit matchedDeposit,
                out bool foundOccupiedOnly);
            Bounds footprint = new(resolvedPos, new Vector3(footprintSize.x, footprintSize.y, 1f));

            bool withinRange = IsWithinBuildRange(seatId, resolvedPos);
            double now = TimeManager != null ? TimeManager.Tick * TimeManager.TickDelta : Time.timeAsDouble;
            bool isOnCooldown = _nextAllowedBuildTimeBySeat.TryGetValue(seatId, out double readyAt) && now < readyAt;

            bool insideTerritory = _context?.Territory == null || !seatExists
                || _context.Territory.IsInsideTeamTerritory(seat.Team, resolvedPos);

            bool validTerrain = !cardExists
                || !card.RequiresGroundSupport
                || _context?.Terrain == null
                || _context.Terrain.HasGroundSupport(footprint, 0.5f);

            bool overlapsExisting = OverlapsExistingStructure(footprint);
            bool requiresDeposit = cardExists && card.RequiresGoldDeposit;
            bool withinGroundProximity = IsWithinGroundProximity(seatId, requestedPosition, resolvedPos, card);

            BuildRejectReason reason = StructurePlacementValidator.Validate(new BuildValidationInput(
                phase: phase,
                seatExists: seatExists,
                callerOwnsSeat: callerOwnsSeat,
                cardExists: cardExists,
                cardInActiveLoadout: cardInLoadout,
                isOnCooldown: isOnCooldown,
                currentGold: currentGold,
                buildCostGold: cost,
                withinBuildRange: withinRange,
                insideTeamTerritory: insideTerritory,
                validTerrain: validTerrain,
                overlapsExistingStructure: overlapsExisting,
                requiresGoldDeposit: requiresDeposit,
                hasValidGoldDeposit: matchedDeposit != null,
                depositOccupied: foundOccupiedOnly,
                withinGroundProximity: withinGroundProximity));

            if (reason != BuildRejectReason.None)
            {
                SendFeedbackToCaller(sender, BuildFeedback.CreateRejected(seatId, cardId, reason, resolvedPos));
                return;
            }

            StructureBase prefabToSpawn = ResolveStructurePrefab(card);
            if (prefabToSpawn == null)
            {
                SendFeedbackToCaller(sender, BuildFeedback.CreateRejected(seatId, cardId, BuildRejectReason.UnknownCard, resolvedPos));
                return;
            }

            if (!TrySpendSeatGold(seat, seatId, cost))
            {
                SendFeedbackToCaller(sender, BuildFeedback.CreateRejected(seatId, cardId, BuildRejectReason.InsufficientGold, resolvedPos));
                return;
            }

            SpawnStructureInstanceServer(seat, seatId, card, footprintSize, resolvedPos, matchedDeposit, now);

            BuildFeedback accepted = BuildFeedback.CreateAccepted(seatId, card.CardId, resolvedPos);
            SendFeedbackToCaller(sender, accepted);
            NotifyStructureBuiltObserversRpc(accepted);
        }

        private void SpawnStructureInstanceServer(
            PlayerSeatController seat,
            byte seatId,
            StructureCardSO card,
            Vector2 footprintSize,
            Vector2 resolvedPos,
            IGoldDeposit matchedDeposit,
            double serverNow)
        {
            StructureBase prefabToSpawn = ResolveStructurePrefab(card);
            StructureBase instance = Instantiate(prefabToSpawn, new Vector3(resolvedPos.x, resolvedPos.y, 0f), Quaternion.identity);
            instance.name = $"Structure_{seat.Team}_{card.DisplayName}";
            ServerManager.Spawn(instance.NetworkObject, seat.Owner, gameObject.scene);
            instance.InitializeServer(
                card.CardId,
                seatId,
                seat.Team,
                card.Category,
                card.MaxHealth,
                footprintSize,
                card.TerritoryExpansionRadius,
                card.IncomePerSecondBonus,
                _context,
                card.BuildDurationSeconds);

            if (matchedDeposit != null)
            {
                matchedDeposit.OccupyServer(instance.NetworkObject.ObjectId);
                instance.BindGoldDepositServer(matchedDeposit);
            }

            if (card.CanFireWeapon)
            {
                instance.InitializeWeaponServer(card.ToWeaponConfig(), _context?.ProjectilePrefab);
            }

            _nextAllowedBuildTimeBySeat[seatId] = serverNow + Math.Max(0f, card.BuildCooldownSeconds);
        }

        private bool TrySpendSeatGold(PlayerSeatController seat, byte seatId, int cost)
        {
            return _context?.Economy != null
                ? _context.Economy.TrySpendGoldServer(seatId, cost)
                : (seat != null && seat.TrySpendGoldServer(cost));
        }

        private void RefundSeatGold(PlayerSeatController seat, byte seatId, int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            if (_context?.Economy != null)
            {
                _context.Economy.RefundGoldServer(seatId, amount);
            }
            else
            {
                seat?.AddGoldServer(amount);
            }
        }

        private ushort ResolveSlotCardIdServer(byte seatId, int slotIndex)
        {
            if (_context != null
                && _context.TryGetSeat(seatId, out PlayerSeatController seat)
                && seat != null
                && slotIndex >= 0
                && slotIndex < seat.ActiveLoadout.Count)
            {
                return seat.ActiveLoadout[slotIndex];
            }

            return 0;
        }

        private int ResolveCardSlotIndexServer(byte seatId, ushort cardId)
        {
            if (_context != null
                && cardId != 0
                && _context.TryGetSeat(seatId, out PlayerSeatController seat)
                && seat != null)
            {
                for (int i = 0; i < seat.ActiveLoadout.Count; i++)
                {
                    if (seat.ActiveLoadout[i] == cardId)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        private Vector2 ResolvePlacementPosition(
            Vector2 requestedPosition,
            StructureCardSO card,
            Vector2 footprintSize,
            out IGoldDeposit matchedDeposit,
            out bool foundOccupiedDepositOnly)
        {
            matchedDeposit = null;
            foundOccupiedDepositOnly = false;

            Rect bounds = _context != null ? _context.PlayableAirspace : new Rect(-18f, -2f, 36f, 16f);
            float halfW = footprintSize.x * 0.5f;
            float halfH = footprintSize.y * 0.5f;

            float minX = bounds.xMin + halfW;
            float maxX = Mathf.Max(minX, bounds.xMax - halfW);
            float clampedX = Mathf.Clamp(requestedPosition.x, minX, maxX);

            float minY = bounds.yMin + halfH;
            float maxY = Mathf.Max(minY, bounds.yMax - halfH);

            float resolvedY;
            if (card != null && card.RequiresGroundSupport && _context?.Terrain != null)
            {
                float surfaceY = _context.Terrain.SampleGroundSurfaceY(clampedX);
                resolvedY = surfaceY + halfH;
            }
            else
            {
                resolvedY = Mathf.Clamp(requestedPosition.y, minY, maxY);
            }

            Vector2 projected = new(clampedX, resolvedY);
            if (card != null && card.RequiresGoldDeposit)
            {
                if (GoldDepositMatcher.TryFindNearestDeposit(
                        projected,
                        _context?.GoldDeposits,
                        out matchedDeposit,
                        out foundOccupiedDepositOnly)
                    && matchedDeposit != null)
                {
                    float snappedX = Mathf.Clamp(matchedDeposit.AnchorWorld.x, minX, maxX);
                    float snappedY = card.RequiresGroundSupport && _context?.Terrain != null
                        ? _context.Terrain.SampleGroundSurfaceY(snappedX) + halfH
                        : Mathf.Clamp(matchedDeposit.AnchorWorld.y, minY, maxY);
                    return new Vector2(snappedX, snappedY);
                }
            }

            return projected;
        }

        private bool IsWithinBuildRange(byte seatId, Vector2 targetPos)
        {
            if (_maxBuildRange <= 0f || _context == null)
            {
                return true;
            }

            if (!_context.TryGetAirship(seatId, out AirshipController ship) || ship == null)
            {
                return true;
            }

            Vector2 shipPos = ship.transform.position;
            return (targetPos - shipPos).sqrMagnitude <= _maxBuildRange * _maxBuildRange;
        }

        private bool IsWithinGroundProximity(byte seatId, Vector2 requestedPosition, Vector2 resolvedPosition, StructureCardSO card)
        {
            if (card == null || !card.RequiresGroundSupport || _maxGroundPlacementDistance <= 0f || _context?.Terrain == null)
            {
                return true;
            }

            float surfaceY = _context.Terrain.SampleGroundSurfaceY(resolvedPosition.x);
            float sourceY = requestedPosition.y;
            if (_context.TryGetAirship(seatId, out AirshipController ship) && ship != null)
            {
                sourceY = Mathf.Max(requestedPosition.y, ship.transform.position.y);
            }

            return GroundProximityEvaluator.IsWithinGroundProximity(
                requiresGroundSupport: true,
                sourceHeightY: sourceY,
                groundSurfaceY: surfaceY,
                maxGroundPlacementDistance: _maxGroundPlacementDistance);
        }

        private bool OverlapsExistingStructure(Bounds candidateFootprint)
        {
            if (_context == null)
            {
                return false;
            }

            Vector3 shrunkSize = new(
                Mathf.Max(0.1f, candidateFootprint.size.x - 0.1f),
                Mathf.Max(0.1f, candidateFootprint.size.y - 0.1f),
                2f);
            Bounds shrunkCandidate = new(candidateFootprint.center, shrunkSize);

            IReadOnlyList<IStructure> structures = _context.Structures;
            for (int i = 0; i < structures.Count; i++)
            {
                IStructure existing = structures[i];
                if (existing == null || !existing.IsAlive)
                {
                    continue;
                }

                Bounds other = existing.FootprintBounds;
                other.size = new Vector3(other.size.x, other.size.y, 2f);
                if (other.Intersects(shrunkCandidate))
                {
                    return true;
                }
            }

            return false;
        }

        private StructureBase ResolveStructurePrefab(StructureCardSO card)
        {
            if (card != null && card.StructurePrefab != null && card.StructurePrefab.TryGetComponent(out StructureBase cardStructure))
            {
                return cardStructure;
            }

            return _defaultStructurePrefab;
        }

        private void SendFeedbackToCaller(NetworkConnection sender, BuildFeedback feedback)
        {
            if (sender != null && sender.IsActive)
            {
                NotifyBuildFeedbackTargetRpc(sender, feedback);
            }
        }

        [TargetRpc]
        private void NotifyBuildFeedbackTargetRpc(NetworkConnection target, BuildFeedback feedback)
        {
            BuildFeedbackReceived?.Invoke(feedback);
        }

        [TargetRpc]
        private void NotifyCarryCancelledTargetRpc(NetworkConnection target, byte seatId, ushort cardId, int refundedGold)
        {
            CarryCancelled?.Invoke(seatId, cardId, refundedGold);
        }

        [TargetRpc]
        private void NotifyStructureSoldTargetRpc(NetworkConnection target, byte seatId, ushort cardId, int refundedGold)
        {
            StructureSold?.Invoke(seatId, cardId, refundedGold);
        }

        [ObserversRpc]
        private void NotifyStructureBuiltObserversRpc(BuildFeedback feedback)
        {
            StructureBuilt?.Invoke(feedback);
        }
    }
}
