using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 스테이지 맵 에디터
// - 왼쪽 팔레트 블록을 클릭하면 선택(다시 클릭하면 해제). 선택 중엔 칸을 클릭/드래그해서 놓기(덮어쓰기)
// - 우클릭(누른 채 드래그 가능): 그 칸 삭제
// - 선택 없을 때 맵의 블록을 끌어 다른 칸에 놓으면 이동, 휴지통에 놓으면 삭제, 그 외 위치에 놓으면 원래 자리로
// - SAVE: 맵 파일에 저장 → 게임에 바로 적용
// - PLAY: 저장하지 않고 지금 맵으로 바로 플레이. 게임의 MENU를 누르면 편집 중이던 맵으로 돌아옴
public class MapEditor : MonoBehaviour
{
    public static int Index;          // 편집할 스테이지 (0부터)
    public static bool IsTutorial;    // true면 튜토리얼 맵 편집

    [SerializeField] RectTransform sidePanel;       // 왼쪽 패널(이 폭만큼 카메라 영역을 오른쪽으로 밀어냄)
    [SerializeField] RectTransform paletteViewport;  // 팔레트 스크롤 영역(가려진 항목 클릭 방지용)
    [SerializeField] RectTransform[] paletteItems;
    [SerializeField] string paletteChars = "#DHISU^v<>AVLRP*CK@B"; // paletteItems와 같은 순서. P = 포탈(번호 자동)
    [SerializeField] RectTransform trash;
    [SerializeField] Image ghost;                    // 드래그 중 포인터를 따라다니는 미리보기
    [SerializeField] Text titleLabel, statusLabel;

    static readonly Vector2Int NoCell = new Vector2Int(-1, -1);

    char[,] grid;              // [행, 열]. 0행 = 맵 파일 맨 윗줄
    SpriteRenderer[,] cells;
    int rows, cols;
    char dragChar;             // 끌고 있는 블록 ('\0'이면 드래그 중 아님)
    char brush;                // 팔레트에서 선택한 블록 ('\0'이면 선택 없음)
    Vector2Int dragFrom;       // 맵에서 집어 든 칸 (팔레트에서 시작했으면 NoCell)
    Sprite square, circle, spike, arrow, ring;
    Camera cam;
    Transform mergedRoot; // 이어 붙는 블록 그림(게임과 같은 모양). 맵이 바뀔 때마다 다시 그림
    float panMin, panMax; // 넓은 맵에서 카메라 가로 이동 범위 (같으면 이동 없음)
    Vector3 camLogical;   // 스냅 전 카메라 위치(좌우 이동 계산용). 실제 카메라는 이걸 픽셀 경계에 맞춘 위치
    float camSize;        // 원래 의도한 카메라 크기 (픽셀 스냅 기준)

    void Start()
    {
        square = GameManager.MakeSprite('s');
        circle = GameManager.MakeSprite('c');
        spike = GameManager.MakeSprite('t');
        arrow = GameManager.MakeSprite('a');
        ring = GameManager.MakeSprite('r');
        titleLabel.text = (IsTutorial ? "EDIT TUTORIAL " : "EDIT STAGE ") + (Index + 1);
        statusLabel.text = GameManager.TestMap != null ? "NOT SAVED" : "";
        ghost.gameObject.SetActive(false);

        // 테스트 플레이에서 돌아왔으면 저장 안 된 편집 상태를 이어서
        var lines = (GameManager.TestMap ?? StageStore.Load(IsTutorial, Index)).Replace("\r", "").Trim('\n').Split('\n');
        GameManager.TestMap = null;
        rows = lines.Length;
        foreach (var l in lines) cols = Mathf.Max(cols, l.Length);
        grid = new char[rows, cols];
        cells = new SpriteRenderer[rows, cols];
        for (int r = 0; r < rows; r++) // 격자를 먼저 다 채움 (타일마다 이웃을 보고 외곽선을 정하므로)
            for (int c = 0; c < cols; c++) grid[r, c] = c < lines[r].Length ? lines[r][c] : '.';
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                var pos = new Vector2(c, rows - 1 - r);
                NewSprite("Cell", pos, square, new Color(1f, 1f, 1f, 0.15f), 0.92f, 0); // 격자 배경
                cells[r, c] = NewSprite("Tile", pos, square, Color.white, 1f, 1);
                Refresh(r, c);
            }
        RebuildMerged();

