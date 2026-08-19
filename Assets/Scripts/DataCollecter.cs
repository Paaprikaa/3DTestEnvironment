using System.Collections.Generic;
using System.IO;
using TMPro;
using Unity.Profiling;
using UnityEngine;

[System.Serializable]
public class CollectedData
{
    public List<float> fps = new List<float>();
    public List<long> drawCalls = new List<long>();
    public List<long> batches = new List<long>();
    public List<long> setPassCalls = new List<long>();
    public List<long> triangles = new List<long>();
    public List<long> vertices = new List<long>();
    public List<float> mainThreadMs = new List<float>();
    public List<long> gcReservedMemory = new List<long>();
}

[System.Serializable]
public class SessionData
{
    public bool developmentBuild;
    public CollectedData collectedData = new CollectedData();
}

public class DataCollecter : MonoBehaviour
{
    public static DataCollecter Instance { get; private set; }

    public TextMeshProUGUI fpsText;
    public TextMeshProUGUI timeText;
    public float pollingTime = 0.1f;
    private float _realTime;
    private bool _updateClock;

    private float deltaTime = 0.0f;
    float fps;

    public List<float> fpsList = new List<float>();
    public List<long> drawCallsList = new List<long>();
    public List<long> batchesList = new List<long>();
    public List<long> setPassCallsList = new List<long>();
    public List<long> trianglesList = new List<long>();
    public List<long> verticesList = new List<long>();
    public List<float> mainThreadMsList = new List<float>();
    public List<long> gcReservedMemoryList = new List<long>();

    // only collected in Editor or Development Builds
    private ProfilerRecorder _drawCallsRecorder;
    private ProfilerRecorder _batchesRecorder;
    private ProfilerRecorder _setPassCallsRecorder;
    private ProfilerRecorder _trianglesRecorder;
    private ProfilerRecorder _verticesRecorder;
    private ProfilerRecorder _mainThreadRecorder;
    private ProfilerRecorder _gcReservedMemoryRecorder;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void OnEnable()
    {
        _drawCallsRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
        _batchesRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
        _setPassCallsRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
        _trianglesRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
        _verticesRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Vertices Count");
        _mainThreadRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
        _gcReservedMemoryRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Reserved Memory");
    }

    private void OnDisable()
    {
        _drawCallsRecorder.Dispose();
        _batchesRecorder.Dispose();
        _setPassCallsRecorder.Dispose();
        _trianglesRecorder.Dispose();
        _verticesRecorder.Dispose();
        _mainThreadRecorder.Dispose();
        _gcReservedMemoryRecorder.Dispose();
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
        ClearData();
        _updateClock = true;
        InvokeRepeating("SaveTime", 0f, pollingTime);
    }

    public void StopClock()
    {
        _updateClock = false;
        _realTime = 0f;
        CancelInvoke("SaveTime");
    }

    public void ClearData()
    {
        fpsList.Clear();
        drawCallsList.Clear();
        batchesList.Clear();
        setPassCallsList.Clear();
        trianglesList.Clear();
        verticesList.Clear();
        mainThreadMsList.Clear();
        gcReservedMemoryList.Clear();
    }

    public void SaveTime()
    {
        fpsList.Add(fps);

        if (_drawCallsRecorder.Valid) drawCallsList.Add(_drawCallsRecorder.LastValue);
        if (_batchesRecorder.Valid) batchesList.Add(_batchesRecorder.LastValue);
        if (_setPassCallsRecorder.Valid) setPassCallsList.Add(_setPassCallsRecorder.LastValue);
        if (_trianglesRecorder.Valid) trianglesList.Add(_trianglesRecorder.LastValue);
        if (_verticesRecorder.Valid) verticesList.Add(_verticesRecorder.LastValue);

        if (_mainThreadRecorder.Valid)
        {
            mainThreadMsList.Add(_mainThreadRecorder.LastValue * 1e-6f); // ns to ms
        }

        if (_gcReservedMemoryRecorder.Valid) gcReservedMemoryList.Add(_gcReservedMemoryRecorder.LastValue);
    }

    public SessionData BuildSessionData()
    {
        return new SessionData
        {
            developmentBuild = Debug.isDebugBuild,
            collectedData = new CollectedData
            {
                fps = fpsList,
                drawCalls = drawCallsList,
                batches = batchesList,
                setPassCalls = setPassCallsList,
                triangles = trianglesList,
                vertices = verticesList,
                mainThreadMs = mainThreadMsList,
                gcReservedMemory = gcReservedMemoryList
            }
        };
    }

    public string SaveToJsonFile(string filePath)
    {
        string json = JsonUtility.ToJson(BuildSessionData(), true);
        File.WriteAllText(filePath, json);
        return json;
    }
}