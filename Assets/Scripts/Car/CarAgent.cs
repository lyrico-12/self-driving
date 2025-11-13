// SerialID: [e4a22a75-a938-4302-8b9a-d405c01db428]
using System.Collections.Generic;
using UnityEngine;
using System;

public class CarAgent : Agent
{
    [SerializeField] private int currentStep = 0;
    private int CurrentStep { get { return currentStep; } set { currentStep = value; } }

    [SerializeField] private int currentStepMax = 5000;
    private int CurrentStepMax => currentStepMax;

    [SerializeField] private int localStep = 0;
    private int LocalStep { get { return localStep; } set { localStep = value; } }

    [SerializeField] private int localStepMax = 200;
    private int LocalStepMax => localStepMax;

    [SerializeField] private bool allowPlusReward = true;
    private bool AllowPlusReward => allowPlusReward;

    [SerializeField] private bool isLearning = true;
    private bool IsLearning => isLearning;

    [SerializeField] private bool backUpOnCollision = false;
    private bool BackUpOnCollision => backUpOnCollision;

    private Sensor[] Sensors { get; set; }
    private CarController Controller { get; set; }
    private Rigidbody CarRb { get; set; }
    private Vector3 StartPosition { get; set; }
    private Quaternion StartRotation { get; set; }
    private Vector3 LastPosition { get; set; }
    public float TotalDistance { get; set; }
    private int WaypointIndex { get; set; }
    private bool passLastPoint=false;
    // 次のWaypointの方向（グローバル座標）
    private Vector3 NextWaypointDirection = Vector3.forward;

    [SerializeField] private bool useDenseSpeedReward = true;
    [SerializeField] private bool useForwardComponent = true;   // 進行方向成分で評価（true）。純粋な速度で評価したいなら false
    [SerializeField] private float targetSpeed = 15f;           // 目標速度[m/s]
    [SerializeField] private float minSpeed = 3f;             // 遅すぎ判定
    [SerializeField] private float maxSpeed = 35f;              // 速すぎ判定
    [SerializeField] private float stepRewardScale = 0.004f;   // 速度に対する加点のスケール
    [SerializeField] private float lowSpeedPenalty = -0.002f;   // 遅すぎ減点/step
    [SerializeField] private float highSpeedPenalty = -0.0005f;  // 速すぎ減点/step
    [SerializeField] private float reversePenalty = -0.004f;    // 逆走減点/step
    [SerializeField] private float collisionPenaltyScale = 1f;    // 衝突減点のスケール

    [SerializeField] private float nearWallThreshold = 0.15f;     // 壁接近とみなす正規化距離
    [SerializeField] private float nearWallPenalty = -0.0015f;    // 1ステップ減点
    [SerializeField] private bool useNearWallPenalty = false;

    // エピソード内の速度統計
    private float speedSum = 0f;
    private int speedCount = 0;
    private float episodeMaxSpeed = 0f;
    private float episodeMinSpeed = float.PositiveInfinity;

    // 公開用プロパティ（環境側が参照）
    public float EpisodeAvgSpeed => speedCount > 0 ? speedSum / speedCount : 0f;
    public float EpisodeMaxSpeed => speedCount > 0 ? episodeMaxSpeed : 0f;
    public float EpisodeMinSpeed => speedCount > 0 ? episodeMinSpeed : 0f;

    private void Awake() {
        CarRb = GetComponent<Rigidbody>();
        Controller = GetComponent<CarController>();
        Sensors = GetComponentsInChildren<Sensor>();
        isBattle=false;
    }

    public void Start() {
        StartPosition = transform.position;
        StartRotation = transform.rotation;
        CurrentStep = 0;
        LocalStep = 0;
        LastPosition = StartPosition;
        TotalDistance = 0;

        speedSum = 0f;
        speedCount = 0;
        episodeMaxSpeed = 0f;
        episodeMinSpeed = float.PositiveInfinity;
    }

