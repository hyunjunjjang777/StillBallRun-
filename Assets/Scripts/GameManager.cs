using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class GameManager : MonoBehaviour
{
    public const int StageCount = 15, TutorialCount = 10;
    public static bool IsTutorial;
    public static int Index; // 0부터 시작
    public static GameManager I { get; private set; }
    public static string TestMap; // 맵 에디터 PLAY로 들어온 경우 저장 안 된 맵. null이면 일반 플레이

    [Header("강화 슬라임")]
    public float superSlimeSpeed = 12f;   // 화살표 면에 닿았을 때 그 방향으로 가하는 속도(칸/초)
    [Header("발사 횟수 제한 스테이지 (튜토리얼 제외)")]
    public int limitedFrom = 6, limitedTo = 10; // 이 범위 스테이지는 발사 횟수 제한
    public int limitedShots = 1;                // 시작 발사 횟수. 코인 하나당 +1
    public int[] limitedTutorials = { 5 };      // 발사 횟수 제한을 똑같이 적용할 튜토리얼 번호
    [Header("불안정한 블록")]
    public float unstableDelay = 2f;      // 공이 닿은 뒤 부서지기까지(초)
    [Header("선풍기")]
    public int fanRange = 4;              // 바람 길이(칸). 폭은 1칸
    public float fanAccel = 15f;          // 바람 칸 안에서 받는 가속도(칸/초²)
    [Header("카메라 (맵이 한 화면보다 넓을 때 공 따라가기)")]
    [Range(0.1f, 0.5f)] public float followScreenX = 0.3f; // 공이 놓일 화면 가로 위치(0=왼끝, 0.5=가운데)
    public float followSmooth = 0.35f;                      // 따라가는 부드러움(초). 클수록 천천히 따라옴
    const int ViewColumns = 24;                             // 한 화면에 보이는 최대 가로 칸 수(기존 스테이지 크기)

    [SerializeField] GameObject resultPanel;
    [SerializeField] Text resultText, stageLabel, statsLabel;
    [SerializeField] Text commentLabel;   // 스테이지 이름 아래 작은 코멘트 (Resources/Comments.txt)
    [SerializeField] Text liveStatsLabel; // 왼쪽 아래 죽은 수·시간
    [SerializeField] GameObject tutorialPopup;          // 튜토리얼 들어올 때 뜨는 설명 팝업
    [SerializeField] Text tutorialTitle, tutorialBody;  // 설명 내용: Resources/튜토리얼_설명.txt
    string stageName;

    // 클리어 기록: 스테이지에 들어온 뒤(재시작 포함) 죽은 횟수와 걸린 시간. 메뉴/NEXT/에디터 PLAY로 새로 들어오면 초기화
    public static bool NewSession = true;
    static int deaths;
    static float playTime;
    [SerializeField] Button nextButton;

    Transform ball;
    float killY;
    bool ended;
    int stars, starTotal;
    int shotsLeft; // 남은 발사 횟수. -1이면 무제한
    readonly Dictionary<GameObject, Vector2> portalExits = new(); // 시작 포탈 → 연결된 끝 포탈 위치
    readonly List<(Rect area, Vector2 dir)> winds = new();          // 선풍기 바람 칸
    readonly HashSet<GameObject> crumbling = new();                  // 부서지는 중인 불안정 블록
    bool follow;        // 맵이 화면보다 넓어서 카메라가 공을 따라가는지
    int mapWidth;
    float camVelocity;  // SmoothDamp용
    Rigidbody2D ballRb;
    float desiredCamSize;  // 원래 의도한 카메라 크기. 픽셀 스냅은 이 근처의 "칸당 정수 픽셀" 크기로 맞춤
    Vector3 camLogical;    // 스냅 전 카메라 위치(따라가기 계산용). 실제 카메라는 이걸 픽셀 경계에 맞춘 위치

    void Awake() => I = this;

    void Start()
    {
        resultPanel.SetActive(false);
        bool entered = NewSession; // 메뉴·NEXT·에디터 PLAY로 새로 들어옴 (재시작이 아님)
        if (NewSession)
        {
            deaths = 0;
            playTime = 0f;
            NewSession = false;
        }
        // 튜토리얼에 들어올 때마다 설명 팝업 (재시작 때는 안 뜸). 닫을 때까지 일시정지
        var guide = IsTutorial && entered ? StageStore.TutorialText(Index) : "";
        tutorialPopup.SetActive(guide != "");
        if (guide != "")
        {
            tutorialTitle.text = "TUTORIAL " + (Index + 1);
            tutorialBody.text = guide;
            Time.timeScale = 0f;
        }
        stageName = (TestMap != null ? "TEST " : "") + (IsTutorial ? "TUTORIAL " : "STAGE ") + (Index + 1);
        commentLabel.text = StageStore.Comment(IsTutorial, Index);
        bool limited = IsTutorial ? System.Array.IndexOf(limitedTutorials, Index + 1) >= 0
                                  : Index + 1 >= limitedFrom && Index + 1 <= limitedTo;
        shotsLeft = limited ? limitedShots : -1;
        Build(TestMap ?? StageStore.Load(IsTutorial, Index));
        UpdateStars();
    }

    public bool HasShot => shotsLeft != 0;
    public void UseShot()
    {
        if (shotsLeft > 0) shotsLeft--;
        UpdateStars();
    }

    void Update()
    {
        if (ended) return;
        playTime += Time.deltaTime;
        liveStatsLabel.text = StatsText();
        if (ball.position.y < killY) End(false);
    }

    // 넓은 맵: 공이 화면 왼쪽(followScreenX)에 오도록 카메라를 가로로 부드럽게 따라감
    void LateUpdate()
    {
        if (follow) camLogical.x = Mathf.SmoothDamp(camLogical.x, FollowTargetX(), ref camVelocity, followSmooth);
        SnapCamera();
    }

    void SnapCamera() => PixelSnap(Camera.main, camLogical, desiredCamSize);

    // 픽셀 스냅 (게임·맵 에디터 공용): 타일 1칸 = 정수 픽셀, 타일 경계(정수+0.5)가 화면 픽셀 경계에 오도록 카메라를 맞춤.
    // 안 맞추면 타일 경계가 픽셀 중간에 걸려, 위치에 따라 외곽선이 한쪽만 1px 두껍거나 얇게(또는 통째로 안) 보임
    public static void PixelSnap(Camera cam, Vector3 logical, float desiredSize)
    {
        float w = cam.pixelWidth, h = cam.pixelHeight;
        float ppu = Mathf.Max(1f, Mathf.Floor(h / (2f * desiredSize))); // 칸당 픽셀 (원래 크기보다 살짝 크게 보이는 쪽으로 내림)
        cam.orthographicSize = h / (2f * ppu);
        float x = Mathf.Round((logical.x - 0.5f) * ppu - w / 2f), y = Mathf.Round((logical.y - 0.5f) * ppu - h / 2f);
        cam.transform.position = new Vector3(0.5f + (x + w / 2f) / ppu, 0.5f + (y + h / 2f) / ppu, -10f);
    }

    float FollowTargetX()
    {
        var cam = Camera.main;
        float halfW = cam.orthographicSize * cam.aspect;
        if (ballRb == null) ballRb = ball.GetComponent<Rigidbody2D>();
        // 앞보기: 공 속도만큼 미리 앞을 비춰서 SmoothDamp 지연을 상쇄 → 움직이는 중에도 공이 followScreenX 근처에 유지
        float lead = Mathf.Clamp(ballRb.linearVelocity.x * followSmooth, -halfW * 0.5f, halfW * 0.5f);
        float x = ball.position.x + lead + halfW * (1f - 2f * followScreenX);
        return Mathf.Clamp(x, halfW - 1f, mapWidth - halfW); // 맵 바깥 빈 공간이 보이지 않게
    }

    public void End(bool clear)
    {
        if (ended) return;
        ended = true;
        ball.GetComponent<BallController>().enabled = false;
        if (clear) ball.GetComponent<Rigidbody2D>().simulated = false;
        resultText.text = clear ? "CLEAR!" : "GAME OVER";
        Sfx.Play(Sfx.Bank != null ? (clear ? Sfx.Bank.clear : Sfx.Bank.gameOver) : null);
        if (!clear) deaths++;
        statsLabel.text = clear ? StatsText() : "";
        liveStatsLabel.text = StatsText();
        // 다음 스테이지 해금 + 별 기록. UNLOCK(모든 스테이지 열기) 켠 상태에서 깬 건 기록하지 않음
        if (clear && !IsTutorial && TestMap == null && !Progress.UnlockAll) Progress.Clear(Index, Mathf.Min(stars, 3));
        nextButton.interactable = clear && TestMap == null && Index + 1 < (IsTutorial ? TutorialCount : StageCount);
        resultPanel.SetActive(true);
    }

    public void Next() { Index++; NewSession = true; Retry(); }
    // 재시작: 플레이 도중에 누르면 죽은 것으로 침. 게임오버 후(이미 셈)·클리어 후·NEXT는 세지 않음
    public void Retry()
    {
        if (!ended) deaths++;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
    public void Menu() => SceneManager.LoadScene(TestMap != null ? "MapEditorScene" : "TitleScene"); // 테스트 중이면 에디터로 복귀

    // 튜토리얼 설명 팝업의 START 버튼
    public void CloseTutorial()
    {
        tutorialPopup.SetActive(false);
        Time.timeScale = 1f;
    }

    // 공이 닿은 오브젝트 처리 (BallController에서 호출). 이름은 Build에서 붙인 것
    public void Touch(GameObject other)
    {
        // 시작 포탈: 연결된 끝 포탈 위치로 이동(속도 유지)
        if (portalExits.TryGetValue(other, out var exit))
        {
            var purple = new Color(0.7f, 0.35f, 1f);
            Dust.Ring(ball.position, purple, 8, 2.5f, 1.1f); // 들어간 곳
            ball.GetComponent<Rigidbody2D>().position = exit;
            ball.position = exit;
            Dust.Ring(exit, purple, 8, 2.5f, 1.1f);          // 나온 곳
            Sfx.Play(Sfx.Bank != null ? Sfx.Bank.portal : null);
            return;
        }
        switch (other.name)
        {
            case "Unstable":
                if (crumbling.Add(other)) StartCoroutine(Crumble(other));
                break;
            case "BlackCoin":
                var bc = ball.GetComponent<BallController>();
                if (bc.airCharge) break; // 최대 1회 충전. 이미 충전돼 있으면 코인은 그대로 둠
                bc.SetAirCharge(true);
                Destroy(other);
                Sfx.Play(Sfx.Bank != null ? Sfx.Bank.blackCoin : null);
                break;
            case "Goal": End(true); break;
            case "Spikes":
                End(false);
                // 공이 터짐: 공 색 조각 + 흰 조각이 사방으로 (공 그림이 있으면 그 색은 그림 대신 기본 주황 계열)
                Dust.Ring(ball.position, ball.GetComponent<SpriteRenderer>().color == Color.white ? TileColor('B') : ball.GetComponent<SpriteRenderer>().color, 10, 4f, 1.4f);
                Dust.Ring(ball.position, Color.white, 4, 3f, 1.1f);
                ball.gameObject.SetActive(false);
                break;
            case "Star":
                stars++; UpdateStars(); Destroy(other);
                Sfx.Play(Sfx.Bank != null ? Sfx.Bank.star : null);
                break;
            case "Coin":
                if (shotsLeft >= 0) shotsLeft++; // 발사 제한 스테이지에서만 의미 있음
                UpdateStars();
                Destroy(other);
                Sfx.Play(Sfx.Bank != null ? Sfx.Bank.coin : null);
                break;
        }
    }

    // 왼쪽 위 한 줄: 스테이지 이름(크게) + 별·발사 횟수(작게)
    void UpdateStars() => stageLabel.text = $"<size=44>{stageName}</size>     <size=28><color=#FFD94A>STAR {stars} / {starTotal}</color>" +
                                            (shotsLeft >= 0 ? $"     SHOT {shotsLeft}" : "") + "</size>";

    string StatsText() => $"Death : {deaths}    time : {(int)playTime / 60}m {(int)playTime % 60}s";

    // 강화 슬라임 그룹 이름 → 발사 방향. 강화 슬라임이 아니면 zero
    public static Vector2 SuperSlimeDir(string groupName) => groupName switch
    {
        "SlimeUp" => Vector2.up,
        "SlimeDown" => Vector2.down,
        "SlimeLeft" => Vector2.left,
        "SlimeRight" => Vector2.right,
        _ => Vector2.zero,
    };

    // 맵 문자: . 공기, B 공 시작, * 별, C 코인, K 검은 코인, @ 도착, # 일반, D 가시, H 꿀, I 얼음, S 슬라임,
    //         ^ v < > 강화 슬라임(화살표 방향 면에 닿으면 그 방향으로 발사), U 불안정한 블록,
    //         A V L R 선풍기(위·아래·왼·오), 1~9 시작 포탈 / a~i 끝 포탈(1↔a, 2↔b …). 맨 아랫줄이 y=0
    void Build(string map)
    {
        var rows = map.Replace("\r", "").Trim('\n').Split('\n');
        var square = MakeSprite('s');
        var circle = MakeSprite('c');
        var spike = MakeSprite('t');
        var arrow = MakeSprite('a');
        var ring = MakeSprite('r');
        var fans = Group("Fans", 0f, 0.6f);
        var portalStarts = new GameObject[9];
        var portalEnds = new Vector2?[9];
        var slimeUp = Group("SlimeUp", 0.95f, 0f);
        var slimeDown = Group("SlimeDown", 0.95f, 0f);
        var slimeLeft = Group("SlimeLeft", 0.95f, 0f);
        var slimeRight = Group("SlimeRight", 0.95f, 0f);
        var blocks = Group("Blocks", 0.4f, 0.6f);
        var honey = Group("Honey", 0f, 1f);
        var ice = Group("Ice", 0f, 0f);
        var slime = Group("Slime", 0.95f, 0f); // 마찰 0 → 튕길 때 x축 속도 유지
        // 맵 격자를 먼저 만들어 둠 (타일마다 이웃 블록을 보고 맞닿은 면의 외곽선을 지우는 데 사용)
        int width = 0;
        foreach (var row in rows) width = Mathf.Max(width, row.Length);
        var grid = new char[rows.Length, width];
        for (int r = 0; r < rows.Length; r++)
            for (int c = 0; c < width; c++) grid[r, c] = c < rows[r].Length ? rows[r][c] : '.';
        buildGrid = grid;
        for (int r = 0; r < rows.Length; r++)
        {
            for (int c = 0; c < rows[r].Length; c++)
            {
                var pos = new Vector2(c, rows.Length - 1 - r);
                char ch = rows[r][c];
                switch (ch)
                {
                    case '.': break;
                    case '#': Tile(blocks, pos, '#', square); break;
                    case 'D': Solo("Spikes", pos, 'D', spike, true); break;       // 판정: 지름 1칸 원
                    case 'U': Solo("Unstable", pos, 'U', square, false); break;   // 따로 부서지므로 개별 오브젝트
                    case 'A' or 'V' or 'L' or 'R':
                        Tile(fans, pos, ch, arrow, TileAngle(ch));
                        AddWind(pos, FanDir(ch), square);
                        break;
                    case 'K': Pickup("BlackCoin", pos, 'K', circle, 1f); break; // 블록과 같은 1칸 크기
                    case 'H': Tile(honey, pos, 'H', square); break;
                    case 'I': Tile(ice, pos, 'I', square); break;
                    case 'S': Tile(slime, pos, 'S', square); break;
                    case '^': Tile(slimeUp, pos, '^', arrow, TileAngle('^')); break;
                    case 'v': Tile(slimeDown, pos, 'v', arrow, TileAngle('v')); break;
                    case '<': Tile(slimeLeft, pos, '<', arrow, TileAngle('<')); break;
                    case '>': Tile(slimeRight, pos, '>', arrow, TileAngle('>')); break;
                    case 'C': Pickup("Coin", pos, 'C', circle, 1f); break;      // 블록과 같은 1칸 크기
                    case 'B': ball = SpawnBall(pos, circle); break;
                    case '*': starTotal++; Pickup("Star", pos, '*', circle, 0.5f); break;
                    case '@': Pickup("Goal", pos, '@', square, 0.8f); break;
                    default:
                        if (IsPortalStart(ch)) portalStarts[ch - '1'] = Pickup("PortalStart", pos, ch, ring, 0.9f);
                        else if (IsPortalEnd(ch)) { portalEnds[ch - 'a'] = pos; Pickup("PortalEnd", pos, ch, ring, 0.9f); }
                        else Debug.LogWarning($"맵 {r + 1}행 {c + 1}열: 알 수 없는 문자 '{ch}' (공기로 처리)");
                        break;
                }
            }
        }
        if (ball == null) Debug.LogError("맵에 공 시작 위치 B가 없습니다");
        for (int k = 0; k < 9; k++)
        {
            if (portalStarts[k] != null && portalEnds[k] is Vector2 end) portalExits[portalStarts[k]] = end;
            else if (portalStarts[k] != null || portalEnds[k] != null) Debug.LogWarning($"포탈 {k + 1}번: 시작/끝 중 하나가 없어 연결 안 됨");
        }

        // 이어 붙는 블록(Mergeable) 그림을 덩어리 단위로 그림 (지금은 Mergeable이 비어 있어 아무것도 안 그림)
        DrawMerged(grid, new GameObject("MergedArt").transform, square);
        var cam = Camera.main;
        // 배율은 기존 스테이지와 같게(가로 최대 ViewColumns칸). 위쪽엔 HUD 자리만큼 여백
        cam.orthographicSize = Mathf.Max(rows.Length / 2f + 0.6f, Mathf.Min(width, ViewColumns) / 2f / cam.aspect) + 0.6f;
        mapWidth = width;
        follow = width / 2f > cam.orthographicSize * cam.aspect; // 한 화면에 안 들어오면 따라가기
        float camX = follow && ball != null ? FollowTargetX() : (width - 1) / 2f;
        camLogical = new Vector3(camX, (rows.Length - 1) / 2f + 0.6f, -10f);
        killY = camLogical.y - cam.orthographicSize - 1f;
        desiredCamSize = cam.orthographicSize;
        SnapCamera();
    }

    // 같은 재질 타일을 CompositeCollider로 합쳐 타일 이음매에 공이 걸리지 않게 함
    static Transform Group(string name, float bounciness, float friction)
    {
        var go = new GameObject(name);
        go.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;
        go.AddComponent<CompositeCollider2D>().sharedMaterial = new PhysicsMaterial2D { bounciness = bounciness, friction = friction };
        return go.transform;
    }

    static void Tile(Transform parent, Vector2 pos, char ch, Sprite fallback, float angle = 0f)
    {
        var go = new GameObject("Tile");
        go.transform.SetParent(parent);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, 0f, angle));
        // 이어 붙는 블록은 그림을 DrawMerged에서 덩어리째 그림. 여기선 1×1 콜라이더만
        if (Mergeable.IndexOf(ch) < 0)
        {
            var sr = go.AddComponent<SpriteRenderer>();
            ApplyArt(sr, ch, fallback, 1f);
            // 같은 블록과 맞닿은 면의 외곽선을 지운 그림으로 교체 (격자: 0행이 맨 위)
            TileJoin.Apply(sr, ch, buildGrid, buildGrid.GetLength(0) - 1 - Mathf.RoundToInt(pos.y), Mathf.RoundToInt(pos.x));
        }
        go.AddComponent<BoxCollider2D>().compositeOperation = Collider2D.CompositeOperation.Merge; // 1×1칸
    }

    static char[,] buildGrid; // Build 중인 맵 격자 (Tile에서 이웃 확인용)

    // 같은 종류끼리 이어 붙여 한 장(9-slice 반복)으로 그리는 블록. 비우면 모든 블록을 한 칸 = 그림 한 장으로 그림.
    // 이어 그리기를 다시 켜려면 "#HIS" 처럼 문자를 넣으면 됨 (단, 반복 단위가 한 칸과 달라짐)
    public const string Mergeable = "";

    // 같은 블록이 직사각형으로 모인 덩어리마다 Tiled(9-slice) 스프라이트 한 장을 그림.
    // 스프라이트 Border(Sprite Editor) 바깥 테두리는 덩어리 가장자리에만, 안쪽은 반복으로 채워 이음매가 없음.
    // ponytail: 가로 우선 탐욕 분할이라 ㄱ자·계단 모양은 직사각형끼리 만나는 곳에 이음매가 남음
    public static void DrawMerged(char[,] grid, Transform parent, Sprite fallback)
    {
        int rows = grid.GetLength(0), cols = grid.GetLength(1);
        var used = new bool[rows, cols];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                char ch = grid[r, c];
                if (used[r, c] || Mergeable.IndexOf(ch) < 0) continue;
                int w = 1, h = 1;
                while (c + w < cols && grid[r, c + w] == ch && !used[r, c + w]) w++;
                while (r + h < rows && RowIs(grid, used, r + h, c, w, ch)) h++;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++) used[r + y, c + x] = true;

                var go = new GameObject("Art_" + ch + "_" + w + "x" + h);
                go.transform.SetParent(parent);
                go.transform.position = new Vector2(c + (w - 1) / 2f, rows - 1 - r - (h - 1) / 2f); // 덩어리 중심 (0행이 맨 위)
                var sr = go.AddComponent<SpriteRenderer>();
                ApplyArt(sr, ch, fallback, 1f); // 1칸 = 그림 1장 크기로 맞춤
                sr.drawMode = SpriteDrawMode.Tiled;
                sr.tileMode = SpriteTileMode.Continuous;
                var b = sr.sprite.bounds.size;
                sr.size = new Vector2(w * b.x, h * b.y);
            }
    }

    static bool RowIs(char[,] grid, bool[,] used, int r, int c, int w, char ch)
    {
        for (int x = 0; x < w; x++)
            if (grid[r, c + x] != ch || used[r, c + x]) return false;
        return true;
    }

    // 닿으면 Touch()로 처리되는 트리거 오브젝트 (도착지점, 별, 코인, 포탈)
    static GameObject Pickup(string name, Vector2 pos, char ch, Sprite fallback, float size)
    {
        var go = new GameObject(name);
        go.transform.position = pos;
        ApplyArt(go.AddComponent<SpriteRenderer>(), ch, fallback, size);
        go.AddComponent<BoxCollider2D>().isTrigger = true;
        return go;
    }

    // 개별 오브젝트 블록. 그림은 자식에 둬서 그림 크기와 상관없이 콜라이더를 정확히 지정
    // circle: 지름 1칸 원형 판정(가시), 아니면 1×1 사각(불안정한 블록)
    static GameObject Solo(string name, Vector2 pos, char ch, Sprite fallback, bool circle)
    {
        var go = new GameObject(name);
        go.transform.position = pos;
        var art = new GameObject("Art");
        art.transform.SetParent(go.transform, false);
        ApplyArt(art.AddComponent<SpriteRenderer>(), ch, fallback, 1f);
        if (circle) go.AddComponent<CircleCollider2D>().radius = 0.5f;
        else go.AddComponent<BoxCollider2D>().size = Vector2.one;
        return go;
    }

    // 불안정한 블록: 닿으면 unstableDelay초 동안 평상시 그림 → unstableNext 그림들(예: 부서진 흙_0 → 1 → 2 → 3)로
    // 같은 간격씩 서서히 바뀐 뒤 사라짐. 다음 단계 그림을 위에 겹쳐 투명도를 올리는 방식(크로스페이드).
    // 단계 그림이 없으면 색만 점점 어둡게
    // ponytail: 단계 그림들이 같은 해상도라고 가정하고 크기를 첫 단계에 맞춤. 크기가 다르면 단계별 ApplyArt로 교체
    IEnumerator Crumble(GameObject tile)
    {
        var baseSr = tile.GetComponentInChildren<SpriteRenderer>();
        var art = TileArt.I;
        var stages = new List<Sprite> { baseSr.sprite };
        if (art != null && art.unstableNext != null)
            foreach (var s in art.unstableNext) if (s != null) stages.Add(s);
        var top = new GameObject("ArtNext").AddComponent<SpriteRenderer>();
        top.transform.SetParent(baseSr.transform.parent, false);
        top.transform.localScale = baseSr.transform.localScale;
        top.sortingOrder = baseSr.sortingOrder + 1;
        var startColor = baseSr.color;
        var darkColor = new Color(startColor.r * 0.4f, startColor.g * 0.4f, startColor.b * 0.4f, 1f);
        for (float t = 0f; t < unstableDelay; t += Time.deltaTime)
        {
            if (stages.Count > 1)
            {
                float p = t / unstableDelay * (stages.Count - 1); // 0~1: 0→1단계, 1~2: 1→2단계 …
                int from = Mathf.Min((int)p, stages.Count - 2);
                baseSr.sprite = stages[from];
                top.sprite = stages[from + 1];
                top.color = new Color(1f, 1f, 1f, p - from);
            }
            else baseSr.color = Color.Lerp(startColor, darkColor, t / unstableDelay);
            yield return null;
        }
        Destroy(tile);
        Sfx.Play(Sfx.Bank != null ? Sfx.Bank.crumble : null);
    }

    public static Vector2 FanDir(char ch) => ch switch { 'A' => Vector2.up, 'V' => Vector2.down, 'L' => Vector2.left, 'R' => Vector2.right, _ => Vector2.zero };

    // 선풍기 바람 칸: 선풍기 바로 다음 칸부터 dir 방향으로 fanRange칸, 폭 1칸.
    // TileArt에 바람 그림이 있으면 칸마다 차례로 나타났다 사라지는 애니메이션(WindFx), 없으면 반투명 띠로 표시
    void AddWind(Vector2 fanPos, Vector2 dir, Sprite square)
    {
        var center = fanPos + dir * (1f + (fanRange - 1) / 2f);
        var size = dir.x != 0f ? new Vector2(fanRange, 1f) : new Vector2(1f, fanRange);
        winds.Add((new Rect(center - size / 2f, size), dir));
        var art = TileArt.I;
        if (art != null && art.wind != null && art.wind.Length > 0)
        {
            new GameObject("Wind").AddComponent<WindFx>().Init(fanPos, dir, fanRange, art.wind);
            return;
        }
        var go = new GameObject("Wind");
        go.transform.position = center;
        go.transform.localScale = size;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = square;
        sr.color = new Color(1f, 1f, 1f, 0.15f);
        sr.sortingOrder = -1;
    }

    // 위치 p에 작용하는 바람 가속도 합 (BallController.FixedUpdate에서 사용)
    public Vector2 WindAt(Vector2 p)
    {
        var a = Vector2.zero;
        foreach (var w in winds)
            if (w.area.Contains(p)) a += w.dir * fanAccel;
        return a;
    }

    public static bool IsPortalStart(char ch) => ch >= '1' && ch <= '9';
    public static bool IsPortalEnd(char ch) => ch >= 'a' && ch <= 'i';

    // 블록 그림 적용: TileArt에 스프라이트가 있으면 그 그림(원래 색), 없으면 생성 도형 + 기본 색.
    // 그림 해상도·여백과 상관없이 size×size 칸에 딱 맞게 크기 조정 (콜라이더도 이 크기를 따라감)
    public static void ApplyArt(SpriteRenderer sr, char ch, Sprite fallback, float size)
    {
        var art = TileArt.I != null ? TileArt.I.For(ch) : null;
        sr.sprite = art != null ? art : fallback;
        sr.color = art != null ? Color.white : TileColor(ch);
        var b = sr.sprite.bounds.size;
        sr.transform.localScale = new Vector3(size / b.x, size / b.y, 1f);
    }

    // UI(팔레트·미리보기)용: 그림이 있으면 그 그림(시트 블록은 단독 조각), 없으면 생성 도형
    public static Sprite ArtOr(char ch, Sprite fallback)
    {
        var art = TileArt.I != null ? TileArt.I.For(ch) : null;
        if (art == null) return fallback;
        return TileJoin.Joinable.IndexOf(ch) >= 0 ? TileJoin.Piece(art, 0, 0) : art;
    }

    public static Color ArtColor(char ch) => TileArt.I != null && TileArt.I.For(ch) != null ? Color.white : TileColor(ch);

    static Transform SpawnBall(Vector2 pos, Sprite sprite)
    {
        var go = new GameObject("Ball");
        go.transform.position = pos;
        var sr = go.AddComponent<SpriteRenderer>();
        ApplyArt(sr, 'B', sprite, 0.6f);
        sr.sortingOrder = 1;
        go.AddComponent<CircleCollider2D>();
        var rb = go.AddComponent<Rigidbody2D>();
        rb.freezeRotation = true;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        var line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = sr.sharedMaterial;
        line.widthMultiplier = 0.08f;
        line.sortingOrder = 2;
        line.enabled = false;
        go.AddComponent<BallController>();
        return go.transform;
    }

    // 맵 문자별 색 (게임과 맵 에디터가 같이 사용)
    public static Color TileColor(char ch) => ch switch
    {
        '#' => new Color(0.55f, 0.36f, 0.2f),
        'D' => new Color(0.85f, 0.2f, 0.2f),
        'H' => new Color(1f, 0.7f, 0.1f),
        'I' => new Color(0.75f, 0.95f, 1f),
        'S' => new Color(0.45f, 0.9f, 0.4f),
        'B' => new Color(1f, 0.45f, 0.2f),
        '*' => new Color(1f, 0.9f, 0.1f),
        '@' => new Color(0.6f, 0.3f, 0.9f),
        'C' => new Color(0.8f, 0.6f, 0.05f), // 진한 금색 (공 주황·별 노랑과 구분)
        '^' or 'v' or '<' or '>' => new Color(0.1f, 0.75f, 0.45f),
        'U' => new Color(0.7f, 0.55f, 0.4f),
        'A' or 'V' or 'L' or 'R' => new Color(0.55f, 0.65f, 0.85f),
        'K' => new Color(0.12f, 0.12f, 0.15f),
        'P' or (>= '1' and <= '9') => new Color(0.25f, 0.55f, 1f), // 시작 포탈: 파랑
        >= 'a' and <= 'i' => new Color(1f, 0.55f, 0.15f),        // 끝 포탈: 주황
        _ => Color.clear,
    };

    // 화살표 회전 (스프라이트는 위쪽 화살표 기준): 강화 슬라임 ^ v < >, 선풍기 A V L R
    public static float TileAngle(char ch) => ch switch { '<' or 'L' => 90f, 'v' or 'V' => 180f, '>' or 'R' => -90f, _ => 0f };

    // 런타임 생성 단색 스프라이트. TileArt에 그림이 없는 블록의 대체 도형
    // shape: 's' 사각형, 'c' 원, 't' 위로 뾰족한 삼각형(가시), 'a' 위쪽 화살표가 밝게 그려진 사각형(강화 슬라임·선풍기),
    //        'r' 고리(포탈)
    public static Sprite MakeSprite(char shape)
    {
        const int n = 64;
        var tex = new Texture2D(n, n);
        var px = new Color[n * n];
        for (int i = 0; i < px.Length; i++)
        {
            float x = i % n - n / 2f + 0.5f, y = i / n - n / 2f + 0.5f;
            bool inside = shape == 'c' ? x * x + y * y <= n * n / 4f
                        : shape == 't' ? Mathf.Abs(x) <= (n / 2f - y) / 2f
                        : shape == 'r' ? x * x + y * y <= n * n / 4f && x * x + y * y >= n * n * 0.09f
                        : true;
            bool arrowPart = shape == 'a' && y > -0.25f * n && y < 0.35f * n && Mathf.Abs(x) <= (0.35f * n - y) * 0.5f;
            px[i] = !inside ? Color.clear : shape == 'a' && !arrowPart ? new Color(0.6f, 0.6f, 0.6f) : Color.white;
        }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), Vector2.one * 0.5f, n, 0, SpriteMeshType.FullRect); // FullRect: Tiled 그리기용
    }
}
