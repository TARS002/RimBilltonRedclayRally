

## Codely Structured Memories

### User
- [2026-11-11 00:05:28] 用户中文沟通，编程与建模零基础，偏好简洁讲解（明确要求"尽可能短"）；项目是非商业同人 fan game，重视先做出"能开的小步原型"，解释宜通俗、少术语堆砌。

### Feedback

### Project
- [2026-11-11 00:05:23] 项目"雷姆必拓红土拉力赛"使用团结引擎 Tuanjie 1.6.14（内核 2022.3.61t15），工程在 `New Tuanjie Project/`，编辑器 `D:\Program Files\Tuanjie 2022.3.61t15\Editor\Tuanjie.exe`；项目为内置渲染管线 + 旧版 Input Manager（activeInputHandler=0），可用 batchmode `-executeMethod` 做编译验证与场景生成。
- [2026-11-11 00:05:23] 团结引擎 2022.3 的 EditorSceneManager 缺少 `SaveCurrentModifiedScenesIfUserWantsToChange` 方法（CS0117），写编辑器脚本时要避开。
- [2026-11-11 00:05:23] 团结引擎项目模板缺省不含 com.unity.ugui（UnityEngine.UI / EventSystems 用不了），需在 Packages/manifest.json 显式加 `"com.unity.ugui": "1.0.0"`；首次添加后第一轮编译可能仍报缺失，重跑即可。

### Reference
- [2026-11-11 00:05:28] 项目总纲文档：`Docs/项目启动与学习路线.md`（引擎/平台策略/MVP 范围/里程碑/待办清单均以此为准，优先读它确认方向）。当前策略：PC + 自用安卓手机优先，B 站小游戏降为远期可选。
