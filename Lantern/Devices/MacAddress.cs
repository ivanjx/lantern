namespace Lantern.Devices;

internal static class MacAddress
{
    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = "";

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        Span<char> hex = stackalloc char[12];
        var index = 0;
        foreach (var character in value.Trim())
        {
            if (character is ':' or '-')
            {
                continue;
            }

            if (index >= hex.Length || !Uri.IsHexDigit(character))
            {
                return false;
            }

            hex[index++] = char.ToUpperInvariant(character);
        }

        if (index != hex.Length)
        {
            return false;
        }

        normalized = string.Create(17, hex.ToArray(), static (destination, source) =>
        {
            var sourceIndex = 0;

            for (var destinationIndex = 0; destinationIndex < destination.Length; destinationIndex++)
            {
                if ((destinationIndex + 1) % 3 == 0)
                {
                    destination[destinationIndex] = ':';
                }
                else
                {
                    destination[destinationIndex] = source[sourceIndex++];
                }
            }
        });
        return true;
    }
}
