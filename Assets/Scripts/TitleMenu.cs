using UnityEngine;
using UnityEngine.SceneManagement;

public class TitleMenu : MonoBehaviour
{
    [SerializeField] GameObject mainPanel, stagePanel, tutorialPanel, editorPanel;
    [SerializeField] Sprite unlockedSprite, lockedSprite; // 스테이지 버튼: 나무 판자 / 잠김(이끼 낀 돌판)
    [SerializeField] UnityEngine.UI.Button unlockAllButton; // 스테이지 메뉴 오른쪽 위: 모든 스테이지 열기 토글

    void Start()
    {
        RefreshStages();
        Show(mainPanel);
    }

    // 스테이지 버튼: 해금 안 된 건 회색·클릭 불가, 버튼 아래 별 3개는 최고 기록만큼 노란색
    public void RefreshStages()
    {
        for (int i = 0; i < GameManager.StageCount; i++)
        {
            var btn = stagePanel.transform.Find("Stage" + (i + 1));
            bool unlocked = Progress.Unlocked(i);
            btn.GetComponent<UnityEngine.UI.Button>().interactable = unlocked;
            btn.GetComponent<UnityEngine.UI.Image>().sprite = unlocked ? unlockedSprite : lockedSprite;
            var stars = btn.Find("Stars");
            for (int s = 0; s < stars.childCount; s++)
                stars.GetChild(s).GetComponent<UnityEngine.UI.Image>().color =
                    s < Progress.Stars(i) ? new Color(1f, 0.85f, 0.15f) : new Color(0.55f, 0.55f, 0.55f);
        }
        // 토글 버튼 모양: 켜짐 = 나무 판자, 꺼짐 = 돌판 (설정 팝업과 같은 표현)
        bool on = Progress.UnlockAll;
        unlockAllButton.GetComponent<UnityEngine.UI.Image>().sprite = on ? unlockedSprite : lockedSprite;
        unlockAllButton.GetComponentInChildren<UnityEngine.UI.Text>().text = on ? "UNLOCK ON" : "UNLOCK OFF";
    }

    public void ToggleUnlockAll()
    {
        Progress.UnlockAll = !Progress.UnlockAll;
        RefreshStages();
    }

    public void Show(GameObject panel)
    {
        mainPanel.SetActive(panel == mainPanel);
        stagePanel.SetActive(panel == stagePanel);
        tutorialPanel.SetActive(panel == tutorialPanel);
        editorPanel.SetActive(panel == editorPanel);
    }

    public void EditStage(int index) => Edit(false, index);
    public void EditTutorial(int index) => Edit(true, index);

    static void Edit(bool tutorial, int index)
    {
        MapEditor.IsTutorial = tutorial;
        MapEditor.Index = index;
        SceneManager.LoadScene("MapEditorScene");
    }

    public void PlayStage(int index) => Play(false, index);
    public void PlayTutorial(int index) => Play(true, index);

    static void Play(bool tutorial, int index)
    {
        GameManager.IsTutorial = tutorial;
        GameManager.Index = index;
        GameManager.NewSession = true;
        SceneManager.LoadScene("GameScene");
    }

    public void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
