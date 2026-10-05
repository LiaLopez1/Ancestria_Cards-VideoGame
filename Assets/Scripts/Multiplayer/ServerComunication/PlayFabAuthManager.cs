using System;
using System.Collections.Generic;
using PlayFab;
using PlayFab.ClientModels;
using UnityEngine;

/// <summary>
/// Maneja el login del jugador contra PlayFab.
/// Para el prototipo usamos LoginWithCustomID con el DeviceUniqueIdentifier,
/// asi no pedimos usuario/contrasena todavia (eso lo agregamos despues).
///
/// MODO OFFLINE: el jugador lo elige a mano (boton "Modo offline" en
/// StartupFlowUI). No se activa solo: asi un fallo puntual de red nunca
/// deja a un jugador online atrapado en offline sin saberlo.
/// </summary>
public class PlayFabAuthManager : MonoBehaviour
{
    public static PlayFabAuthManager Instance { get; private set; }

    public string PlayFabId { get; private set; }
    public string DisplayName { get; private set; }
    public string EntityId { get; private set; }
    public string EntityType { get; private set; }

    /// <summary>
    /// ¿Este jugador ya completó el tutorial? Se consulta a PlayFab (UserData,
    /// ligado a la cuenta/PC) apenas termina el login, ANTES de disparar
    /// OnLoginSuccess - así, cuando ese evento llega, este valor ya está
    /// listo para decidir qué botones mostrar.
    /// </summary>
    public bool TutorialCompletado { get; private set; }

    /// <summary>true cuando el jugador eligio jugar 100% local (sin PlayFab ni Relay).</summary>
    public bool ModoOffline { get; private set; }

    /// <summary>
    /// true si el ultimo intento de login fallo por falta de conexion
    /// (no por otro tipo de error, como cuenta bloqueada o titulo mal configurado).
    /// </summary>
    public bool ErrorDeConexion { get; private set; }

    private const string TutorialCompletadoKey = "TutorialCompletado";

    // Copia local del estado del tutorial, para poder usarlo sin conexion.
    private const string TutorialLocalKey = "TutorialCompletadoLocal";

    public bool HasDisplayName => !string.IsNullOrEmpty(DisplayName);

    public event Action OnLoginSuccess;
    public event Action<string> OnLoginFailed;
    public event Action OnDisplayNameUpdated;

    /// <summary>Se dispara cuando MarcarTutorialCompletado() confirma el guardado en PlayFab.</summary>
    public event Action OnTutorialCompletadoConfirmado;

