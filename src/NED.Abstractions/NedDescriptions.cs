using System.Linq;
using System.Reflection;

namespace NED.Abstractions;

public static class NedDescriptions
{
    public static string? Of(ICustomAttributeProvider? member) =>
        (member?.GetCustomAttributes(typeof(NedDescriptionAttribute), true)
                .FirstOrDefault() as NedDescriptionAttribute)?.Text;
}
