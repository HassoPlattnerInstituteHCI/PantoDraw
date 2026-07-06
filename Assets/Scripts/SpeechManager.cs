using UnityEngine;
using System.Collections.Generic;
using SpeechIO;

public class SpeechManager : MonoBehaviour
{

    public static SpeechManager Instance;

    SpeechOut speechOut;

    void Awake()
    {
        // Singleton
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        speechOut = new SpeechOut();

    }

    public async void Speak(string text)
    {
        await speechOut.Speak(text);
    }
}
