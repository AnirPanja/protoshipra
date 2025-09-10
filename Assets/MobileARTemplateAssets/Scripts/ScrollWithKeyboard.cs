using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class MobileInputScroll : MonoBehaviour
{
    public ScrollRect scrollRect;        // ScrollView reference
    private TMP_InputField currentInput;
    private float lastKeyboardHeight = 0f;

    void Start()
    {
        // Attach listeners to all input fields
        TMP_InputField[] inputs = GetComponentsInChildren<TMP_InputField>(true);
        foreach (var input in inputs)
        {
            input.onSelect.AddListener((val) => OnInputSelected(input));
            input.onDeselect.AddListener((val) => OnInputDeselected());
        }
    }

    void Update()
    {
#if UNITY_ANDROID || UNITY_IOS
        float keyboardHeight = TouchScreenKeyboard.visible ? TouchScreenKeyboard.area.height : 0;

        if (Mathf.Abs(keyboardHeight - lastKeyboardHeight) > 5f)
        {
            lastKeyboardHeight = keyboardHeight;

            if (keyboardHeight > 0)
            {
                if (currentInput != null)
                    StartCoroutine(ScrollToInput(currentInput, keyboardHeight));
            }
            else
            {
                // Reset scroll when keyboard closes
                scrollRect.normalizedPosition = new Vector2(0, 1);
            }
        }
#endif
    }

    private void OnInputSelected(TMP_InputField input)
    {
        currentInput = input;
    }

    private void OnInputDeselected()
    {
        currentInput = null;
    }

    private IEnumerator ScrollToInput(TMP_InputField input, float keyboardHeight)
    {
        yield return null; // wait 1 frame for layout update

        // World position of input field
        RectTransform inputRect = input.GetComponent<RectTransform>();
        Vector3[] inputCorners = new Vector3[4];
        inputRect.GetWorldCorners(inputCorners);

        // World position of viewport
        Vector3[] viewportCorners = new Vector3[4];
        scrollRect.viewport.GetWorldCorners(viewportCorners);

        float inputBottom = inputCorners[0].y;
        float inputTop = inputCorners[1].y;
        float viewportBottom = viewportCorners[0].y;
        float viewportTop = viewportCorners[1].y;

        // Convert keyboard height to world units
        float keyboardWorldHeight = keyboardHeight * (viewportTop - viewportBottom) / Screen.height;
        float safeBottom = viewportBottom + keyboardWorldHeight + 50f; // extra padding

        // If input is hidden behind keyboard → scroll up
        if (inputBottom < safeBottom)
        {
            float diff = safeBottom - inputBottom;

            // Move scroll by ratio
            float scrollAmount = diff / (scrollRect.content.rect.height - scrollRect.viewport.rect.height);
            scrollRect.normalizedPosition -= new Vector2(0, scrollAmount);
        }
    }
}