    private void Awake()
    {
        // Singleton simple para poder acceder desde cualquier escena/menu.
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// ignorarChequeoDeRed = true: intenta el login de verdad aunque el
    /// dispositivo diga que no hay red (Application.internetReachability
    /// puede dar falsos "sin conexion" con VPN o portales cautivos).
    /// </summary>
    public void Login(bool ignorarChequeoDeRed = false)
    {
        ModoOffline = false;
        ErrorDeConexion = false;

        if (!ignorarChequeoDeRed && Application.internetReachability == NetworkReachability.NotReachable)
        {
            ErrorDeConexion = true;
            Debug.LogWarning("[PlayFab] El dispositivo reporta que no hay conexion a internet.");
            OnLoginFailed?.Invoke("Sin conexión a internet.");
            return;
        }

        string customId = SystemInfo.deviceUniqueIdentifier;

        var request = new LoginWithCustomIDRequest
        {
            CustomId = customId,
            CreateAccount = true,
            InfoRequestParameters = new GetPlayerCombinedInfoRequestParams
            {
                GetPlayerProfile = true
            }
        };

        PlayFabClientAPI.LoginWithCustomID(request, OnLoginResult, OnLoginError);
    }

    /// <summary>
    /// Activa el modo 100% local. Lo llama StartupFlowUI cuando el jugador
    /// aprieta "Modo offline". No toca la red para nada.
    /// </summary>
    public void IniciarModoOffline()
    {
        ModoOffline = true;
        ErrorDeConexion = false;
        PlayFabId = "offline";
        EntityId = null;
        EntityType = null;
        TutorialCompletado = PlayerPrefs.GetInt(TutorialLocalKey, 0) == 1;

        Debug.Log("[PlayFab] Modo OFFLINE activado por el jugador.");
        OnLoginSuccess?.Invoke();
    }

    /// <summary>
    /// Limpia el estado de sesion para poder reintentar el login desde cero
    /// (por ejemplo, desde el boton "Reintentar").
    /// </summary>
    public void ReiniciarSesion()
    {
        ModoOffline = false;
        ErrorDeConexion = false;
        PlayFabId = null;
    }

    private void OnLoginResult(LoginResult result)
    {
        // El jugador eligio offline mientras el login seguia en curso:
        // la respuesta tardia no debe pisar ese modo.
        if (ModoOffline) return;

        PlayFabId = result.PlayFabId;
        DisplayName = result.InfoResultPayload?.PlayerProfile?.DisplayName;
        EntityId = result.EntityToken?.Entity?.Id;
        EntityType = result.EntityToken?.Entity?.Type;

        Debug.Log($"[PlayFab] Login OK. PlayFabId: {PlayFabId} (deviceId usado: {SystemInfo.deviceUniqueIdentifier}). HasDisplayName: {HasDisplayName}");

        // No disparamos OnLoginSuccess todavia - primero hay que saber si
        // el tutorial ya esta completo, para que quien escuche el evento
        // (StartupFlowUI) ya tenga ese dato listo de una.
        ConsultarEstadoTutorial();
    }

    private void ConsultarEstadoTutorial()
    {
        var request = new GetUserDataRequest { Keys = new List<string> { TutorialCompletadoKey } };

        PlayFabClientAPI.GetUserData(request,
            result =>
            {
                if (ModoOffline) return;

                TutorialCompletado = result.Data != null
                    && result.Data.TryGetValue(TutorialCompletadoKey, out UserDataRecord registro)
                    && registro.Value == "true";

                // Guardamos una copia local para poder usarla sin conexion.
                PlayerPrefs.SetInt(TutorialLocalKey, TutorialCompletado ? 1 : 0);
                PlayerPrefs.Save();

                Debug.Log($"[PlayFab] Estado del tutorial: {(TutorialCompletado ? "ya completado" : "todavía no")}.");

                OnLoginSuccess?.Invoke();
            },
            error =>
            {
                if (ModoOffline) return;

                Debug.LogError($"[PlayFab] Error al consultar el estado del tutorial: {error.GenerateErrorReport()}");

                // Si no podemos saberlo por PlayFab, usamos la copia local. Si
                // tampoco hay copia, asumimos que YA lo completo - mejor eso
                // que bloquear a un jugador existente por un error de red
                // puntual al consultar este dato.
                TutorialCompletado = PlayerPrefs.HasKey(TutorialLocalKey)
                    ? PlayerPrefs.GetInt(TutorialLocalKey) == 1
                    : true;

                OnLoginSuccess?.Invoke();
            }
        );
    }

    private void OnLoginError(PlayFabError error)
    {
        // El jugador ya eligio offline mientras esperaba: ignoramos el error.
        if (ModoOffline) return;

        Debug.LogError($"[PlayFab] Error de login: {error.GenerateErrorReport()}");

        ErrorDeConexion = error.Error == PlayFabErrorCode.ConnectionError;
        OnLoginFailed?.Invoke(error.ErrorMessage);
    }

    /// <summary>
    /// El nick es "oneshot" (se pide siempre, se pisa cada partida) y
    /// nada en el proyecto lo lee de vuelta desde PlayFab - por eso NO usamos
    /// UpdateUserTitleDisplayName: esa API exige que el nombre sea único en
    /// TODO el título de PlayFab (entre TODAS las cuentas), lo cual no tiene
    /// sentido para un nick cosmético y descartable, y bloqueaba a cualquier
    /// jugador que quisiera usar un nick que otra PC ya haya usado antes.
    /// Lo guardamos puramente local, sin llamada de red.
    /// </summary>
    public void SetDisplayName(string newName)
    {
        DisplayName = newName;
        Debug.Log($"[PlayFab] DisplayName actualizado (local, sin llamada a PlayFab): {DisplayName}");
        OnDisplayNameUpdated?.Invoke();
    }

    /// <summary>
    /// Llamar cuando el jugador termina el tutorial - guarda el progreso en
    /// PlayFab (UserData, no exige unicidad, a diferencia del nick viejo)
    /// para que la proxima vez que este mismo PC/cuenta inicie sesion, ya
    /// no se le vuelva a pedir el tutorial. Tambien se guarda una copia local.
    /// </summary>
    public void MarcarTutorialCompletado()
    {
        PlayerPrefs.SetInt(TutorialLocalKey, 1);
        PlayerPrefs.Save();

        if (ModoOffline)
        {
            TutorialCompletado = true;
            OnTutorialCompletadoConfirmado?.Invoke();
            return;
        }

        var request = new UpdateUserDataRequest
        {
            Data = new Dictionary<string, string> { { TutorialCompletadoKey, "true" } }
        };

        PlayFabClientAPI.UpdateUserData(request,
            result =>
            {
                TutorialCompletado = true;
                Debug.Log("[PlayFab] Tutorial marcado como completado.");
                OnTutorialCompletadoConfirmado?.Invoke();
            },
            error => Debug.LogError($"[PlayFab] Error al marcar el tutorial como completado: {error.GenerateErrorReport()}")
        );
    }

    /// <summary>
    /// SOLO para pruebas: borra la bandera del tutorial en PlayFab, para
    /// poder repetir el flujo completo (jugador nuevo -> tutorial -> volver
    /// al menu) sin tener que ir al dashboard de PlayFab a mano cada vez.
    /// No se llama desde ningun lado del flujo normal del juego - conectala
    /// a un boton/atajo de debug mientras estés probando.
    /// </summary>
    public void ResetearTutorialCompletado()
    {
        PlayerPrefs.DeleteKey(TutorialLocalKey);
        PlayerPrefs.Save();

        if (ModoOffline)
        {
            TutorialCompletado = false;
            Debug.Log("[PlayFab] Tutorial reseteado en local (modo offline).");
            return;
        }

        var request = new UpdateUserDataRequest
        {
            KeysToRemove = new List<string> { TutorialCompletadoKey }
        };

        PlayFabClientAPI.UpdateUserData(request,
            result =>
            {
                TutorialCompletado = false;
                Debug.Log("[PlayFab] Tutorial reseteado (para pruebas) - la próxima vez vuelve a pedirse.");
            },
            error => Debug.LogError($"[PlayFab] Error al resetear el tutorial: {error.GenerateErrorReport()}")
        );
    }
}