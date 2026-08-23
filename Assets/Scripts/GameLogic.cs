using System;
using System.IO;
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
        DataCollecter.Instance.StartClock();
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
        DataCollecter.Instance.StopClock();

        string fileName = "SessionData_" + DateTime.Now.ToString("dd.MM.yyyy_HH-mm-ss") + ".json";
        string filePath = Path.Combine(Application.persistentDataPath, fileName);

        DataCollecter.Instance.SaveToJsonFile(filePath);

        Debug.Log("Data saved: " + filePath);

        _buttonStart.SetActive(true);
    }
}