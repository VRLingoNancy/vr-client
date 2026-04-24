using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

[RequireComponent(typeof(Animator))]
public class AICharacterAnimator : MonoBehaviour
{
    public AnimationClip idleClip;
    public AnimationClip listeningClip;

    public float blendSpeed = 6f;

    // Procedural talking bob applied on top of idle.
    public float talkHeadBobAmplitude = 5f;     // degrees
    public float talkHeadBobSpeed = 4f;
    public float talkChestBobAmplitude = 2f;

    private Animator animator;
    private PlayableGraph graph;
    private AnimationMixerPlayable mixer;
    private AnimationClipPlayable idlePlayable;
    private AnimationClipPlayable listeningPlayable;

    private float talkingWeight; // 0..1, smooth

    void Start()
    {
        if (idleClip != null) idleClip.wrapMode = WrapMode.Loop;
        if (listeningClip != null) listeningClip.wrapMode = WrapMode.Loop;

        animator = GetComponent<Animator>();

        graph = PlayableGraph.Create($"AIAnim_{gameObject.GetInstanceID()}");
        var output = AnimationPlayableOutput.Create(graph, "output", animator);
        mixer = AnimationMixerPlayable.Create(graph, 2);
        output.SetSourcePlayable(mixer);

        idlePlayable = Attach(0, idleClip);
        listeningPlayable = Attach(1, listeningClip);

        mixer.SetInputWeight(0, 1f);
        mixer.SetInputWeight(1, 0f);
        graph.Play();
    }

    AnimationClipPlayable Attach(int port, AnimationClip clip)
    {
        if (clip == null) return default;
        var p = AnimationClipPlayable.Create(graph, clip);
        p.SetApplyFootIK(false);
        graph.Connect(p, 0, mixer, port);
        return p;
    }

    private bool lastTalking, lastListening;

    void Update()
    {
        if (!graph.IsValid()) return;

        bool talking = VRLingoClient.IsAiSpeaking;
        bool listening = VRLingoClient.IsUserSpeaking;

        if (talking != lastTalking || listening != lastListening)
        {
            Debug.Log($"[AIAnim] state change: talking={talking}, listening={listening}");
            lastTalking = talking;
            lastListening = listening;
        }

        float targetIdle = !listening ? 1f : 0f;
        float targetListen = listening ? 1f : 0f;

        float dt = Time.deltaTime * blendSpeed;
        mixer.SetInputWeight(0, Mathf.MoveTowards(mixer.GetInputWeight(0), targetIdle, dt));
        mixer.SetInputWeight(1, Mathf.MoveTowards(mixer.GetInputWeight(1), targetListen, dt));

        talkingWeight = Mathf.MoveTowards(talkingWeight, talking ? 1f : 0f, dt);
    }

    void LateUpdate()
    {
        if (animator == null || talkingWeight <= 0.001f) return;

        var head = animator.GetBoneTransform(HumanBodyBones.Head);
        var chest = animator.GetBoneTransform(HumanBodyBones.Chest);

        float t = Time.time;
        if (head != null)
        {
            float yaw = Mathf.Sin(t * talkHeadBobSpeed * Mathf.PI) * talkHeadBobAmplitude;
            float pitch = Mathf.Sin(t * talkHeadBobSpeed * Mathf.PI * 0.5f) * talkHeadBobAmplitude * 0.5f;
            head.localRotation *= Quaternion.Euler(pitch * talkingWeight, yaw * talkingWeight, 0f);
        }
        if (chest != null)
        {
            float roll = Mathf.Sin(t * talkHeadBobSpeed * Mathf.PI * 0.7f) * talkChestBobAmplitude;
            chest.localRotation *= Quaternion.Euler(0f, 0f, roll * talkingWeight);
        }
    }

    void OnDestroy()
    {
        if (graph.IsValid()) graph.Destroy();
    }
}
