using Mancoffee.GridSystem;
using Mancoffee.Staff;
using UnityEngine;

namespace Mancoffee.Core
{
    /// <summary>
    /// Arranque de la partida: si hay un guardado, pregunta si continuar; si no, arma una partida nueva
    /// (paredes alrededor del local y dos empleados).
    /// Corre después del resto de los Start para que todos los sistemas ya estén listos.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class GameBootstrap : MonoBehaviour
    {
        [SerializeField] private GridManager grid;
        [SerializeField] private StaffManager staff;
        [SerializeField] private SaveSystem saveSystem;
        [SerializeField] private DayCycle dayCycle;
        [SerializeField] private int startingStaff = 2;

        public bool AwaitingChoice { get; private set; }

        private void Start()
        {
            if (SaveSystem.HasSave)
            {
                AwaitingChoice = true;
                dayCycle.SetModalPause(true);
            }
            else
            {
                NewGame();
            }
        }

        public void ChooseContinue()
        {
            AwaitingChoice = false;
            dayCycle.SetModalPause(false);
            if (!saveSystem.Load()) NewGame();
        }

        public void ChooseNewGame()
        {
            AwaitingChoice = false;
            dayCycle.SetModalPause(false);
            NewGame();
        }

        private void NewGame()
        {
            CafeState.Instance.ResetNewGame();
            Inventory.Instance?.ResetNewGame();
            grid.BuildPerimeterWalls();
            for (int i = 0; i < startingStaff; i++) staff.Hire(false);
            dayCycle.ResetDay();
        }
    }
}
