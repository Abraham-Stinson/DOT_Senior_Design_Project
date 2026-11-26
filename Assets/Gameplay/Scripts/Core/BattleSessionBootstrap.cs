
using UnityEngine;

namespace PirateGame.Gameplay
{
    public class BattleSessionBootstrap : MonoBehaviour
    {
        [Header("Battle Configuration")]
        [SerializeField] private BattleMode gameMode =
            BattleMode.LocalPvP;

        [Header("Scene Players")]
        [SerializeField] private ZeppelinMotor player1Motor;
        [SerializeField] private ZeppelinMotor player2Motor;

        [SerializeField] private ZeppelinLocalInput player1Input;
        [SerializeField] private ZeppelinLocalInput player2Input;

        public BattleMode CurrentMode => gameMode;

        public bool IsOnline =>
            gameMode == BattleMode.OnlinePvP;

        private void Awake()
        {
            ConfigurePlayers();
        }

        private void ConfigurePlayers()
        {
            if (player1Motor == null ||
                player2Motor == null ||
                player1Input == null ||
                player2Input == null)
            {
                Debug.LogError(
                    "Battle Session: Missing player references.");
                enabled = false;
                return;
            }

            player1Motor.SetMoveInput(Vector2.zero);
            player2Motor.SetMoveInput(Vector2.zero);

            player1Input.enabled = false;
            player2Input.enabled = false;

            player1Motor.enabled = false;
            player2Motor.enabled = false;

            switch (gameMode)
            {
                case BattleMode.Campaign:
                case BattleMode.BattleVsAI:

                    player1Motor.enabled = true;
                    player2Motor.enabled = true;
                    player1Input.enabled = true;

                    // Player 2 will receive AI input later.
                    break;

                case BattleMode.LocalPvP:

                    player1Motor.enabled = true;
                    player2Motor.enabled = true;

                    player1Input.enabled = true;
                    player2Input.enabled = true;
                    break;

                case BattleMode.OnlinePvP:

                    // Online players will be spawned and
                    // controlled by the network authority.
                    player1Motor.gameObject.SetActive(false);
                    player2Motor.gameObject.SetActive(false);
                    break;
            }

            Debug.Log("Battle Mode: " + gameMode);
        }
    }
}
