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

        Debug.Log($"[PlayFab] Login OK. PlayFabId: {PlayFabId}. HasDisplayName: {HasDisplayName}");

        OnLoginSuccess?.Invoke();
    }

    private void OnLoginError(PlayFabError error)
    {
        Debug.LogError($"[PlayFab] Error de login: {error.GenerateErrorReport()}");
        OnLoginFailed?.Invoke(error.ErrorMessage);
    }

    public void SetDisplayName(string newName)
    {
        var request = new UpdateUserTitleDisplayNameRequest { DisplayName = newName };

        PlayFabClientAPI.UpdateUserTitleDisplayName(request,
            result =>
            {
                DisplayName = result.DisplayName;
                Debug.Log($"[PlayFab] DisplayName actualizado: {DisplayName}");
                OnDisplayNameUpdated?.Invoke();
            },
            error => Debug.LogError($"[PlayFab] Error al actualizar nombre: {error.GenerateErrorReport()}")
        );
    }
}