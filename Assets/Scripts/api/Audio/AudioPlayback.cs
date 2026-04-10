using UnityEngine;
using System;
using System.Collections.Generic;

public class AudioPlayback : MonoBehaviour
{
    private readonly object lockObj = new object();
    private Queue<float> audioQueue = new Queue<float>();
    public AudioSource source;

    public int sampleRate = 24000;

    public void PushPCM(byte[] pcm)
    {
        float[] samples = PCM16ToFloat(pcm);

        lock (lockObj)
        {
            foreach (var s in samples)
                audioQueue.Enqueue(s);
        }
    }

    void OnAudioFilterRead(float[] data, int channels)
    {
        lock (lockObj)
        {
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = audioQueue.Count > 0 ? audioQueue.Dequeue() : 0;
            }
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
