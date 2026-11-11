using UnityEngine;
using UnityEngine.Rendering;

/// 车尾/车轮尘烟：速度越快 / 轮胎滑移越大，烟越浓。
/// 可挂整车（不指定 wheel，用全局滑移），也可挂单个车轮（指定 wheel，按该轮滑移发尘）。
/// 整条 ParticleSystem 用代码配置，场景无需任何资源。
public class DustTrail : MonoBehaviour
{
    public RallyCar car;
    public WheelCollider wheel;   // 可选：指定后按该车轮的滑移发尘

    ParticleSystem ps;

    void Awake()
    {
        ps = gameObject.AddComponent<ParticleSystem>();
        Configure();
    }

    void Update()
    {
        if (car == null) return;

        bool grounded = car.IsGrounded;
        float slip = car.SlipAmount;
        if (wheel != null)
        {
            if (wheel.GetGroundHit(out WheelHit hit))
            {
                grounded = true;
                slip = Mathf.Abs(hit.forwardSlip) + Mathf.Abs(hit.sidewaysSlip);
            }
            else
            {
                grounded = false;
            }
        }

        float rate = 0f;
        if (grounded)
            rate = Mathf.Clamp01(car.Speed / 5f) * (4f + slip * 45f);

        var em = ps.emission;
        em.rateOverTime = rate;
    }

    void Configure()
    {
        var main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.22f, 0.55f);
        main.startColor = new Color(0.85f, 0.62f, 0.48f, 0.55f);    // 红土色
        main.maxParticles = 45;                                      // 每个发射器预算（4 轮合计 180）
        main.gravityModifier = -0.05f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = ps.emission;
        emission.rateOverTime = 0f;                                  // 速率由 Update 动态控制

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 22f;
        shape.radius = 0.22f;

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
        Texture2D radial = MakeRadialTexture();   // 程序生成圆形贴图，不依赖任何内置资源

        try
        {
            if (GraphicsSettings.currentRenderPipeline != null)
            {
                // URP：URP 粒子无光照透明材质
                Shader urp = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (urp != null) return BuildUrpMaterial(urp, radial);
            }
            else
            {
                // 内置管线：团结引擎没有 Default-Particle.mat，改用常见内置粒子 shader（按可靠性排序）
                string[] candidates =
                {
                    "Particles/Standard Unlit",
                    "Legacy Shaders/Particles/Alpha Blended",
                    "Sprites/Default",
                };
                foreach (string shaderName in candidates)
                {
                    Shader shader = Shader.Find(shaderName);
                    if (shader == null) continue;
                    var m = new Material(shader);
                    if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", radial);
                    if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", new Color(1f, 1f, 1f, 0.6f));
                    if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
                    m.SetOverrideTag("RenderType", "Transparent");
                    m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                    m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                    m.SetInt("_ZWrite", 0);
                    m.EnableKeyword("_ALPHABLEND_ON");
                    m.renderQueue = 3000;
                    return m;
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[DustTrail] 尘烟材质创建失败：" + e.Message);
        }

        Debug.LogWarning("[DustTrail] 未找到可用粒子 shader，尘烟已停用（不影响车辆）。");
        GetComponent<ParticleSystemRenderer>().enabled = false;
        return null;
    }

    static Material BuildUrpMaterial(Shader shader, Texture2D tex)
    {
        var m = new Material(shader);
        m.SetTexture("_BaseMap", tex);
        m.SetColor("_BaseColor", Color.white);
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.renderQueue = 3000;
        return m;
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
