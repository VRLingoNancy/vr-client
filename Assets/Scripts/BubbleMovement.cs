using UnityEngine;

public class BubbleMovement : MonoBehaviour
{
    public float speed = 50f;
    private RectTransform rect;
    private float screenTop = Screen.height + 100f;
    private float screenBottom = -100f;

    void Start()
    {
        rect = GetComponent<RectTransform>();
        // screenTop = Screen.height + 100f;
    }

    void Update()
    {
        rect.anchoredPosition += Vector2.up * speed * Time.deltaTime;

        //if (rect.anchoredPosition.y > screenTop)
            //rect.anchoredPosition.y = screenBottom;
    }
}
