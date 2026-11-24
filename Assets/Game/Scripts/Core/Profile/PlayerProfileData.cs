using System;
using System.Collections.Generic;

namespace Game.Core
{
    [Serializable]
    public sealed class PlayerProfileData
    {
        public int SchemaVersion = 1;
        public int SoftCurrency;
        public List<ushort> UnlockedCardIds = new();
        public List<ushort> LastUsedLoadout = new();
    }
}
