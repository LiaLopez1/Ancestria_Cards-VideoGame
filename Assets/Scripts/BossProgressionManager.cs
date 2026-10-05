using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class BossProgressionManager : MonoBehaviour
{
    public static BossProgressionManager Instance { get; private set; }

    [Tooltip("Nombres exactos de las escenas de jefes (todas en Build Settings).")]
    [SerializeField] private string[] escenasJefes;
    [Tooltip("Escena a la que ir cuando se vencen todos los jefes.")]
    

    private List<string> orden = new List<string>();
    private int indice;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>Baraja los jefes y devuelve la primera escena. Llamar al crear sala.</summary>
    public string IniciarNuevaRun()
    {
          if (escenasJefes == null || escenasJefes.Length == 0)
            {
                Debug.LogError("[Jefes] No hay escenas configuradas en BossProgressionManager.");
                return null;
            } 
        orden = new List<string>();
        foreach (var nombre in escenasJefes)
        {
            if (string.IsNullOrWhiteSpace(nombre)) continue;

            if (!Application.CanStreamedLevelBeLoaded(nombre))
            {
                Debug.LogWarning($"[Jefes] '{nombre}' no esta en Build Settings, se ignora.");
                continue;
            }
            orden.Add(nombre);
        }

        if (orden.Count == 0)
        {
            Debug.LogError("[Jefes] No hay escenas de jefes validas en el array.");
            return null;
        }

        // Fisher-Yates: cada jefe sale una sola vez, sin repetirse
        for (int i = orden.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (orden[i], orden[j]) = (orden[j], orden[i]);
        }

        indice = 0;
        return orden[0];
    }
    public bool HayMasJefes => indice + 1 < orden.Count;
    /// <summary>La llama el servidor cuando los jugadores ganan las 3 rondas.</summary>
    public void SiguienteJefe()                    // al apretar el botón
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;
        if (!HayMasJefes) return;

        indice++;
        nm.SceneManager.OnLoadEventCompleted += AlTerminarCarga;
        nm.SceneManager.LoadScene(orden[indice], LoadSceneMode.Single);
    }

    private void AlTerminarCarga(string escena, LoadSceneMode modo,
        List<ulong> completados, List<ulong> fallidos)
    {
        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= AlTerminarCarga;

        // Los cubos de los jugadores sobreviven al cambio de escena,
        // pero OnNetworkSpawn NO se repite: hay que recolocarlos a mano.
        /*foreach (var cubo in FindObjectsByType<PlayerCube>(FindObjectsSortMode.None))
        {
            cubo.ReposicionarEnServidor();
        }*/
    }

    public void ReiniciarJefeActual()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;

        nm.SceneManager.OnLoadEventCompleted += AlTerminarCarga;
        nm.SceneManager.LoadScene(orden[indice], LoadSceneMode.Single);
    }
}