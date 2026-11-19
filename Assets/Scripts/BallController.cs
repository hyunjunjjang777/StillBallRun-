using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// 화면 아무 곳이나 누르고 당겼다 놓으면 당긴 반대 방향으로 발사. 타일에 닿아 있을 때만 발사 가능.
public class BallController : MonoBehaviour
{
    public float maxDrag = 2.5f;   // 당기기 최대 거리(월드 단위)
    public float power = 4f;       // 당긴 거리 → 발사 속도 배율
    Vector2 dragStartScreen;       // 누르기 시작한 위치(화면 좌표). 매 프레임 현재 카메라 기준 월드로 바꿔서
                                   // 카메라가 움직여도 시작점이 화면에 고정된 채 같이 이동

    Rigidbody2D rb;
    LineRenderer line;
    bool dragging;
    public bool isTouchingTile;    // 타일(블록·얼음)에 닿아 있는 동안만 true → 이때만 발사 가능
    static readonly ContactFilter2D tileFilter = new ContactFilter2D { useTriggers = false }; // 도착지점(트리거)은 제외
    public int releaseFrames = 4;  // 발사 후 이 프레임 수가 지나면 isTouchingTile을 false로 전환
    int launchFrame = int.MinValue / 2;
    float launchTime = -1f;
    float launchVelocityY;              // 마지막 발사 때의 Y축 초기속도 (슬라임 옆면에서 속력으로 사용)
    Vector2 velocityBeforeStep;         // 물리 계산 직전 속도 = 충돌 직전 이동 방향 확인용
    GameObject newestContact;           // 가장 최근에 새로 닿은 블록. 두 블록에 걸치면 이 블록의 기믹만 적용

    public bool airCharge;              // 검은 코인: 공중에서 1회 발사 가능 (최대 1회)
    SpriteRenderer sr;
    Color normalColor;

    public bool isSlidingOnIce;         // 얼음 윗면에 닿은 채 옆으로 움직이는 중 → 미끄러짐 소리 반복
    public float iceSlideMinSpeed = 0.3f; // 이 가로 속도(칸/초) 이상이어야 미끄러지는 것으로 판정
    bool iceContact;                    // 직전 물리 스텝에 얼음 윗면에 닿아 있었는지

