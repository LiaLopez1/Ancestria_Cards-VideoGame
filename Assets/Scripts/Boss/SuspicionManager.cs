using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Nivel de sospecha COMPARTIDO entre los 3 jugadores (un solo "equipo"
/// contra el boss) - no es por jugador. Autoridad total del servidor: los
/// clientes solo piden permiso via ServerRpc y leen el valor sincronizado
/// para mostrar la barra.
///
/// PROTOTIPO: los 4 botones están siempre activos (sin validar de quién es
/// el turno) - la lógica real de la trampa (qué carta se pide/dice/
/// intercambia) todavía no existe. Esto solo prueba que la sospecha suba
/// correctamente según el estado de atención del boss, y que el proceso de
/// intercambio suba de a poco mientras está en curso.
///
/// Conectar los botones desde el Inspector (OnClick) directo a los 4
/// métodos Rpc de abajo - no hace falta ningún script de UI intermedio.
/// </summary>
public class SuspicionManager : NetworkBehaviour
{
    [Header("Referencias")]
    [SerializeField] private BossManager bossManager;
    [SerializeField] private GameManager gameManager;

    [Header("Configuración")]
    [SerializeField] private float sospechaMaxima = 10f;
    [Tooltip("Cuánto suma Decir/Pedir si el boss está revisando cartas.")]
    [SerializeField] private float incrementoPorRevisar = 1f;
    [Tooltip("Cuánto suma Decir/Pedir si el boss está mirando a los oponentes.")]
    [SerializeField] private float incrementoPorMirar = 2f;
    [Tooltip("Cuánto suma el intercambio por cada intervalo, mientras está en curso.")]
    [SerializeField] private float incrementoPorIntervaloIntercambio = 0.5f;
    [SerializeField] private float intervaloIncrementoIntercambio = 1f;

    // Compartido entre los 3 jugadores - una sola barra, no una por cliente.
    private readonly NetworkVariable<float> nivelSospecha = new NetworkVariable<float>(0f);

    // Habilita/deshabilita "Aceptar intercambio" del lado de la UI, y evita
    // que se pueda arrancar un segundo intercambio mientras uno ya está en
    // curso - el único "estado" que hace falta para esto, no algo más grande.
    private readonly NetworkVariable<bool> intercambioEnCurso = new NetworkVariable<bool>(false);

    private Coroutine corrutinaIntercambio;

    public float NivelSospecha => nivelSospecha.Value;
    public float SospechaMaxima => sospechaMaxima;
    public bool IntercambioEnCurso => intercambioEnCurso.Value;

    /// <summary>Para que la UI (barra, texto) se actualice sin tener que hacer polling.</summary>
    public event Action<float> OnSospechaCambio;
    public event Action<bool> OnIntercambioCambio;

    public override void OnNetworkSpawn()
    {
        nivelSospecha.OnValueChanged += (anterior, nuevo) => OnSospechaCambio?.Invoke(nuevo);
        intercambioEnCurso.OnValueChanged += (anterior, nuevo) => OnIntercambioCambio?.Invoke(nuevo);
    }

    // ---------- Decir / Pedir: instantáneas, dependen del estado del boss ----------

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void SolicitarTrampaDecirRpc()
    {
        SumarSospechaSegunAtencionDelBoss("Decir carta");
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void SolicitarTrampaPedirRpc()
    {
        SumarSospechaSegunAtencionDelBoss("Pedir color");
    }

    private void SumarSospechaSegunAtencionDelBoss(string nombreAccion)
    {
        if (bossManager == null)
        {
            Debug.LogWarning("[SuspicionManager] No se asignó BossManager - no se puede consultar su atención.");
            return;
        }

        float incremento = bossManager.AtencionActual == BossManager.EstadoAtencionBoss.RevisandoCartas
            ? incrementoPorRevisar
            : incrementoPorMirar;

        SumarSospecha(incremento);

        Debug.Log($"[Sospecha] {nombreAccion} mientras el boss está {bossManager.AtencionActual} -> +{incremento} (total: {nivelSospecha.Value}/{sospechaMaxima})");
    }

    // ---------- Intercambiar / Aceptar: proceso con duración ----------

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void IniciarIntercambioRpc()
    {
        // TODO (más adelante): validar que quien llama esto tenga el turno
        // actual (turnManager.EsTurnoDelSlot(...)) - por ahora, sin validar,
        // para poder probar la funcionalidad primero (según lo acordado).

        if (intercambioEnCurso.Value)
        {
            Debug.LogWarning("[SuspicionManager] Ya hay un intercambio en curso.");
            return;
        }

        intercambioEnCurso.Value = true;
        corrutinaIntercambio = StartCoroutine(IncrementarSospechaMientrasIntercambia());

        Debug.Log("[Sospecha] Intercambio iniciado - empieza a subir de a poco.");
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void AceptarIntercambioRpc()
    {
        if (!intercambioEnCurso.Value)
        {
            Debug.LogWarning("[SuspicionManager] No hay ningún intercambio en curso para aceptar.");
            return;
        }

        if (corrutinaIntercambio != null)
        {
            StopCoroutine(corrutinaIntercambio);
            corrutinaIntercambio = null;
        }

        intercambioEnCurso.Value = false;

        Debug.Log($"[Sospecha] Intercambio aceptado - deja de subir. Total: {nivelSospecha.Value}/{sospechaMaxima}");
    }

    private IEnumerator IncrementarSospechaMientrasIntercambia()
    {
        while (true)
        {
            yield return new WaitForSeconds(intervaloIncrementoIntercambio);
            SumarSospecha(incrementoPorIntervaloIntercambio);
        }
    }

    // ---------- Compartido ----------

    private void SumarSospecha(float cantidad)
    {
        if (!IsServer)
        {
            return;
        }

        nivelSospecha.Value = Mathf.Min(sospechaMaxima, nivelSospecha.Value + cantidad);

        Debug.Log($"[Sospecha] +{cantidad} (total: {nivelSospecha.Value}/{sospechaMaxima})");

        if (nivelSospecha.Value >= sospechaMaxima)
        {
            if (gameManager == null)
            {
                Debug.LogError("[SuspicionManager] La barra llegó al máximo, pero no se asignó GameManager en el Inspector - no se puede declarar la derrota.");
                return;
            }

            gameManager.DeclararDerrotaPorSospecha();
        }
    }
}