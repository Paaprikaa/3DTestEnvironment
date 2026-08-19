using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class FPSDisplay : MonoBehaviour
{
    public static FPSDisplay Instance { get; private set; }

    public TextMeshProUGUI fpsText;
    public TextMeshProUGUI timeText;
    public float pollingTime = 0.1f;
    private float _realTime;
    private bool _updateClock;

    private float deltaTime = 0.0f;
    float fps;

    public List<float> fpsList = new List<float>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    void Update()
    {
        deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;
        fps = Mathf.Ceil(1.0f / deltaTime);
        fpsText.text = $"FPS: {fps}";

        if (_updateClock)
        {
            _realTime += Time.deltaTime;
            timeText.text = "Time: " + Mathf.RoundToInt(_realTime) + "s";
        }
    }


    public void StartClock()
    {
        _updateClock = true;
        InvokeRepeating("SaveTime", 0f, pollingTime);
    }
    public void StopClock()
    {
        _updateClock = false;
        _realTime = 0f;
    }

    public void SaveTime()
    {
        fpsList.Add(fps);
    }

}