    public override void AgentReset() {
        transform.position = StartPosition;
        transform.rotation = StartRotation;

        Controller.GasInput = 0;
        Controller.SteerInput = 0;
        Controller.BrakeInput = 0;

        gameObject.SetActive(false);
        gameObject.SetActive(true);

        CurrentStep = 0;
        LocalStep = 0;
        TotalDistance = 0;
        LastPosition = StartPosition;

        WaypointIndex = 0;

        speedSum = 0f;
        speedCount = 0;
        episodeMaxSpeed = 0f;
        episodeMinSpeed = float.PositiveInfinity;
    }

    /// <summary>
    /// 取得可能なエージェントの状態をすべて返す．
    /// </summary>
    /// <remarks>
    /// 返り値のリスト（状態）の中身は以下の通り．
    /// 
    /// | インデックス | 内容 |
    /// | --- | --- |
    /// | 0--4 | 前方の対壁センサー（Sensors_0_Wall）
    /// | 5--9 | 右方の対壁センサー（Sensors_1_Wall）
    /// | 10--14 | 左方の対壁センサー（Sensors_2_Wall）
    /// | 15--19 | 後方の対壁センサー（Sensors_3_Wall）
    /// | 20--24 | 前方の対車センサー（Sensors_0_Player）
    /// | 25--29 | 前方の対車センサー（Sensors_1_Player）
    /// | 30--34 | 前方の対車センサー（Sensors_2_Player）
    /// | 35--39 | 前方の対車センサー（Sensors_3_Player）
    /// | --- | --- |
    /// | 40--42 | 自車のローカル速度 |
    /// | --- | --- |
    /// | 43--45 | コース上の前方向ベクトル（次のWaypointの方向）
    /// | --- | --- |
    /// 
    /// </remarks>
    /// <returns>状態</returns>
    public override List<double> GetAllObservations() {
        var results = new List<double>();
        // センサー
        Array.ForEach(Sensors, sensor => {
            results.AddRange(sensor.Hits());
        });
        // 速度
        Vector3 local_v = CarRb.transform.InverseTransformDirection(CarRb.linearVelocity);
        for(int i = 0; i < 3; i++) {
            results.Add(local_v[i] / 5.0f);
        }
        // 前方向
        Vector3 localNextDirection = CarRb.transform.InverseTransformDirection(NextWaypointDirection);
        for(int i = 0; i < 3; i++) {
            results.Add(localNextDirection[i]);
        }
        return results;
    }

    /// <summary>
    /// センサーの角度を変更する．
    /// </summary>
    /// <param name="config">センサーの角度のリスト</param>
    public override void SetAgentConfig(List<double> config)
    {
        base.SetAgentConfig(config);

        if(config == null) return;

        int configIndex = 0;
        foreach(Sensor sensor in Sensors)
        {
            int sensorIndex = 0;
            while(configIndex < config.Count && sensorIndex < sensor.Angles.Length)
            {
                sensor.Angles[sensorIndex] = (float)config[configIndex];
                sensorIndex++;
                configIndex++;
            }
        }
    }

    public override int GetState() { 
        var stateDivide = 3;
        var results = new List<double>();
        var r = 0;
        Array.ForEach(Sensors, sensor => {
            results.AddRange(sensor.Hits());
        });

        // Sensors to use (up to 7).
        int[] indices = { 0, 1, 2, 3, 4, 40, 42 };

        List<double> filteredResult = new List<double>();

        foreach (int index in indices)
        {
            if (index >= 0 && index < results.Count)
            {
                filteredResult.Add(results[index]);
            }
        }

        for(int i = 0; i < filteredResult.Count; i++) { //　各センサーの値を0以上stateDivide未満の整数に離散化
            var v = Mathf.FloorToInt(Mathf.Lerp(0, stateDivide - 1, (float)filteredResult[i]));
            if(filteredResult[i] >= 0.99f) {
                v = stateDivide - 1;
            }
            r += (int)(v * Mathf.Pow(stateDivide, i)); // 各センサの離散化した値を一つの離散変数rにまとめる
        }
        var numStates = (int)Mathf.Pow(stateDivide, filteredResult.Count);
        int n;
        if(CarRb.linearVelocity.magnitude < 10) { n = 0; } // 車の速度の絶対値を状態として加える
        else if(CarRb.linearVelocity.magnitude < 15) { n = 1; }
        else { n = 2; }
        r += numStates * n;
        return r;
    }

