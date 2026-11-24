using System.Collections.Generic;
using Game.Core;
using Game.Data;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Client-side visual indicator for airship carried payload, projected placement ghost (green valid / red invalid),
    /// Territory Balloon expansion preview ring, and Gold Deposit slot highlighting.
    /// </summary>
    public sealed class AirshipPlacementGhostVisual : MonoBehaviour
    {
        private AirshipController _airship;
        private SpriteRenderer _carriedPayloadRenderer;
        private SpriteRenderer _ghostRenderer;
        private TerritoryCircleVisual _previewCircle;
        private bool _wasHighlightingDeposits;
        private bool _isRequestingGoldDeposit;
        private Vector2 _requestingFootprint = new(1.8f, 1.4f);

        private static readonly HashSet<AirshipPlacementGhostVisual> s_depositRequesters = new();
        private static Texture2D s_unitSquareTex;
        private static Sprite s_unitSquareSprite;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            s_depositRequesters.Clear();
            s_unitSquareSprite = null;
            s_unitSquareTex = null;
        }

        public void Attach(AirshipController owner)
        {
            _airship = owner;
            RefreshVisuals();
        }

        private void OnDisable()
        {
            SetRequestingDepositHighlight(false, _requestingFootprint);
        }

        private void OnDestroy()
        {
            SetRequestingDepositHighlight(false, _requestingFootprint);
            if (_ghostRenderer != null)
            {
                Destroy(_ghostRenderer.gameObject);
                _ghostRenderer = null;
            }
            if (_previewCircle != null)
            {
                Destroy(_previewCircle.gameObject);
                _previewCircle = null;
            }
        }

        private void LateUpdate()
        {
            if (_airship == null)
            {
                return;
            }

            RefreshVisuals();
        }

        public void RefreshVisuals()
        {
            if (_airship == null)
            {
                return;
            }

            AirshipCarryState carry = _airship.CarryState;
            if (!carry.IsCarrying)
            {
                SetChildrenActive(false);
                SetRequestingDepositHighlight(false, _requestingFootprint);
                return;
            }

            EnsureVisualObjects();

            StructureBuildController buildCtrl = ActiveBuildControllerRegistry.Current;
            CardCatalogSO catalog = buildCtrl != null ? buildCtrl.CardCatalog : null;
            StructureCardSO card = null;
            bool hasCard = catalog != null && catalog.TryGetCard(carry.CardId, out card) && card != null;

            Vector2 footprint = hasCard ? card.FootprintSize : new Vector2(1.8f, 1.4f);
            bool requiresGround = !hasCard || card.RequiresGroundSupport;
            bool requiresGoldDeposit = hasCard && card.RequiresGoldDeposit;
            float territoryRadius = hasCard ? card.TerritoryExpansionRadius : 0f;

            Vector2 shipPos = _airship.transform.position;
            Vector2 targetPos = ResolveGhostPosition(shipPos, footprint, requiresGround, requiresGoldDeposit);

            bool withinRange = buildCtrl == null
                || buildCtrl.MaxBuildRange <= 0f
                || (targetPos - shipPos).sqrMagnitude <= buildCtrl.MaxBuildRange * buildCtrl.MaxBuildRange;

            bool withinGroundProximity = true;
            if (requiresGround && buildCtrl != null && buildCtrl.MaxGroundPlacementDistance > 0f)
            {
                float groundSurfaceY = SampleGroundY(targetPos.x);
                withinGroundProximity = GroundProximityEvaluator.IsWithinGroundProximity(
                    requiresGroundSupport: true,
                    sourceHeightY: shipPos.y,
                    groundSurfaceY: groundSurfaceY,
                    maxGroundPlacementDistance: buildCtrl.MaxGroundPlacementDistance);
            }

            TerritoryController territoryCtrl = ActiveTerritoryControllerRegistry.Current;
            bool insideTerritory = territoryCtrl == null
                || territoryCtrl.IsInsideTeamTerritory(_airship.Team, targetPos);

            bool depositValid = true;
            if (requiresGoldDeposit)
            {
                IReadOnlyList<GoldDepositNode> deposits = ActiveGoldDepositsRegistry.ActiveDeposits;
                depositValid = GoldDepositMatcher.TryFindNearestDeposit(
                    targetPos,
                    deposits,
                    out _,
                    out bool foundOccupiedOnly) && !foundOccupiedOnly;
            }

            bool canPlace = withinRange && insideTerritory && withinGroundProximity && depositValid;
            Color statusColor = canPlace
                ? new Color(0.24f, 0.94f, 0.42f, 0.45f)
                : new Color(0.95f, 0.25f, 0.25f, 0.45f);

            // Payload suspended beneath airship.
            _carriedPayloadRenderer.gameObject.SetActive(true);
            _carriedPayloadRenderer.transform.localPosition = new Vector3(0f, -0.85f, 0f);
            _carriedPayloadRenderer.transform.localScale = new Vector3(0.75f, 0.55f, 1f);
            _carriedPayloadRenderer.color = hasCard && card.Category == StructureCategory.Economy
                ? new Color(1f, 0.85f, 0.22f, 0.95f)
                : (hasCard && card.Category == StructureCategory.Territory
                    ? new Color(0.38f, 0.92f, 1f, 0.95f)
                    : Color.white);

            // Ghost footprint at target placement point (world space, detached from airship transform).
            _ghostRenderer.gameObject.SetActive(true);
            _ghostRenderer.transform.position = new Vector3(targetPos.x, targetPos.y, 0f);
            _ghostRenderer.transform.localScale = new Vector3(footprint.x, footprint.y, 1f);
            _ghostRenderer.color = statusColor;

            // Expansion circle preview when carrying Territory Balloon.
            if (territoryRadius > 0.01f)
            {
                if (_previewCircle == null)
                {
                    var ringGo = new GameObject($"GhostTerritoryPreview_{_airship.SeatId}");
                    _previewCircle = ringGo.AddComponent<TerritoryCircleVisual>();
                }

                Color ringColor = canPlace
                    ? new Color(0.32f, 0.95f, 0.55f)
                    : new Color(0.96f, 0.3f, 0.3f);
                _previewCircle.Configure(targetPos, territoryRadius, ringColor, fillAlpha: 0.07f, borderAlpha: 0.85f, borderWidth: 0.06f);
            }
            else if (_previewCircle != null)
            {
                _previewCircle.gameObject.SetActive(false);
            }

            if (_airship.IsOwner)
            {
                SetRequestingDepositHighlight(requiresGoldDeposit, footprint);
            }
            else
            {
                SetRequestingDepositHighlight(false, footprint);
            }
        }

        private Vector2 ResolveGhostPosition(Vector2 shipPos, Vector2 footprint, bool requiresGround, bool requiresGoldDeposit)
        {
            float x = shipPos.x;
            float y = shipPos.y;
            float halfH = footprint.y * 0.5f;

            if (requiresGround)
            {
                y = SampleGroundY(x) + halfH;
            }

            Vector2 projected = new(x, y);
            if (requiresGoldDeposit)
            {
                IReadOnlyList<GoldDepositNode> deposits = ActiveGoldDepositsRegistry.ActiveDeposits;
                if (GoldDepositMatcher.TryFindNearestDeposit(projected, deposits, out IGoldDeposit deposit, out _)
                    && deposit != null)
                {
                    float groundY = requiresGround ? SampleGroundY(deposit.AnchorWorld.x) + halfH : deposit.AnchorWorld.y;
                    return new Vector2(deposit.AnchorWorld.x, groundY);
                }
            }

            return projected;
        }

        private static float SampleGroundY(float worldX)
        {
            RaycastHit2D[] hits = Physics2D.RaycastAll(new Vector2(worldX, 16f), Vector2.down, 28f);
            bool found = false;
            float bestY = -2f;
            for (int i = 0; i < hits.Length; i++)
            {
                Collider2D col = hits[i].collider;
                if (col == null || col.isTrigger)
                {
                    continue;
                }

                if (col.GetComponentInParent<IStructure>() != null
                    || col.GetComponentInParent<AirshipController>() != null
                    || col.GetComponentInParent<DockableAnchorNode>() != null
                    || col.GetComponentInParent<GoldDepositNode>() != null)
                {
                    continue;
                }

                if (!found || hits[i].point.y > bestY)
                {
                    found = true;
                    bestY = hits[i].point.y;
                }
            }

            return bestY;
        }

        private void SetRequestingDepositHighlight(bool requesting, Vector2 footprint)
        {
            _requestingFootprint = footprint;
            _isRequestingGoldDeposit = requesting;

            bool changed = requesting
                ? s_depositRequesters.Add(this)
                : s_depositRequesters.Remove(this);

            if (!changed && !_wasHighlightingDeposits && !requesting)
            {
                return;
            }

            _wasHighlightingDeposits = requesting;
            RefreshAllDepositHighlights();
        }

        private static void RefreshAllDepositHighlights()
        {
            s_depositRequesters.RemoveWhere(v => v == null || v._airship == null || !v._isRequestingGoldDeposit);
            IReadOnlyList<GoldDepositNode> deposits = ActiveGoldDepositsRegistry.ActiveDeposits;
            if (s_depositRequesters.Count == 0)
            {
                for (int i = 0; i < deposits.Count; i++)
                {
                    if (deposits[i] != null)
                    {
                        deposits[i].SetCarrySpotState(GoldDepositSpotState.Neutral);
                    }
                }
                return;
            }

            TerritoryController territoryCtrl = ActiveTerritoryControllerRegistry.Current;
            IReadOnlyList<TerritoryZone> zones = territoryCtrl != null ? territoryCtrl.Zones : null;
            StructureBuildController buildCtrl = ActiveBuildControllerRegistry.Current;
            float maxBuildRange = buildCtrl != null ? buildCtrl.MaxBuildRange : 0f;

            for (int i = 0; i < deposits.Count; i++)
            {
                GoldDepositNode deposit = deposits[i];
                if (deposit == null)
                {
                    continue;
                }

                GoldDepositSpotState bestState = GoldDepositSpotState.OutsideTerritory;
                int bestPriority = -1;

                foreach (AirshipPlacementGhostVisual requester in s_depositRequesters)
                {
                    float groundY = SampleGroundY(deposit.AnchorWorld.x);
                    float halfH = requester._requestingFootprint.y * 0.5f;
                    Vector2 resolvedPos = new Vector2(
                        deposit.AnchorWorld.x,
                        groundY + halfH);

                    GoldDepositSpotState state = GoldDepositMatcher.EvaluateDepositSpot(
                        deposit,
                        requester._airship.Team,
                        zones,
                        requester._airship.transform.position,
                        maxBuildRange,
                        resolvedPos,
                        buildCtrl != null ? buildCtrl.MaxGroundPlacementDistance : 0f,
                        groundY);

                    int priority = GetHighlightPriority(state);
                    if (priority > bestPriority)
                    {
                        bestPriority = priority;
                        bestState = state;
                    }

                    if (bestState == GoldDepositSpotState.Valid)
                    {
                        break;
                    }
                }

                deposit.SetCarrySpotState(bestState);
            }
        }

        private static int GetHighlightPriority(GoldDepositSpotState state)
        {
            return state switch
            {
                GoldDepositSpotState.Valid => 4,
                GoldDepositSpotState.Occupied => 3,
                GoldDepositSpotState.OutOfRange => 2,
                GoldDepositSpotState.OutsideTerritory => 1,
                _ => 0
            };
        }

        private void SetChildrenActive(bool active)
        {
            if (_carriedPayloadRenderer != null) _carriedPayloadRenderer.gameObject.SetActive(active);
            if (_ghostRenderer != null) _ghostRenderer.gameObject.SetActive(active);
            if (_previewCircle != null) _previewCircle.gameObject.SetActive(active);
        }

        private void EnsureVisualObjects()
        {
            Sprite sprite = GetOrCreateUnitSquareSprite();

            if (_carriedPayloadRenderer == null)
            {
                var go = new GameObject("CarriedPayloadVisual");
                go.transform.SetParent(transform, false);
                _carriedPayloadRenderer = go.AddComponent<SpriteRenderer>();
                _carriedPayloadRenderer.sprite = sprite;
                _carriedPayloadRenderer.sortingOrder = 6;
            }

            if (_ghostRenderer == null)
            {
                var go = new GameObject($"PlacementGhost_{_airship.SeatId}");
                _ghostRenderer = go.AddComponent<SpriteRenderer>();
                _ghostRenderer.sprite = sprite;
                _ghostRenderer.sortingOrder = 2;
            }
        }

        private static Sprite GetOrCreateUnitSquareSprite()
        {
            if (s_unitSquareSprite != null)
            {
                return s_unitSquareSprite;
            }

            s_unitSquareTex = new Texture2D(4, 4, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.DontSave
            };
            var pixels = new Color32[16];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(255, 255, 255, 255);
            }
            s_unitSquareTex.SetPixels32(pixels);
            s_unitSquareTex.Apply();

            s_unitSquareSprite = Sprite.Create(s_unitSquareTex, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 4f);
            s_unitSquareSprite.hideFlags = HideFlags.DontSave;
            return s_unitSquareSprite;
        }
    }
}
