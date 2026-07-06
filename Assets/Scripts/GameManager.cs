using UnityEngine;

public class GameManager : MonoBehaviour
{


    [TextArea]
    public string introText;

    public bool playIntroOnStart = true;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if (playIntroOnStart)
        {
            SpeechManager.Instance.Speak(introText);
        }
    }
    
    // Update is called once per frame
    void Update()
    {
        
    }
}
