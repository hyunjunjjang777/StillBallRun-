using UnityEngine;

// 선풍기 바람 칸 연출: 선풍기 바로 앞 칸부터 바람 그림(0, 1, 2, 3 …)이 차례로 나타났다 사라지며
// 바람이 앞으로 흘러가는 것처럼 보이게 함. 바람 그림(바람_0~3)은 왼쪽으로 부는 방향으로 그려져 있음
// (오른쪽에서 시작해 왼쪽으로 뻗고 왼쪽 끝에서 말림) → 선풍기 방향에 맞게 회전
// (오른쪽은 회전 대신 좌우 뒤집기 → 소용돌이 모양이 위아래로 뒤집히지 않음)
public class WindFx : MonoBehaviour
{
    public float cycle = 0.8f;   // 바람 한 번이 끝 칸까지 흘러가는 데 걸리는 시간(초)
    public float showRatio = 0.5f; // 한 칸이 보이는 시간 비율(cycle 대비). 클수록 여러 칸이 겹쳐 보임

    SpriteRenderer[] cells;

    public void Init(Vector2 fanPos, Vector2 dir, int range, Sprite[] frames)
    {
        cells = new SpriteRenderer[range];
        for (int k = 0; k < range; k++)
        {
            var go = new GameObject("WindCell" + k);
            go.transform.SetParent(transform);
            go.transform.position = fanPos + dir * (k + 1);
            // 왼쪽(그림 기준) → 위: 시계 방향 90°, 아래: 반시계 90°, 오른쪽: 좌우 뒤집기
            if (dir == Vector2.up) go.transform.rotation = Quaternion.Euler(0f, 0f, -90f);
            else if (dir == Vector2.down) go.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = frames[Mathf.Min(k, frames.Length - 1)]; // 첫 칸 바람_0, 둘째 칸 바람_1 … (칸이 더 많으면 마지막 그림 반복)
            sr.flipX = dir == Vector2.right;
            sr.sortingOrder = -1;                                // 블록·공 뒤
            var b = sr.sprite.bounds.size;
            go.transform.localScale = new Vector3(1f / b.x, 1f / b.y, 1f); // 한 칸 크기
            cells[k] = sr;
        }
        Update();
    }

    void Update()
    {
        float phase = Time.time / cycle;
        for (int k = 0; k < cells.Length; k++)
        {
            // 칸마다 조금씩 늦게 시작 → 앞 칸부터 차례로 나타났다가 사라짐
            float local = Mathf.Repeat(phase - (float)k / cells.Length, 1f);
            float a = local < showRatio ? Mathf.Sin(local / showRatio * Mathf.PI) : 0f;
            cells[k].color = new Color(1f, 1f, 1f, a);
        }
    }
}
