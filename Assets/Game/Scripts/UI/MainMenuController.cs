using Game.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Game.UI
{
    public sealed class MainMenuController : MonoBehaviour
    {
        [SerializeField] private Button _localPvPButton = null;
        [SerializeField] private Button _onlinePvPButton = null;
        [SerializeField] private Button _storyWipButton = null;
        [SerializeField] private Button _quitButton = null;
        [SerializeField] private Text _statusLabel = null;

        private void Awake()
        {
            if (_localPvPButton != null)
            {
                _localPvPButton.onClick.AddListener(SelectLocalPvP);
            }

            if (_onlinePvPButton != null)
            {
                _onlinePvPButton.onClick.AddListener(SelectOnlinePvP);
            }

            if (_storyWipButton != null)
            {
                _storyWipButton.interactable = false;
                _storyWipButton.onClick.AddListener(SelectStoryWip);
            }

            if (_quitButton != null)
            {
                _quitButton.onClick.AddListener(QuitGame);
            }

            SetStatus(string.Empty);
        }

        private void OnDestroy()
        {
            if (_localPvPButton != null)
            {
                _localPvPButton.onClick.RemoveListener(SelectLocalPvP);
            }

            if (_onlinePvPButton != null)
            {
                _onlinePvPButton.onClick.RemoveListener(SelectOnlinePvP);
            }

            if (_storyWipButton != null)
            {
                _storyWipButton.onClick.RemoveListener(SelectStoryWip);
            }

            if (_quitButton != null)
            {
                _quitButton.onClick.RemoveListener(QuitGame);
            }
        }

        public void SelectLocalPvP()
        {
            TryLaunchMode(MatchMode.LocalPvP);
        }

        public void SelectOnlinePvP()
        {
            TryLaunchMode(MatchMode.OnlinePvP);
        }

        public void SelectStoryWip()
        {
            TryLaunchMode(MatchMode.Story);
        }

        public bool TryLaunchMode(MatchMode mode)
        {
            MenuRouteDecision decision = MainMenuRouter.Resolve(mode);
            SetStatus(decision.StatusMessage);

            if (!decision.IsEnabled)
            {
                return false;
            }

            if (!Application.CanStreamedLevelBeLoaded(decision.TargetScene))
            {
                SetStatus($"Sahne bulunamadı: {decision.TargetScene}.");
                return false;
            }

            MatchLaunchConfig.SelectMode(mode);
            SceneManager.LoadScene(decision.TargetScene, LoadSceneMode.Single);
            return true;
        }

        public void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void SetStatus(string text)
        {
            if (_statusLabel != null)
            {
                _statusLabel.text = text;
            }
        }
    }
}
