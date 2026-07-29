using System;
using PlayFab;
using PlayFab.ClientModels;
using UnityEngine;

/// <summary>
/// Maneja el login del jugador contra PlayFab.
/// Para el prototipo usamos LoginWithCustomID con el DeviceUniqueIdentifier,
/// asi no pedimos usuario/contrasena todavia (eso lo agregamos despues).
/// </summary>
public class PlayFabAuthManager : MonoBehaviour
{
    public static PlayFabAuthManager Instance { get; private set; }

    public string PlayFabId { get; private set; }
    public string DisplayName { get; private set; }
    public string EntityId { get; private set; }
    public string EntityType { get; private set; }

    public bool HasDisplayName => !string.IsNullOrEmpty(DisplayName);

    public event Action OnLoginSuccess;
    public event Action<string> OnLoginFailed;
    public event Action OnDisplayNameUpdated;

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

    public void Login()
    {
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

    private void OnLoginResult(LoginResult result)
    {
        PlayFabId = result.PlayFabId;
        DisplayName = result.InfoResultPayload?.PlayerProfile?.DisplayName;
        EntityId = result.EntityToken?.Entity?.Id;
        EntityType = result.EntityToken?.Entity?.Type;

        Debug.Log($"[PlayFab] Login OK. PlayFabId: {PlayFabId} (deviceId usado: {SystemInfo.deviceUniqueIdentifier}). HasDisplayName: {HasDisplayName}");

        OnLoginSuccess?.Invoke();
    }

    private void OnLoginError(PlayFabError error)
    {
        Debug.LogError($"[PlayFab] Error de login: {error.GenerateErrorReport()}");
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
}