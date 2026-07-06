using SpeechIO;
using System.Collections.Generic;
using DualPantoToolkit;
using UnityEngine;
using System.Threading.Tasks;

public class SpeechRecognizer : MonoBehaviour
{

    private SpeechIn speechIn;

    private LowerHandle itHandle;
    void Start()
    {

        speechIn = new SpeechIn(onRecognized);
        speechIn.StartListening();
        
    }

    void onRecognized(string message)
    {
        Debug.Log("[" + this.GetType() + "]: " + message);
    }
    public async Task UpdateShapeList(List<Shape> shapes)
    {
        speechIn.StopListening();
        // Update the list of shapes in the speech recognizer
        // This could be used to dynamically adjust the recognized commands based on available shapes
        List<string> shapeNames = new List<string>();
        foreach (var shape in shapes)
        {
            shapeNames.Add(shape.shapeName);
        }
        ShapeListener(shapeNames.ToArray());
    }
    
    async void ShapeListener(string[] shapeNames)
    {
        string recognizedShape = await speechIn.Listen(shapeNames);
        Debug.Log("Recognized shape: " + recognizedShape);
    }

}
