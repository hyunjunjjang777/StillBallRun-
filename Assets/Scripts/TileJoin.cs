using System.Collections.Generic;
using UnityEngine;

// 같은 블록끼리 붙은 모양에 맞춰 4×4 타일 시트에서 알맞은 조각을 골라 줌 (한 칸 = 조각 한 장).
// 시트 배치 (조각 = 시트 가로의 1/4, 행은 위에서부터):
//   열 0 가로 단독 / 1 왼쪽 끝 / 2 가로 가운데 / 3 오른쪽 끝
//   행 0 세로 단독 / 1 위쪽 끝 / 2 세로 가운데 / 3 아래쪽 끝
// ㄷ자처럼 가로줄과 세로줄이 만나는 모양에서는 맨 위·아래 가로줄이 끊기지 않는 쪽을 우선함 (Mask 참고)
public static class TileJoin
{
    public const string Joinable = "#HIS"; // 시트로 이어 그리는 블록 (일반·꿀·얼음·슬라임)

    static readonly Dictionary<(Sprite, int, int), Sprite> cache = new();

    // 그림 적용 후 호출: 이웃 모양에 맞는 시트 조각으로 교체하고 한 칸 크기로 맞춤
    public static void Apply(SpriteRenderer sr, char ch, char[,] grid, int r, int c)
    {
        if (Joinable.IndexOf(ch) < 0 || sr.sprite == null) return;
        int m = Mask(grid, r, c);
        bool up = (m & 1) != 0, right = (m & 2) != 0, down = (m & 4) != 0, left = (m & 8) != 0;
        int col = !left && !right ? 0 : !left ? 1 : right ? 2 : 3;
        int row = !up && !down ? 0 : !up ? 1 : down ? 2 : 3;
        sr.sprite = Piece(sr.sprite, col, row);
        var b = sr.sprite.bounds.size;
        sr.transform.localScale = new Vector3(1f / b.x, 1f / b.y, 1f);
    }

    // 붙은 면 비트: 1 위, 2 오른쪽, 4 아래, 8 왼쪽 (grid는 0행이 맨 위)
    // 좌우는 같은 블록이면 항상 이어 붙임 → 가로줄은 절대 끊기지 않음.
    // 위아래는 두 칸의 "좌우 연결 모양"이 같을 때만 이어 붙임 → ㄷ자에서 맨 위·아래 줄이 각각 하나의 띠로 유지되고,
    // 그 사이 세로줄은 따로 그려짐. 꽉 찬 직사각형 덩어리는 줄마다 모양이 같아서 위아래도 그대로 이어짐
    public static int Mask(char[,] g, int r, int c)
    {
        char ch = g[r, c];
        bool Same(int rr, int cc) => rr >= 0 && rr < g.GetLength(0) && cc >= 0 && cc < g.GetLength(1) && g[rr, cc] == ch;
        bool SameRowShape(int rr) => Same(rr, c - 1) == Same(r, c - 1) && Same(rr, c + 1) == Same(r, c + 1);
        int m = 0;
        if (Same(r - 1, c) && SameRowShape(r - 1)) m |= 1;
        if (Same(r, c + 1)) m |= 2;
        if (Same(r + 1, c) && SameRowShape(r + 1)) m |= 4;
        if (Same(r, c - 1)) m |= 8;
        return m;
    }

    // 시트에서 (col, row) 조각을 잘라 옴 (row 0이 맨 위). 조각마다 한 번만 만들고 재사용.
    // 조각 PPU = 조각 픽셀 수 → 조각 하나가 정확히 1칸 크기
    public static Sprite Piece(Sprite sheet, int col, int row)
    {
        if (cache.TryGetValue((sheet, col, row), out var piece)) return piece;
        var rect = sheet.textureRect;
        float s = rect.width / 4f;
        piece = Sprite.Create(sheet.texture, new Rect(rect.x + col * s, rect.y + (3 - row) * s, s, s), Vector2.one * 0.5f, s, 0, SpriteMeshType.FullRect);
        cache[(sheet, col, row)] = piece;
        return piece;
    }
}
