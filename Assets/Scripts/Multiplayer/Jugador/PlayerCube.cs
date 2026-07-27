using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Representa a cada jugador conectado como un cubo en el mapa.
///
/// IMPORTANTE: cada cliente ve TODOS los cubos (el suyo y el de los demas),
/// pero solo debe usar la camara/audio de SU PROPIO cubo - si todas las camaras
/// quedaran activas, se pisarian entre si (solo se veria/oiria una al azar).
/// Por eso, al aparecer, cada cubo revisa si es el "dueno" (IsOwner) antes de
/// activar su camara.
///
/// El material/posicion/rotacion NO se deciden por OwnerClientId directamente:
/// ese numero no se reinicia entre sesiones de hosting (cerrar sala y crear
/// una nueva puede seguir subiendo los ids en vez de volver a 0/1/2). En vez
/// de eso, se usa un "slot" (0/1/2) asignado por NetworkBootstrap segun el
/// orden real de conexion de la sesion actual, sincronizado a todos via
/// NetworkVariable.
///
/// El nick tambien se sincroniza por red (NetworkVariable con permiso de
/// escritura del DUENO): cada cliente conoce su propio nick de PlayFab, pero
/// nadie mas lo sabe hasta que se lo mandamos. Cuando slot y nick ya estan
/// disponibles, se avisa a PlayerNamePanelsUI para que actualice el panel
/// correspondiente.
/// </summary>
public class PlayerCube : NetworkBehaviour
{
    /// <summary>
    /// El slot (0/1/2) del jugador local en ESTA maquina. -1 si todavia no
    /// se sabe. Cualquier sistema (como TurnManager) puede consultar esto
    /// para saber "cual es mi propio slot" sin tener que buscar el cubo.
    /// </summary>
    public static int MiSlot { get; private set; } = -1;

    [Header("Referencias")]
    [SerializeField] private Camera camaraJugador;
    [SerializeField] private AudioListener audioListenerJugador;
    [SerializeField] private Renderer rendererCubo;

    [Header("Materiales por slot (0=host, 1=invitado1, 2=invitado2)")]
    [SerializeField] private Material materialHost;
    [SerializeField] private Material materialInvitado1;
    [SerializeField] private Material materialInvitado2;

    [Header("Posiciones de aparicion por slot")]
    [SerializeField] private Vector3 posicionHost = new Vector3(-3f, 0.5f, 0f);
    [SerializeField] private Vector3 posicionInvitado1 = new Vector3(0f, 0.5f, 0f);
    [SerializeField] private Vector3 posicionInvitado2 = new Vector3(3f, 0.5f, 0f);

    [Header("Rotaciones de aparicion por slot (grados, ejes X/Y/Z)")]
    [SerializeField] private Vector3 rotacionHost = new Vector3(0f, 0f, 0f);
    [SerializeField] private Vector3 rotacionInvitado1 = new Vector3(0f, 0f, 0f);
    [SerializeField] private Vector3 rotacionInvitado2 = new Vector3(0f, 0f, 0f);

    // Slot: se escribe solo en el servidor, se lee/replica a todos por defecto.
    private readonly NetworkVariable<int> slotJugador = new NetworkVariable<int>(-1);

    // Nick: cada cliente escribe SOLO el suyo (permiso de dueno), todos lo leen.
    private readonly NetworkVariable<FixedString64Bytes> nickJugador =
        new NetworkVariable<FixedString64Bytes>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public override void OnNetworkSpawn()
    {
        camaraJugador.gameObject.SetActive(IsOwner);
        audioListenerJugador.gameObject.SetActive(IsOwner);

        // Por si algun valor llega un poco despues del spawn en algun cliente.
        slotJugador.OnValueChanged += (anterior, nuevo) =>
        {
            AsignarMaterialSegunSlot();
            AvisarPanelDeNombre();

            if (IsOwner)
            {
                MiSlot = nuevo;
            }
        };
        nickJugador.OnValueChanged += (anterior, nuevo) => AvisarPanelDeNombre();

        if (IsOwner)
        {
            // Cada cliente solo conoce su propio nick (PlayFab es local a cada
            // maquina) - lo publicamos para que los demas lo puedan leer.
            nickJugador.Value = PlayFabAuthManager.Instance.DisplayName;
        }

        if (IsServer)
        {
            slotJugador.Value = NetworkBootstrap.Instance.ObtenerOAsignarSlot(OwnerClientId);

            // Solo el servidor puede mover/rotar el objeto de forma autoritativa
            // (el NetworkTransform del prefab replica esto a todos).
            AsignarPosicionSegunSlot();
            AsignarRotacionSegunSlot();
        }

        AsignarMaterialSegunSlot();
        AvisarPanelDeNombre();

        if (IsOwner && slotJugador.Value >= 0)
        {
            MiSlot = slotJugador.Value;
        }
    }

    /// <summary>
    /// Le permite a PlayerNamePanelsUI pedirle a este cubo que reintente
    /// avisar su nombre, por si el cubo ya habia spawneado (y ya intento
    /// avisar) antes de que el panel existiera todavia.
    /// </summary>
    public void ReintentarAvisoDePanel()
    {
        AvisarPanelDeNombre();
    }

    private void AvisarPanelDeNombre()
    {
        Debug.Log($"[DEBUG PlayerCube] AvisarPanelDeNombre: slot={slotJugador.Value}, nick='{nickJugador.Value}', PlayerNamePanelsUI.Instance es null? {PlayerNamePanelsUI.Instance == null}");

        if (slotJugador.Value < 0)
        {
            Debug.Log("[DEBUG PlayerCube] Todavia no hay slot asignado, salgo.");
            return;
        }
        if (nickJugador.Value.Length == 0)
        {
            Debug.Log("[DEBUG PlayerCube] Todavia no hay nick asignado, salgo.");
            return;
        }

        PlayerNamePanelsUI.Instance?.ActualizarNombre(slotJugador.Value, nickJugador.Value.ToString());
    }

    private void AsignarPosicionSegunSlot()
    {
        if (slotJugador.Value == 0)
        {
            transform.position = posicionHost;
        }
        else if (slotJugador.Value == 1)
        {
            transform.position = posicionInvitado1;
        }
        else
        {
            transform.position = posicionInvitado2;
        }
    }

    private void AsignarRotacionSegunSlot()
    {
        if (slotJugador.Value == 0)
        {
            transform.eulerAngles = rotacionHost;
        }
        else if (slotJugador.Value == 1)
        {
            transform.eulerAngles = rotacionInvitado1;
        }
        else
        {
            transform.eulerAngles = rotacionInvitado2;
        }
    }

    private void AsignarMaterialSegunSlot()
    {
        if (slotJugador.Value == 0)
        {
            rendererCubo.material = materialHost;
        }
        else if (slotJugador.Value == 1)
        {
            rendererCubo.material = materialInvitado1;
        }
        else
        {
            rendererCubo.material = materialInvitado2;
        }
    }
}