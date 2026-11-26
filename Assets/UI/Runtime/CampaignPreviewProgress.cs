namespace PirateGame.UI
{
    // Exists for one preview session only. Nothing is written to a player profile.
    public sealed class CampaignPreviewProgress
    {
        private readonly bool[] won = new bool[2];
        public int LastVictory => won[1] ? 2 : won[0] ? 1 : 0;
        public int AccessibleLevel => won[0] ? 2 : 1;
        public int Stars => (won[0] ? 2 : 0) + (won[1] ? 2 : 0);
        public int StarsFor(int level) => level >= 1 && level <= won.Length && won[level - 1] ? 2 : 0;
        public bool RecordPreviewWin(int level)
        {
            if (level < 1 || level > AccessibleLevel || won[level - 1]) return false;
            won[level - 1] = true;
            return true;
        }
    }
}