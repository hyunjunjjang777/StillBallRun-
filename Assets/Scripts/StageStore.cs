using System.IO;
using UnityEngine;

// 맵 텍스트 읽기/쓰기.
// 에디터: Assets/Resources 파일을 직접 읽고 씀(저장하면 원본 맵 파일이 바뀜).
// 빌드: Resources는 읽기 전용이라 persistentDataPath에 저장하고, 그 파일이 있으면 우선 사용.
public static class StageStore
{
    static string Name(bool tutorial, int index) => (tutorial ? "Tutorials/tut" : "Stages/stage") + (index + 1).ToString("00");

    static string FilePath(bool tutorial, int index) =>
#if UNITY_EDITOR
        Path.Combine(Application.dataPath, "Resources", Name(tutorial, index) + ".txt");
#else
        Path.Combine(Application.persistentDataPath, Name(tutorial, index) + ".txt");
#endif

    public static string Load(bool tutorial, int index)
    {
        var path = FilePath(tutorial, index);
        return File.Exists(path) ? File.ReadAllText(path) : Resources.Load<TextAsset>(Name(tutorial, index)).text;
    }

    // 스테이지 코멘트: Assets/Resources/Comments.txt 의 "번호 = 내용" 줄 (튜토리얼은 "T번호"). 없으면 빈 문자열
    public static string Comment(bool tutorial, int index)
    {
        var file = Resources.Load<TextAsset>("Comments");
        if (file == null) return "";
        string key = (tutorial ? "T" : "") + (index + 1);
        foreach (var raw in file.text.Split('\n'))
        {
            var line = raw.Trim();
            int eq = line.IndexOf('=');
            if (eq > 0 && line.Substring(0, eq).Trim() == key)
                return line.Substring(eq + 1).Trim().Replace("\\n", "\n"); // \n 으로 줄바꿈
        }
        return "";
    }

    // 튜토리얼 설명: Assets/Resources/튜토리얼_설명.txt 에서 "# 번호" 줄 아래부터 다음 "# 번호" 전까지 (여러 줄 가능)
    public static string TutorialText(int index)
    {
        var file = Resources.Load<TextAsset>("튜토리얼_설명");
        if (file == null) return "";
        var sb = new System.Text.StringBuilder();
        bool inSection = false;
        foreach (var raw in file.text.Replace("\r", "").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.StartsWith("//")) continue; // 설명용 주석 줄
            if (line.StartsWith("#"))
            {
                if (inSection) break;
                inSection = line.Substring(1).Trim() == (index + 1).ToString();
                continue;
            }
            if (inSection) sb.Append(line).Append('\n');
        }
        return sb.ToString().Trim();
    }

    public static void Save(bool tutorial, int index, string map)
    {
        var path = FilePath(tutorial, index);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, map);
#if UNITY_EDITOR
        UnityEditor.AssetDatabase.ImportAsset("Assets/Resources/" + Name(tutorial, index) + ".txt");
#endif
    }
}

// 스테이지 진행 상황(클리어 여부, 최고 별 개수). PlayerPrefs에 저장되어 게임을 꺼도 유지
public static class Progress
{
    public static int Stars(int stage) => PlayerPrefs.GetInt("stars" + stage, 0);
    public static bool Cleared(int stage) => PlayerPrefs.GetInt("clear" + stage, 0) == 1;
    public static bool Unlocked(int stage) => UnlockAll || stage == 0 || Cleared(stage - 1); // 이전 스테이지를 깨야 해금

    // 기록 초기화(설정 팝업): 모든 스테이지의 클리어·별 기록만 지움 → 1스테이지만 열린 처음 상태.
    // 음소거·UNLOCK 토글 같은 설정은 그대로
    public static void ResetAll()
    {
        for (int i = 0; i < GameManager.StageCount; i++)
        {
            PlayerPrefs.DeleteKey("clear" + i);
            PlayerPrefs.DeleteKey("stars" + i);
        }
        PlayerPrefs.Save();
    }

    // 스테이지 메뉴 오른쪽 위 토글: 켜면 깨지 않은 스테이지도 모두 열림 (클리어·별 기록은 그대로)
    public static bool UnlockAll
    {
        get => PlayerPrefs.GetInt("unlockAll", 0) == 1;
        set { PlayerPrefs.SetInt("unlockAll", value ? 1 : 0); PlayerPrefs.Save(); }
    }

    public static void Clear(int stage, int stars)
    {
        PlayerPrefs.SetInt("clear" + stage, 1);
        if (stars > Stars(stage)) PlayerPrefs.SetInt("stars" + stage, stars); // 최고 기록만
        PlayerPrefs.Save();
    }
}
