using Avalonia;

namespace HmDesktopCalendar.DesktopIntegration;

public sealed class CalendarBoundsController
{
    public bool IsEditing { get; private set; }
    public PixelRect OriginalBounds { get; private set; }

    public void BeginEditing(PixelRect originalBounds)
    {
        OriginalBounds = originalBounds;
        IsEditing = true;
    }

    public void EndEditing() => IsEditing = false;
}
