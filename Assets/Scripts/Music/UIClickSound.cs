using UnityEngine;
using UnityEngine.UI;


public class UIClickSound : MonoBehaviour
{
    public SoundData clickSound;

    void Start()
    {
        GetComponent<Button>().onClick.AddListener(() => clickSound.Play());
    }
}