    public override List<double> CollectObservations() { // 環境や車についての情報を取得し、現在の環境におけるエージェントの状態を決定する
        // センサーの距離をリストに追加する
        var results = new List<double>();
        Array.ForEach(Sensors, sensor => {
            results.AddRange(sensor.Hits());
        });
        Vector3 local_v = CarRb.transform.InverseTransformDirection(CarRb.linearVelocity);
        results.Add(local_v.x / 5.0f);
        results.Add(local_v.z / 5.0f);
        return results;
    }

    /*************編集ポイント***************
        センサ情報等、あるフレームにおける環境情報を取得するための関数
    */

    public override List<double> OriginalObservations(){
        //センサ情報を取得
        var results = new List<double>();
        Array.ForEach(Sensors, sensor => {
            results.AddRange(sensor.Hits());
        });
        /*
            必要に応じて追加で格納する情報を追加
        */
        results.Add(CarRb.linearVelocity.magnitude);
        return results;
    }

    public override double[] ActionNumberToVectorAction(int ActionNumber) {
        var action = new double[3];
        var steering = 0.0d;
        var braking = 0.0d;
        if(ActionNumber % 6 == 1) {
            steering = 1d;
        }
        else if(ActionNumber % 6 == 2) {
            steering = -1d;
        }
        else if(ActionNumber % 6 == 3) {
            steering = 0.5d;
        }
        else if(ActionNumber % 6 == 4) {
            steering = -0.5d;
        }
        else if(ActionNumber % 6 == 5) {
            braking = 0.5d;
        }

        var gasInput = 0.5d;
        action[0] = steering;
        action[1] = gasInput;
        action[2] = braking;
        return action;
    }

    public override void AgentAction(double[] vectorAction, bool inReverse) {
        CurrentStep++;
        LocalStep++;
        TotalDistance += (transform.position - LastPosition).magnitude;

        if (IsLearning)
        {
            if (CurrentStep > CurrentStepMax)
            {
                DoneWithReward(TotalDistance);
                return;
            }

            if (LocalStep > LocalStepMax)
            {
                DoneWithReward(-1.0f / TotalDistance);
                return;
            }
        }
        
        // 速度統計の更新（毎ステップ）
        float currentSpeed = CarRb.linearVelocity.magnitude;
        speedSum += currentSpeed;
        speedCount++;
        if (currentSpeed > episodeMaxSpeed) episodeMaxSpeed = currentSpeed;
        if (currentSpeed < episodeMinSpeed) episodeMinSpeed = currentSpeed;


        var steering = Mathf.Clamp((float)vectorAction[0], -1.0f, 1.0f);
        float gasInput = 0.0f;
        if (!inReverse) {
            gasInput = Mathf.Clamp((float)vectorAction[1], 0.0f, 1.0f);
        } else {
            gasInput = Mathf.Clamp((float)vectorAction[1], -0.3f, 0.0f);
        }
        var braking = Mathf.Clamp((float)vectorAction[2], 0.0f, 1.0f);

        Controller.SteerInput = steering; // ハンドル
        Controller.GasInput = gasInput; // アクセル
        Controller.BrakeInput = braking; // ブレーキ

        // 密報酬（速度）
        if (useDenseSpeedReward)
        {
            float dense = CalcSpeedDenseReward();
            if (dense > 0f && !AllowPlusReward) dense = 0f;

            // ML-Agents 互換の Agent なら AddReward が使えます
            AddReward(dense);
            // AddReward が無い場合は、SetReward(GetReward()+dense) 等に置き換えてください
        }
        
        // 壁接近ペナルティ
        if (useNearWallPenalty) {
            float minWallDist = GetMinWallSensorDistance();
            if (minWallDist >= 0f && minWallDist < nearWallThreshold) {
                AddReward(nearWallPenalty);
            }
        }

        LastPosition = transform.position;
    }

    public override void GoStraight(){
        var gasInput = Mathf.Clamp(1.0f, 0.5f, 1.0f);
        Controller.GasInput = gasInput;
        LastPosition = transform.position;
    }