    void FixedUpdate()
    {
        iceContact = false;             // 이번 스텝 충돌 처리(OnCollisionStay2D)에서 다시 채움
        velocityBeforeStep = rb.linearVelocity;
        rb.linearVelocity += GameManager.I.WindAt(rb.position) * Time.fixedDeltaTime; // 선풍기 바람
    }
    public float honeyStartSpeed = 0.25f; // 꿀에 붙은 직후 하강 속도(칸/초) = 기존(≈0.5)의 0.5배
    public float honeyAccel = 3f;     // 붙어 있는 동안 하강 가속도(칸/초²) = 시작 속도의 3.5배
    public float honeyMaxSpeed = 5f;      // 하강 최대 속도(칸/초) = 시작 속도의 4배
    float honeySpeed;                     // 현재 꿀 하강 속도. 0이면 꿀에 붙어 있지 않은 상태

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        line = GetComponent<LineRenderer>();
        sr = GetComponent<SpriteRenderer>();
        normalColor = sr.color;
    }

    // 검은 코인 충전 상태 + 공 색 (충전 중엔 검은색)
    public void SetAirCharge(bool on)
    {
        airCharge = on;
        sr.color = on ? new Color(0.1f, 0.1f, 0.1f) : normalColor;
    }

    void Update()
    {
        int sinceLaunch = Time.frameCount - launchFrame;
        if (sinceLaunch == releaseFrames) isTouchingTile = false;                  // 발사 4프레임 뒤 강제 false
        else if (sinceLaunch > releaseFrames) isTouchingTile = rb.IsTouching(tileFilter); // 그 전엔 값 유지

        // 얼음 미끄러짐 판정 → 미끄러짐 소리를 반복 재생, false가 되는 즉시 멈춤
        isSlidingOnIce = iceContact && Mathf.Abs(rb.linearVelocity.x) > iceSlideMinSpeed;
        Sfx.Loop(Sfx.Bank != null ? Sfx.Bank.iceSlideLoop : null, isSlidingOnIce);

        var pointer = Pointer.current; // 마우스와 터치 모두 포함
        if (pointer == null) return;
        Vector2 world = Camera.main.ScreenToWorldPoint(pointer.position.ReadValue());

        // UI 버튼(RETRY·MENU) 위에서 누른 건 제외
        if (pointer.press.wasPressedThisFrame && !EventSystem.current.IsPointerOverGameObject())
        {
            dragging = true;
            dragStartScreen = pointer.position.ReadValue();
        }
        if (!dragging) return;

        Vector2 dragStart = Camera.main.ScreenToWorldPoint(dragStartScreen);
        var pull = Vector2.ClampMagnitude(dragStart - world, maxDrag);
        line.enabled = true;
        line.startColor = line.endColor = (isTouchingTile && GameManager.I.HasShot) || airCharge ? Color.white : Color.gray;
        line.SetPosition(0, rb.position);
        line.SetPosition(1, rb.position + pull);

        if (pointer.press.wasReleasedThisFrame)
        {
            dragging = false;
            line.enabled = false;
            if (isTouchingTile && GameManager.I.HasShot) // 발사 제한 스테이지면 남은 횟수 차감
            {
                GameManager.I.UseShot();
                Launch(pull * power);
                Sfx.Play(Sfx.Bank != null ? Sfx.Bank.launch : null);
            }
            else if (airCharge) // 검은 코인 충전으로 발사 (발사 횟수는 차감 안 함)
            {
                SetAirCharge(false);
                Launch(pull * power);
                Sfx.Play(Sfx.Bank != null ? Sfx.Bank.launch : null);
            }
        }
    }

    public void Launch(Vector2 velocity)
    {
        rb.linearVelocity = velocity;
        launchFrame = Time.frameCount;
        launchTime = Time.time;
        launchVelocityY = velocity.y;
        honeySpeed = 0f;
    }

    void OnTriggerEnter2D(Collider2D other) => GameManager.I.Touch(other.gameObject); // 도착지점, 별

    void OnCollisionEnter2D(Collision2D c)
    {
        newestContact = c.gameObject;
        var normal = c.GetContact(0).normal;
        // 부딪힘 효과음: 블록 종류 + 면에 수직으로 들어온 속도(세게 부딪힐수록 크게)
        float impact = Mathf.Abs(Vector2.Dot(velocityBeforeStep, normal));
        Sfx.Impact(c.gameObject.name, impact);
        // 부딪힘 가루: 약한 충돌(bounce_weak 기준 미만)은 없음, 세게 부딪힐수록 2→3→4개
        float weak = Sfx.Bank != null ? Sfx.Bank.weakSpeed : 2.5f;
        bool slimy = c.gameObject.name.StartsWith("Slime"); // 슬라임·강화 슬라임
        if (slimy && impact >= weak * 0.6f) // 슬라임: 초록 방울이 튕겨 나가는 쪽으로 통통 (조금 더 잘 보이게)
            Dust.Burst(c.GetContact(0).point, normal, GameManager.TileColor('S'), impact < 6f ? 4 : 5, 3.5f, 50f, 1.2f);
        else if (impact >= weak && c.gameObject.name != "Spikes")
            Dust.Burst(c.GetContact(0).point, normal, Dust.ColorFor(c.gameObject.name), impact < 5f ? 2 : impact < 9f ? 3 : 4, 2.5f, 70f);
        GameManager.I.Touch(c.gameObject); // 가시
        OnCollisionStay2D(c);

        // 슬라임 옆면(법선이 수평 쪽): X는 탄성으로 반사, Y는 충돌 직전 방향(위/아래) 유지 + 크기는 발사 때 Y 초기속력
        if (c.gameObject.name == "Slime" && Mathf.Abs(normal.x) > Mathf.Abs(normal.y))
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, Mathf.Sign(velocityBeforeStep.y) * Mathf.Abs(launchVelocityY));

        // 강화 슬라임: 화살표가 가리키는 면(법선 = 화살표 방향)에 닿으면 그 방향으로 superSlimeSpeed,
        // 나머지 축은 충돌 직전 관성 유지. 다른 면은 일반 슬라임처럼 튕김
        var dir = GameManager.SuperSlimeDir(c.gameObject.name);
        if (dir != Vector2.zero && Vector2.Dot(normal, dir) > 0.7f)
        {
            float s = GameManager.I.superSlimeSpeed;
            rb.linearVelocity = dir.x != 0f ? new Vector2(dir.x * s, velocityBeforeStep.y)
                                            : new Vector2(velocityBeforeStep.x, dir.y * s);
        }
    }

    // 꿀: 닿아 있는 동안 시작 속도로 천천히 미끄러지다 가속, 최대 속도 제한. 발사 직후 0.1초는 떨어져 나갈 수 있게 예외
    // ponytail: 프레임 대신 시간 기준 — 고주사율에선 4프레임이 물리 1스텝보다 짧아 발사가 막힘
    void OnCollisionStay2D(Collision2D c)
    {
        if (c.gameObject.name == "Ice" && c.GetContact(0).normal.y > 0.5f) iceContact = true; // 얼음 윗면에 닿음
        if (c.gameObject.name != "Honey" || Time.time - launchTime <= 0.1f) return;
        if (newestContact != null && newestContact != c.gameObject) return; // 더 최근에 닿은 블록이 있으면 꿀 효과 끔
        float gravityStep = -Physics2D.gravity.y * rb.gravityScale * Time.fixedDeltaTime;
        // 꿀 아랫면(접촉점이 공 중심보다 위)에 닿으면 매달린 채 정지: 다음 스텝 중력만큼 위로 줘서 순속도 0
        if (c.GetContact(0).point.y > rb.position.y + 0.15f)
        {
            rb.linearVelocity = Vector2.up * gravityStep;
            return;
        }
        honeySpeed =honeySpeed == 0f ? honeyStartSpeed
                   : Mathf.Min(honeySpeed + honeyAccel * Time.fixedDeltaTime, honeyMaxSpeed);
        // 다음 물리 스텝에서 더해질 중력만큼 미리 빼서, 실제 하강 속도가 honeySpeed와 같게 함
        rb.linearVelocity = Vector2.down * (honeySpeed - gravityStep);

        // 꿀벽(옆면)을 타고 내려가는 동안 꿀 방울을 가끔 하나씩 (바닥 위에 서 있을 때는 없음)
        var contact = c.GetContact(0);
        if (Mathf.Abs(contact.normal.x) > 0.5f && (honeyDustTimer -= Time.fixedDeltaTime) <= 0f)
        {
            honeyDustTimer = 0.12f;
            Dust.Burst(contact.point, contact.normal + Vector2.down, GameManager.TileColor('H'), 1, 0.6f, 25f);
        }
    }

    float honeyDustTimer; // 꿀 방울 간격 타이머

    void OnCollisionExit2D(Collision2D c)
    {
        if (c.gameObject.name == "Honey") honeySpeed = 0f;
        if (c.gameObject == newestContact) newestContact = null; // 최근 블록에서 떨어지면 아직 닿아 있는 블록 효과로 복귀
    }

    void OnDisable()
    {
        dragging = false;
        if (line) line.enabled = false;
        isSlidingOnIce = false;
        Sfx.Loop(null, false); // 공이 꺼지면(가시·클리어·씬 이동) 미끄러짐 소리 즉시 정지
    }
}
