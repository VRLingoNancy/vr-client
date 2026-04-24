using UnityEngine;

public class SpeakingAnimator : MonoBehaviour
{
    public Transform target;
    public AudioPlayback playback;

    public float speakingBobAmplitude = 0.03f;
    public float speakingBobSpeed = 6f;
    public float idleBreathAmplitude = 0.01f;
    public float idleBreathSpeed = 1.2f;
    public float transitionSpeed = 6f;

    private Vector3 baseScale;
    private float speakingWeight = 0f;

    void Start()
    {
        if (target == null) target = transform;
        if (playback == null) playback = FindAnyObjectByType<AudioPlayback>();
        baseScale = target.localScale;
    }

    void Update()
    {
        if (target == null) return;
        if (playback == null)
            playback = FindAnyObjectByType<AudioPlayback>();

        bool speaking = playback != null && playback.IsPlaying;
        speakingWeight = Mathf.MoveTowards(speakingWeight, speaking ? 1f : 0f, Time.deltaTime * transitionSpeed);

        float speakingWave = Mathf.Sin(Time.time * speakingBobSpeed * Mathf.PI * 2f) * speakingBobAmplitude;
        float idleWave = Mathf.Sin(Time.time * idleBreathSpeed * Mathf.PI * 2f) * idleBreathAmplitude;
        float bob = Mathf.Lerp(idleWave, speakingWave, speakingWeight);

        target.localScale = baseScale * (1f + bob);
    }
}
