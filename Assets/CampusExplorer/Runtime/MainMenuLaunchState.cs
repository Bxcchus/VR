namespace CampusExplorer
{
    public enum MainMenuLaunchMode
    {
        None,
        FreeTour,
        GuidedTour,
    }

    public struct MainMenuLaunchRequest
    {
        public MainMenuLaunchMode Mode;
        public float SnapAngle;
        public bool ContinuousMovement;
    }

    /// <summary>One scene transition's choices; consumed when the villa scene loads.</summary>
    public static class MainMenuLaunchState
    {
        static MainMenuLaunchRequest pending;

        public static void Queue(MainMenuLaunchMode mode, float snapAngle, bool continuousMovement)
        {
            pending = new MainMenuLaunchRequest
            {
                Mode = mode,
                SnapAngle = snapAngle == 30f ? 30f : 45f,
                ContinuousMovement = continuousMovement,
            };
        }

        public static bool TryConsume(out MainMenuLaunchRequest request)
        {
            request = pending;
            pending = default;
            return request.Mode != MainMenuLaunchMode.None;
        }

        public static void Clear() => pending = default;
    }
}
