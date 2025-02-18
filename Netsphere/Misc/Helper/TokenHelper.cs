// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace Netsphere.Misc;

internal static class TokenHelper
{
    public const char StartChar = '{';
    public const char EndChar = '}';

    public static bool TryParse<T>(char identifier, ReadOnlySpan<char> source, [MaybeNullWhen(false)] out T instance, out int read)
        where T : ITinyhandSerializable<T>
    {
        instance = default;
        read = 0;
        source = source.Trim();
        if (source.Length < 3)
        {
            return false;
        }
        else if (source[0] != StartChar || source[1] != identifier)
        {
            return false;
        }

        var last = source.IndexOf(EndChar);
        if (last < 0)
        {
            return false;
        }

        source = source.Slice(2, last - 2);
        var decodedLength = Base64.Url.GetMaxDecodedLength(source.Length);

        byte[]? rent = null;
        Span<byte> span = decodedLength <= 4096 ?
            stackalloc byte[decodedLength] : (rent = ArrayPool<byte>.Shared.Rent(decodedLength));

        var result = Base64.Url.FromStringToSpan(source, span, out var written);
        try
        {
            if (!result)
            {
                return false;
            }

            TinyhandSerializer.TryDeserializeObject<T>(span, out instance);
            if (instance is null)
            {
                return false;
            }

            read = last + 1;
            return true;
        }
        finally
        {
            if (rent != null)
            {
                ArrayPool<byte>.Shared.Return(rent);
            }
        }
    }

    public static bool TryFormat<T>(T value, char identifier, Span<char> destination, out int written)
        where T : ITinyhandSerializable<T>
    {
        written = 0;
        var b = TinyhandSerializer.SerializeObject(value);
        var length = 3 + Base64.Url.GetEncodedLength(b.Length);

        if (destination.Length < length)
        {
            return false;
        }

        var span = destination.Slice(2);
        if (!Base64.Url.FromByteArrayToSpan(b, span, out var w))
        {
            return false;
        }

        destination[0] = StartChar;
        destination[1] = identifier;
        span = span.Slice(w);
        span[0] = EndChar;

        written = 3 + w;
        return true;
    }

    public static string ToBase64<T>(T value, char identifier)
        where T : ITinyhandSerializable<T>
    {
        return "{" + identifier + Base64.Url.FromByteArrayToString(TinyhandSerializer.SerializeObject(value)) + "}";
    }
}
