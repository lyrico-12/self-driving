using UnityEngine;
using System.Collections.Generic;
using System; // StringComparison を使うなら
using System.Linq;

[DisallowMultipleComponent]
public class StageManager : MonoBehaviour
{
    [SerializeField] private List<GameObject> stagePrefabs = new List<GameObject>();
    private int currentStageIdx = 0;
    private Transform stageRoot = null;
    private string cameraAnchorTag = "CameraAnchor";
    private string cameraAnchorName = "CameraAnchor";
    private float defaultFarClip = 1000f;

    [SerializeField] private int generationFreqency = 1;
    [SerializeField] private int stage4GenerationFreqency = 1;
    [SerializeField] private int challenge2GenerationFreqency = 2;

    private GameObject currentStage;

    // 直近で適用した「世代グループ」(= generation/generationFreqency)
    private int lastGenGroup = int.MinValue;

    private void Awake() {
        if (stageRoot == null) {
            var go = new GameObject("StageRoot");
            stageRoot = go.transform;
            stageRoot.position = Vector3.zero;
            stageRoot.rotation = Quaternion.identity;
            stageRoot.localScale = Vector3.one;
        }
    }

    public void InitFirstStage(int generation) {
        ApplyStageForGeneration(generation, forceReload: true);
    }

    public void SwitchStage(int generation) {
        ApplyStageForGeneration(generation, forceReload: false);
    }

    // 世代に応じてステージを決定（Challenge* は2世代、その他は1世代）
    private void ApplyStageForGeneration(int generation, bool forceReload) {
        if (stagePrefabs == null || stagePrefabs.Count == 0) {
            Debug.LogWarning("StageManager: stagePrefabs is empty.");
            return;
        }

        // 各ステージの保持世代数を決める（Challengeで始まるものは2、それ以外は1）
        int[] freqs = new int[stagePrefabs.Count];
        int cycleLength = 0;
        for (int i = 0; i < stagePrefabs.Count; i++) {
            var name = stagePrefabs[i]?.name ?? "";
            if (name.StartsWith("Challenge2", StringComparison.OrdinalIgnoreCase)){
                freqs[i] = challenge2GenerationFreqency;
            } else if (name.StartsWith("Stage4", StringComparison.OrdinalIgnoreCase)) {
                freqs[i] = stage4GenerationFreqency;
            } else {
                freqs[i] = generationFreqency;
            }
            cycleLength += freqs[i];
        }

        if (cycleLength == 0) {
            Debug.LogWarning("StageManager: cycleLength became 0.");
            return;
        }

        // generation をサイクル長で折り返して現在位置を求める
        int pos = generation % cycleLength;

        // pos を使ってインデックスを決定
        int selectedIdx = 0;
        for (int i = 0; i < freqs.Length; i++) {
            if (pos < freqs[i]) {
                selectedIdx = i;
                break;
            }
            pos -= freqs[i];
        }

        if (forceReload || selectedIdx != currentStageIdx || lastGenGroup == int.MinValue) {
            lastGenGroup = generation; // 記録用（用途に応じ変更可）
            LoadStage(selectedIdx);
        }

        ApplyCameraFromStage();
    }

    private void LoadStage(int newIndex) {
        newIndex = Mathf.Clamp(newIndex, 0, stagePrefabs.Count - 1);
        currentStageIdx = newIndex;
        ClearStage();
        currentStage = Instantiate(stagePrefabs[currentStageIdx], Vector3.zero, Quaternion.identity, stageRoot);
        // 必要ならここで SpawnPoint 再取得など
    }

    private void ClearStage() {
        if (currentStage != null) {
            Destroy(currentStage);
            currentStage = null;
        }
        if (stageRoot != null) {
            for (int i = stageRoot.childCount - 1; i >= 0; --i) {
                Destroy(stageRoot.GetChild(i).gameObject);
            }
        }
    }

    // ステージ内の CameraAnchor を見つけてカメラをスナップ
    private void ApplyCameraFromStage() {
        var cam = Camera.main;
        if (cam == null || currentStage == null) return;

        Transform anchor = null;
        foreach (var t in currentStage.GetComponentsInChildren<Transform>(true)) {
            if (t.CompareTag(cameraAnchorTag)) { anchor = t; break; }
        }
        if (anchor == null) {
            foreach (var t in currentStage.GetComponentsInChildren<Transform>(true)) {
                if (t.name == cameraAnchorName) { anchor = t; break; }
            }
        }

        if (anchor != null) {
            cam.transform.SetPositionAndRotation(anchor.position, anchor.rotation);

            // ここでステージ名に応じて Far を切り替え（Instantiate後は "(Clone)" が付く点に注意）
            var stageName = currentStage.name.Replace("(Clone)", "");
            if (stageName == "Stage5") {
                cam.farClipPlane = 1400f;
            } else {
                cam.farClipPlane = defaultFarClip;
            }

            // 任意: 他のカメラ設定をここで反映
        } else {
            Debug.LogWarning("StageManager: CameraAnchor not found in stage. Camera not moved.");
        }
    }

    public IReadOnlyList<Transform> GetSpawnPoints()
    {
        var list = new List<Transform>();
        if (currentStage == null) return list;
        foreach (var t in currentStage.GetComponentsInChildren<Transform>(true)) {
            if (t.CompareTag("SpawnPoint")) list.Add(t);
        }
        list.Sort((a,b) => string.Compare(a.name, b.name, StringComparison.Ordinal));
        return list;
    }
}
