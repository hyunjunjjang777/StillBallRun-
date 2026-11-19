using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// 오른쪽 위 설정 버튼 + 설정 팝업 (배경음악·효과음 음소거).
// 프리팹 Assets/Prefabs/SettingsUI 를 각 씬 Canvas 맨 뒤(가장 위에 그려지게)에 둠.
// 팝업이 열려 있는 동안은 게임 일시정지 + 화면 전체를 가려서 공 조작·다른 버튼 클릭을 막음
public class SettingsMenu : MonoBehaviour
{
    [SerializeField] GameObject popup;          // 어두운 배경 + 패널
    [SerializeField] RectTransform panel;       // 열릴 때 튀어나오는 연출 대상
    [SerializeField] Button bgmButton, sfxButton;
    [SerializeField] Sprite onSprite, offSprite; // 켜짐: 나무 판자, 꺼짐: 돌판 (잠긴 스테이지와 같은 표현)
    [SerializeField] Text resetLabel;            // 기록 초기화 버튼 글자 (RESET → SURE? → DONE!)
    bool resetConfirming;                        // 한 번 눌러 확인을 기다리는 중

    void Start()
    {
        popup.SetActive(false);
        Refresh();
    }

    public void Open()
    {
        popup.SetActive(true);
        Time.timeScale = 0f;
        Refresh();
        resetConfirming = false;
        resetLabel.text = "RESET";
        StartCoroutine(PopIn());
    }

    public void Close()
    {
        popup.SetActive(false);
        Time.timeScale = 1f;
    }

    public void ToggleBgm()
    {
        Sfx.BgmMuted = !Sfx.BgmMuted;
        Refresh();
    }

    public void ToggleSfx()
    {
        Sfx.SfxMuted = !Sfx.SfxMuted;
        Refresh();
    }

    // 기록 초기화: 실수 방지로 두 번 눌러야 실행 (1번째 SURE? → 2번째 초기화)
    public void ResetRecords()
    {
        if (!resetConfirming)
        {
            resetConfirming = true;
            resetLabel.text = "SURE?";
            return;
        }
        Progress.ResetAll();
        resetConfirming = false;
        resetLabel.text = "DONE!";
        var title = FindFirstObjectByType<TitleMenu>(); // 타이틀 화면이면 스테이지 버튼 잠금·별 바로 갱신
        if (title != null) title.RefreshStages();
    }

    void Refresh()
    {
        SetToggle(bgmButton, !Sfx.BgmMuted);
        SetToggle(sfxButton, !Sfx.SfxMuted);
    }

    void SetToggle(Button b, bool on)
    {
        b.GetComponent<Image>().sprite = on ? onSprite : offSprite;
        b.GetComponentInChildren<Text>().text = on ? "ON" : "OFF";
    }

    // 작게 시작해서 살짝 커졌다가 제자리로 (일시정지 중이라 실제 시간 기준)
    IEnumerator PopIn()
    {
        const float duration = 0.18f;
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            float p = t / duration;
            panel.localScale = Vector3.one * (p < 0.7f ? Mathf.Lerp(0.6f, 1.06f, p / 0.7f) : Mathf.Lerp(1.06f, 1f, (p - 0.7f) / 0.3f));
            yield return null;
        }
        panel.localScale = Vector3.one;
    }

    void OnDestroy() => Time.timeScale = 1f; // 열린 채로 씬이 바뀌어도 일시정지가 남지 않게
}
