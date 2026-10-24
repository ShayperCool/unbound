namespace UnityEngine.UIElements
{
    public static partial class BackgroundPropertyHelper
    {
        public static BackgroundPosition ConvertScaleModeToBackgroundPosition(ScaleMode scaleMode = ScaleMode.StretchToFill) =>
            new BackgroundPosition(BackgroundPositionKeyword.Center);

        public static BackgroundRepeat ConvertScaleModeToBackgroundRepeat(ScaleMode scaleMode = ScaleMode.StretchToFill) =>
            new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);

        public static BackgroundSize ConvertScaleModeToBackgroundSize(ScaleMode scaleMode = ScaleMode.StretchToFill) => scaleMode switch
        {
            ScaleMode.ScaleAndCrop => new BackgroundSize(BackgroundSizeType.Cover),
            ScaleMode.ScaleToFit => new BackgroundSize(BackgroundSizeType.Contain),
            _ => new BackgroundSize(Length.Percent(100f), Length.Percent(100f)),
        };

        public static ScaleMode ResolveUnityBackgroundScaleMode(BackgroundPosition backgroundPositionX, BackgroundPosition backgroundPositionY, BackgroundRepeat backgroundRepeat, BackgroundSize backgroundSize, out bool valid)
        {
            foreach (var mode in new[] { ScaleMode.ScaleAndCrop, ScaleMode.ScaleToFit, ScaleMode.StretchToFill })
            {
                if (backgroundPositionX == ConvertScaleModeToBackgroundPosition(mode) && backgroundPositionY == ConvertScaleModeToBackgroundPosition(mode) &&
                    backgroundRepeat == ConvertScaleModeToBackgroundRepeat(mode) && backgroundSize == ConvertScaleModeToBackgroundSize(mode))
                {
                    valid = true;
                    return mode;
                }
            }
            valid = false;
            return ScaleMode.StretchToFill;
        }
    }
}
