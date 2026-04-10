using UnityEngine;
using UnityEngine.Android;
using System;

public class MicrophoneStreamer
{
    private string device;
    private AudioClip clip;
    private int lastPos = 0;

    public int sampleRate = 24000;
    public int chunkSize = 2048;

    public Action<byte[]> OnChunkReady;

    public async void Start()
    {
        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            Permission.RequestUserPermission(Permission.Microphone);

            while (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
                await System.Threading.Tasks.Task.Delay(100);
        }

        device = Microphone.devices.Length > 0 ? Microphone.devices[0] : null;

        if (device == null)
        {
            Debug.LogError("[Mic] No microphone found!");
            return;
        }

        clip = Microphone.Start(device, true, 1, sampleRate);
    }

    public void Update()
    {
        if (clip == null || device == null) return;

        int pos = Microphone.GetPosition(device);
        int diff = pos - lastPos;

        if (diff < 0) diff += clip.samples;
        if (diff < chunkSize) return;

        float[] samples = new float[diff];
        clip.GetData(samples, lastPos);
        lastPos = pos;

        OnChunkReady?.Invoke(FloatToPCM16(samples));
    }

    byte[] FloatToPCM16(float[] samples)
    {
        byte[] bytes = new byte[samples.Length * 2];

        for (int i = 0; i < samples.Length; i++)
        {
            short s = (short)(samples[i] * short.MaxValue);
            bytes[i * 2] = (byte)(s & 0xff);
            bytes[i * 2 + 1] = (byte)((s >> 8) & 0xff);
        }

        return bytes;
    }
}
