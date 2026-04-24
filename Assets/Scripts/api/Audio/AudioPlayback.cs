using UnityEngine;
using System;
using System.Collections.Generic;

[RequireComponent(typeof(AudioSource))]
public class AudioPlayback : MonoBehaviour
{
    private AudioSource source;
    private readonly object lockObj = new object();
    private readonly List<float> buffer = new List<float>();
    public int sampleRate = 24000;

    private bool isPlaying = false;
    private int playbackPosition = 0;

    public bool IsPlaying => source != null && source.isPlaying;

    // Per-response playback progress. Reset via BeginSession at start of a new AI response.
    public int SessionSamplesReceived { get; private set; }
    public int SessionSamplesPlayed => (source != null && source.clip != null) ? source.timeSamples : 0;

    public void BeginSession()
    {
        SessionSamplesReceived = 0;
    }

    void Start()
    {
        source = GetComponent<AudioSource>();
        if (source == null)
            source = gameObject.AddComponent<AudioSource>();

        source.spatialBlend = 0f;
        source.volume = 1f;
        source.playOnAwake = false;
    }

    public void PushPCM(byte[] pcm)
    {
        float[] samples = PCM16ToFloat(pcm);

        lock (lockObj)
        {
            buffer.AddRange(samples);
        }
        SessionSamplesReceived += samples.Length;

        if (!isPlaying)
            TryPlay();
    }

    void TryPlay()
    {
        int count;
        lock (lockObj) { count = buffer.Count; }

        if (count < sampleRate / 4) return; // wait for 250ms of audio before starting

        lock (lockObj)
        {
            var clip = AudioClip.Create("AIResponse", buffer.Count, 1, sampleRate, false);
            clip.SetData(buffer.ToArray(), 0);
            source.clip = clip;
            source.Play();
            isPlaying = true;
            playbackPosition = buffer.Count;
        }
    }

    void Update()
    {
        if (!isPlaying || source.clip == null) return;

        // Check if there's new data to append
        int newSamples;
        lock (lockObj) { newSamples = buffer.Count - playbackPosition; }

        if (newSamples > 0)
        {
            lock (lockObj)
            {
                var clip = AudioClip.Create("AIResponse", buffer.Count, 1, sampleRate, false);
                clip.SetData(buffer.ToArray(), 0);

                int currentSample = source.timeSamples;
                source.clip = clip;
                source.timeSamples = currentSample;
                if (!source.isPlaying)
                    source.Play();
                playbackPosition = buffer.Count;
            }
        }

        // Reset when done playing
        if (!source.isPlaying && isPlaying)
        {
            isPlaying = false;
            lock (lockObj) { buffer.Clear(); }
            playbackPosition = 0;
        }
    }

    float[] PCM16ToFloat(byte[] bytes)
    {
        float[] samples = new float[bytes.Length / 2];

        for (int i = 0; i < samples.Length; i++)
        {
            short s = BitConverter.ToInt16(bytes, i * 2);
            samples[i] = s / 32768f;
        }

        return samples;
    }
}
