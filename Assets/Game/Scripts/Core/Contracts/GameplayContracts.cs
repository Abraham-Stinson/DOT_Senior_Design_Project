using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Game.Core
{
    public interface IDamageable
    {
        TeamId Team { get; }
        float CurrentHealth { get; }
        float MaxHealth { get; }
        bool IsAlive { get; }
        void ApplyDamageServer(in DamageInfo damage);
    }

    public interface IDockable
    {
        TeamId Team { get; }
        Vector2 DockAnchorWorld { get; }
        bool CanDock(byte seatId, TeamId team);
        void OnAirshipDockedServer(byte seatId);
        void OnAirshipUndockedServer(byte seatId);
    }

    public interface IInteractable
    {
        bool CanInteract(byte seatId, TeamId team);
        void HandleActionServer(byte seatId, in InteractPayload payload);
    }

    public interface IStructure : IDamageable, IDockable
    {
        ushort CardId { get; }
        byte OwnerSeatId { get; }
        bool IsConstructed { get; }
        Bounds FootprintBounds { get; }
    }

    public interface ITerrainService
    {
        float SampleGroundSurfaceY(float worldX);
        bool IsSolid(Vector2 worldPoint);
        bool HasGroundSupport(Bounds footprint, float minSupportRatio);
    }

    public interface ITerritoryService
    {
        bool IsInsideTeamTerritory(TeamId team, Vector2 worldPoint);
        int GetZoneCount(TeamId team);
        void AddOrUpdateZoneServer(int sourceId, TeamId team, Vector2 center, float radius);
        void RemoveZoneServer(int sourceId);
    }

    public interface IGoldDeposit
    {
        int DepositId { get; }
        Vector2 AnchorWorld { get; }
        float SnapRadius { get; }
        bool IsOccupied { get; }
        void OccupyServer(int structureObjectId);
        void ReleaseServer(int structureObjectId);
    }

    public interface IEconomyService
    {
        int GetGold(byte seatId);
        float GetIncomePerSecond(byte seatId);
        bool TrySpendGoldServer(byte seatId, int amount);
        void AddGoldServer(byte seatId, int amount);
        void RefundGoldServer(byte seatId, int amount);
        void RegisterIncomeSourceServer(byte seatId, int sourceId, float goldPerSecond);
        void UnregisterIncomeSourceServer(byte seatId, int sourceId);
    }

    public interface IUnlockedCardProvider
    {
        bool IsCardUnlocked(ushort cardId);
        IReadOnlyCollection<ushort> UnlockedCardIds { get; }
    }

    public interface ISaveService
    {
        PlayerProfileData Load();
        void Save(PlayerProfileData data);
        Task<PlayerProfileData> LoadAsync(CancellationToken ct = default);
        Task SaveAsync(PlayerProfileData data, CancellationToken ct = default);
    }

    public interface IMatchRuleContext
    {
        MatchMode Mode { get; }
        ICardCatalogLookup CardCatalog { get; }
        ITerrainService Terrain { get; }
        ITerritoryService Territory { get; }
        IEconomyService Economy { get; }
    }

    public interface IMatchRule
    {
        void InitializeServer(IMatchRuleContext context);
        void TickServer(float deltaTime);
        bool TryEvaluateOutcome(out MatchOutcome outcome);
    }
}
