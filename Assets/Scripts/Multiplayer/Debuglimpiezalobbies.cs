using PlayFab;
using PlayFab.MultiplayerModels;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// SOLO PARA DEBUG - BORRAR DESPUES DE USAR.
///
/// Al presionar la tecla configurada (por defecto L), busca TODAS las salas
/// donde el jugador actual sea dueno O miembro (cubre ambos casos, a diferencia
/// de la limpieza normal que solo revisa "dueno"), y las vacia: saca a
/// cualquier otro miembro y sale el mismo. Sirve para forzar la limpieza de
/// salas huerfanas que quedaron atascadas de pruebas anteriores.
///
/// Requiere estar logueado (necesita authManager.EntityId/EntityType ya
/// asignados), asi que presiona la tecla DESPUES de que el login haya
/// terminado (por ejemplo, ya en el panel de nick o mas adelante).
///
/// IMPORTANTE: si tu cuenta actual ya no es ni dueno ni miembro de una sala en
/// particular (por ejemplo, porque ya lograste salir de ella en un intento
/// anterior, pero otra persona se quedo atascada adentro), este script tampoco
/// va a poder tocarla - en ese caso, solo esa otra cuenta (o el paso del TTL
/// de 1 hora sin actividad) puede limpiarla.
/// </summary>
public class DebugLimpiezaLobbies : MonoBehaviour
{
    [SerializeField] private PlayFabAuthManager authManager;

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.lKey.wasPressedThisFrame)
        {
            if (string.IsNullOrEmpty(authManager.EntityId))
            {
                Debug.LogWarning("[DEBUG] Todavia no has iniciado sesion, espera a que termine el login.");
                return;
            }

            Debug.Log("[DEBUG] ==== Iniciando limpieza total de mis lobbies ====");
            BuscarYVaciar("lobby/amOwner eq 'true'");
            BuscarYVaciar("lobby/amMember eq 'true'");
        }
    }

    private void BuscarYVaciar(string filtro)
    {
        PlayFabMultiplayerAPI.FindLobbies(new FindLobbiesRequest { Filter = filtro },
            result =>
            {
                Debug.Log($"[DEBUG] Filtro '{filtro}': {result.Lobbies.Count} sala(s) encontrada(s).");
                foreach (var lobby in result.Lobbies)
                {
                    VaciarSala(lobby.LobbyId);
                }
            },
            error => Debug.LogError($"[DEBUG] Error buscando con filtro '{filtro}': {error.GenerateErrorReport()}")
        );
    }

    private void VaciarSala(string lobbyId)
    {
        PlayFabMultiplayerAPI.GetLobby(new GetLobbyRequest { LobbyId = lobbyId },
            result =>
            {
                foreach (var miembro in result.Lobby.Members)
                {
                    if (miembro.MemberEntity.Id != authManager.EntityId)
                    {
                        string idColgado = miembro.MemberEntity.Id;
                        PlayFabMultiplayerAPI.RemoveMember(
                            new RemoveMemberFromLobbyRequest { LobbyId = lobbyId, MemberEntity = miembro.MemberEntity },
                            r => Debug.Log($"[DEBUG] Saque a {idColgado} de la sala {lobbyId}"),
                            e => Debug.LogWarning($"[DEBUG] No pude sacar a {idColgado} de {lobbyId}: {e.GenerateErrorReport()}")
                        );
                    }
                }

                PlayFabMultiplayerAPI.LeaveLobby(
                    new LeaveLobbyRequest
                    {
                        LobbyId = lobbyId,
                        MemberEntity = new EntityKey { Id = authManager.EntityId, Type = authManager.EntityType }
                    },
                    r => Debug.Log($"[DEBUG] Sali de la sala {lobbyId} (deberia autoborrarse si quedo vacia)"),
                    e => Debug.LogWarning($"[DEBUG] No pude salir de {lobbyId}: {e.GenerateErrorReport()}")
                );
            },
            error => Debug.LogWarning($"[DEBUG] No pude leer la sala {lobbyId}: {error.GenerateErrorReport()}")
        );
    }
}