using MudBlazor;
using OrderSphere.Web.Services;

namespace OrderSphere.Web.Components.Ui;

/// <summary>Bridges the design system's tone to MudBlazor's colour enum, for the few Mud components (timeline, alerts) that still take one.</summary>
public static class StatusToneExtensions
{
    public static Color ToMudColor(this StatusTone tone) => tone switch
    {
        StatusTone.Success => Color.Success,
        StatusTone.Warning => Color.Warning,
        StatusTone.Danger => Color.Error,
        StatusTone.Info => Color.Info,
        StatusTone.Accent => Color.Primary,
        _ => Color.Default,
    };
}