        for (int i = 0; i < paletteItems.Length; i++)
        {
            var img = paletteItems[i].GetComponent<Image>();
            img.sprite = GameManager.ArtOr(paletteChars[i], SpriteFor(paletteChars[i]));
            img.color = GameManager.ArtColor(paletteChars[i]);
            img.preserveAspect = true;
            paletteItems[i].localRotation = Quaternion.Euler(0f, 0f, GameManager.TileAngle(paletteChars[i]));
        }

        // 카메라는 왼쪽 패널을 뺀 나머지 화면에 맵 전체가 보이게
        cam = Camera.main;
        float panelPx = sidePanel.rect.width * sidePanel.GetComponentInParent<Canvas>().scaleFactor;
        int panelX = Mathf.RoundToInt(panelPx); // 맵 영역 시작점을 정수 픽셀로 (픽셀 스냅이 정확히 맞도록)
        cam.pixelRect = new Rect(panelX, 0, Screen.width - panelX, Screen.height);
        // 배율은 가로 최대 24칸 기준. 그보다 넓은 맵은 휠·A/D 키로 좌우 이동
        cam.orthographicSize = Mathf.Max(rows / 2f, Mathf.Min(cols, 24) / 2f / cam.aspect) + 0.5f;
        float halfW = cam.orthographicSize * cam.aspect;
        panMin = halfW - 1f;
        panMax = cols - halfW;
        if (panMax <= panMin) panMin = panMax = (cols - 1) / 2f; // 한 화면에 다 들어오면 가운데 고정
        else if (statusLabel.text == "") statusLabel.text = "WHEEL / A,D : SCROLL";
        camSize = cam.orthographicSize;
        camLogical = new Vector3(panMin, (rows - 1) / 2f, -10f);
        GameManager.PixelSnap(cam, camLogical, camSize);
    }

    void Update()
    {
        var p = Pointer.current; // 마우스·터치
        if (p == null) return;
        Vector2 screen = p.position.ReadValue();

        // 넓은 맵 좌우 이동: 맵 위에서 마우스 휠(한 칸 굴림 = 2칸), 또는 A/D·←/→ 키
        if (panMax > panMin)
        {
            float dx = 0f;
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) dx += 20f * Time.deltaTime;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) dx -= 20f * Time.deltaTime;
            }
            var wheel = Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;
            if (wheel != 0f && cam.pixelRect.Contains(screen)) dx -= Mathf.Sign(wheel) * 2f;
            camLogical.x = Mathf.Clamp(camLogical.x + dx, panMin, panMax);
        }
        GameManager.PixelSnap(cam, camLogical, camSize); // 매 프레임 픽셀 경계에 맞춤 (이동·창 크기 변경 대응)

        // 우클릭(누르고 있는 동안): 포인터 아래 칸 삭제
        var mouse = Mouse.current;
        if (mouse != null && mouse.rightButton.isPressed && dragChar == '\0')
        {
            var cell = CellAt(screen);
            if (cell != NoCell) Set(cell, '.');
        }

        if (p.press.wasPressedThisFrame) BeginDrag(screen);

        // 선택된 블록: 미리보기가 포인터를 따라다니고, 누르고 있는 동안 지나가는 칸에 놓기
        if (brush != '\0')
        {
            ghost.rectTransform.position = screen;
            var cell = CellAt(screen);
            // 포탈은 시작→끝을 한 번씩 찍어야 하므로 드래그 연속 배치 없이 클릭할 때만
            bool clickOnly = brush == 'P' || GameManager.IsPortalEnd(brush);
            bool pressing = clickOnly ? p.press.wasPressedThisFrame : p.press.isPressed;
            if (pressing && cell != NoCell && grid[cell.x, cell.y] != brush) Place(cell, brush);
            return;
        }

        if (dragChar == '\0') return;
        ghost.rectTransform.position = screen;
        if (p.press.wasReleasedThisFrame) EndDrag(screen);
    }

    void BeginDrag(Vector2 screen)
    {
        // 팔레트 클릭: 그 블록 선택, 이미 선택된 블록이면 해제 (스크롤로 가려진 항목은 제외)
        bool inPalette = RectTransformUtility.RectangleContainsScreenPoint(paletteViewport, screen);
        for (int i = 0; i < paletteItems.Length; i++)
            if (inPalette && RectTransformUtility.RectangleContainsScreenPoint(paletteItems[i], screen))
            {
                SelectBrush(brush == paletteChars[i] ? '\0' : paletteChars[i]);
                return;
            }
        if (brush != '\0') return; // 선택 중 칸 클릭은 Update에서 놓기 처리
        var cell = CellAt(screen);
        if (cell != NoCell && grid[cell.x, cell.y] != '.')
        {
            StartGhost(grid[cell.x, cell.y]);
            dragFrom = cell;
            Set(cell, '.'); // 집어 들기
        }
    }

    void EndDrag(Vector2 screen)
    {
        var cell = CellAt(screen);
        if (RectTransformUtility.RectangleContainsScreenPoint(trash, screen)) { } // 휴지통: 버림
        else if (cell != NoCell) Place(cell, dragChar);
        else if (dragFrom != NoCell) Set(dragFrom, dragChar); // 맵 밖에 놓으면 원래 자리로
        dragChar = '\0';
        ghost.gameObject.SetActive(false);
    }

    // 팔레트 선택 상태 변경 + 선택된 슬롯 강조
    void SelectBrush(char ch)
    {
        brush = ch;
        for (int i = 0; i < paletteItems.Length; i++)
            paletteItems[i].parent.GetComponent<Image>().color =
                paletteChars[i] == ch || (paletteChars[i] == 'P' && GameManager.IsPortalEnd(ch)) // 끝 포탈 놓는 중에도 PORTAL 강조
                    ? new Color(1f, 0.9f, 0.3f, 0.6f) : new Color(1f, 1f, 1f, 0.08f);
        if (ch == '\0') ghost.gameObject.SetActive(false);
        else ShowGhost(ch);
    }

    void StartGhost(char ch)
    {
        dragChar = ch;
        ShowGhost(ch);
    }

    void ShowGhost(char ch)
    {
        ghost.sprite = GameManager.ArtOr(ch, SpriteFor(ch));
        ghost.rectTransform.localRotation = Quaternion.Euler(0f, 0f, GameManager.TileAngle(ch));
        var color = GameManager.ArtColor(ch);
        color.a = 0.8f;
        ghost.color = color;
        ghost.gameObject.SetActive(true);
    }

    void Place(Vector2Int cell, char ch)
    {
        if (ch == 'P') // 포탈: 비어 있는 가장 작은 번호로 시작 포탈을 놓고, 곧바로 그 번호의 끝 포탈 놓기로 전환
        {
            int k = FreePortalNumber();
            if (k == 0) { statusLabel.text = "MAX 9 PORTALS"; return; }
            Set(cell, (char)('0' + k));
            SelectBrush((char)('a' + k - 1));
            statusLabel.text = "PLACE PORTAL END";
            return;
        }
        if (ch == 'B' || GameManager.IsPortalStart(ch) || GameManager.IsPortalEnd(ch)) // 공·포탈 번호는 하나씩만
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    if (grid[r, c] == ch) Set(new Vector2Int(r, c), '.');
        Set(cell, ch);
        if (GameManager.IsPortalEnd(ch) && brush == ch) // 끝 포탈까지 놓았으면 다시 새 포탈 놓기 상태로
        {
            SelectBrush('P');
            statusLabel.text = "";
        }
    }

    int FreePortalNumber()
    {
        for (int k = 1; k <= 9; k++)
        {
            bool used = false;
            foreach (var ch in grid) used |= ch == (char)('0' + k) || ch == (char)('a' + k - 1);
            if (!used) return k;
        }
        return 0;
    }

    // 화면 좌표 → 맵 칸 (행, 열). 맵 밖이면 NoCell
    Vector2Int CellAt(Vector2 screen)
    {
        if (!cam.pixelRect.Contains(screen)) return NoCell;
        Vector2 world = cam.ScreenToWorldPoint(screen);
        int c = Mathf.RoundToInt(world.x), r = rows - 1 - Mathf.RoundToInt(world.y);
        return r >= 0 && r < rows && c >= 0 && c < cols ? new Vector2Int(r, c) : NoCell;
    }

    void Set(Vector2Int cell, char ch)
    {
        grid[cell.x, cell.y] = ch;
        Refresh(cell.x, cell.y);
        // 주변 3×3(대각선 포함) 칸도 다시 그림: 시트 조각은 위아래 칸의 좌우 이웃(=대각선)까지 보고 정해지므로
        // 상하좌우만 갱신하면 까는 순서에 따라 대각선 칸이 예전 조각으로 남음
        for (int dr = -1; dr <= 1; dr++)
            for (int dc = -1; dc <= 1; dc++)
            {
                int r = cell.x + dr, c = cell.y + dc;
                if ((dr != 0 || dc != 0) && r >= 0 && r < rows && c >= 0 && c < cols) Refresh(r, c);
            }
        RebuildMerged();
    }

    // ponytail: 칸 하나 바뀔 때마다 전체 다시 그림(24×13이라 충분히 가벼움). 큰 맵이면 바뀐 덩어리만 갱신
    void RebuildMerged()
    {
        if (mergedRoot != null) Destroy(mergedRoot.gameObject);
        mergedRoot = new GameObject("MergedArt").transform;
        GameManager.DrawMerged(grid, mergedRoot, square);
        foreach (var sr in mergedRoot.GetComponentsInChildren<SpriteRenderer>()) sr.sortingOrder = 1; // 격자 배경 위

        // 연결된 포탈 쌍은 선으로 이어서 표시
        for (int k = 1; k <= 9; k++)
        {
            var a = Find((char)('0' + k));
            var b = Find((char)('a' + k - 1));
            if (a == NoCell || b == NoCell) continue;
            var line = new GameObject("PortalLink" + k).AddComponent<LineRenderer>();
            line.transform.SetParent(mergedRoot);
            line.sharedMaterial = cells[0, 0].sharedMaterial;
            line.widthMultiplier = 0.06f;
            line.startColor = GameManager.TileColor('1');
            line.endColor = GameManager.TileColor('a');
            line.sortingOrder = 3;
            line.SetPosition(0, new Vector2(a.y, rows - 1 - a.x));
            line.SetPosition(1, new Vector2(b.y, rows - 1 - b.x));
        }
    }

    Vector2Int Find(char ch)
    {
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                if (grid[r, c] == ch) return new Vector2Int(r, c);
        return NoCell;
    }

    void Refresh(int r, int c)
    {
        char ch = grid[r, c];
        var sr = cells[r, c];
        sr.enabled = ch != '.' && GameManager.Mergeable.IndexOf(ch) < 0; // 이어 붙는 블록은 RebuildMerged가 그림
        float size = ch switch
        {
            '*' => 0.5f, '@' => 0.8f, 'B' => 0.6f,
            _ when GameManager.IsPortalStart(ch) || GameManager.IsPortalEnd(ch) => 0.9f,
            _ => 1f,
        };
        GameManager.ApplyArt(sr, ch, SpriteFor(ch), size); // 게임과 같은 그림·크기
        TileJoin.Apply(sr, ch, grid, r, c);                  // 같은 블록과 맞닿은 면 외곽선 지우기
        sr.transform.rotation = Quaternion.Euler(0f, 0f, GameManager.TileAngle(ch));
    }

    Sprite SpriteFor(char ch) => ch switch
    {
        'D' => spike,
        '*' or 'B' or 'C' or 'K' => circle,
        '^' or 'v' or '<' or '>' or 'A' or 'V' or 'L' or 'R' => arrow,
        'P' or (>= '1' and <= '9') or (>= 'a' and <= 'i') => ring,
        _ => square,
    };

    SpriteRenderer NewSprite(string name, Vector2 pos, Sprite sprite, Color color, float scale, int order)
    {
        var go = new GameObject(name);
        go.transform.position = pos;
        go.transform.localScale = Vector3.one * scale;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = color;
        sr.sortingOrder = order;
        return sr;
    }

    // 현재 맵을 텍스트로. 공(B)이 없으면 null + 안내 표시
    string Serialize()
    {
        var sb = new System.Text.StringBuilder();
        bool hasBall = false;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                sb.Append(grid[r, c]);
                hasBall |= grid[r, c] == 'B';
            }
            sb.Append('\n');
        }
        if (!hasBall)
        {
            statusLabel.text = "NEED BALL (B)";
            return null;
        }
        for (int k = 1; k <= 9; k++) // 시작·끝 중 하나만 있는 포탈이 있으면 저장/플레이 불가
            if ((Find((char)('0' + k)) == NoCell) != (Find((char)('a' + k - 1)) == NoCell))
            {
                statusLabel.text = "PORTAL " + k + " NOT LINKED";
                return null;
            }
        return sb.ToString();
    }

    public void Save()
    {
        var map = Serialize();
        if (map == null) return;
        StageStore.Save(IsTutorial, Index, map);
        statusLabel.text = "SAVED!";
    }

    // 저장하지 않고 지금 맵으로 플레이
    public void TestPlay()
    {
        var map = Serialize();
        if (map == null) return;
        GameManager.TestMap = map;
        GameManager.IsTutorial = IsTutorial;
        GameManager.Index = Index;
        GameManager.NewSession = true;
        SceneManager.LoadScene("GameScene");
    }

    public void Back() => SceneManager.LoadScene("TitleScene");
}
