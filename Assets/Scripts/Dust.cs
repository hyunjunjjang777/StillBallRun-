using UnityEngine;

// 작은 가루 파티클 (공이 타일에 부딪힐 때, 꿀벽을 타고 내려갈 때). 과하지 않게 몇 개씩만.
// 씬마다 파티클 시스템 하나를 만들어 두고 필요한 순간에 몇 개씩 직접 뿌림(Emit)
public static class Dust
{
    static ParticleSystem ps;

    static ParticleSystem Get()
    {
        if (ps != null) return ps;
        var go = new GameObject("Dust");
        ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.5f);
        main.startSpeed = 0f;
        main.gravityModifier = 0.8f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 100;
        var emission = ps.emission; emission.enabled = false; // 자동으로 뿜지 않음
        var shape = ps.shape; shape.enabled = false;

        // 사라질 때 점점 투명 + 작아짐
        var col = ps.colorOverLifetime; col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                     new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.5f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;
        var size = ps.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));

        // 텍스처 없는 스프라이트 기본 재질 → 단색 네모 (픽셀아트 느낌)
        var r = go.GetComponent<ParticleSystemRenderer>();
        var temp = new GameObject().AddComponent<SpriteRenderer>();
        r.sharedMaterial = temp.sharedMaterial;
        Object.Destroy(temp.gameObject);
        r.sortingOrder = 2; // 공(1)보다 앞
        return ps;
    }

    // normal 방향(부딪힌 면 바깥쪽)으로 ±spread도 퍼지게 count개. spread 180이면 사방으로. size는 크기 배율
    public static void Burst(Vector2 pos, Vector2 normal, Color color, int count, float speed, float spread = 60f, float size = 1f)
    {
        if (count <= 0) return;
        var p = Get();
        var e = new ParticleSystem.EmitParams { applyShapeToPosition = false };
        for (int i = 0; i < count; i++)
        {
            var dir = Quaternion.Euler(0f, 0f, Random.Range(-spread, spread)) * normal;
            e.position = pos;
            e.velocity = dir * speed * Random.Range(0.5f, 1f);
            e.startColor = color;
            e.startSize = Random.Range(0.06f, 0.11f) * size;
            p.Emit(e, 1);
        }
    }

    // 사방으로 고르게 퍼지는 원형 폭발 (가시에 터질 때, 포탈)
    public static void Ring(Vector2 pos, Color color, int count, float speed, float size = 1f)
    {
        var p = Get();
        var e = new ParticleSystem.EmitParams { applyShapeToPosition = false };
        for (int i = 0; i < count; i++)
        {
            float a = (i + Random.Range(-0.3f, 0.3f)) / count * Mathf.PI * 2f;
            e.position = pos;
            e.velocity = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * speed * Random.Range(0.7f, 1f);
            e.startColor = color;
            e.startSize = Random.Range(0.06f, 0.11f) * size;
            p.Emit(e, 1);
        }
    }

    // 블록 그룹 이름 → 가루 색 (블록 기본 색과 같은 계열). 가시는 없음
    public static Color ColorFor(string group) => group switch
    {
        "Honey" => GameManager.TileColor('H'),
        "Ice" => GameManager.TileColor('I'),
        "Slime" or "SlimeUp" or "SlimeDown" or "SlimeLeft" or "SlimeRight" => GameManager.TileColor('S'),
        "Unstable" => GameManager.TileColor('U'),
        "Fans" => new Color(0.75f, 0.8f, 0.9f),
        _ => GameManager.TileColor('#'),
    };
}
