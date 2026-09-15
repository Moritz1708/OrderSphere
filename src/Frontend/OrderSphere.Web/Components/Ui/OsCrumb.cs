namespace OrderSphere.Web.Components.Ui;

/// <summary>One breadcrumb. The last item usually has no <see cref="Href"/> — it is the current page.</summary>
public sealed record OsCrumb(string Text, string? Href = null);
