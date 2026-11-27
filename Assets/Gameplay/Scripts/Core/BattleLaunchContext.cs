using System;

namespace PirateGame.Gameplay
{
    // Two bits per slot. Zero is empty; one, two and three are the three starter cards.
    public static class BattleCards
    {
        public const ushort Starters = 1 | (2 << 2) | (3 << 4);
        public static int Card(ushort hand, int slot) => ((hand >> (2 * slot)) & 3) - 1;
        public static bool Valid(ushort hand)
        {
            if ((hand & 3) != 1 || hand >= 1024) return false;
            int seen = 0;
            for (int i = 0; i < 5; i++)
            {
                int card = Card(hand, i);
                if (card < 0) continue;
                if ((seen & (1 << card)) != 0) return false;
                seen |= 1 << card;
            }
            return true;
        }
    }

    public sealed class BattleLaunchContext
    {
        public const string DefaultMap = "Level_01";
        public readonly string MapId;
        public readonly BattleMode Mode;
        public readonly int CampaignLevelId, AiDifficulty;
        public readonly ushort BlueLoadout, RedLoadout;
        public readonly ulong LobbyId;
        public readonly string MatchId;
        // Team 1 is blue, team 2 red. Online player IDs are authenticated FishNet connection IDs.
        public readonly int BluePlayerId, RedPlayerId;
        public BattleLaunchContext(BattleMode mode, int level, int difficulty, ushort blue, ushort red,
            ulong lobby = 0, string match = "", int blueId = 0, int redId = 1, string mapId = DefaultMap)
        {
            if (mapId != DefaultMap || !BattleCards.Valid(blue) || !BattleCards.Valid(red) || !Enum.IsDefined(typeof(BattleMode), mode)
                || level != (mode == BattleMode.Campaign ? 1 : 0) || difficulty < 0 || difficulty > 2)
                throw new ArgumentException("Invalid battle configuration.");
            if (mode == BattleMode.OnlinePvP && (lobby == 0 || string.IsNullOrEmpty(match) || blueId < 0 || redId < 0 || blueId == redId))
                throw new ArgumentException("Missing authenticated roster.");
            MapId = mapId; Mode = mode; CampaignLevelId = level; AiDifficulty = difficulty;
            BlueLoadout = blue; RedLoadout = red; LobbyId = lobby; MatchId = match;
            BluePlayerId = blueId; RedPlayerId = redId;
        }
    }
}
