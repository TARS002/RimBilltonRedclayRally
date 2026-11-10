using UnityEngine;
using UnityEngine.Rendering;

/// 车尾尘烟：整条 ParticleSystem 用代码配置（场景无需任何资源），
/// 速度越快 / 侧滑越大，烟越浓 —— 红土的颜色也在这里。
public class DustTrail : MonoBehaviour
{
    public CarController car;

    ParticleSystem ps;

    void Awake()
    {
        ps = gameObject.AddComponent<ParticleSystem>();
        Configure();
    }

    void Update()
    {
        if (car == null) return;
        float speed = Mathf.Abs(car.ForwardSpeed);
        float slip = Mathf.Abs(car.LateralSpeed);
        // 起步 5m/s 后才见烟；漂移时额外加浓
        float rate = car.IsGrounded
            ? Mathf.Clamp01(speed / 5f) * (8f + slip * 14f) + (car.IsDrifting ? 10f : 0f)
            : 0f;
        var em = ps.emission;
        em.rateOverTime = rate;
    }

    void Configure()
    {
        var main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
        main.startColor = new Color(0.85f, 0.62f, 0.48f, 0.55f);  // 红土色
        main.maxParticles = 80;                                    // 移动端粒子预算
        main.gravityModifier = -0.05f;                             // 尘往上飘一点
        main.simulationSpace = ParticleSystemSimulationSpace.World; // 尘留在身后

        var emission = ps.emission;
        emission.rateOverTime = 0f;                                // 速率由 Update 动态控制

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 22f;
        shape.radius = 0.25f;

        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0f, 0.35f), new Keyframe(1f, 1.5f)));

        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0f, 1f) });
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(grad);

        var psr = GetComponent<ParticleSystemRenderer>();
        psr.material = MakeDustMaterial();
        psr.renderMode = ParticleSystemRenderMode.Billboard;
    }

    Material MakeDustMaterial()
    {
        try
        {
            if (GraphicsSettings.currentRenderPipeline != null)
            {
                // URP 项目：生成径向渐变贴图 + URP 粒子无光照透明材质
                Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (shader != null)
                {
                    var m = new Material(shader);
                    m.SetTexture("_BaseMap", MakeRadialTexture());
                    m.SetColor("_BaseColor", Color.white);
                    m.SetFloat("_Surface", 1f);   // Transparent
                    m.SetFloat("_Blend", 0f);     // Alpha 混合
                    m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    m.SetOverrideTag("RenderType", "Transparent");
                    m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                    m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                    m.SetInt("_ZWrite", 0);
                    m.renderQueue = 3000;
                    return m;
                }
            }
            else
            {
                // 内置管线：直接用引擎自带的圆形粒子材质
                Material builtin = Resources.GetBuiltinResource<Material>("Default-Particle.mat");
                if (builtin != null) return builtin;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[DustTrail] 尘烟材质创建失败，已停用粒子：" + e.Message);
        }
        // 兜底：宁可没烟也不能报错
        GetComponent<ParticleSystemRenderer>().enabled = false;
        return null;
    }

    static Texture2D MakeRadialTexture()
    {
        const int size = 32;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(size / 2f, size / 2f)) / (size / 2f);
                float a = Mathf.Clamp01(1f - d);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
        }
        tex.Apply();
        return tex;
    }
}
