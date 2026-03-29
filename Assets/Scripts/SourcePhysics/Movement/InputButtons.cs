
namespace Fragsurf.Movement {

    /// <summary>
    /// Legacy input button enumeration. Use InputFrame bit flags directly instead.
    /// </summary>
    [System.Obsolete("Use InputFrame button constants (BTN_JUMP, BTN_DASH, etc.) directly. This enum is obsolete.", false)]
    [System.Flags]
    public enum InputButtons {
        None = 0,
        Jump = 1 << 1,
        Duck = 1 << 2,
        Speed = 1 << 3,
        MoveLeft = 1 << 4,
        MoveRight = 1 << 5,
        MoveForward = 1 << 6,
        MoveBack = 1 << 7
    }

}
