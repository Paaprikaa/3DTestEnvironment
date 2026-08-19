using System;
using UnityEngine;
using UnityEngine.Playables;

public class GameLogic : MonoBehaviour
{
    [SerializeField] private GameObject _buttonStart;
    [SerializeField] private PlayableDirector _director;
    [SerializeField] private GameObject _timeline;

    public void StartCinematic()
    {
        _buttonStart.SetActive(false);
        Debug.Log(Application.persistentDataPath);
        FPSDisplay.Instance.StartClock();
        _director.Play();
    }

    private void OnEnable()
    {
        _director.stopped += OnTimelineStopped;
    }

    private void OnDisable()
    {
        _director.stopped -= OnTimelineStopped;
    }
    
    private void OnTimelineStopped(PlayableDirector director)
    {
        string fpsListString = "[" + string.Join(",", FPSDisplay.Instance.fpsList) + "]";
        System.IO.File.WriteAllText(Application.persistentDataPath + "/FPS_" + DateTime.Now.ToString("dd.MM.yyyy_HH-mm-ss") + ".txt", fpsListString);

        FPSDisplay.Instance.StopClock();
        _buttonStart.SetActive(true);
    }
}
