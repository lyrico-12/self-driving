// SerialID: [77a855b2-f53d-4b80-9c94-c40562952b74]
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System;
using System.Linq;

#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

public class NEEnvironmentNew : Environment
{
    [Header("Settings"), SerializeField] private int totalPopulation = 100;
    private int TotalPopulation { get { return totalPopulation; } }

    [SerializeField] private int tournamentSelection = 85;
    private int TournamentSelection { get { return tournamentSelection; } }

    [SerializeField] private int eliteSelection = 4;
    private int EliteSelection { get { return eliteSelection; } }

    [SerializeField] public bool[] selectedInputs = new bool[46];
    [SerializeField] public List<double> sensorAngleConfig = new List<double>();

    private int InputSize { get; set; }

    private List<int> SelectedInputsList { get; set; }

    [SerializeField] private int hiddenSize = 8;
    private int HiddenSize { get { return hiddenSize; } }

    [SerializeField] private int hiddenLayers = 1;
    private int HiddenLayers { get { return hiddenLayers; } }

    [SerializeField] private int outputSize = 4;
    private int OutputSize { get { return outputSize; } }

    [SerializeField] private int nAgents = 4;
    private int NAgents { get { return nAgents; } }

    [Header("Agent Prefab"), SerializeField] private GameObject gObject = null;
    private GameObject GObject => gObject;

    [SerializeField] private bool isChallenge4 = false;
    private bool IsChallenge4 { get { return isChallenge4; } }

    [Header("UI References"), SerializeField] private Text populationText = null;
    private Text PopulationText { get { return populationText; } }

    private float GenBestRecord { get; set; }

    private float SumReward { get; set; }
    private float AvgReward { get; set; }

    private List<NNBrain> Brains { get; set; } = new List<NNBrain>();
    private List<GameObject> GObjects { get; } = new List<GameObject>();
    private List<Agent> Agents { get; } = new List<Agent>();
    private int Generation { get; set; }

    private float BestRecord { get; set; }

    private List<AgentPair> AgentsSet { get; } = new List<AgentPair>();
    private Queue<NNBrain> CurrentBrains { get; set; }

    private List<Obstacle> Obstacles { get; } = new List<Obstacle>();

    // 前世代（直近に完了した世代）の速度統計
    private float LastGenAvgSpeed = 0f;
    private float LastGenMaxSpeed = 0f;
    private float LastGenMinSpeed = 0f;

    // 現在進行中の世代での集計用
    private float SumEpisodeAvgSpeed = 0f;
    private float GenMaxSpeed = float.NegativeInfinity;
    private float GenMinSpeed = float.PositiveInfinity;

    [Header("Stage (Prefab selection)")]
    [SerializeField] private StageManager stageManager; // ← 追加


    private void Start() {
        // 1) 入力次元などの初期化
        // Calculate and set input size.
        int sensorCount = 0;
        foreach (bool value in selectedInputs)
        {
            if (value) sensorCount++;
        }
        InputSize = sensorCount;

        // Calculate and set sensors list.
        List<int> selectedInputsList = new List<int>();
        for (int i = 0; i < selectedInputs.Length; i++)
        {
            if (selectedInputs[i]) selectedInputsList.Add(i);
        }
        SelectedInputsList = selectedInputsList;

        // Initialize brain.
        for(int i = 0; i < TotalPopulation; i++) {
            Brains.Add(new NNBrain(InputSize, HiddenSize, HiddenLayers, OutputSize));
        }

        // 2) 先にステージ生成
        if (stageManager != null) {
            stageManager.InitFirstStage(Generation);
        }

        // 3) エージェント生成（まだ位置は気にしない）
        for(int i = 0; i < NAgents; i++) {
            var obj = Instantiate(GObject);
            obj.SetActive(true);
            GObjects.Add(obj);
            Agents.Add(obj.GetComponent<Agent>());
        }
        foreach(Agent agent in Agents) {
            agent.SetAgentConfig(sensorAngleConfig);
        }

        // 4) スポーン地点へ配置して開始姿勢を更新
        PlaceAgentsAtStageSpawns();

        BestRecord = -9999;
        SetStartAgents();
        if (IsChallenge4) {
            Obstacles.AddRange(FindObjectsOfType<Obstacle>());
        }
        UpdateText();
    }

    void SetStartAgents() {
        CurrentBrains = new Queue<NNBrain>(Brains);
        AgentsSet.Clear();
        var size = Math.Min(NAgents, TotalPopulation);
        for(var i = 0; i < size; i++) {
            AgentsSet.Add(new AgentPair {
                agent = Agents[i],
                brain = CurrentBrains.Dequeue()
            });
        }
    }

    void FixedUpdate() {
        foreach(var pair in AgentsSet.Where(p => !p.agent.IsDone)) {
            AgentUpdate(pair.agent, pair.brain);
        }

        AgentsSet.RemoveAll(p => {
            if(p.agent.IsDone) {
                p.agent.Stop();
                p.agent.gameObject.SetActive(false);
                float r = p.agent.Reward;
                BestRecord = Mathf.Max(r, BestRecord);
                GenBestRecord = Mathf.Max(r, GenBestRecord);
                p.brain.Reward = r;
                SumReward += r;

                // エージェントの速度統計を取り込む
                var car = p.agent as CarAgent;
                if (car != null) {
                    SumEpisodeAvgSpeed += car.EpisodeAvgSpeed;
                    GenMaxSpeed = Mathf.Max(GenMaxSpeed, car.EpisodeMaxSpeed);
                    GenMinSpeed = Mathf.Min(GenMinSpeed, car.EpisodeMinSpeed);
                }
            }
            return p.agent.IsDone;
        });

        if(CurrentBrains.Count == 0 && AgentsSet.Count == 0) {
            SetNextGeneration();
        }
        else {
            SetNextAgents();
        }
    }

