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
/// </summary>
public class PlayerCube : NetworkBehaviour
{
    [Header("Referencias")]
    [SerializeField] private Camera camaraJugador;
    [SerializeField] private AudioListener audioListenerJugador;
    [SerializeField] private Renderer rendererCubo;

    [Header("Materiales por orden de conexion")]
    [SerializeField] private Material materialHost;
    [SerializeField] private Material materialInvitado1;
    [SerializeField] private Material materialInvitado2;

    [Header("Posiciones de aparicion por orden de conexion")]
    [SerializeField] private Vector3 posicionHost = new Vector3(-3f, 0.5f, 0f);
    [SerializeField] private Vector3 posicionInvitado1 = new Vector3(0f, 0.5f, 0f);
    [SerializeField] private Vector3 posicionInvitado2 = new Vector3(3f, 0.5f, 0f);

    [Header("Rotaciones de aparicion por orden de conexion (grados, ejes X/Y/Z)")]
    [SerializeField] private Vector3 rotacionHost = new Vector3(0f, 0f, 0f);
    [SerializeField] private Vector3 rotacionInvitado1 = new Vector3(0f, 0f, 0f);
    [SerializeField] private Vector3 rotacionInvitado2 = new Vector3(0f, 0f, 0f);

    public override void OnNetworkSpawn()
    {
        camaraJugador.gameObject.SetActive(IsOwner);
        audioListenerJugador.gameObject.SetActive(IsOwner);

        AsignarMaterialSegunOrdenDeConexion();

        // Solo el servidor puede mover/rotar el objeto de forma autoritativa
        // (el NetworkTransform del prefab replica esto a todos).
        if (IsServer)
        {
            AsignarPosicionSegunOrdenDeConexion();
            AsignarRotacionSegunOrdenDeConexion();
        }
    }

    private void AsignarPosicionSegunOrdenDeConexion()
    {
        if (OwnerClientId == 0)
        {
            transform.position = posicionHost;
        }
        else if (OwnerClientId == 1)
        {
            transform.position = posicionInvitado1;
        }
        else
        {
            transform.position = posicionInvitado2;
        }
    }

    private void AsignarRotacionSegunOrdenDeConexion()
    {
        if (OwnerClientId == 0)
        {
            transform.eulerAngles = rotacionHost;
        }
        else if (OwnerClientId == 1)
        {
            transform.eulerAngles = rotacionInvitado1;
        }
        else
        {
            transform.eulerAngles = rotacionInvitado2;
        }
    }

    private void AsignarMaterialSegunOrdenDeConexion()
    {
        // El host siempre es el client id 0 en Netcode. Simplificacion inicial:
        // el material depende del orden de conexion (0 = host, 1 = primer invitado, etc).
        if (OwnerClientId == 0)
        {
            rendererCubo.material = materialHost;
        }
        else if (OwnerClientId == 1)
        {
            rendererCubo.material = materialInvitado1;
        }
        else
        {
            rendererCubo.material = materialInvitado2;
        }
    }
}