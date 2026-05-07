namespace EqApoTray.Services;

public enum TrayIconState
{
    DoubleBlue = -2,
    SingleBlue = -1,
    Neutral = 0,
    SingleRed = 1,
    DoubleRed = 2,
}

public static class TrayIconStateClassifier
{
    public static TrayIconState Classify(double db) => db switch
    {
        < -10 => TrayIconState.DoubleBlue,
        <  -5 => TrayIconState.SingleBlue,
        <=  5 => TrayIconState.Neutral,
        <= 10 => TrayIconState.SingleRed,
        _     => TrayIconState.DoubleRed,
    };
}