    private void AgentUpdate(Agent a, NNBrain b) {
        var observation = a.GetAllObservations();
        var rearranged = RearrangeObservation(observation, SelectedInputsList);
        var action = b.GetAction(rearranged); // [steer, gas, brake]を出力
        a.AgentAction(action, false);
    }

    private void SetNextAgents() {
        int size = Math.Min(NAgents - AgentsSet.Count, CurrentBrains.Count);
        for(var i = 0; i < size; i++) {
            var nextAgent = Agents.First(a => a.IsDone);
            var nextBrain = CurrentBrains.Dequeue();
            nextAgent.Reset();
            AgentsSet.Add(new AgentPair {
                agent = nextAgent,
                brain = nextBrain
            });
        }
        UpdateText();
    }

    private void SetNextGeneration() {
        AvgReward = SumReward / TotalPopulation;
        LastGenAvgSpeed = SumEpisodeAvgSpeed / TotalPopulation;
        LastGenMaxSpeed = GenMaxSpeed;
        LastGenMinSpeed = GenMinSpeed;

        GenPopulation();

        SumReward = 0;
        GenBestRecord = -9999;
        SumEpisodeAvgSpeed = 0f;
        GenMaxSpeed = float.NegativeInfinity;
        GenMinSpeed = float.PositiveInfinity;

        // ステージ入れ替え
        if (stageManager != null) {
            stageManager.SwitchStage(Generation + 1);
        }

        // 新ステージのSpawnPointへ配置し直してからReset
        PlaceAgentsAtStageSpawns();
        Agents.ForEach(a => a.Reset());
        SetStartAgents();
        UpdateText();
    }

    private static int CompareBrains(Brain a, Brain b) {
        if(a.Reward > b.Reward) return -1;
        if(b.Reward > a.Reward) return 1;
        return 0;
    }

    private void GenPopulation() {
        var children = new List<NNBrain>();
        var bestBrains = Brains.ToList();

        // Elite selection
        bestBrains.Sort(CompareBrains);
        if(EliteSelection > 0) {
            children.AddRange(bestBrains.Take(EliteSelection));
        }

#if UNITY_EDITOR
        var path = string.Format("Assets/LearningData/NE/{0}.json", EditorSceneManager.GetActiveScene().name);
        bestBrains[0].Save(path);
#endif

        while(children.Count < TotalPopulation) {
            var tournamentMembers = Brains.AsEnumerable().OrderBy(x => Guid.NewGuid()).Take(tournamentSelection).ToList();
            tournamentMembers.Sort(CompareBrains);
            children.Add(tournamentMembers[0].Mutate(Generation));
            children.Add(tournamentMembers[1].Mutate(Generation));
        }
        Brains = children;
        Generation++;
    }

    protected List<double> RearrangeObservation(List<double> observation, List<int> indexesToUse)
    {
        if(observation == null || indexesToUse == null) return null;

        List<double> rearranged = new List<double>();
        foreach(int index in indexesToUse)
        {
            if(index >= observation.Count)
            {
                rearranged.Add(0);
                continue;
            }
            rearranged.Add(observation[index]);
        }

        return rearranged;
    }

    private void UpdateText() {
        PopulationText.text = "Current Stage: " + (stageManager != null && stageManager.GetSpawnPoints().Count > 0 ? stageManager.GetSpawnPoints()[0].parent.name : "N/A")
            + "\nPopulation: " + (TotalPopulation - CurrentBrains.Count) + "/" + TotalPopulation
            + "\nGeneration: " + (Generation + 1)
            + "\nBest Record: " + BestRecord
            + "\nBest this gen: " + GenBestRecord
            + "\nAverage: " + AvgReward
            + "\nPrev gen AvgSpeed: " + LastGenAvgSpeed.ToString("F2") + " m/s"
            + "\nPrev gen MaxSpeed: " + LastGenMaxSpeed.ToString("F2") + " m/s"
            + "\nPrev gen MinSpeed: " + LastGenMinSpeed.ToString("F2") + " m/s";
    }

    // SpawnPointに配置するヘルパ
    private void PlaceAgentsAtStageSpawns()
    {
        var spawns = stageManager != null ? stageManager.GetSpawnPoints() : Array.Empty<Transform>();
        if (spawns == null || spawns.Count == 0)
        {
            Debug.LogWarning("NEEnvironmentNew: No SpawnPoint found in current stage. Agents keep current positions.");
            return;
        }
        for (int i = 0; i < Agents.Count; i++)
        {
            var t = spawns[i % spawns.Count];
            if (Agents[i] is CarAgent car)
            {
                car.SetStartPose(t.position, t.rotation, true);
            }
            else
            {
                Agents[i].transform.SetPositionAndRotation(t.position, t.rotation);
            }
        }
    }

    private struct AgentPair
    {
        public NNBrain brain;
        public Agent agent;
    }
}
