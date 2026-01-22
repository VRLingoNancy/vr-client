using UnityEngine;

public class BubbleSpawner : MonoBehaviour
{
    public RectTransform canvasRect;
    public GameObject bubblePrefab;
    public float spawnInterval = 0.2f;
    public Vector2 bubbleSizeRange = new Vector2(20, 120);

    private float timer;

    void Update()
    {
        timer += Time.deltaTime;
        if (timer >= spawnInterval)
        {
            timer = 0f;
            SpawnBubble();
        }
    }

    void SpawnBubble()
    {
        GameObject bubble = Instantiate(bubblePrefab, canvasRect);

        RectTransform rect = bubble.GetComponent<RectTransform>();

        // Random horizontal spawn
        float x = Random.Range(0, canvasRect.rect.width);

        // Spawn slightly below screen
        rect.anchoredPosition = new Vector2(x, -100f);

        // Random bubble size
        float size = Random.Range(bubbleSizeRange.x, bubbleSizeRange.y);
        rect.sizeDelta = new Vector2(size, size);

        // Random upward speed
        bubble.GetComponent<BubbleMovement>().speed = Random.Range(20f, 100f);
    }
}
