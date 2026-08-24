namespace NED.Core;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class NedCommandAttribute : Attribute
{
    public string Path { get; }
    public string? Shortcut { get; set; }
    public string? Icon { get; set; }
    public int Order { get; set; }

    public NedCommandAttribute(string path) => Path = path;
}
