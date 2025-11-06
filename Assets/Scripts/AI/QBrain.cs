// SerialID: [77a855b2-f53d-4b80-9c94-c40562952b74]
using UnityEngine;
using System.IO;
using System.Linq;

public class QBrain : Brain
{
    public int StateSize { get; private set; }
    public int ActionSize { get; private set; }

    private float[][] QTable { get; set; }
    private float Epsilon { get; set; } = 1.0f;
    private float EpsilonMin { get; set; } = 0.0f;
    private int AnnealingSteps { get; set; } = 100000;
    private float Gamma { get; set; } = 0.75f;
    private float Alpha { get; set; } = 0.1f;

    public void CreateTable() { // Qテーブルの初期化
        QTable = new float[StateSize][];
        for(int i = 0; i < StateSize; i++) {
            QTable[i] = new float[ActionSize];
        }
    }

    public QBrain(int stateSize, int actionSize) {
        StateSize = stateSize;
        ActionSize = actionSize;
        CreateTable();
    }

    public int GetAction(int state) {
        int action;

        if(Epsilon <= UnityEngine.Random.Range(0.0f, 1.0f)) {
            action = QTable[state].ToList().IndexOf(QTable[state].Max());
        }
        else {
            action = UnityEngine.Random.Range(0, ActionSize);
        }

        if(Epsilon > EpsilonMin) {
            Epsilon -= ((1f - EpsilonMin) / AnnealingSteps);
        }

        return action;
    }

    public int GetActionWithoutEpsilon(int state) {
        return QTable[state].ToList().IndexOf(QTable[state].Max());
    }

    public float[] GetValue() {
        float[] value_table = new float[QTable.Length];
        for(int i = 0; i < QTable.Length; i++) {
            value_table[i] = QTable[i].Average();
        }
        return value_table;
    }

    public void UpdateTable(int lastState, int nextState, int action, float reward, bool done) {
        if(action == -1) {
            return;
        }

        if(done) {
            QTable[lastState][action] += Alpha * (reward - QTable[lastState][action]);
            return;
        }

        if(nextState != lastState) {
            QTable[lastState][action] += Alpha * (reward + Gamma * QTable[nextState].Max() - QTable[lastState][action]);
        }
        else {
            QTable[lastState][action] += Alpha * (reward - QTable[lastState][action]);
        }
    }

    public override void Save(string path) {
        using(var bw = new BinaryWriter(new FileStream(path, FileMode.OpenOrCreate))) {
            bw.Write(StateSize);
            bw.Write(ActionSize);
            for(var i = 0; i < StateSize; i++) {
                for(var j = 0; j < ActionSize; j++) {
                    bw.Write(QTable[i][j]);
                }
            }
        }
    }

    public static QBrain Load(TextAsset text) {
        var bytes = text.bytes;
        QBrain brain = null;
        using(var br = new BinaryReader(new MemoryStream(text.bytes))) {
            var stateSize = br.ReadInt32();
            var actionSize = br.ReadInt32();
            brain = new QBrain(stateSize, actionSize);
            for(var i = 0; i < stateSize; i++) {
                for(var j = 0; j < actionSize; j++) {
                    brain.QTable[i][j] = br.ReadSingle();
                }
            }
        }
        return brain;
    }
}
