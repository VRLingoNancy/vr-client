using UnityEngine;
using UnityEngine.UI;

public class MicSensitivitySlider : MonoBehaviour
{
    public Slider slider;

    // Max threshold when slider is at 0 (least sensitive, strongest gate).
    const float MAX_THRESHOLD = 0.1f;
    const string PREF_KEY = "micNoiseGate";

    void Start()
    {
        if (slider == null) return;

        if (PlayerPrefs.HasKey(PREF_KEY))
            AuthState.micNoiseGate = PlayerPrefs.GetFloat(PREF_KEY);

        slider.SetValueWithoutNotify(ThresholdToSlider(AuthState.micNoiseGate));
        slider.onValueChanged.AddListener(OnChanged);
    }

    void OnChanged(float value)
    {
        float threshold = SliderToThreshold(value);
        AuthState.micNoiseGate = threshold;
        PlayerPrefs.SetFloat(PREF_KEY, threshold);
    }

    float SliderToThreshold(float slider)
    {
        float t = 1f - (slider - this.slider.minValue) / (this.slider.maxValue - this.slider.minValue);
        return Mathf.Clamp01(t) * MAX_THRESHOLD;
    }

    float ThresholdToSlider(float threshold)
    {
        float t = 1f - Mathf.Clamp01(threshold / MAX_THRESHOLD);
        return slider.minValue + t * (slider.maxValue - slider.minValue);
    }
}
