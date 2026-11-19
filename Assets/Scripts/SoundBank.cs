using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 효과음 모음. Assets/Resources/SoundBank.asset 을 선택해 Inspector에서 소리를 넣거나 바꾸면 바로 적용됨.
[CreateAssetMenu(menuName = "BounceBall/Sound Bank")]
public class SoundBank : ScriptableObject
{
    [Header("타일에 부딪힘")]
    public AudioClip bounceDirt;   // 일반·불안정·선풍기 블록
    public AudioClip bounceHoney, bounceIce, bounceSlime;
    public AudioClip bounceSpike;  // 가시(게임오버)
    public AudioClip bounceWeak;   // 약하게 부딪힘(느린 속도)
    public AudioClip iceSlideLoop; // 얼음 위에서 미끄러지는 동안 반복
    [Header("게임")]
    public AudioClip click, launch, star, coin, blackCoin, portal, crumble, clear, gameOver;
    [Range(0f, 1f)] public float volume = 1f;
    [Tooltip("이 속도(칸/초)보다 약하게 부딪히면 bounceWeak, 0.8 미만이면 소리 없음")]
    public float weakSpeed = 2.5f;
    [Header("배경음악")]
    public AudioClip bgm;                       // 게임 시작부터 반복 재생
    [Range(0f, 1f)] public float bgmVolume = 0.6f;
}

// 효과음 재생. 씬이 바뀌어도 유지되는 오브젝트 하나에서 재생하고, 모든 씬의 버튼에 클릭음을 자동으로 붙임
public static class Sfx
{
    static SoundBank bank;
    static AudioSource oneShot, loop, music;
    static bool loopOn; // 반복 재생을 켜 둔 상태 (isPlaying 대신 이 값으로 판단 → 에디터 일시정지 등에 흔들리지 않음)
    static readonly Dictionary<AudioClip, float> lastPlayed = new();

    public static SoundBank Bank => bank != null ? bank : bank = Resources.Load<SoundBank>("SoundBank");

    // 음소거 설정 (설정 팝업에서 바꿈). PlayerPrefs에 저장되어 게임을 꺼도 유지
    public static bool BgmMuted
    {
        get => PlayerPrefs.GetInt("bgmMuted", 0) == 1;
        set
        {
            PlayerPrefs.SetInt("bgmMuted", value ? 1 : 0);
            PlayerPrefs.Save();
            if (music != null) music.mute = value;
        }
    }

    public static bool SfxMuted
    {
        get => PlayerPrefs.GetInt("sfxMuted", 0) == 1;
        set
        {
            PlayerPrefs.SetInt("sfxMuted", value ? 1 : 0);
            PlayerPrefs.Save();
            if (value) Loop(null, false); // 미끄러짐 반복음도 즉시 정지
        }
    }

    static void Ensure()
    {
        if (oneShot != null) return;
        var go = new GameObject("Sfx");
        Object.DontDestroyOnLoad(go);
        oneShot = go.AddComponent<AudioSource>();
        loop = go.AddComponent<AudioSource>();
        loop.loop = true;
        music = go.AddComponent<AudioSource>();
        music.loop = true;
    }

    // 배경음악 시작 (게임 시작 시 한 번). 씬이 바뀌어도 끊기지 않고 계속 재생
    static void StartBgm()
    {
        if (Bank == null || Bank.bgm == null) return;
        Ensure();
        if (music.isPlaying && music.clip == Bank.bgm) return;
        music.clip = Bank.bgm;
        music.volume = Bank.bgmVolume;
        music.mute = BgmMuted;
        music.Play();
    }

    public static void Play(AudioClip clip, float volume = 1f)
    {
        if (clip == null || Bank == null || SfxMuted) return;
        // 같은 소리가 한 순간에 여러 번 겹쳐 울리지 않게 (튕김이 연달아 날 때)
        if (lastPlayed.TryGetValue(clip, out var t) && Time.unscaledTime - t < 0.05f) return;
        lastPlayed[clip] = Time.unscaledTime;
        Ensure();
        oneShot.PlayOneShot(clip, volume * Bank.volume);
    }

    // 반복 재생 켜기/끄기. on이 바뀔 때만 재생/정지하고, false가 되는 즉시 멈춤
    public static void Loop(AudioClip clip, bool on)
    {
        if (SfxMuted) on = false;         // 효과음 음소거 중엔 반복음도 끔
        if (loop == null) loopOn = false; // 소스가 새로 만들어지면 상태 초기화
        if (on == loopOn) return;
        if (!on)
        {
            loop.Stop();
            loopOn = false;
            return;
        }
        if (clip == null || Bank == null) return;
        Ensure();
        loop.clip = clip;
        loop.volume = Bank.volume;
        loop.Play();
        loopOn = true;
    }

    // 타일에 부딪힘: 블록 그룹 이름(GameManager.Build에서 붙인 이름)과 부딪힌 속도로 소리를 고름
    public static void Impact(string group, float speed)
    {
        var b = Bank;
        if (b == null) return;
        if (group == "Spikes") { Play(b.bounceSpike); return; }
        if (speed < 0.8f) return; // 굴러가며 살짝 스친 정도는 무음
        var clip = speed < b.weakSpeed ? b.bounceWeak : group switch
        {
            "Honey" => b.bounceHoney,
            "Ice" => b.bounceIce,
            "Slime" or "SlimeUp" or "SlimeDown" or "SlimeLeft" or "SlimeRight" => b.bounceSlime,
            _ => b.bounceDirt,
        };
        Play(clip, Mathf.Clamp01(0.4f + speed / 12f));
    }

    // 게임 시작 시: 배경음악 재생 + 모든 씬의 버튼에 클릭음 연결
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void HookButtons()
    {
        StartBgm();
        AddClickSounds();
        SceneManager.sceneLoaded += (_, _) => AddClickSounds();
    }

    static void AddClickSounds()
    {
        foreach (var btn in Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            btn.onClick.AddListener(() => Play(Bank != null ? Bank.click : null));
    }
}
