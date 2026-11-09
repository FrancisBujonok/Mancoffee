using Mancoffee.Customers;
using Mancoffee.Staff;
using UnityEngine;

namespace Mancoffee.Core
{
    public enum DayPhase
    {
        Abierto,
        Cerrando,
        Resumen
    }

    /// <summary>
    /// Reloj del juego y velocidad. El local abre a las 8 y cierra a las 20; después se atiende a los que
    /// quedan y, cuando se va el último, se pagan sueldos y se muestra el resumen del día.
    /// </summary>
    [DefaultExecutionOrder(-80)]
    public class DayCycle : MonoBehaviour
    {
        public static DayCycle Instance { get; private set; }

        [SerializeField] private int openHour = 8;
        [SerializeField] private int closeHour = 20;
        [Tooltip("A esta hora los últimos clientes se van sí o sí.")]
        [SerializeField] private int lastCallHour = 22;
        [Tooltip("Minutos de juego por segundo real a velocidad 1x. Con 3, un día abierto dura 4 minutos.")]
        [SerializeField] private float gameMinutesPerSecond = 3f;

        [SerializeField] private CustomerSpawner spawner;
        [SerializeField] private StaffManager staff;
        [SerializeField] private SaveSystem saveSystem;

        private bool modalPause;
        private int speedBeforePause = 1;

        public float Minutes { get; private set; }
        public DayPhase Phase { get; private set; }
        public int Speed { get; private set; } = 1;
        public bool IsOpen => Phase == DayPhase.Abierto;
        public DailyLedger LastReport { get; private set; }
        public int LastSalaries { get; private set; }

        public string ClockText => $"{(int)(Minutes / 60f) % 24:00}:{(int)(Minutes % 60f):00}";

        private void Awake()
        {
            Instance = this;
            ResetDay();
            ApplyTimeScale();
        }

        private void OnDestroy()
        {
            if (Instance == this) Time.timeScale = 1f;
        }

        private void Update()
        {
            if (Phase == DayPhase.Resumen) return;

            Minutes += Time.deltaTime * gameMinutesPerSecond;

            if (Phase == DayPhase.Abierto && Minutes >= closeHour * 60f)
            {
                Phase = DayPhase.Cerrando;
                CafeState.Instance.Notify("Cerró la cafetería: se atiende a los que quedan");
            }

            if (Phase == DayPhase.Cerrando)
            {
                if (Minutes >= lastCallHour * 60f) spawner.SendEveryoneHome();
                if (spawner.ActiveCount == 0) EndDay();
            }
        }

        private void EndDay()
        {
            LastSalaries = staff.TotalSalaries;
            CafeState.Instance.ForceSpend(LastSalaries, MoneyCategory.Sueldos);
            LastReport = CafeState.Instance.Today;
            Phase = DayPhase.Resumen;
            SetModalPause(true);
        }

        /// <summary>Desde el resumen: empieza el día siguiente y guarda la partida.</summary>
        public void StartNextDay()
        {
            CafeState.Instance.StartNewDay();
            ResetDay();
            SetModalPause(false);
            saveSystem.Save();
        }

        public void ResetDay()
        {
            Minutes = openHour * 60f;
            Phase = DayPhase.Abierto;
        }

        public void SetSpeed(int speed)
        {
            Speed = Mathf.Clamp(speed, 0, 3);
            if (Speed > 0) speedBeforePause = Speed;
            ApplyTimeScale();
        }

        public void TogglePause() => SetSpeed(Speed == 0 ? speedBeforePause : 0);

        /// <summary>Pausa mientras hay una ventana abierta (resumen, menú inicial), sin perder la velocidad elegida.</summary>
        public void SetModalPause(bool paused)
        {
            modalPause = paused;
            ApplyTimeScale();
        }

        private void ApplyTimeScale() => Time.timeScale = modalPause ? 0f : Speed;
    }
}
