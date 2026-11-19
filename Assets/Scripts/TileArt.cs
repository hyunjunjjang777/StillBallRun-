using UnityEngine;

// 블록별 그림 모음. Assets/Resources/TileArt.asset 을 선택해 Inspector에서 스프라이트를 넣거나 바꾸면
// 게임·맵 에디터에 모두 적용됨. 비워 둔 칸은 기존 도형+색으로 표시.
[CreateAssetMenu(menuName = "BounceBall/Tile Art")]
public class TileArt : ScriptableObject
{
    public Sprite block, spike, honey, ice, slime, superSlime, star, coin, goal, ball;
    public Sprite unstable, fan, portalStart, portalEnd, blackCoin;
    [Tooltip("불안정한 블록이 닿은 뒤 순서대로 바뀌는 그림 (평상시 그림 = Unstable). 예: 부서진 흙_1, _2, _3")]
    public Sprite[] unstableNext;
    [Tooltip("선풍기 바람 칸 애니메이션. 선풍기 바로 앞 칸부터 0, 1, 2, 3… 순서. 오른쪽으로 부는 방향으로 그린 그림 기준")]
    public Sprite[] wind;

    public Sprite For(char ch) => ch switch
    {
        'U' => unstable,
        'A' or 'V' or 'L' or 'R' => fan,
        'P' or (>= '1' and <= '9') => portalStart,
        >= 'a' and <= 'i' => portalEnd,
        'K' => blackCoin,
        '#' => block,
        'D' => spike,
        'H' => honey,
        'I' => ice,
        'S' => slime,
        '^' or 'v' or '<' or '>' => superSlime,
        '*' => star,
        'C' => coin,
        '@' => goal,
        'B' => ball,
        _ => null,
    };

    static TileArt instance;
    public static TileArt I => instance != null ? instance : instance = Resources.Load<TileArt>("TileArt");
}
