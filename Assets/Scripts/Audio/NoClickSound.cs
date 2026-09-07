using UnityEngine;

/// <summary>
/// Marker component: add this to any Button that should NOT play the
/// universal UI click sound (e.g. hold-to-move buttons, on-screen sticks).
/// UISoundManager skips every Button that carries this component.
///
/// Must live in its own file named NoClickSound.cs so Unity allows
/// attaching it from the Add Component menu.
/// </summary>
public class NoClickSound : MonoBehaviour { }