    public override float GetDistance()
    {
        return TotalDistance;
    }

    /// <summary>
    /// 衝突時に呼び出されるコールバック
    /// </summary>
    /// <param name="collision"></param>
    public void OnCollisionEnter(Collision collision) { // オブジェクトに衝突したときのコールバック関数
        if(collision.gameObject.tag == "wall") {
            if (BackUpOnCollision) {
                StartBackingUp();
            } else {
                DoneWithReward(-(1.0f / TotalDistance) * collisionPenaltyScale);
            }
        }
    }

    public void OnTriggerEnter(Collider other) { // 逆走した時のコールバック間数
        var waypoint = other.GetComponent<Waypoint>();
        if(waypoint == null) {
            return;
        }

        //逆走した時
        if (!BackUpOnCollision) {
            bool reverseRunFromStartPosition = waypoint.Index>WaypointIndex+1;
            bool reverseRunFromOtherPosition = waypoint.Index<=WaypointIndex;
            if( reverseRunFromOtherPosition|| reverseRunFromStartPosition){
                DoneWithReward(-1.0f / TotalDistance);
                return;
            }
        }

        WaypointIndex = waypoint.Index;
        if(waypoint.IsLast) {
            WaypointIndex = 0;
            passLastPoint=true;
        }
        if(isBattle && WaypointIndex==1 && passLastPoint==true){
            agentExecutor.Win(agentIndex);
        }
        LocalStep = 0;

        NextWaypointDirection = waypoint.NextDirection;
    }

    public override void Stop()
    {
        CarRb.linearVelocity = Vector3.zero;
        CarRb.angularVelocity = Vector3.zero;
        Controller.Stop();
    }

    private float CalcSpeedDenseReward()
    {
        Vector3 v = CarRb.linearVelocity;
        float speed = v.magnitude;
        if (speed < 1e-3f)
        {
            // ほぼ停止は遅すぎペナルティのみ
            return lowSpeedPenalty;
        }

        Vector3 trackDir = NextWaypointDirection.sqrMagnitude > 1e-6f
            ? NextWaypointDirection.normalized
            : Vector3.forward;

        float forwardSpeed = Vector3.Dot(v, trackDir);

        float dense = 0f;

        // 目標速度に近づくほど加点（進行方向成分 or 純速度）
        float basis = useForwardComponent ? Mathf.Max(forwardSpeed, 0f) : speed;
        float ratio = Mathf.Clamp01(basis / Mathf.Max(targetSpeed, 0.01f));
        dense += ratio * stepRewardScale;

        // 遅すぎ・速すぎのペナルティ
        if (speed < minSpeed) dense += lowSpeedPenalty;
        if (speed > maxSpeed) dense += highSpeedPenalty;

        // 逆走（進行方向が負）
        if (forwardSpeed < -0.5f) dense += reversePenalty;

        return dense;
    }
    
    // 壁センサー距離の最小値取得（Hits() は実距離[m]を返す前提）
    // ここで各センサーの最大距離で割って 0〜1 に正規化して最小値を返す
    private float GetMinWallSensorDistance() {
        if (Sensors == null) return -1f;

        float minNorm = float.PositiveInfinity;
        foreach (var sensor in Sensors) {
            var hits = sensor.Hits();
            if (hits == null || hits.Count == 0) continue;

            float maxDist = sensor.Distance;           // そのセンサーの最大到達距離[m]
            if (maxDist <= 1e-6f) continue;            // 0除算防止

            for (int i = 0; i < hits.Count; i++) {
                float raw = (float)hits[i];            // 実距離[m]（未ヒット時は maxDist が返る実装）
                float norm = Mathf.Clamp01(raw / maxDist); // 0〜1 に正規化（近いほど小さい）
                if (norm < minNorm) minNorm = norm;
            }
        }
        return float.IsInfinity(minNorm) ? -1f : minNorm;
    }

    private void DoneWithReward(float reward) {
        if(reward > 0 && !AllowPlusReward) {
            reward = 0;
        }

        SetReward(reward);
        Done();
    }
}
