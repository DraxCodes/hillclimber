namespace ClimberWeb.Components.Ui;

/// <summary>Joins conditional class fragments without leaving double spaces.</summary>
public static class UiClass
{
    public static string Join(params string?[] parts) =>
        string.Join(" ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
}