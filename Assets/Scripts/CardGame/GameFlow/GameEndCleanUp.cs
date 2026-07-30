using UnityEngine;

/// <summary>
/// Centraliza TODO lo que hay que "matar"/limpiar apenas la partida
/// termina (victoria, derrota por boss, o derrota por sospecha) - antes
/// esto estaba repartido en cada script por separado (TradeManager,
/// TradeUIManager), lo que hacia facil olvidarse de conectar alguno. Ahora
/// hay un solo lugar central para esto.
///
/// Se dispara INMEDIATAMENTE cuando GameManager.OnResultadoCambio marca la
/// partida como terminada - no espera a que alguien apriete "Volver a
/// jugar" ni al reinicio real del host. En ese mismo instante:
///   - Se limpia la mano visual de este jugador.
///   - Se cierra el panel de trampas (y sus sub-paneles: categorias,
///     seleccionar companero de intercambio).
///   - Se cierran (por las dudas, ademas de que TradeUIManager ya se
///     encarga de esto por su cuenta) los paneles del flujo de intercambio.
///
/// Vive en el GameScene, un solo objeto, siempre activo.
/// </summary>
public class GameEndCleanup : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private GameManager gameManager;
    [SerializeField] private HandManager handManager;
    [SerializeField] private TrapsPanelUI trapsPanelUI;
    [SerializeField] private TradeUIManager tradeUI;

    private void Start()
    {
        if (gameManager != null)
        {
            gameManager.OnResultadoCambio += ManejarFinDePartida;
        }
        else
        {
            Debug.LogError("[GameEndCleanup] No se asignó Game Manager en el Inspector - nada de esta limpieza va a funcionar.");
        }
    }

    private void OnDestroy()
    {
        if (gameManager != null)
        {
            gameManager.OnResultadoCambio -= ManejarFinDePartida;
        }
    }

    private void ManejarFinDePartida(ResultadoPartida resultado)
    {
        if (resultado == ResultadoPartida.EnCurso)
        {
            return;
        }

        Debug.Log($"[GameEndCleanup] Partida terminada ({resultado}) - limpiando mano, panel de trampas e intercambio.");

        if (handManager != null)
        {
            handManager.LimpiarManoCompleta();
        }

        if (trapsPanelUI != null)
        {
            trapsPanelUI.CerrarPanel();
        }

        if (tradeUI != null)
        {
            tradeUI.CerrarPanelSeleccionJugador();
            tradeUI.CerrarTodo();
        }
    }
}