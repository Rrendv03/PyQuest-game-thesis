/// <summary>
/// Sound contract for any UI controller that hosts DraggableOption cards
/// (Predict The Output, Pair A Code, ...). DraggableOption and DropSlot talk
/// to their host only through this interface, so every drag-based puzzle can
/// share the same click / hold / return audio wiring.
/// </summary>
public interface IDraggableOptionSounds
{
    /// <summary>Click one-shot fired on pointer press, even if no drag starts.</summary>
    void PlayOptionClickSound();

    /// <summary>Starts the looping hold sound. Fired by OnBeginDrag.</summary>
    void StartOptionHoldSound();

    /// <summary>Stops the looping hold sound. Fired by OnEndDrag, OnDrop and OnDisable.</summary>
    void StopOptionHoldSound();

    /// <summary>Return one-shot fired when the card glides back to its original position.</summary>
    void PlayOptionReturnSound();
}
