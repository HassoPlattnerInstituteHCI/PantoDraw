// using System.Diagnostics;
// using UnityEngine;
// using Button = UnityEngine.UI.Button;
// using Toggle = UnityEngine.UI.Toggle;

// namespace Whisper.Samples
// {
//     /// <summary>
//     /// Record audio clip from microphone and make a transcription.
//     /// </summary>
//     public class VoiceRecognizer : MonoBehaviour
//     {
//         public MicrophoneRecord microphoneRecord;
        
//         private string _buffer;

//         private void Awake()
//         {
            
            
//             microphoneRecord.OnRecordStop += OnRecordStop;
            
//         }

//         private void OnVadChanged(bool vadStop)
//         {
//             microphoneRecord.vadStop = vadStop;
//         }

//         private void OnButtonPressed()
//         {
//             if (!microphoneRecord.IsRecording)
//             {
//                 microphoneRecord.StartRecord();
                
//             }
//             else
//             {
//                 microphoneRecord.StopRecord();
                
//             }
//         }
        
//         private async void OnRecordStop(AudioChunk recordedAudio)
//         {
            
//             _buffer = "";

//             var sw = new Stopwatch();
//             sw.Start();
            
//             var res = await recordedAudio.GetTextAsync(recordedAudio.Data, recordedAudio.Frequency, recordedAudio.Channels);
//             if (res == null) 
//                 return;

//             var time = sw.ElapsedMilliseconds;
//             var rate = recordedAudio.Length / (time * 0.001f);
//             timeText.text = $"Time: {time} ms\nRate: {rate:F1}x";

//             var text = res.Result;
//             if (printLanguage)
//                 text += $"\n\nLanguage: {res.Language}";
            
//             outputText.text = text;
//             UiUtils.ScrollDown(scroll);
//         }
        
//         private void OnLanguageChanged(int ind)
//         {
//             var opt = languageDropdown.options[ind];
//             whisper.language = opt.text;
//         }
        
//         private void OnTranslateChanged(bool translate)
//         {
//             whisper.translateToEnglish = translate;
//         }

//         private void OnProgressHandler(int progress)
//         {
//             if (!timeText)
//                 return;
//             timeText.text = $"Progress: {progress}%";
//         }
        
//         private void OnNewSegment(WhisperSegment segment)
//         {
//             if (!streamSegments || !outputText)
//                 return;

//             _buffer += segment.Text;
//             outputText.text = _buffer + "...";
//             UiUtils.ScrollDown(scroll);
//         }
//     }
// }