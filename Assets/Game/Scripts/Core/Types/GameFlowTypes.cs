namespace Game.Core
{
    public static class GameSceneNames
    {
        public const string MainMenu = "MainMenu";
        public const string CannonBrawlArena = "CannonBrawlArena";
        public const string SteamOnlineLobby = "NetworkTest";
    }

    public enum MenuNavigationAction : byte
    {
        LoadArenaLocalPvP = 0,
        LoadSteamOnlineLobby = 1,
        BlockedWorkInProgress = 2
    }

    public readonly struct MenuRouteDecision
    {
        public readonly MatchMode Mode;
        public readonly MenuNavigationAction Action;
        public readonly string TargetScene;
        public readonly bool IsEnabled;
        public readonly string StatusMessage;

        public MenuRouteDecision(
            MatchMode mode,
            MenuNavigationAction action,
            string targetScene,
            bool isEnabled,
            string statusMessage)
        {
            Mode = mode;
            Action = action;
            TargetScene = targetScene ?? string.Empty;
            IsEnabled = isEnabled;
            StatusMessage = statusMessage ?? string.Empty;
        }
    }

    public static class MainMenuRouter
    {
        public static MenuRouteDecision Resolve(MatchMode mode)
        {
            switch (mode)
            {
                case MatchMode.LocalPvP:
                    return new MenuRouteDecision(
                        MatchMode.LocalPvP,
                        MenuNavigationAction.LoadArenaLocalPvP,
                        GameSceneNames.CannonBrawlArena,
                        true,
                        "Local PvP başlatılıyor...");

                case MatchMode.OnlinePvP:
                    return new MenuRouteDecision(
                        MatchMode.OnlinePvP,
                        MenuNavigationAction.LoadSteamOnlineLobby,
                        GameSceneNames.SteamOnlineLobby,
                        true,
                        "Steam / FishNet çevrimiçi lobi açılıyor...");

                case MatchMode.Story:
                default:
                    return new MenuRouteDecision(
                        MatchMode.Story,
                        MenuNavigationAction.BlockedWorkInProgress,
                        string.Empty,
                        false,
                        "Hikaye Modu şu anda yapım aşamasında (WIP).");
            }
        }

        public static bool CanLaunch(MatchMode mode)
        {
            return Resolve(mode).IsEnabled;
        }
    }

    /// <summary>
    /// Lightweight cross-scene handoff container carrying the menu's selected MatchMode.
    /// </summary>
    public static class MatchLaunchConfig
    {
        public static MatchMode SelectedMode { get; private set; } = MatchMode.LocalPvP;
        public static bool HasExplicitSelection { get; private set; }

        public static void SelectMode(MatchMode mode)
        {
            SelectedMode = mode;
            HasExplicitSelection = true;
        }

        public static void Reset()
        {
            SelectedMode = MatchMode.LocalPvP;
            HasExplicitSelection = false;
        }
    }
}
