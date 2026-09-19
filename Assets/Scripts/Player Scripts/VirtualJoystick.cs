using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Self-contained on-screen joystick - the "no package, no menu hunting"
/// route. Works with the plain legacy input system, so it works whether
/// Active Input Handling is Input Manager (Old) or Both.
///
/// SETUP (1 minute):
///   1. GameObject -> UI -> Image, name it "Joystick". Give it a round
///      sprite (or a generated circle), size ~200x200. This is the base.
///      Keep its pivot centered (the default 0.5, 0.5).
///   2. Right-click the base -> UI -> Image, name it "Knob", size ~90x90,
///      round sprite, anchors middle, position 0,0 (dead center).
///   3. Add this script to the BASE, then drag the Knob into its Knob slot.
///   4. Place the base where the thumb should rest (e.g. bottom-left) on
///      your HUD Canvas.
///
/// The player script reads VirtualJoystick.InputVector - x turns, y walks.
/// Same contract as the old HUD buttons: up = forward, left = turn left.
/// </summary>
public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [Tooltip("The joystick base image this script sits on.")]
    public RectTransform baseRect;

    [Tooltip("The knob image (child of the base).")]
    public RectTransform knob;

    [Tooltip("Drag distance (canvas units) that equals full input.")]
    public float radius = 100f;

    /// <summary>Current stick vector, each axis in [-1, 1]. Zero when released.</summary>
    public static Vector2 InputVector { get; private set; }

    void Awake()
    {
        if (baseRect == null) baseRect = (RectTransform)transform;
        if (knob == null)
        {
            Transform child = transform.Find("Knob");
            if (child != null) knob = (RectTransform)child;
        }
    }

    public void OnPointerDown(PointerEventData eventData) => ProcessDrag(eventData);

    public void OnDrag(PointerEventData eventData) => ProcessDrag(eventData);

    private void ProcessDrag(PointerEventData eventData)
    {
        // Convert the pointer position into the base's local space, clamp it
        // to the radius so the knob can never leave the base, and scale to
        // [-1, 1] for the player script.
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                baseRect, eventData.position, eventData.pressEventCamera, out Vector2 localPoint))
        {
            Vector2 clamped = Vector2.ClampMagnitude(localPoint, radius);
            knob.anchoredPosition = clamped;
            InputVector = clamped / radius;
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        InputVector = Vector2.zero;
        knob.anchoredPosition = Vector2.zero;   // snap back to center
    }

    private void OnDisable()
    {
        // If the HUD is hidden mid-drag the pointer-up event never arrives;
        // reset so the player doesn't keep walking forever.
        InputVector = Vector2.zero;
        if (knob != null) knob.anchoredPosition = Vector2.zero;
    }
}
