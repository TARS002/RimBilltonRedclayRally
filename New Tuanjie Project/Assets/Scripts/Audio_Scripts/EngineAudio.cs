using UnityEngine;

/// 发动机音效：程序合成的引擎轰鸣（无需外部音频文件、零版权顾虑）。
/// 一个循环 AudioClip + AudioSource，音调(pitch)与音量随发动机转速升高。
/// 之后若想换真实引擎录音，把 EngineAudio.clip 指到一个 AudioClip 即可（优先于合成）。
public class EngineAudio : MonoBehaviour
{
    [Header("引用")]
    public RallyCar car;
    public AudioClip clip;      // 留空则自动合成一个引擎循环

    [Header("转速 → 音调 / 音量")]
    public float minRpm = 1200f;    // 怠速（雅力士）
    public float maxRpm = 8600f;    // 红线
    public float minPitch = 0.95f;  // 怠速音调（整体抬高）
    public float maxPitch = 2.35f;  // 高转音调（超过 3 会非常刺耳，慎调）
    public float pitchSmooth = 10f; // 音调跟随平滑速度
    public float minVolume = 0.55f;
    public float maxVolume = 1f;

    const int SampleRate = 44100;
    const float LoopSeconds = 2f;
    const float BaseFreq = 100f;     // 基础音高（Hz），抬高后更接近高转引擎

    AudioSource source;
    float smoothedPitch;
    float smoothedVolume;

    void Awake()
    {
        if (car == null) car = GetComponent<RallyCar>();

        if (clip == null) clip = BuildEngineClip();

        source = gameObject.AddComponent<AudioSource>();
        source.clip = clip;
        source.loop = true;
        source.playOnAwake = true;
        source.spatialBlend = 0f;      // M1 先用 2D（简单、稳定）；以后可改 3D 跟随
        source.volume = minVolume;
        source.pitch = minPitch;
        smoothedPitch = minPitch;
        smoothedVolume = minVolume;
        source.Play();
    }

    void Update()
    {
        if (car == null || source == null) return;

        float t = Mathf.InverseLerp(minRpm, maxRpm, car.EngineRpm);
        float targetPitch = Mathf.Lerp(minPitch, maxPitch, t);
        float targetVolume = Mathf.Lerp(minVolume, maxVolume, t);

        float k = Time.deltaTime * pitchSmooth;
        smoothedPitch = Mathf.Lerp(smoothedPitch, targetPitch, k);
        smoothedVolume = Mathf.Lerp(smoothedVolume, targetVolume, k);

        source.pitch = smoothedPitch;
        source.volume = smoothedVolume;
    }

    /// 合成一个无缝循环的低频引擎轰鸣：几个谐波叠加 + 点火拍频。
    /// 基频与时长取整数周期，保证首尾衔接无爆音。
    static AudioClip BuildEngineClip()
    {
        int n = Mathf.RoundToInt(SampleRate * LoopSeconds);   // 2s × 60Hz = 120 个整数周期
        float[] data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SampleRate;
            float s =
                Mathf.Sin(2f * Mathf.PI * BaseFreq * t)
                + 0.50f * Mathf.Sin(2f * Mathf.PI * 2f * BaseFreq * t)
                + 0.28f * Mathf.Sin(2f * Mathf.PI * 3f * BaseFreq * t)
                + 0.15f * Mathf.Sin(2f * Mathf.PI * 4f * BaseFreq * t);

            // 点火拍频（幅度调制），让声音有"突突"的周期感
            float am = 0.65f + 0.35f * Mathf.Sin(2f * Mathf.PI * (BaseFreq * 0.5f) * t);
            s *= am;

            data[i] = s * 0.22f;   // 归一化，留余量避免削波
        }

        var generated = AudioClip.Create("EngineLoop", n, 1, SampleRate, false);
        generated.SetData(data, 0);
        return generated;
    }
}
