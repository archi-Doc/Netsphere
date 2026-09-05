// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Netsphere.Misc;

/// <summary>
/// Provides signing, verification, and size estimation for tokens.
/// </summary>
public static class TokenHelper
{
    public const char StartChar = '{';
    public const char EndChar = '}';

    public static int CalculateMaxStringLength<T>(T value)
        where T : ITinyhandSerializable<T>
    {
        var rentMemory = TinyhandSerializer.SerializeObjectToRentMemory(value);
        var length = 3 + Base64Url.GetEncodedLength(rentMemory.Length); // {identifier+base64}
        rentMemory.Return();
        return length;
    }

    [SkipLocalsInit]
    public static bool TryParse<T>(char identifier, ReadOnlySpan<char> source, [MaybeNullWhen(false)] out T instance, out int read, IConversionOptions? conversionOptions = default)
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
        var length = Base64Url.GetDecodedLength(source);
        var spanowner = new SpanOwner<byte>(stackalloc byte[BaseHelper.StackallocThreshold], length);
        try
        {
            var span = spanowner.Span;
            if (!Base64Url.TryDecode(source, span, out _))
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
            spanowner.Dispose();
        }
    }

    public static bool TryFormat<T>(T value, char identifier, Span<char> destination, out int written, IConversionOptions? conversionOptions = default)
        where T : ITinyhandSerializable<T>
    {
        written = 0;
        var b = TinyhandSerializer.SerializeObject(value);
        var length = 3 + Base64Url.GetEncodedLength(b.Length);

        if (destination.Length < length)
        {
            return false;
        }

        var span = destination.Slice(2);
        var w = Base64Url.Encode(b, span);

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
        return "{" + identifier + Base64Url.EncodeToString(TinyhandSerializer.SerializeObject(value)) + "}";
    }
}
